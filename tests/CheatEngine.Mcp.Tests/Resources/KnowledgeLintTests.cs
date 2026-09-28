using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Prompts.Workflows;
using CheatEngine.Mcp.Resources.Knowledge;
using CheatEngine.Mcp.Tests.Prompts;
using CheatEngine.Mcp.Tests.Support;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Resources;

/// <summary>
///     Lints that keep the knowledge text (the documents and the workflow bodies) and the workflow definitions true to
///     the tools, resources and settings the composition serves. Each failure names the file and line (or the prompt),
///     the citation and what the served contract offers instead, so the text can be fixed where it is written.
/// </summary>
public sealed partial class KnowledgeLintTests
{
	private static readonly string[] HouseSections = ["Goal", "Steps", "Decisions", "Pitfalls", "Report"];

	/// <summary>The projects whose sources and READMEs must not hard-code counts of prompts or documents.</summary>
	private static readonly string[] KnowledgeProjects = ["CheatEngine.Mcp.Prompts", "CheatEngine.Mcp.Resources"];

	/// <summary>The served tools by name in gateway form (every backend tool plus instance_list).</summary>
	private static readonly Lazy<Dictionary<string, Tool>> Tools = new(static () =>
		TestComposition.GatewayTools.ToDictionary(static tool => tool.Name, StringComparer.Ordinal));

	/// <summary>The resources and templates a backend composes, which the gateway serves locally or routes.</summary>
	private static readonly Lazy<McpPrimitiveCatalog> Catalog =
		new(static () => McpPrimitiveCatalog.Create(TestComposition.BackendManifest));

	/// <summary>Every knowledge file, as a path relative to the knowledge folder.</summary>
	public static TheoryData<string> KnowledgeFiles => new(Directory
		.EnumerateFiles(KnowledgeResourceTests.KnowledgeFolder, "*.md", SearchOption.AllDirectories)
		.Select(static path => Path.GetRelativePath(KnowledgeResourceTests.KnowledgeFolder, path).Replace('\\', '/'))
		.Order(StringComparer.Ordinal));

	/// <summary>Every workflow prompt.</summary>
	public static TheoryData<string> Workflows =>
		new(CheatEngineWorkflows.All.Select(static workflow => workflow.Prompt));

	/// <summary>T1: a call form names a served tool and only parameters its input schema has.</summary>
	[Theory]
	[MemberData(nameof(KnowledgeFiles))]
	public void Calls_CiteServedToolsAndTheirParameters(string file)
	{
		List<string> failures = [];
		foreach (Call call in Calls(file))
		{
			if (!CheatEngineToolNames.All.Contains(call.Tool))
			{
				failures.Add($"{call.Where}: {call.Tool}(...) is not a tool name.");
				continue;
			}

			if (call.Schema is not { } tool)
			{
				failures.Add($"{call.Where}: {call.Tool} is not served by the composed catalog.");
				continue;
			}

			string[] parameters = PropertyNames(tool.InputSchema);
			failures.AddRange(call.Arguments.Where(argument => !parameters.Contains(argument.Name))
				.Select(argument => $"{call.Where}: {call.Tool}({argument.Name}=...) has no parameter " +
									$"'{argument.Name}'; it takes {Listing(parameters)}."));
		}

		AssertClean(failures);
	}

	/// <summary>T2: an object inside an array-of-object or object parameter uses only the item schema's keys.</summary>
	[Theory]
	[MemberData(nameof(KnowledgeFiles))]
	public void Calls_NestedKeys_ExistInTheItemSchema(string file)
	{
		List<string> failures = [];
		foreach (Call call in Calls(file))
		{
			foreach ((string name, string value) in call.Arguments)
			{
				if (Member(call, name) is not { } property || Members(property) is not { } members)
				{
					continue;
				}

				string[] keys = PropertyNames(members);
				failures.AddRange(ObjectMembers(value).Where(member => !keys.Contains(member.Key))
					.Select(member => $"{call.Where}: {call.Tool}({name}=[{{{member.Key}: ...}}]) has no item key " +
									  $"'{member.Key}'; items take {Listing(keys)}."));
			}
		}

		AssertClean(failures);
	}

	/// <summary>
	///     T3: a literal value matches its schema: a quoted value is one of the enum values and not text for a number, and
	///     a bare number or hex literal is not passed where the schema wants a string or a decimal integer.
	/// </summary>
	[Theory]
	[MemberData(nameof(KnowledgeFiles))]
	public void Calls_LiteralValues_MatchTheSchema(string file)
	{
		List<string> failures = [];
		foreach (Call call in Calls(file))
		{
			foreach ((string name, string value) in call.Arguments)
			{
				if (Member(call, name) is not { } property)
				{
					continue;
				}

				if (Mismatch(property, value) is { } problem)
				{
					failures.Add($"{call.Where}: {call.Tool}({name}={value}): {problem}");
				}

				if (Members(property) is not { } members)
				{
					continue;
				}

				foreach ((string key, string nested) in ObjectMembers(value))
				{
					if (members.TryGetProperty("properties", out JsonElement keys) &&
						keys.TryGetProperty(key, out JsonElement item) && Mismatch(item, nested) is { } itemProblem)
					{
						failures.Add($"{call.Where}: {call.Tool}({name}=[{{{key}: {nested}}}]): {itemProblem}");
					}
				}
			}
		}

		AssertClean(failures);
	}

