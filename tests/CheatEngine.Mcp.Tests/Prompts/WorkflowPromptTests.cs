using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Prompts;
using CheatEngine.Mcp.Prompts.Workflows;
using CheatEngine.Mcp.Resources.Knowledge;
using CheatEngine.Mcp.Tests.Resources;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.Mcp.Tools.Pointer;
using CheatEngine.Mcp.Tools.Record;
using CheatEngine.Mcp.Tools.Structures;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Prompts;

/// <summary>
///     The guided workflow prompts: names, bodies, definitions, arguments, excerpts, gates, rendering, budget and cited
///     tools. The knowledge-text lints live in <see cref="KnowledgeLintTests" />.
/// </summary>
public sealed partial class WorkflowPromptTests
{
	/// <summary>The frozen prompt names, in the order of the workflow index; renaming one is a contract change.</summary>
	private static readonly string[] PlannedPrompts =
	[
		"attach_and_orient", "engine_triage", "plan_cheat", "find_known_value", "find_unknown_value",
		"find_float_value", "find_flag", "find_position", "find_timer", "find_text", "group_scan", "compare_snapshots",
		"identify_address", "freeze_value", "find_writer", "find_code_by_string", "trace_logic", "patch_branch",
		"nop_patch", "aob_injection", "review_aa_script", "shared_code_filter", "injection_copy_base",
		"make_aob_signature", "call_game_function", "manual_pointer_chain", "pointer_scan", "dissect_structure",
		"find_entity_list", "unity_mono_recon", "unity_il2cpp_recon", "dotnet_recon", "unreal_recon",
		"emulator_memory", "speedhack", "write_lua_script", "use_cheat_table", "build_robust_table",
		"repair_after_update", "session_report", "explain_error", "cleanup_session", "ce_tutorial_walkthrough"
	];