	/// <summary>
	///     T4: a parameter named outside a call form (<c>`name=value`</c>) belongs to a tool cited in the same paragraph,
	///     and <c>tool.member</c> names a parameter or result field of that tool.
	/// </summary>
	[Theory]
	[MemberData(nameof(KnowledgeFiles))]
	public void LooseParameters_BelongToACitedTool(string file)
	{
		string text = Read(file);
		List<string> failures = [];
		foreach ((int start, string paragraph) in Paragraphs(text))
		{
			string[] cited = SnakeToken().Matches(paragraph).Select(static match => match.Value)
				.Where(static token => CheatEngineToolNames.All.Contains(token)).Distinct(StringComparer.Ordinal)
				.ToArray();
			HashSet<string> members = new(cited.Where(Tools.Value.ContainsKey).SelectMany(static tool =>
				AllPropertyNames(Tools.Value[tool].InputSchema).Concat(Tools.Value[tool].OutputSchema is { } output
					? AllPropertyNames(output)
					: [])), StringComparer.Ordinal);
			foreach (Match loose in LooseParameter().Matches(paragraph))
			{
				string name = loose.Groups["name"].Value;
				if (!members.Contains(name))
				{
					failures.Add($"{file}:{Line(text, start + loose.Index)}: `{loose.Groups["span"].Value}` names " +
								 $"'{name}', which is no parameter or result field of the tools this paragraph cites " +
								 $"({Listing(cited)}); cite it inside a call form of the tool that takes it.");
				}
			}
		}

		foreach (Match member in ToolMember().Matches(text))
		{
			string tool = member.Groups["tool"].Value;
			string name = member.Groups["member"].Value;
			if (!CheatEngineToolNames.All.Contains(tool))
			{
				continue;
			}

			if (!Tools.Value.TryGetValue(tool, out Tool? schema))
			{
				failures.Add($"{file}:{Line(text, member.Index)}: {tool} is not served by the composed catalog.");
			}
			else if (!AllPropertyNames(schema.InputSchema).Concat(schema.OutputSchema is { } output
						 ? AllPropertyNames(output)
						 : []).Contains(name))
			{
				failures.Add($"{file}:{Line(text, member.Index)}: {tool}.{name}: {tool} has no parameter or result " +
							 $"field '{name}'.");
			}
		}

		AssertClean(failures);
	}

	/// <summary>
	///     T5: every <c>cheatengine://</c> URI in the text is a served resource or template, in its backend or gateway
	///     form, with only the query parameters the template declares, and a workflow URI names a served workflow; a
	///     placeholder such as <c>{instanceId}</c> or <c>&lt;slug&gt;</c> stands for one path segment.
	/// </summary>
	[Theory]
	[MemberData(nameof(KnowledgeFiles))]
	public void ResourceUris_AreServed(string file)
	{
		string text = Read(file);
		List<string> failures = [];
		foreach (Match match in ResourceUri().Matches(text))
		{
			string uri = match.Value.TrimEnd('.', ':');
			if (!IsServed(uri))
			{
				failures.Add($"{file}:{Line(text, match.Index)}: {uri} is not a served resource or template; served: " +
							 $"{Listing(ServedForms())}.");
			}
		}

		AssertClean(failures);
	}

	/// <summary>
	///     The URI matcher of T5 accepts the served forms and placeholders, with or without the query parameters their
	///     templates declare, and refuses everything else, including a workflow that is not served.
	/// </summary>
	[Theory]
	[InlineData("cheatengine://docs/safety", true)]
	[InlineData("cheatengine://docs/workflows/find-writer", true)]
	[InlineData("cheatengine://docs/workflows/<name-with-dashes>", true)]
	[InlineData("cheatengine://docs/workflows/{workflow}", true)]
	[InlineData("cheatengine://docs/workflows/no-such-workflow", false)]
	[InlineData("cheatengine://docs/workflows/find_writer", false)]
	[InlineData("cheatengine://docs/<slug>", true)]
	[InlineData("cheatengine://docs/...", true)]
	[InlineData("cheatengine://instance/memory/regions", true)]
	[InlineData("cheatengine://instances/{instanceId}/modules", true)]
	[InlineData("cheatengine://instances/ce-42-0123456789abcdef0123456789abcdef/records", true)]
	[InlineData("cheatengine://instances", true)]
	[InlineData("cheatengine://docs/no-such-document", false)]
	[InlineData("cheatengine://instance/modules/{module}", true)]
	[InlineData("cheatengine://instances/{instanceId}/disassembly/{address}", true)]
	[InlineData("cheatengine://instance/modules/{module}/imports", false)]
	[InlineData("cheatengine://instances/{instanceId}/disassembly", false)]
	[InlineData("cheatengine://instances/{instanceId}/memory/{address}?size=64", true)]
	[InlineData("cheatengine://instances/{instanceId}/memory/{address}?count=64", false)]
	[InlineData("cheatengine://instance/memory/{address}{?size}", true)]
	[InlineData("cheatengine://instance/disassembly/{address}?count=40", true)]
	[InlineData("cheatengine://instance/disassembly/7FF6A1B2C3D0?count=40", true)]
	[InlineData("cheatengine://instance/disassembly/7FF6A1B2C3D0?count={count}", true)]
	[InlineData("cheatengine://instance/disassembly/7FF6A1B2C3D0?size=40", false)]
	[InlineData("cheatengine://instances/ce-42-0123456789abcdef0123456789abcdef/memory/7FF6A1B2C3D0?size=8", true)]
	[InlineData("cheatengine://instance/modules?limit=5&offset=10", true)]
	[InlineData("cheatengine://instance/modules{?offset,limit}", true)]
	[InlineData("cheatengine://instance/modules{?offset,limit,sort}", false)]
	[InlineData("cheatengine://instance/process?limit=5", false)]
	[InlineData("cheatengine://instance/address-list", false)]
	public void ResourceUris_Matcher_KnowsTheServedForms(string uri, bool served)
	{
		Assert.Equal(served, IsServed(uri));
	}

	/// <summary>
	///     T6: an enumerated argument that the workflow passes to a tool offers only values that tool parameter accepts
	///     (its schema enum, or the values its description lists); <c>auto</c> stays in the prompt.
	/// </summary>
	[Theory]
	[MemberData(nameof(Workflows))]
	public void Arguments_OfferOnlyValuesTheirTargetToolAccepts(string prompt)
	{
		WorkflowDefinition workflow = Definition(prompt);
		List<string> failures = [];
		foreach (WorkflowArgument argument in workflow.Arguments.Where(static argument => argument.Target is not null))
		{
			WorkflowTarget target = argument.Target!.Value;
			string subject = $"{prompt}.{argument.Name} -> {target.Tool}.{target.Parameter}";
			if (!Tools.Value.TryGetValue(target.Tool, out Tool? tool))
			{
				failures.Add($"{subject}: {target.Tool} is not served by the composed catalog.");
				continue;
			}

			if (!tool.InputSchema.GetProperty("properties").TryGetProperty(target.Parameter, out JsonElement property))
			{
				failures.Add($"{subject}: {target.Tool} has no parameter '{target.Parameter}'.");
				continue;
			}

			string[] vocabulary = Vocabulary(property);
			string[] refused = (argument.Allowed ?? []).Where(static value => value != "auto")
				.Where(value => !vocabulary.Contains(value)).ToArray();
			if (refused.Length > 0)
			{
				failures.Add($"{subject}: offers {Listing(refused)}, which the tool does not accept; it accepts " +
							 $"{Listing(vocabulary)}.");
			}
		}

		AssertClean(failures);
	}

	/// <summary>T7: the documents a prompt links are exactly the documents its body links.</summary>
	[Theory]
	[MemberData(nameof(Workflows))]
	public void LinkedDocuments_AreTheDocumentsTheBodyLinks(string prompt)
	{
		WorkflowDefinition workflow = Definition(prompt);
		string file = WorkflowFile(workflow);
		string[] linked = DocumentLink().Matches(Read(file)).Select(static link => link.Groups["slug"].Value)
			.Distinct(StringComparer.Ordinal).ToArray();

		string[] missing = workflow.Documents.Except(linked, StringComparer.Ordinal).ToArray();
		string[] extra = linked.Except(workflow.Documents, StringComparer.Ordinal).ToArray();
		Assert.True(missing.Length == 0 && extra.Length == 0,
			$"{file} links the documents {Listing(linked)}, but {prompt} links {Listing(workflow.Documents)}: " +
			(missing.Length > 0 ? $"the body never links {Listing(missing)}; " : "") +
			(extra.Length > 0 ? $"the definition omits {Listing(extra)}; " : "") +
			"link each document of the definition from the body (usually its Report) and nothing else.");
	}

	/// <summary>
	///     T8: a body has the house sections in order and, for every combination of
	///     <see cref="WorkflowPromptTests.WorstCases" /> (each argument at its longest rendering or omitted, each
	///     enumerated argument at every value), renders within the prompt budget, counting the linked documents' listed
	///     titles and descriptions.
	/// </summary>
	[Theory]
	[MemberData(nameof(Workflows))]
	public void Body_HasTheHouseSectionsWithinBudget(string prompt)
	{
		WorkflowDefinition workflow = Definition(prompt);
		string body = Body(workflow);
		string[] sections = SectionHeading().Matches(body).Select(static match => match.Groups["title"].Value)
			.ToArray();
		int links = workflow.Documents.Sum(static slug => ListedLinkLength(slug));
		(int length, string worst) = WorkflowPromptTests.WorstCases(workflow)
			.Select(values => (Rendered(workflow, body, values) + links,
				WorkflowPromptTests.DescribeCase(workflow, values)))
			.MaxBy(static candidate => candidate.Item1);

		Assert.True(HouseSections.SequenceEqual(sections),
			$"{WorkflowFile(workflow)} has the sections {Listing(sections)}; a body has exactly " +
			$"{Listing(HouseSections)}, in that order.");
		Assert.True(length <= WorkflowPrompt.MaximumRenderedLength,
			$"{WorkflowFile(workflow)} renders {length} characters" +
			(worst.Length > 0 ? $" with {worst}" : "") +
			$"; trim the body so the prompt fits {WorkflowPrompt.MaximumRenderedLength}.");
	}