	/// <summary>
	///     Snake-case identifiers that name something other than a tool: the error kinds, which <c>explain_error</c>
	///     renders, the record activation failure reasons (such as <c>aob_not_found</c>), and the Cheat Engine symbol and
	///     Mono export that bodies name as they are.
	/// </summary>
	private static readonly HashSet<string> NonToolIdentifiers = new(
		WorkflowValues.ErrorKinds.Concat(Enum.GetValues<RecordActivationFailureReason>()
				.Select(static reason => JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(reason))!))
			.Concat(["speedhack_wantedspeed", "mono_error_ok"]), StringComparer.Ordinal);

	private static readonly Lazy<McpPrimitiveCatalog> PromptCatalog =
		new(static () => McpPrimitiveCatalog.Create(KnowledgeResourceTests.KnowledgeManifest));

	/// <summary>Every workflow prompt.</summary>
	public static TheoryData<string> PromptNames =>
		new(CheatEngineWorkflows.All.Select(static workflow => workflow.Prompt));

	[Fact]
	public void Prompts_Names_AreThePlannedWorkflows()
	{
		Assert.Equal(PlannedPrompts.Order(StringComparer.Ordinal),
			PromptCatalog.Value.Prompts.Select(static prompt => prompt.Name));
		Assert.Equal(PlannedPrompts, CheatEngineWorkflows.All.Select(static workflow => workflow.Prompt));
	}

	[Fact]
	public void Prompts_BijectWithTheEmbeddedWorkflowBodies()
	{
		Assert.All(CheatEngineWorkflows.All, static workflow =>
			Assert.Equal(workflow.Prompt.Replace('_', '-'), workflow.Workflow));
		Assert.Equal(
			CheatEngineWorkflows.All.Select(static workflow => workflow.Workflow).Order(StringComparer.Ordinal),
			WorkflowPrompt.EmbeddedWorkflows.Order(StringComparer.Ordinal));
		Assert.Equal(CheatEngineKnowledge.WorkflowNames,
			WorkflowPrompt.EmbeddedWorkflows.Order(StringComparer.Ordinal));
		Assert.All(CheatEngineWorkflows.All, static workflow =>
			Assert.Equal(CheatEngineKnowledge.TryReadWorkflow(workflow.Workflow, out string? served) ? served : null,
				WorkflowPrompt.Body(workflow.Workflow)));
		Assert.All(CheatEngineWorkflows.All.SelectMany(static workflow => workflow.Documents),
			static slug => Assert.Contains(slug, CheatEngineKnowledge.DocumentSlugs));
	}

	[Fact]
	public void Prompts_Titles_AreTheWorkflowHeadings()
	{
		foreach (WorkflowDefinition workflow in CheatEngineWorkflows.All)
		{
			Prompt prompt = Assert.Single(PromptCatalog.Value.Prompts, candidate => candidate.Name == workflow.Prompt);
			Assert.Equal(KnowledgeResourceTests.Heading(WorkflowPrompt.Body(workflow.Workflow)), prompt.Title);
			Assert.False(string.IsNullOrWhiteSpace(prompt.Description));
			Assert.All(prompt.Arguments ?? [], static argument =>
				Assert.False(string.IsNullOrWhiteSpace(argument.Description)));
		}
	}

	[Fact]
	public void Prompts_Descriptions_EndWithTheGateLine()
	{
		foreach (WorkflowDefinition workflow in CheatEngineWorkflows.All)
		{
			string description = PromptCatalog.Value.Prompts.Single(prompt => prompt.Name == workflow.Prompt)
				.Description!;
			string gates = WorkflowPrompt.GateLine(workflow.Gates);
			if (gates.Length == 0)
			{
				Assert.DoesNotContain("Needs Mcp:", description, StringComparison.Ordinal);
			}
			else
			{
				Assert.EndsWith(" " + gates, description, StringComparison.Ordinal);
			}
		}
	}

	[Fact]
	public void Prompts_Methods_DeclareTheDefinitionArgumentsInOrder()
	{
		MethodInfo[] methods = typeof(CheatEngineWorkflowPrompts).GetMethods(BindingFlags.Public | BindingFlags.Static)
			.Where(static method => method.GetCustomAttribute<McpServerPromptAttribute>() is not null).ToArray();

		Assert.Equal(CheatEngineWorkflows.All.Select(static workflow => workflow.Prompt).Order(StringComparer.Ordinal),
			methods.Select(static method => method.GetCustomAttribute<McpServerPromptAttribute>()!.Name!)
				.Order(StringComparer.Ordinal));
		foreach (MethodInfo method in methods)
		{
			string prompt = method.GetCustomAttribute<McpServerPromptAttribute>()!.Name!;
			WorkflowDefinition workflow = Definition(prompt);
			ParameterInfo[] parameters =
				[.. method.GetParameters().Where(static parameter => parameter.ParameterType == typeof(string))];

			Assert.Equal(workflow.Arguments.Select(static argument => argument.Name),
				parameters.Select(static parameter => parameter.Name));
			foreach ((WorkflowArgument argument, ParameterInfo parameter) in workflow.Arguments.Zip(parameters))
			{
				Assert.True(argument.Required == !parameter.HasDefaultValue,
					$"{prompt}.{argument.Name} is {(argument.Required ? "" : "not ")}required in its definition.");
				// A tool name is checked by its kind, and completes over every frozen tool name.
				Assert.Equal(argument.Kind is WorkflowInputKind.ToolName
						? [.. WorkflowValues.ToolNames]
						: argument.Allowed?.ToArray(),
					parameter.GetCustomAttribute<AllowedValuesAttribute>()?.Values.Cast<string>().ToArray());
				Assert.True(argument.Default is null || argument.Allowed is null ||
							argument.Allowed.Contains(argument.Default),
					$"{prompt}.{argument.Name} defaults to a value it does not allow.");
			}
		}
	}

	[Fact]
	public void Prompts_Arguments_KeepTheFreeTextArgumentLast()
	{
		foreach (WorkflowDefinition workflow in CheatEngineWorkflows.All)
		{
			WorkflowArgument[] prose = [.. workflow.Arguments.Where(static argument =>
				argument.Kind == WorkflowInputKind.Prose)];
			Assert.True(prose.Length <= 1, $"{workflow.Prompt} has more than one free-text argument.");
			if (prose.Length == 0)
			{
				continue;
			}

			// C# declares required parameters first, so a required free-text argument is the last required one.
			WorkflowArgument[] group = [.. workflow.Arguments.Where(argument => argument.Required == prose[0].Required)];
			Assert.True(group[^1] == prose[0],
				$"{workflow.Prompt}.{prose[0].Name} is free text, so it comes last among the " +
				$"{(prose[0].Required ? "required" : "optional")} arguments.");
		}
	}

	[Fact]
	public void Prompts_Selectors_AreEnumeratedArguments()
	{
		Assert.All(CheatEngineWorkflows.All.SelectMany(static workflow => workflow.Selectors.Select(selector =>
			(workflow.Prompt, Argument: workflow.Arguments.SingleOrDefault(argument => argument.Name == selector)))),
			static selector => Assert.True(selector.Argument?.Allowed is not null,
				$"A selector of {selector.Prompt} is not an enumerated argument."));
	}

	[Fact]
	public void Prompts_ErrorVocabularies_AreTheContractEnums()
	{
		Assert.Equal(["invalid_argument", "memory_read_failed", "instance_unavailable", "internal"],
			WorkflowValues.ErrorKinds.Where(static kind =>
				kind is "invalid_argument" or "memory_read_failed" or "instance_unavailable" or "internal"));
		Assert.Equal(Enum.GetValues<ToolErrorKind>().Length, WorkflowValues.ErrorKinds.Count);
		Assert.Equal(["not_started", "not_applied", "started", "completed", "cleanup_unconfirmed", "unknown"],
			WorkflowValues.HostEffects);
	}

	[Fact]
	public async Task Prompts_Rendered_StayWithinBudgetAndCiteOnlyFrozenToolNames()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);
		List<string> sizes = [];
		foreach (WorkflowDefinition workflow in CheatEngineWorkflows.All)
		{
			GetPromptResult result = await GetAsync(pipeline, workflow.Prompt, new Dictionary<string, object?>());
			int size = RenderedLength(result);
			sizes.Add($"{workflow.Prompt}={size}");
			Assert.True(size <= WorkflowPrompt.MaximumRenderedLength,
				$"{workflow.Prompt} renders {size} characters; the budget is {WorkflowPrompt.MaximumRenderedLength}.");
			string text = Assert.IsType<TextContentBlock>(result.Messages[0].Content).Text;
			string[] cited = SnakeToken().Matches(text).Select(static match => match.Value)
				.Where(static token => IsToolDomain(token.Split('_')[0]) && !NonToolIdentifiers.Contains(token))
				.Distinct(StringComparer.Ordinal).ToArray();
			Assert.NotEmpty(cited);
			Assert.All(cited, token => Assert.True(CheatEngineToolNames.All.Contains(token),
				$"{workflow.Prompt} cites '{token}', which is not a frozen tool name."));
		}

		TestContext.Current.TestOutputHelper?.WriteLine(string.Join(' ', sizes));
	}

	/// <summary>
	///     T9: every combination of <see cref="WorstCases" /> (each argument at its longest rendering or omitted, each
	///     enumerated argument at every value) renders within the budget through a served <c>prompts/get</c>.
	/// </summary>
	[Theory]
	[MemberData(nameof(PromptNames))]
	public async Task Prompts_WorstCaseArguments_StayWithinBudget(string prompt)
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);
		WorkflowDefinition workflow = Definition(prompt);
		int largest = 0;
		string worst = "";
		foreach (string?[] values in WorstCases(workflow))
		{
			int size = RenderedLength(await pipeline.Client.GetPromptAsync(prompt, Arguments(workflow, values),
				cancellationToken: TestContext.Current.CancellationToken));
			if (size > largest)
			{
				largest = size;
				worst = DescribeCase(workflow, values);
			}
		}

		TestContext.Current.TestOutputHelper?.WriteLine($"{prompt} renders at most {largest} characters with {worst}.");
		Assert.True(largest <= WorkflowPrompt.MaximumRenderedLength,
			$"{prompt} renders {largest} characters with {worst}; the budget is " +
			$"{WorkflowPrompt.MaximumRenderedLength}.");
	}

	/// <summary>
	///     The worst cases bound every other accepted input: replacing one value of any worst case with a padded,
	///     blank, differently cased or explicitly defaulted value never renders a longer prompt than the worst cases.
	/// </summary>
	[Theory]
	[MemberData(nameof(PromptNames))]
	public void WorstCases_BoundEveryOtherAcceptedValue(string prompt)
	{
		WorkflowDefinition workflow = Definition(prompt);
		string?[][] cases = [.. WorstCases(workflow)];
		int largest = cases.Max(values => RenderedLength(WorkflowPrompt.Render(null, workflow, values)));
		List<string> longer = [];
		foreach (string?[] values in cases)
		{
			for (int index = 0; index < values.Length; index++)
			{
				foreach (string? challenger in Challengers(workflow.Arguments[index]))
				{
					string?[] changed = [.. values];
					changed[index] = challenger;
					if (!IsAccepted(workflow, changed))
					{
						continue;
					}

					int size = RenderedLength(WorkflowPrompt.Render(null, workflow, changed));
					if (size > largest)
					{
						longer.Add($"{DescribeCase(workflow, changed)} renders {size}");
					}
				}
			}
		}

		Assert.True(longer.Count == 0,
			$"{prompt}: the worst cases reach {largest} characters, but " + string.Join("; ", longer.Take(5)));
	}

	[Fact]
	public void WorstCases_OmitOptionalArgumentsAndTryEveryAllowedValue()
	{
		string?[][] lua = [.. WorstCases(RuntimeWorkflows.WriteLuaScript)];
		string?[][] tutorial = [.. WorstCases(SessionWorkflows.CeTutorialWalkthrough)];
		string?[][] error = [.. WorstCases(SessionWorkflows.ExplainError)];

		Assert.Equal([[Longest(RuntimeWorkflows.WriteLuaScript.Arguments[0]), "read_only"],
			[Longest(RuntimeWorkflows.WriteLuaScript.Arguments[0]), "mutating"],
			[Longest(RuntimeWorkflows.WriteLuaScript.Arguments[0]), null]], lua);
		Assert.DoesNotContain(tutorial, static values => values[0] == "4" && values[1] == "gtutorial");
		Assert.Contains(tutorial, static values => values[0] == "9" && values[1] is null);
		Assert.Equal(WorkflowValues.ErrorKinds.Count * (WorkflowValues.HostEffects.Count + 1) * 2, error.Length);
		Assert.Contains(error, static values => values[1] is null && values[2] is null);
	}

	[Theory]
	[InlineData("find_writer", "trigger", "read")]
	[InlineData("find_writer", "size", "3")]
	[InlineData("speedhack", "speed", "0")]
	[InlineData("speedhack", "speed", "fast")]
	[InlineData("speedhack", "speed", "NaN")]
	[InlineData("speedhack", "speed", "1000.5")]
	[InlineData("pointer_scan", "maxOffset", "-4")]
	[InlineData("pointer_scan", "maxOffset", "2147483648")]
	[InlineData("pointer_scan", "maxOffset", "1048577")]
	[InlineData("dissect_structure", "size", "0")]
	[InlineData("dissect_structure", "size", "65537")]
	[InlineData("compare_snapshots", "size", "16777217")]
	[InlineData("injection_copy_base", "symbolName", "player base")]
	[InlineData("find_known_value", "description", "two\nlines")]
	[InlineData("find_known_value", "valueType", "uint16")]
	[InlineData("ce_tutorial_walkthrough", "tutorial", "gtutorial")]
	[InlineData("plan_cheat", "goal", "fly")]
	[InlineData("patch_branch", "mode", "sometimes")]
	[InlineData("explain_error", "kind", "oops")]
	[InlineData("explain_error", "tool", "no_such_tool")]
	[InlineData("review_aa_script", "recordId", "-1")]
	[InlineData("review_aa_script", "recordId", "x")]
	[InlineData("review_aa_script", "recordId", "+3")]
	[InlineData("compare_snapshots", "valueType", "bytes")]
	[InlineData("freeze_value", "valueType", "pointer")]
	[InlineData("freeze_value", "valueType", "utf16")]
	[InlineData("call_game_function", "functionAddress", "two\nlines")]
	[InlineData("use_cheat_table", "cheat", "two\nlines")]
	public async Task Prompts_InvalidArgument_IsInvalidParamsWithTheContractData(string prompt, string argument,
		string value)
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);
		Dictionary<string, object?> arguments = new(StringComparer.Ordinal)
		{
			[argument] = value
		};
		if (prompt == "ce_tutorial_walkthrough")
		{
			arguments["step"] = "7";
		}

		McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
			await GetAsync(pipeline, prompt, arguments));

		Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
		Assert.Equal("invalid_argument", exception.Data["kind"]);
		Assert.Equal("not_started", exception.Data["hostEffect"]);
	}

	[Fact]
	public async Task Prompts_MissingRequiredArgument_IsInvalidParams()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);

		McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
			await pipeline.Client.GetPromptAsync("find_writer", new Dictionary<string, object?>(),
				cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
		Assert.Equal("invalid_argument", exception.Data["kind"]);
	}

	[Fact]
	public async Task Prompts_WhitespaceRequiredArgument_IsInvalidParamsWithTheContractData()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);
		int checkedArguments = 0;
		foreach (Prompt prompt in PromptCatalog.Value.Prompts)
		{
			foreach (PromptArgument argument in (prompt.Arguments ?? []).Where(static argument =>
						 argument.Required == true))
			{
				Dictionary<string, object?> arguments = new(StringComparer.Ordinal)
				{
					[argument.Name] = "   "
				};
				McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
					await GetAsync(pipeline, prompt.Name, arguments));

				Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
				Assert.Equal("invalid_argument", exception.Data["kind"]);
				Assert.Equal("not_started", exception.Data["hostEffect"]);
				checkedArguments++;
			}
		}

		Assert.Equal(CheatEngineWorkflows.All.Sum(static workflow =>
			workflow.Arguments.Count(static argument => argument.Required)), checkedArguments);
	}

	[Fact]
	public async Task Prompts_EveryAllowedValue_RendersAndAnythingElseIsRefused()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);
		int checkedValues = 0;
		foreach (WorkflowDefinition workflow in CheatEngineWorkflows.All)
		{
			foreach (WorkflowArgument argument in workflow.Arguments.Where(static argument => argument.Allowed is not null))
			{
				foreach (string value in argument.Allowed!)
				{
					// gtutorial only has levels 1 to 3.
					Dictionary<string, object?> arguments = new(StringComparer.Ordinal)
					{
						[argument.Name] = value.ToUpperInvariant()
					};
					if (workflow.Prompt == "ce_tutorial_walkthrough" && argument.Name == "tutorial")
					{
						arguments["step"] = "1";
					}

					string text = Assert.IsType<TextContentBlock>((await GetAsync(pipeline, workflow.Prompt, arguments))
						.Messages[0].Content).Text;
					Assert.Contains($"- {{{argument.Name}}}: `{value}`", text, StringComparison.Ordinal);
					checkedValues++;
				}

				await Assert.ThrowsAsync<McpProtocolException>(async () => await GetAsync(pipeline, workflow.Prompt,
					new Dictionary<string, object?>(StringComparer.Ordinal) { [argument.Name] = "not-a-value" }));
			}
		}

		Assert.Equal(CheatEngineWorkflows.All.Sum(static workflow =>
			workflow.Arguments.Sum(static argument => argument.Allowed?.Count ?? 0)), checkedValues);
	}

	[Fact]
	public async Task Prompts_AllowedValues_CompleteWithoutAHandler()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);

		CompleteResult trigger = await pipeline.Client.CompleteAsync(new PromptReference { Name = "find_writer" },
			"trigger", "a", cancellationToken: TestContext.Current.CancellationToken);
		CompleteResult keep = await pipeline.Client.CompleteAsync(new PromptReference { Name = "cleanup_session" },
			"keepAddressList", string.Empty, cancellationToken: TestContext.Current.CancellationToken);
		CompleteResult goal = await pipeline.Client.CompleteAsync(new PromptReference { Name = "plan_cheat" },
			"goal", "infinite", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(["access"], trigger.Completion.Values);
		Assert.Equal(["yes", "no"], keep.Completion.Values);
		Assert.Equal(["infinite_health", "infinite_ammo"], goal.Completion.Values);
	}

	[Fact]
	public async Task Prompts_Get_RendersPreambleInputsBodyAndServedDocumentLinks()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);

		GetPromptResult result = await GetAsync(pipeline, "find_writer",
			new Dictionary<string, object?> { ["trigger"] = "ACCESS" });

		string text = Assert.IsType<TextContentBlock>(result.Messages[0].Content).Text;
		Assert.StartsWith("# Find what writes or accesses an address\n\n" + WorkflowPrompt.Preamble + "\n\nInputs", text,
			StringComparison.Ordinal);
		Assert.Contains("- {address}: `7FF6A1B2C3D0`\n- {trigger}: `access`\n- {size}: `4` (default)\n", text,
			StringComparison.Ordinal);
		Assert.Contains("](cheatengine://docs/workflows/shared-code-filter)", text, StringComparison.Ordinal);
		Assert.DoesNotContain(".md)", text, StringComparison.Ordinal);
		Assert.Equal("Find what writes or accesses an address", result.Description);
		ResourceLinkBlock[] links = result.Messages.Skip(1).Select(static message =>
			Assert.IsType<ResourceLinkBlock>(message.Content)).ToArray();
		Assert.All(result.Messages, static message => Assert.Equal(Role.User, message.Role));
		Assert.Equal(["cheatengine://docs/debugger", "cheatengine://docs/code-analysis"],
			links.Select(static link => link.Uri));
		Assert.Equal(["Debugger methods", "Disassembly and code analysis"], links.Select(static link => link.Title));
		Assert.All(links, static link =>
		{
			Assert.Equal(McpResourceUris.MarkdownMimeType, link.MimeType);
			Assert.True(link.Size > 0);
			Assert.Null(link.Description);
		});
	}

	[Fact]
	public void Preamble_IsHostNeutral()
	{
		Assert.Contains("When the tools take an instanceId (gateway), call " + CheatEngineToolNames.InstanceList +
						" first", WorkflowPrompt.Preamble, StringComparison.Ordinal);
		Assert.DoesNotContain("Through the gateway", WorkflowPrompt.Preamble, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Prompts_Get_NamesTheGatesBeforeTheInputs()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);

		GetPromptResult patch = await GetAsync(pipeline, "patch_branch", new Dictionary<string, object?>());
		GetPromptResult writer = await GetAsync(pipeline, "find_writer", new Dictionary<string, object?>());

		Assert.Contains(WorkflowPrompt.Preamble + "\n\nNeeds Mcp:EnableAutoAssembler for its gated steps.\n\nInputs",
			Assert.IsType<TextContentBlock>(patch.Messages[0].Content).Text, StringComparison.Ordinal);
		Assert.DoesNotContain("Needs Mcp:", Assert.IsType<TextContentBlock>(writer.Messages[0].Content).Text,
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task Prompts_Get_KeepOnlyTheSelectedExcerpt()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);

		string plan = Assert.IsType<TextContentBlock>((await GetAsync(pipeline, "plan_cheat",
			new Dictionary<string, object?> { ["goal"] = "CURRENCY" })).Messages[0].Content).Text;
		string error = Assert.IsType<TextContentBlock>((await GetAsync(pipeline, "explain_error",
			new Dictionary<string, object?> { ["kind"] = "busy" })).Messages[0].Content).Text;

		Assert.Equal(["### goal: currency"], ExcerptHeading().Matches(plan).Select(static match => match.Value));
		Assert.Contains("## Pitfalls", plan, StringComparison.Ordinal);
		Assert.Equal(["### kind: busy", "### hostEffect: default"],
			ExcerptHeading().Matches(error).Select(static match => match.Value));
	}

	[Theory]
	[InlineData("uint16", "uint16", "record_create variableType 1")]
	[InlineData("WString", "wstring", "record_create variableType 6 with unicode true")]
	[InlineData("bytes", "bytes", "record_create variableType 8")]
	public async Task Prompts_Get_NoteTheRecordTypeOfAFrozenValueType(string valueType, string rendered,
		string note)
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);

		GetPromptResult result = await GetAsync(pipeline, "freeze_value",
			new Dictionary<string, object?> { ["valueType"] = valueType });

		Assert.Contains($"- {{valueType}}: `{rendered}` ({note})\n",
			Assert.IsType<TextContentBlock>(result.Messages[0].Content).Text, StringComparison.Ordinal);
		Assert.Equal(WorkflowValues.RecordValueTypes.Order(StringComparer.Ordinal),
			WorkflowValues.RecordTypeNotes.Keys.Order(StringComparer.Ordinal));
	}

	[Fact]
	public async Task Prompts_Get_CanonicalizeAToolName()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);

		GetPromptResult result = await GetAsync(pipeline, "explain_error",
			new Dictionary<string, object?> { ["tool"] = "MEMORY_WRITE", ["hostEffect"] = "Started" });

		string text = Assert.IsType<TextContentBlock>(result.Messages[0].Content).Text;
		Assert.Contains("- {tool}: `memory_write`\n", text, StringComparison.Ordinal);
		Assert.Contains("### hostEffect: started", text, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Prompts_Get_AcceptRecordIdZero()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);

		GetPromptResult result = await GetAsync(pipeline, "review_aa_script",
			new Dictionary<string, object?> { ["recordId"] = "0" });

		Assert.Contains("- {recordId}: `0`\n", Assert.IsType<TextContentBlock>(result.Messages[0].Content).Text,
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task Prompts_Get_CallGameFunctionNamesItsGateAndAnOptionalInstance()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);

		string free = Assert.IsType<TextContentBlock>((await GetAsync(pipeline, "call_game_function",
			new Dictionary<string, object?>())).Messages[0].Content).Text;
		string method = Assert.IsType<TextContentBlock>((await GetAsync(pipeline, "call_game_function",
			new Dictionary<string, object?> { ["instanceAddress"] = "[game.exe+10]+8" })).Messages[0].Content).Text;

		Assert.StartsWith("# Call a game function once\n\n" + WorkflowPrompt.Preamble +
						  "\n\nNeeds Mcp:EnableTargetCodeExecution for its gated steps.\n\nInputs", free,
			StringComparison.Ordinal);
		Assert.Contains("- {functionAddress}: `7FF6A1B2C3D0`\n- {purpose}: `double the damage dealt`\n" +
						"- {instanceAddress}: not given\n", free, StringComparison.Ordinal);
		Assert.Contains("- {instanceAddress}: `[game.exe+10]+8`\n", method, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Prompts_Get_UseCheatTableTakesOnlyOptionalArguments()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);

		GetPromptResult result = await GetAsync(pipeline, "use_cheat_table", new Dictionary<string, object?>
		{
			["cheat"] = " Infinite health "
		});

		string text = Assert.IsType<TextContentBlock>(result.Messages[0].Content).Text;
		Assert.StartsWith("# Load and use a cheat table\n\n" + WorkflowPrompt.Preamble +
						  "\n\nNeeds Mcp:EnableAutoAssembler for its gated steps.\n\nInputs", text,
			StringComparison.Ordinal);
		Assert.Contains("- {tablePath}: not given\n- {cheat}: `Infinite health`\n", text, StringComparison.Ordinal);
		Assert.Equal(
			["cheatengine://docs/cheat-tables", "cheatengine://docs/safety", "cheatengine://docs/auto-assembler"],
			result.Messages.Skip(1).Select(static message => Assert.IsType<ResourceLinkBlock>(message.Content).Uri));
	}

	[Theory]
	[InlineData("review_aa_script", "recordId", "000", "0")]
	[InlineData("review_aa_script", "recordId", "2147483647", "2147483647")]
	[InlineData("pointer_scan", "maxOffset", "0", "0")]
	[InlineData("pointer_scan", "maxOffset", "0004096", "4096")]
	[InlineData("pointer_scan", "maxOffset", "1048576", "1048576")]
	[InlineData("dissect_structure", "size", " 0256 ", "256")]
	[InlineData("dissect_structure", "size", "65536", "65536")]
	[InlineData("compare_snapshots", "size", "016777216", "16777216")]
	[InlineData("speedhack", "speed", "000.50", "0.5")]
	[InlineData("speedhack", "speed", ".5", "0.5")]
	[InlineData("speedhack", "speed", "2.", "2")]
	[InlineData("speedhack", "speed", "1000", "1000")]
	public async Task Prompts_Get_RenderANumberInItsCanonicalForm(string prompt, string argument, string value,
		string rendered)
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);

		GetPromptResult result = await GetAsync(pipeline, prompt,
			new Dictionary<string, object?>(StringComparer.Ordinal) { [argument] = value });

		Assert.Contains($"- {{{argument}}}: `{rendered}`\n",
			Assert.IsType<TextContentBlock>(result.Messages[0].Content).Text, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("pointer_scan", "maxOffset", "1048577", "maxOffset: must be a decimal integer from 0 to 1048576.")]
	[InlineData("dissect_structure", "size", "0", "size: must be a decimal integer from 1 to 65536.")]
	[InlineData("compare_snapshots", "size", "16777217", "size: must be a decimal integer from 1 to 16777216.")]
	[InlineData("review_aa_script", "recordId", "2147483648", "recordId: must be a non-negative decimal integer.")]
	[InlineData("find_entity_list", "expectedCount", "0", "expectedCount: must be a positive decimal integer.")]
	public void Render_RefusedInteger_NamesTheAcceptedRange(string prompt, string argument, string value,
		string message)
	{
		WorkflowDefinition workflow = Definition(prompt);
		string?[] values =
		[
			.. workflow.Arguments.Select(candidate => candidate.Name == argument ? value :
				candidate.Required ? Sample(candidate) : null)
		];

		ToolError error = Assert.Throws<CheatEngineToolException>(() => WorkflowPrompt.Render(null, workflow, values))
			.Error;

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
		Assert.Equal(message, error.Message);
	}

	/// <summary>
	///     An integer argument's <see cref="WorkflowArgument.Maximum" /> is the smallest limit of the tools its
	///     workflow passes the value to, so the prompt refuses what a step would refuse and nothing the tools accept;
	///     the other integer arguments reach no tool with a limit below <see cref="int.MaxValue" />.
	/// </summary>
	[Fact]
	public void Arguments_Maximum_IsTheLimitOfTheToolsThatReceiveTheValue()
	{
		// pointer_find_paths(maxOffset={maxOffset}).
		Assert.Equal(PointerScanTools.MaximumOffset, Argument(StructureWorkflows.PointerScan, "maxOffset").Maximum);
		// structure_autoguess(size={size}) and memory_create_snapshot(size={size}).
		Assert.Equal(Math.Min(StructureTools.MaxGuessSize, MemorySnapshotStore.MaximumSnapshotBytes),
			Argument(StructureWorkflows.DissectStructure, "size").Maximum);
		// memory_create_snapshot(size={size}).
		Assert.Equal(MemorySnapshotStore.MaximumSnapshotBytes,
			Argument(ValueWorkflows.CompareSnapshots, "size").Maximum);
		Assert.Equal(["compare_snapshots.size", "dissect_structure.size", "pointer_scan.maxOffset"],
			CheatEngineWorkflows.All.SelectMany(static workflow => workflow.Arguments
					.Where(static argument => argument.Maximum is not null)
					.Select(argument => $"{workflow.Prompt}.{argument.Name}"))
				.Order(StringComparer.Ordinal));
		foreach (WorkflowArgument argument in CheatEngineWorkflows.All.SelectMany(static workflow => workflow.Arguments)
					 .Where(static argument => argument.Maximum is not null))
		{
			Assert.True(argument.Kind is WorkflowInputKind.PositiveInteger or WorkflowInputKind.NonNegativeInteger,
				$"{argument.Name} has a maximum but is not an integer argument.");
			Assert.True(argument.Default is null ||
						int.Parse(argument.Default, CultureInfo.InvariantCulture) <= argument.Maximum,
				$"{argument.Name} defaults to more than its maximum.");
		}
	}

	[Fact]
	public async Task Prompts_WithoutTheResources_LinkByUriAndNameOnly()
	{
		CheatEngineMcpPrimitiveOptions manifest =
			CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog,
				static builder => builder.AddPrompts());
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync(manifest, McpPrimitiveBinding.Catalog);

		GetPromptResult result = await GetAsync(pipeline, "speedhack", new Dictionary<string, object?>());

		ResourceLinkBlock link = Assert.IsType<ResourceLinkBlock>(result.Messages[1].Content);
		Assert.Equal("cheatengine://docs/speedhack", link.Uri);
		Assert.Equal("doc_speedhack", link.Name);
		Assert.Null(link.Title);
		Assert.Null(link.Size);
	}

	[Fact]
	public void SelectExcerpts_KeepsTheSelectedSectionOrTheDefault()
	{
		const string body = "# T\n\n## Steps\n\nIntro.\n\n### mode: fast\n\nFast.\n\n#### Detail\n\nKept with fast.\n\n" +
							"### mode: default\n\nFallback.\n\n### other: x\n\nUnrelated.\n\n```\n### mode: fast\n```\n\n" +
							"## Report\n\nDone.";

		string fast = WorkflowPrompt.SelectExcerpts(body,
			new Dictionary<string, string?>(StringComparer.Ordinal) { ["mode"] = "FAST" });
		string slow = WorkflowPrompt.SelectExcerpts(body,
			new Dictionary<string, string?>(StringComparer.Ordinal) { ["mode"] = "slow" });
		string none = WorkflowPrompt.SelectExcerpts(body, new Dictionary<string, string?>(StringComparer.Ordinal));

		Assert.Equal("# T\n\n## Steps\n\nIntro.\n\n### mode: fast\n\nFast.\n\n#### Detail\n\nKept with fast.\n\n" +
					 "### other: x\n\nUnrelated.\n\n```\n### mode: fast\n```\n\n## Report\n\nDone.", fast);
		Assert.Equal("# T\n\n## Steps\n\nIntro.\n\n### mode: default\n\nFallback.\n\n### other: x\n\nUnrelated.\n\n" +
					 "```\n### mode: fast\n```\n\n## Report\n\nDone.", slow);
		Assert.Equal(body, none);
	}

	[Fact]
	public void GateLine_NamesEverySettingOnce()
	{
		Assert.Equal("", WorkflowPrompt.GateLine([]));
		Assert.Equal("Needs Mcp:EnableUnsafeLua for its gated steps.", WorkflowPrompt.GateLine([McpFeature.UnsafeLua]));
		Assert.Equal("Needs Mcp:EnableAutoAssembler, Mcp:EnableTargetCodeExecution and Mcp:EnableKernelAccess for " +
					 "its gated steps.", WorkflowPrompt.GateLine([McpFeature.AutoAssembler,
			McpFeature.TargetCodeExecution, McpFeature.KernelAccess]));
	}

	private static WorkflowDefinition Definition(string prompt)
	{
		return CheatEngineWorkflows.All.Single(workflow => workflow.Prompt == prompt);
	}

	private static WorkflowArgument Argument(WorkflowDefinition workflow, string name)
	{
		return workflow.Arguments.Single(argument => argument.Name == name);
	}

	/// <summary>A value every check accepts, for arguments the test does not set.</summary>
	private static string Sample(WorkflowArgument argument)
	{
		return argument.Allowed is { } allowed
			? allowed[0]
			: argument.Kind switch
			{
				WorkflowInputKind.PositiveInteger => "16",
				WorkflowInputKind.NonNegativeInteger => "0",
				WorkflowInputKind.Speed => "0.5",
				WorkflowInputKind.Symbol => "playerHook",
				WorkflowInputKind.ToolName => CheatEngineToolNames.MemoryWrite,
				WorkflowInputKind.Prose => "double the damage dealt",
				_ => "7FF6A1B2C3D0"
			};
	}

	/// <summary>
	///     The accepted value of an argument that is not enumerated whose input line renders longest: 256 characters of
	///     text, the longest symbol (the argument's <see cref="WorkflowArgument.MaximumLength" /> or else
	///     <see cref="WorkflowPrompt.MaximumSymbolLength" /> characters) or tool name, or the number whose canonical
	///     form is longest, the argument's <see cref="WorkflowArgument.Maximum" /> or else <see cref="int.MaxValue" />
	///     (the renderer drops leading zeros, so a padded number renders no longer).
	/// </summary>
	internal static string Longest(WorkflowArgument argument)
	{
		return argument.Allowed is { } allowed
			? allowed.MaxBy(static value => value.Length)!
			: argument.Kind switch
			{
				WorkflowInputKind.PositiveInteger or WorkflowInputKind.NonNegativeInteger =>
					(argument.Maximum ?? int.MaxValue).ToString(CultureInfo.InvariantCulture),
				// 17 significant digits below 0.1: the longest shortest round-trip form between 0.01 and 1000.
				WorkflowInputKind.Speed => "0.010000000000000002",
				WorkflowInputKind.Symbol =>
					"_" + new string('s', (argument.MaximumLength ?? WorkflowPrompt.MaximumSymbolLength) - 1),
				WorkflowInputKind.ToolName => CheatEngineToolNames.All.MaxBy(static name => name.Length)!,
				_ => string.Concat(Enumerable.Repeat("wordy ", 43))[..WorkflowPrompt.MaximumValueLength]
			};
	}

	/// <summary>
	///     Every combination of argument values that can render a prompt's longest text, as values in argument
	///     order where <see langword="null" /> omits the argument. An enumerated argument takes every allowed value
	///     (each keeps its own excerpt or note), any other argument its <see cref="Longest" /> value, and an optional
	///     argument is also omitted, which renders <c>`default` (default)</c> or <c>not given</c> and keeps the
	///     default's excerpt. The other lines of the prompt do not depend on the values, so the longest of these
	///     renderings is the longest any accepted input renders. Combinations the prompt refuses (a gtutorial step
	///     above 3) are left out.
	/// </summary>
	internal static IEnumerable<string?[]> WorstCases(WorkflowDefinition workflow)
	{
		IEnumerable<string?[]> cases = [[]];
		foreach (WorkflowArgument argument in workflow.Arguments)
		{
			List<string?> candidates = argument.Allowed is { } allowed ? [.. allowed] : [Longest(argument)];
			if (!argument.Required)
			{
				candidates.Add(null);
			}

			cases = cases.SelectMany(values => candidates.Select(value => values.Append(value).ToArray())).ToArray();
		}

		return cases.Where(values => IsAccepted(workflow, values));
	}

	/// <summary>Describes a combination of <see cref="WorstCases" /> for a failure message.</summary>
	internal static string DescribeCase(WorkflowDefinition workflow, IReadOnlyList<string?> values)
	{
		return string.Join(", ", workflow.Arguments.Select(static argument => argument.Name).Zip(values,
			static (name, value) => value switch
			{
				null => $"{name} omitted",
				{ Length: > 24 } => $"{name}=<{value.Length} characters>",
				_ => $"{name}={value}"
			}));
	}

	/// <summary>Whether the prompt method accepts the values: gtutorial has levels 1 to 3 only.</summary>
	private static bool IsAccepted(WorkflowDefinition workflow, IReadOnlyList<string?> values)
	{
		if (workflow.Prompt != "ce_tutorial_walkthrough")
		{
			return true;
		}

		Dictionary<string, string?> named = workflow.Arguments.Zip(values).ToDictionary(
			static pair => pair.First.Name, static pair => pair.Second, StringComparer.Ordinal);
		return !string.Equals(named["tutorial"]?.Trim(), "gtutorial", StringComparison.OrdinalIgnoreCase) ||
			   named["step"]?.Trim() is "1" or "2" or "3";
	}

	/// <summary>
	///     Accepted values outside <see cref="WorstCases" />: blank, the default given explicitly, an allowed value in
	///     upper case with spaces around it, a number padded with leading zeros to the length limit, and text with
	///     backticks, which the renderer replaces.
	/// </summary>
	private static IEnumerable<string?> Challengers(WorkflowArgument argument)
	{
		if (!argument.Required)
		{
			yield return "   ";
		}

		if (argument.Default is { } fallback)
		{
			yield return fallback;
		}

		foreach (string value in argument.Allowed ?? [])
		{
			yield return $" {value.ToUpperInvariant()} ";
		}

		if (argument.Allowed is not null)
		{
			yield break;
		}

		string longest = Longest(argument);
		switch (argument.Kind)
		{
			case WorkflowInputKind.PositiveInteger or WorkflowInputKind.NonNegativeInteger or WorkflowInputKind.Speed:
				yield return new string('0', WorkflowPrompt.MaximumValueLength - longest.Length) + longest;
				break;
			case WorkflowInputKind.ToolName:
				yield return longest.ToUpperInvariant();
				break;
			case WorkflowInputKind.Text or WorkflowInputKind.Prose:
				yield return new string('`', WorkflowPrompt.MaximumValueLength);
				break;
		}
	}

	/// <summary>The <c>prompts/get</c> arguments of a combination: the given values, not the omitted ones.</summary>
	private static Dictionary<string, object?> Arguments(WorkflowDefinition workflow, IReadOnlyList<string?> values)
	{
		return workflow.Arguments.Zip(values).Where(static pair => pair.Second is not null)
			.ToDictionary(static pair => pair.First.Name, static pair => (object?) pair.Second, StringComparer.Ordinal);
	}

	private static async Task<GetPromptResult> GetAsync(TestMcpPipeline pipeline, string prompt,
		Dictionary<string, object?> arguments)
	{
		foreach (WorkflowArgument argument in Definition(prompt).Arguments.Where(static argument => argument.Required))
		{
			arguments.TryAdd(argument.Name, Sample(argument));
		}

		return await pipeline.Client.GetPromptAsync(prompt, arguments,
			cancellationToken: TestContext.Current.CancellationToken);
	}

	internal static int RenderedLength(GetPromptResult result)
	{
		return result.Messages.Sum(static message => message.Content switch
		{
			TextContentBlock text => text.Text.Length,
			ResourceLinkBlock link => link.Uri.Length + link.Name.Length + (link.Title?.Length ?? 0) +
									  (link.Description?.Length ?? 0),
			_ => 0
		});
	}

	private static bool IsToolDomain(string segment)
	{
		return McpContractRules.Domains.Contains(segment) || McpContractRules.GatewayDomains.Contains(segment);
	}

	[GeneratedRegex("(?<![A-Za-z0-9_.])[a-z][a-z0-9]*(?:_[a-z0-9]+)+(?![A-Za-z0-9_])", RegexOptions.CultureInvariant)]
	private static partial Regex SnakeToken();

	[GeneratedRegex(@"^### [A-Za-z][A-Za-z0-9]*: .+$", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
	private static partial Regex ExcerptHeading();
}