	/// <summary>T10: a workflow declares exactly the union of the requirements of the tools its body cites.</summary>
	[Theory]
	[MemberData(nameof(Workflows))]
	public void Gates_AreTheRequirementsOfTheToolsTheBodyCites(string prompt)
	{
		WorkflowDefinition workflow = Definition(prompt);
		string[] cited = SnakeToken().Matches(Body(workflow)).Select(static match => match.Value)
			.Where(static token => CheatEngineToolNames.All.Contains(token)).Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal).ToArray();
		string[] unserved = cited.Where(tool => !Tools.Value.ContainsKey(tool)).ToArray();
		Dictionary<string, string[]> requires = cited.Where(Tools.Value.ContainsKey)
			.ToDictionary(static tool => tool, static tool => Requires(Tools.Value[tool]), StringComparer.Ordinal);
		string[] expected = requires.Values.SelectMany(static names => names).Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal).ToArray();
		string[] declared = workflow.Gates.Select(McpFeatureGate.ContractName).Order(StringComparer.Ordinal).ToArray();

		Assert.True(unserved.Length == 0,
			$"{WorkflowFile(workflow)} cites {Listing(unserved)}, which the composed catalog does not serve, so its " +
			"gates cannot be checked.");
		Assert.True(expected.SequenceEqual(declared),
			$"{prompt} declares the gates {Listing(declared)}, but the tools its body cites require " +
			$"{Listing(expected)} ({Listing(requires.Where(static pair => pair.Value.Length > 0)
				.Select(static pair => $"{pair.Key}: {string.Join('+', pair.Value)}"))}). Update " +
			"WorkflowDefinition.Gates and the prompt description's gate line together.");
	}

	/// <summary>Every <c>{name}</c> placeholder of a body is a prompt argument, and every argument is cited.</summary>
	[Theory]
	[MemberData(nameof(Workflows))]
	public void Placeholders_AreThePromptArguments(string prompt)
	{
		WorkflowDefinition workflow = Definition(prompt);
		string[] placeholders = Placeholder().Matches(Body(workflow))
			.Select(static match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal).ToArray();
		string[] arguments = workflow.Arguments.Select(static argument => argument.Name).ToArray();

		string[] unknown = placeholders.Except(arguments, StringComparer.Ordinal).ToArray();
		string[] unused = arguments.Except(placeholders, StringComparer.Ordinal).ToArray();
		Assert.True(unknown.Length == 0 && unused.Length == 0,
			$"{WorkflowFile(workflow)} cites the placeholders {Listing(placeholders)} and {prompt} has the arguments " +
			$"{Listing(arguments)}: " +
			(unknown.Length > 0 ? $"{Listing(unknown)} are not arguments (the renderer never fills them); " : "") +
			(unused.Length > 0 ? $"{Listing(unused)} are never cited, so their values reach no step; " : "") +
			"cite every argument as {name} and nothing else.");
	}

	/// <summary>
	///     An excerpt selector's every value (and, for an optional one, no value) keeps exactly one of its
	///     <c>### name: value</c> sections; sections exist only for selectors and name only allowed values or default.
	/// </summary>
	[Theory]
	[MemberData(nameof(Workflows))]
	public void Excerpts_EveryAllowedValueSelectsExactlyOneSection(string prompt)
	{
		WorkflowDefinition workflow = Definition(prompt);
		string file = WorkflowFile(workflow);
		string body = Body(workflow);
		List<string> failures = [];
		foreach (Match section in ExcerptHeading().Matches(body))
		{
			string name = section.Groups["name"].Value;
			string value = section.Groups["value"].Value.Trim();
			WorkflowArgument? argument = workflow.Arguments.SingleOrDefault(candidate => candidate.Name == name);
			if (argument is null)
			{
				continue;
			}

			if (!workflow.Selectors.Contains(name))
			{
				failures.Add($"{file}: '### {name}: {value}' is never selected; {name} is not a selector of {prompt}.");
			}
			else if (value != WorkflowPrompt.DefaultExcerpt && !(argument.Allowed ?? []).Contains(value))
			{
				failures.Add($"{file}: '### {name}: {value}' names no allowed value of {name} " +
							 $"({Listing(argument.Allowed ?? [])} or {WorkflowPrompt.DefaultExcerpt}).");
			}
		}

		foreach (string selector in workflow.Selectors)
		{
			WorkflowArgument argument = workflow.Arguments.Single(candidate => candidate.Name == selector);
			IEnumerable<string?> values = argument.Allowed ?? [];
			if (!argument.Required && argument.Default is null)
			{
				values = values.Append(null);
			}

			foreach (string? value in values)
			{
				Dictionary<string, string?> selection = new(StringComparer.Ordinal)
				{
					[selector] = value
				};
				int kept = ExcerptHeading().Matches(WorkflowPrompt.SelectExcerpts(body, selection))
					.Count(match => match.Groups["name"].Value == selector);
				if (kept != 1)
				{
					failures.Add($"{file}: {selector}={value ?? "(not given)"} keeps {kept} '### {selector}: ...' " +
								 $"sections; add '### {selector}: {value ?? WorkflowPrompt.DefaultExcerpt}' or " +
								 $"'### {selector}: {WorkflowPrompt.DefaultExcerpt}'.");
				}
			}
		}

		AssertClean(failures);
	}

	/// <summary>The index of <c>workflows.md</c> lists every prompt with the documents it links, in order.</summary>
	[Theory]
	[MemberData(nameof(Workflows))]
	public void Index_ListsTheWorkflowWithItsDocuments(string prompt)
	{
		WorkflowDefinition workflow = Definition(prompt);
		string index = Read(McpKnowledgeText.DocumentsFolder + "/workflows.md");
		Match? row = IndexRow().Matches(index).FirstOrDefault(match => match.Groups["prompt"].Value == prompt);
		string expected = string.Join(", ", workflow.Documents.Select(static slug => $"[{slug}]({slug}.md)"));

		if (row is null)
		{
			Assert.Fail($"workflows.md has no index row for {prompt}; add: | [{prompt}](../Workflows/" +
						$"{workflow.Workflow}.md) | <use when> | <key tools> | {expected} |");
		}

		string[] documents = DocLink().Matches(row.Groups["docs"].Value).Select(static link => link.Groups["slug"].Value)
			.ToArray();
		Assert.True(documents.SequenceEqual(workflow.Documents),
			$"workflows.md lists {Listing(documents)} for {prompt}, but the prompt links {Listing(workflow.Documents)}; " +
			$"the Docs cell must be: {expected}");
	}

	/// <summary>
	///     T11: no count of prompts, workflows or documents is written in the sources, READMEs or knowledge text; such a
	///     count goes stale with the next addition, so derive it from the collections instead.
	/// </summary>
	[Fact]
	public void Counts_OfPromptsAndDocuments_AreNotHardCoded()
	{
		string root = RepositoryPaths.Root;
		IEnumerable<string> files = KnowledgeProjects
			.SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, "srcs", project), "*.*",
				SearchOption.AllDirectories))
			.Where(static path => Path.GetExtension(path) is ".cs" or ".md" &&
								  !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
									  StringComparison.Ordinal) &&
								  !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
									  StringComparison.Ordinal));
		List<string> failures = [];
		foreach (string path in files.Order(StringComparer.Ordinal))
		{
			string text = File.ReadAllText(path).ReplaceLineEndings("\n");
			failures.AddRange(CountPhrase().Matches(text).Select(match =>
				$"{Path.GetRelativePath(root, path).Replace('\\', '/')}:{Line(text, match.Index)}: " +
				$"'{WhiteSpace().Replace(match.Value, " ")}'"));
		}

		Assert.True(failures.Count == 0,
			"Counts of prompts, workflows or documents go stale; drop them or derive them from the collections:\n" +
			string.Join('\n', failures));
	}

	private static WorkflowDefinition Definition(string prompt)
	{
		return CheatEngineWorkflows.All.Single(workflow => workflow.Prompt == prompt);
	}

	private static string WorkflowFile(WorkflowDefinition workflow)
	{
		return $"{McpKnowledgeText.WorkflowsFolder}/{workflow.Workflow}.md";
	}

	/// <summary>
	///     The body as served, read from disk rather than from the embedded copy, so the lints check the files as written
	///     without a rebuild.
	/// </summary>
	private static string Body(WorkflowDefinition workflow)
	{
		return McpKnowledgeText.RewriteLinks(Read(WorkflowFile(workflow)), true);
	}

	private static string Read(string file)
	{
		return File.ReadAllText(Path.Combine(KnowledgeResourceTests.KnowledgeFolder, file)).ReplaceLineEndings("\n");
	}

	private static int Line(string text, int index)
	{
		return text.AsSpan(0, index).Count('\n') + 1;
	}

	private static void AssertClean(List<string> failures)
	{
		Assert.True(failures.Count == 0, string.Join('\n', failures));
	}

	private static string Listing(IEnumerable<string> values)
	{
		string[] items = values.ToArray();
		return items.Length == 0 ? "none" : string.Join(", ", items);
	}

	private static bool IsToolDomain(string token)
	{
		string domain = token.Split('_')[0];
		return McpContractRules.Domains.Contains(domain) || McpContractRules.GatewayDomains.Contains(domain);
	}

	/// <summary>Every <c>tool(arguments)</c> call form of a file whose name has a tool domain.</summary>
	private static IEnumerable<Call> Calls(string file)
	{
		string text = Read(file);
		foreach (Match match in ToolCall().Matches(text))
		{
			string tool = match.Groups["tool"].Value;
			if (!IsToolDomain(tool))
			{
				continue;
			}

			string arguments = WhiteSpace().Replace(match.Groups["arguments"].Value, " ");
			yield return new Call($"{file}:{Line(text, match.Index)}", tool,
				Tools.Value.GetValueOrDefault(tool), [.. NamedArguments(arguments)]);
		}
	}

	/// <summary>The <c>name=value</c> parts at the top level of a call's argument text.</summary>
	private static IEnumerable<(string Name, string Value)> NamedArguments(string arguments)
	{
		foreach (string part in TopLevel(arguments))
		{
			Match named = NamedPart().Match(part);
			if (named.Success)
			{
				yield return (named.Groups["name"].Value, named.Groups["value"].Value.Trim());
			}
		}
	}

	/// <summary>The keys and values of every object literal in a value: the value itself or each item of a list.</summary>
	private static IEnumerable<(string Key, string Value)> ObjectMembers(string value)
	{
		string trimmed = value.Trim();
		IEnumerable<string> objects = trimmed.StartsWith('{')
			? [trimmed]
			: trimmed.StartsWith('[') && trimmed.EndsWith(']')
				? TopLevel(trimmed[1..^1]).Where(static item => item.StartsWith('{'))
				: [];
		foreach (string item in objects.Where(static item => item.EndsWith('}')))
		{
			foreach (string member in TopLevel(item[1..^1]))
			{
				Match key = ObjectKey().Match(member);
				if (key.Success)
				{
					yield return (key.Groups["key"].Value, key.Groups["value"].Value.Trim());
				}
			}
		}
	}

	/// <summary>Splits on commas outside quotes, brackets, braces and parentheses.</summary>
	private static IEnumerable<string> TopLevel(string text)
	{
		int depth = 0;
		int start = 0;
		bool quoted = false;
		for (int index = 0; index <= text.Length; index++)
		{
			char character = index < text.Length ? text[index] : ',';
			if (character == '"')
			{
				quoted = !quoted;
			}
			else if (!quoted)
			{
				depth += character switch
				{
					'[' or '{' or '(' => 1,
					']' or '}' or ')' => -1,
					_ => 0
				};
			}

			if (character == ',' && depth == 0 && !quoted)
			{
				string part = text[start..index].Trim();
				if (part.Length > 0)
				{
					yield return part;
				}

				start = index + 1;
			}
		}
	}

	private static JsonElement? Member(Call call, string name)
	{
		return call.Schema is { } tool &&
			   tool.InputSchema.TryGetProperty("properties", out JsonElement properties) &&
			   properties.TryGetProperty(name, out JsonElement property)
			? property
			: null;
	}

	/// <summary>The object schema of an object property or of an array property's items.</summary>
	private static JsonElement? Members(JsonElement property)
	{
		if (property.TryGetProperty("items", out JsonElement items) && items.TryGetProperty("properties", out _))
		{
			return items;
		}

		return property.TryGetProperty("properties", out _) ? property : null;
	}

	private static string[] PropertyNames(JsonElement schema)
	{
		return schema.TryGetProperty("properties", out JsonElement properties)
			? [.. properties.EnumerateObject().Select(static property => property.Name)]
			: [];
	}

	/// <summary>Every property name at any depth of a schema: parameters, result fields and nested members.</summary>
	private static IEnumerable<string> AllPropertyNames(JsonElement schema)
	{
		if (schema.ValueKind != JsonValueKind.Object)
		{
			yield break;
		}

		foreach (JsonProperty member in schema.EnumerateObject())
		{
			if (member.Name == "properties" && member.Value.ValueKind == JsonValueKind.Object)
			{
				foreach (JsonProperty property in member.Value.EnumerateObject())
				{
					yield return property.Name;
					foreach (string nested in AllPropertyNames(property.Value))
					{
						yield return nested;
					}
				}
			}
			else if (member.Value.ValueKind == JsonValueKind.Object)
			{
				foreach (string nested in AllPropertyNames(member.Value))
				{
					yield return nested;
				}
			}
			else if (member.Value.ValueKind == JsonValueKind.Array)
			{
				foreach (string nested in member.Value.EnumerateArray().SelectMany(AllPropertyNames))
				{
					yield return nested;
				}
			}
		}
	}

	private static string[] Types(JsonElement property)
	{
		if (!property.TryGetProperty("type", out JsonElement type))
		{
			return [];
		}

		return type.ValueKind == JsonValueKind.Array
			? [.. type.EnumerateArray().Select(static item => item.GetString()!)]
			: [type.GetString()!];
	}

	/// <summary>Why a literal value does not fit its property schema, or <see langword="null" />.</summary>
	private static string? Mismatch(JsonElement property, string value)
	{
		value = value.Trim();
		if (value.Length == 0 || value.StartsWith('[') || value.StartsWith('{') || PlaceholderText().IsMatch(value))
		{
			return null;
		}

		string[] types = Types(property);
		bool text = types.Contains("string");
		bool number = types.Contains("integer") || types.Contains("number");
		Match quoted = QuotedLiteral().Match(value);
		if (quoted.Success)
		{
			string literal = quoted.Groups["literal"].Value;
			if (property.TryGetProperty("enum", out JsonElement values) &&
				!values.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String &&
													 item.GetString() == literal))
			{
				return $"\"{literal}\" is not one of {Listing(values.EnumerateArray().Select(static item =>
					item.ToString()))}.";
			}

			if (number && !text && !NumberLiteral().IsMatch(literal))
			{
				return $"the parameter is a JSON {string.Join('|', types)}, not the text \"{literal}\".";
			}

			return null;
		}

		if (NumberLiteral().IsMatch(value) && text && !number)
		{
			return property.TryGetProperty("enum", out JsonElement names)
				? $"the parameter takes one of {Listing(names.EnumerateArray().Select(static item => item.ToString()))}, " +
				  $"not the number {value}."
				: $"the parameter is a JSON string; quote the value (\"{value}\").";
		}

		if (HexLiteral().IsMatch(value) && number && !text)
		{
			return $"the parameter is a JSON {string.Join('|', types)}; write {value} as a decimal number.";
		}

		return value is "true" or "false" && !types.Contains("boolean") && types.Length > 0
			? $"the parameter is a JSON {string.Join('|', types)}, not a boolean."
			: null;
	}

	/// <summary>The values a target parameter accepts: its enum, or the words its description lists.</summary>
	private static string[] Vocabulary(JsonElement property)
	{
		if (property.TryGetProperty("enum", out JsonElement values))
		{
			return [.. values.EnumerateArray().Select(static value => value.ToString())];
		}

		return property.TryGetProperty("description", out JsonElement description)
			? [.. DescriptionWord().Matches(description.GetString() ?? "").Select(static match => match.Value)]
			: [];
	}

	private static string[] Requires(Tool tool)
	{
		return tool.Meta?[McpFeatureGate.RequiresMetaKey] is JsonArray requires
			? [.. requires.Select(static item => item!.GetValue<string>())]
			: [];
	}

	/// <summary>
	///     The rendered length of the body as written with the given argument values. The values come from
	///     <see cref="WorkflowPromptTests.WorstCases" />, which leaves out what the prompt methods refuse, so a render
	///     failure fails the test instead of passing unmeasured.
	/// </summary>
	private static int Rendered(WorkflowDefinition workflow, string body, string?[] values)
	{
		return WorkflowPromptTests.RenderedLength(WorkflowPrompt.RenderBody(null, workflow, body, values));
	}

	/// <summary>
	///     What the served listing adds to a bare document link in a rendered prompt: its title and any longer name.
	/// </summary>
	private static int ListedLinkLength(string slug)
	{
		string uri = McpResourceUris.Doc(slug);
		Resource? listed = Catalog.Value.LocalResources.Select(static entry => entry.Resource)
			.FirstOrDefault(resource => resource?.Uri == uri);
		string bare = WorkflowPrompt.Link(null, slug).Name;
		return listed is null
			? 0
			: (listed.Title?.Length ?? 0) + Math.Max(0, listed.Name.Length - bare.Length);
	}

	private static IEnumerable<(int Start, string Text)> Paragraphs(string text)
	{
		int start = 0;
		foreach (Match separator in BlankLine().Matches(text))
		{
			yield return (start, text[start..separator.Index]);
			start = separator.Index + separator.Length;
		}

		yield return (start, text[start..]);
	}

	/// <summary>
	///     Whether a cited URI is served: its path matches a served path (through the SDK matcher when it is
	///     concrete, through <see cref="UriPattern" /> when it has placeholders), the served form's <c>{?…}</c>
	///     expression names every query parameter it cites, and a workflow URI names a served workflow.
	/// </summary>
	private static bool IsServed(string uri)
	{
		if (McpResourceUris.IsGatewayInstances(uri))
		{
			return true;
		}

		(string path, string[] names) = SplitQuery(uri);
		if (!IsServedWorkflow(path))
		{
			return false;
		}

		if (PlaceholderText().IsMatch(path) || path.EndsWith('/'))
		{
			Regex pattern = UriPattern(path);
			return ServedForms().Select(SplitQuery).Any(form =>
				pattern.IsMatch(form.Path) && names.All(name => form.Names.Contains(name, StringComparer.Ordinal)));
		}

		string backend = McpResourceUris.TryParseGateway(path, out _, out string? routed) ? routed : path;
		return Catalog.Value.Entries.Any(entry => entry.IsMatch(backend) &&
			names.All(name => SplitQuery(entry.Template.UriTemplate).Names.Contains(name, StringComparer.Ordinal)));
	}

	/// <summary>
	///     Splits a URI or URI template into its path and the query parameter names it has, from a query
	///     (<c>?size=64&amp;count=2</c>) or a form-style expression (<c>{?offset,limit}</c>).
	/// </summary>
	private static (string Path, string[] Names) SplitQuery(string uri)
	{
		Match query = QueryPart().Match(uri);
		if (!query.Success)
		{
			return (uri, []);
		}

		IEnumerable<string> names = query.Groups["form"].Success
			? query.Groups["form"].Value.Split(',')
			: query.Groups["pairs"].Value.Split('&').Select(static pair => pair.Split('=')[0]);
		return (uri[..query.Index],
			[.. names.Select(static name => name.Trim()).Where(static name => name.Length > 0)]);
	}

	/// <summary>
	///     Whether a <c>cheatengine://docs/workflows/…</c> path names a served workflow, which the SDK matcher of
	///     the workflow template does not check: it accepts any name. Other paths, and a placeholder or trailing slash
	///     for the name, pass.
	/// </summary>
	private static bool IsServedWorkflow(string path)
	{
		if (!path.StartsWith(McpResourceUris.WorkflowsPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		string name = path[McpResourceUris.WorkflowsPrefix.Length..];
		return name.Length == 0 || PlaceholderText().IsMatch(name) ||
			   CheatEngineKnowledge.WorkflowNames.Contains(name, StringComparer.Ordinal);
	}

	/// <summary>The served URIs and URI templates, in backend and gateway form.</summary>
	private static IEnumerable<string> ServedForms()
	{
		return Catalog.Value.Entries.Select(static entry => entry.Template.UriTemplate)
			.Concat(Catalog.Value.InstanceResources.Select(static entry =>
				McpResourceUris.ToGateway(entry.Template.UriTemplate, "{" + McpResourceUris.InstanceIdVariable + "}")))
			.Append(McpResourceUris.GatewayInstances);
	}

	/// <summary>
	///     A cited path (without its query) as a pattern over the served forms' paths: a placeholder stands for one
	///     segment, and a trailing ellipsis or slash for any remaining path.
	/// </summary>
	private static Regex UriPattern(string path)
	{
		StringBuilder pattern = new("^");
		int last = 0;
		foreach (Match placeholder in PlaceholderText().Matches(path))
		{
			pattern.Append(Regex.Escape(path[last..placeholder.Index]))
				.Append(placeholder.Value is "..." or "…" ? ".*" : "[^/?#]+");
			last = placeholder.Index + placeholder.Length;
		}

		pattern.Append(Regex.Escape(path[last..]));
		if (path.EndsWith('/'))
		{
			pattern.Append(".+");
		}

		return new Regex(pattern.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
	}

	[GeneratedRegex(
		@"(?<![A-Za-z0-9_.])(?<tool>[a-z][a-z0-9]*(?:_[a-z0-9]+)+)\((?<arguments>[^()]*(?:\([^()]*\)[^()]*)*)\)",
		RegexOptions.CultureInvariant)]
	private static partial Regex ToolCall();

	[GeneratedRegex(@"^(?<name>[A-Za-z][A-Za-z0-9]*)\s*=\s*(?<value>.*)$",
		RegexOptions.CultureInvariant | RegexOptions.Singleline)]
	private static partial Regex NamedPart();

	[GeneratedRegex("^\"?(?<key>[A-Za-z][A-Za-z0-9]*)\"?\\s*[:=]\\s*(?<value>.*)$",
		RegexOptions.CultureInvariant | RegexOptions.Singleline)]
	private static partial Regex ObjectKey();

	[GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
	private static partial Regex WhiteSpace();

	[GeneratedRegex("(?<![A-Za-z0-9_.])[a-z][a-z0-9]*(?:_[a-z0-9]+)+(?![A-Za-z0-9_])", RegexOptions.CultureInvariant)]
	private static partial Regex SnakeToken();

	[GeneratedRegex("`(?<span>(?<name>[a-z][A-Za-z0-9]*)=[^`(]+)`", RegexOptions.CultureInvariant)]
	private static partial Regex LooseParameter();

	[GeneratedRegex(@"(?<![A-Za-z0-9_])(?<tool>[a-z][a-z0-9]*(?:_[a-z0-9]+)+)\.(?<member>[a-z][A-Za-z0-9]*)",
		RegexOptions.CultureInvariant)]
	private static partial Regex ToolMember();

	[GeneratedRegex("cheatengine://[^\\s`'\"()\\[\\],;|*]+", RegexOptions.CultureInvariant)]
	private static partial Regex ResourceUri();

	[GeneratedRegex("\\{[^}]*\\}|<[^>]*>|\\.\\.\\.|…", RegexOptions.CultureInvariant)]
	private static partial Regex PlaceholderText();

	/// <summary>A trailing form-style query expression (<c>{?a,b}</c>), or the query after the first bare ?.</summary>
	[GeneratedRegex(@"\{\?(?<form>[^}]*)\}$|(?<!\{)\?(?<pairs>.*)$", RegexOptions.CultureInvariant)]
	private static partial Regex QueryPart();

	[GeneratedRegex("^\"(?<literal>[^\"]*)\"$", RegexOptions.CultureInvariant)]
	private static partial Regex QuotedLiteral();

	[GeneratedRegex(@"^-?\d+(?:\.\d+)?$", RegexOptions.CultureInvariant)]
	private static partial Regex NumberLiteral();

	[GeneratedRegex("^-?0[xX][0-9A-Fa-f]+$", RegexOptions.CultureInvariant)]
	private static partial Regex HexLiteral();

	[GeneratedRegex("[A-Za-z0-9_]+", RegexOptions.CultureInvariant)]
	private static partial Regex DescriptionWord();

	[GeneratedRegex(@"\]\(\.\./Documents/(?<slug>[a-z0-9-]+)\.md(?:#[^)]*)?\)", RegexOptions.CultureInvariant)]
	private static partial Regex DocumentLink();

	[GeneratedRegex("^## (?<title>.+?)\\s*$", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
	private static partial Regex SectionHeading();

	[GeneratedRegex(@"^### (?<name>[A-Za-z][A-Za-z0-9]*): (?<value>\S.*)$",
		RegexOptions.CultureInvariant | RegexOptions.Multiline)]
	private static partial Regex ExcerptHeading();

	[GeneratedRegex(@"\{(?<name>[A-Za-z][A-Za-z0-9]*)\}", RegexOptions.CultureInvariant)]
	private static partial Regex Placeholder();

	[GeneratedRegex(@"^\| \[(?<prompt>[a-z0-9_]+)\]\(\.\./Workflows/[a-z0-9-]+\.md\) \|[^|]*\|[^|]*\|(?<docs>[^|]*)\|\s*$",
		RegexOptions.CultureInvariant | RegexOptions.Multiline)]
	private static partial Regex IndexRow();

	[GeneratedRegex(@"\[[^\]]+\]\((?<slug>[a-z0-9-]+)\.md\)", RegexOptions.CultureInvariant)]
	private static partial Regex DocLink();

	[GeneratedRegex(@"\n[ \t]*\n", RegexOptions.CultureInvariant)]
	private static partial Regex BlankLine();

	[GeneratedRegex(@"\b(?:\d+|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|" +
					@"sixteen|seventeen|eighteen|nineteen|twenty(?:-[a-z]+)?|thirty(?:-[a-z]+)?|forty(?:-[a-z]+)?)" +
					@"\s+(?:guided\s+)?(?:workflows?|prompts?|documents?|docs|workflow\s+bodies)\b",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex CountPhrase();

	/// <summary>One cited call form.</summary>
	/// <param name="Where">The file and line, as <c>file:line</c>.</param>
	/// <param name="Tool">The cited tool name.</param>
	/// <param name="Schema">The served tool, or <see langword="null" /> when the catalog does not serve it.</param>
	/// <param name="Arguments">The named arguments at the top level of the call.</param>
	private readonly record struct Call(
		string Where,
		string Tool,
		Tool? Schema,
		IReadOnlyList<(string Name, string Value)> Arguments);
}
