using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Prompts;
using CheatEngine.Mcp.Prompts.Workflows;
using CheatEngine.Mcp.Tests.Resources;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Asm;
using CheatEngine.Mcp.Tools.Record;
using CheatEngine.SDK.Engine.Enums;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Prompts;

/// <summary>
///     The prompt arguments against the tools that receive them: argument titles as declared and as listed, their
///     completion within the hosts' bound, the symbol names a workflow derives from an argument, and the record types
///     <c>freeze_value</c> maps to and its body serves.
/// </summary>
public sealed partial class WorkflowPromptTests
{
	private const string InjectionSignature = "48 89 ?? 24";

	private const string InjectionBytes = "48 89 5C 24 08";

	/// <summary>
	///     What the <c>freeze_value</c> body said while the record tools set no byte array length and no string
	///     encoding; both now apply, so neither may be rendered.
	/// </summary>
	private static readonly string[] StaleFreezeWorkarounds =
		["use the integer type of the same width", "no record tool sets a string's Unicode flag"];

	/// <summary>
	///     Every prompt argument carries a <see cref="DisplayAttribute" /> name, the title a client shows instead of
	///     the argument name: short, capitalized, without a final period, and distinct within its prompt.
	/// </summary>
	[Fact]
	public void Prompts_Arguments_CarryATitle()
	{
		int titled = 0;
		foreach (MethodInfo method in PromptMethods())
		{
			string prompt = method.GetCustomAttribute<McpServerPromptAttribute>()!.Name!;
			HashSet<string> titles = new(StringComparer.OrdinalIgnoreCase);
			foreach (ParameterInfo parameter in method.GetParameters()
						 .Where(static parameter => parameter.ParameterType == typeof(string)))
			{
				string? title = parameter.GetCustomAttribute<DisplayAttribute>()?.GetName();

				Assert.False(string.IsNullOrWhiteSpace(title), $"{prompt}.{parameter.Name} has no title.");
				Assert.InRange(title.Length, 2, 32);
				Assert.True(char.IsAsciiLetterUpper(title[0]), $"{prompt}: '{title}' does not start with a capital.");
				Assert.False(title.EndsWith('.'), $"{prompt}: '{title}' ends with a period.");
				Assert.True(titles.Add(title), $"{prompt} has two arguments titled '{title}'.");
			}

			titled += titles.Count;
		}

		Assert.Equal(CheatEngineWorkflows.All.Sum(static workflow => workflow.Arguments.Count), titled);
	}

	/// <summary>
	///     Every prompt argument is listed with its <see cref="DisplayAttribute" /> name as its <c>title</c>, which
	///     Core publishes because the SDK never fills <see cref="PromptArgument.Title" />.
	/// </summary>
	[Fact]
	public async Task Prompts_Listed_ArgumentTitlesAreTheDisplayNames()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);

		IList<McpClientPrompt> prompts =
			await pipeline.Client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(DisplayNames(), ListedTitles(prompts.Select(static prompt => prompt.ProtocolPrompt)));
	}

	/// <summary>
	///     An empty prefix completes every prompt argument with its <c>[AllowedValues]</c> list, which the SDK appends
	///     without a bound: the host's completion bound keeps the first <see cref="McpCompletions.MaximumValues" />
	///     values, the whole count as <c>total</c> and sets <c>hasMore</c> for a longer list, which only
	///     <c>explain_error.tool</c> has (every frozen tool name). An argument without the list is offered nothing.
	/// </summary>
	[Fact]
	public async Task Prompts_EmptyPrefix_CompletesTheAllowedValuesWithinTheBound()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);
		List<string> longer = [];
		int completed = 0;
		foreach (MethodInfo method in PromptMethods())
		{
			PromptReference reference = new()
			{
				Name = method.GetCustomAttribute<McpServerPromptAttribute>()!.Name!
			};
			foreach (ParameterInfo parameter in method.GetParameters()
						 .Where(static parameter => parameter.ParameterType == typeof(string)))
			{
				string[] allowed =
					parameter.GetCustomAttribute<AllowedValuesAttribute>()?.Values.Cast<string>().ToArray() ?? [];

				Completion completion = (await pipeline.Client.CompleteAsync(reference, parameter.Name!,
					string.Empty, cancellationToken: TestContext.Current.CancellationToken)).Completion;

				Assert.Equal(allowed.Take(McpCompletions.MaximumValues), completion.Values);
				Assert.Equal(allowed.Length > 0 ? allowed.Length : null, completion.Total);
				Assert.Equal(allowed.Length > McpCompletions.MaximumValues ? true : null, completion.HasMore);
				if (allowed.Length > McpCompletions.MaximumValues)
				{
					longer.Add($"{reference.Name}.{parameter.Name}");
				}

				completed++;
			}
		}

		Assert.Equal(["explain_error.tool"], longer);
		Assert.Equal(CheatEngineWorkflows.All.Sum(static workflow => workflow.Arguments.Count), completed);
	}

	/// <summary>
	///     <c>explain_error.tool</c> completes over every frozen tool name in ordinal order, by prefix and ignoring
	///     case, within the completion bound.
	/// </summary>
	[Fact]
	public async Task Prompts_ToolName_CompletesTheFrozenToolNamesByPrefix()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);

		(Completion all, Completion memory) = await CompleteToolAsync(pipeline.Client);

		AssertToolNameCompletions(all, memory);
	}

	/// <summary>
	///     Every <c>freeze_value</c> type maps to a <c>record_create</c> variableType, and for UTF-16 text to the
	///     <c>unicode</c> option, that the record tools accept, so the workflow never proposes a record the tool
	///     refuses: UTF-16 text is a string record (6) with <c>unicode</c> true, never the refused 7.
	/// </summary>
	[Fact]
	public void FreezeValue_RecordTypes_AreVariableTypesRecordCreateAccepts()
	{
		Assert.Equal(WorkflowValues.RecordValueTypes.Order(StringComparer.Ordinal),
			WorkflowValues.RecordTypeNotes.Keys.Order(StringComparer.Ordinal));
		Assert.Equal("record_create variableType 6 with unicode true", WorkflowValues.RecordTypeNotes["wstring"]);
		Assert.Equal("record_create variableType 8", WorkflowValues.RecordTypeNotes["bytes"]);
		foreach ((string valueType, string note) in WorkflowValues.RecordTypeNotes)
		{
			Match code = RecordTypeCode().Match(note);
			Assert.True(code.Success, $"{valueType} notes '{note}', which names no record_create variableType.");
			VariableType type = (VariableType) int.Parse(code.Groups["code"].Value, CultureInfo.InvariantCulture);

			RecordArguments.Type(type, valueType);
			RecordArguments.Layout(type, null, code.Groups["unicode"].Success ? true : null, valueType);
		}
	}

	/// <summary>
	///     The <c>freeze_value</c> body, as rendered for each type the prompt accepts, serves that type: it passes the
	///     <c>unicode=true</c> record option a UTF-16 note names, gives a byte array read the <c>memory_read</c> size
	///     the tool requires, and keeps none of the workarounds from when the record tools set no string encoding or
	///     byte array length. The body lives in the Resources knowledge base, so a change of
	///     <see cref="WorkflowValues.RecordValueTypes" /> fails here until the body follows it.
	/// </summary>
	[Fact]
	public void FreezeValue_Body_ServesEveryRecordValueType()
	{
		SortedSet<string> failures = new(StringComparer.Ordinal);
		foreach (string valueType in WorkflowValues.RecordValueTypes)
		{
			GetPromptResult result =
				WorkflowPrompt.Render(null, ValueWorkflows.FreezeValue, "game.exe+10", valueType, null, null);
			// Wrapped lines are joined, so a phrase matches wherever the body breaks it.
			string text =
				WhiteSpaceRun().Replace(Assert.IsType<TextContentBlock>(result.Messages[0].Content).Text, " ");

			if (RecordTypeCode().Match(WorkflowValues.RecordTypeNotes[valueType]).Groups["unicode"].Success &&
				!text.Contains("unicode=true", StringComparison.Ordinal))
			{
				failures.Add($"For {valueType} it never passes unicode=true to the record.");
			}

			if (valueType == "bytes" && !MemoryReadSize().IsMatch(text))
			{
				failures.Add($"For {valueType} it reads the value without memory_read.size, which bytes require.");
			}

			foreach (string stale in StaleFreezeWorkarounds.Where(stale =>
						 text.Contains(stale, StringComparison.OrdinalIgnoreCase)))
			{
				failures.Add($"It still says '{stale}'.");
			}
		}

		Assert.True(failures.Count == 0,
			"Knowledge/Workflows/freeze-value.md does not serve every freeze_value type. " +
			string.Join(" ", failures));
	}

	/// <summary>
	///     A symbol argument leaves room for every name its body builds from it and passes to
	///     <c>asm_generate_injection(symbolName=...)</c>: its <see cref="WorkflowArgument.MaximumLength" /> is the
	///     tool's limit minus the longest suffix the body appends, and the longest accepted symbol with each suffix is
	///     a name the tool accepts.
	/// </summary>
	[Fact]
	public void Arguments_SymbolMaximumLength_LeavesRoomForTheNamesTheToolReceives()
	{
		string limit = "_" + new string('s', WorkflowPrompt.MaximumSymbolLength - 1);
		Assert.Equal(limit,
			AsmTools.GenerateInjection("game.exe", InjectionSignature, InjectionBytes, limit).SymbolName);
		Assert.Throws<CheatEngineToolException>(() =>
			AsmTools.GenerateInjection("game.exe", InjectionSignature, InjectionBytes, limit + "s"));
		List<string> checkedArguments = [];
		foreach (WorkflowDefinition workflow in CheatEngineWorkflows.All)
		{
			foreach (WorkflowArgument argument in workflow.Arguments.Where(static argument =>
						 argument.Kind is WorkflowInputKind.Symbol))
			{
				string[] suffixes =
				[
					.. InjectionSymbol().Matches(WorkflowPrompt.Body(workflow.Workflow))
						.Where(match => match.Groups["name"].Value == argument.Name)
						.Select(static match => match.Groups["suffix"].Value)
				];
				Assert.NotEmpty(suffixes);
				string longest = Longest(argument);

				Assert.Equal(WorkflowPrompt.MaximumSymbolLength - suffixes.Max(static suffix => suffix.Length),
					longest.Length);
				Assert.All(suffixes, suffix => Assert.Equal(longest + suffix,
					AsmTools.GenerateInjection("game.exe", InjectionSignature, InjectionBytes, longest + suffix)
						.SymbolName));
				checkedArguments.Add($"{workflow.Prompt}.{argument.Name}");
			}
		}

		Assert.Equal(["aob_injection.symbolName", "injection_copy_base.symbolName"], checkedArguments);
		Assert.All(CheatEngineWorkflows.All.SelectMany(static workflow => workflow.Arguments)
				.Where(static argument => argument.MaximumLength is not null),
			static argument => Assert.Equal(WorkflowInputKind.Symbol, argument.Kind));
	}

	[Theory]
	[InlineData("injection_copy_base", 61, 60)]
	[InlineData("aob_injection", 65, 64)]
	public void Render_RefusedSymbol_NamesTheAcceptedLength(string prompt, int length, int maximum)
	{
		WorkflowDefinition workflow = Definition(prompt);
		string symbol = "_" + new string('s', length - 1);
		string?[] values =
		[
			.. workflow.Arguments.Select(candidate => candidate.Name == "symbolName" ? symbol :
				candidate.Required ? Sample(candidate) : null)
		];

		ToolError error = Assert.Throws<CheatEngineToolException>(() => WorkflowPrompt.Render(null, workflow, values))
			.Error;

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
		Assert.Equal($"symbolName: must be a symbol name of at most {maximum} characters: a letter or underscore, " +
					 "then letters, digits or underscores.", error.Message);
	}

	/// <summary>
	///     Every <c>variableType=N</c> a workflow body passes to a record tool is a code <c>record_create</c> accepts,
	///     so no body proposes a record the tool refuses, such as a UTF-16 string record (7).
	/// </summary>
	[Fact]
	public void Bodies_RecordVariableTypes_AreCodesRecordCreateAccepts()
	{
		List<string> codes = [];
		foreach (WorkflowDefinition workflow in CheatEngineWorkflows.All)
		{
			foreach (Match match in RecordVariableType().Matches(WorkflowPrompt.Body(workflow.Workflow)))
			{
				string code = match.Groups["code"].Value;
				Exception? refusal = Record.Exception(() => RecordArguments.Type(
					(VariableType) int.Parse(code, CultureInfo.InvariantCulture), "variableType"));

				Assert.True(refusal is null,
					$"{workflow.Prompt} passes variableType={code}, which record_create refuses: {refusal?.Message}");
				codes.Add(code);
			}
		}

		Assert.Contains("2", codes);
		Assert.Contains("11", codes);
	}

	/// <summary>
	///     Completes <c>explain_error.tool</c> with an empty prefix and with the prefix <c>MEMORY_</c> through a served
	///     host.
	/// </summary>
	/// <param name="client">A client of the host, which serves the workflow prompts.</param>
	/// <returns>Both completions.</returns>
	internal static async Task<(Completion All, Completion Memory)> CompleteToolAsync(McpClient client)
	{
		PromptReference reference = new()
		{
			Name = "explain_error"
		};
		CompleteResult all = await client.CompleteAsync(reference, "tool", string.Empty,
			cancellationToken: TestContext.Current.CancellationToken);
		CompleteResult memory = await client.CompleteAsync(reference, "tool", "MEMORY_",
			cancellationToken: TestContext.Current.CancellationToken);
		return (all.Completion, memory.Completion);
	}

	/// <summary>
	///     Asserts the <see cref="CompleteToolAsync" /> completions: the first
	///     <see cref="McpCompletions.MaximumValues" /> frozen tool names in ordinal order with every name counted and
	///     <c>hasMore</c>, then every <c>memory_</c> name.
	/// </summary>
	internal static void AssertToolNameCompletions(Completion all, Completion memory)
	{
		string[] names = [.. CheatEngineToolNames.All.Order(StringComparer.Ordinal)];
		string[] memoryNames =
			[.. names.Where(static name => name.StartsWith("memory_", StringComparison.Ordinal))];

		Assert.True(names.Length > McpCompletions.MaximumValues);
		Assert.Equal(names.Take(McpCompletions.MaximumValues), all.Values);
		Assert.Equal((names.Length, true), (all.Total, all.HasMore));
		Assert.Contains(CheatEngineToolNames.MemoryWrite, memoryNames);
		Assert.Equal(memoryNames, memory.Values);
		Assert.Equal(memoryNames.Length, memory.Total);
		Assert.NotEqual(true, memory.HasMore);
	}

	/// <summary>Every prompt argument's <see cref="DisplayAttribute" /> name as <c>prompt.argument=title</c>.</summary>
	internal static IEnumerable<string> DisplayNames()
	{
		return PromptMethods().SelectMany(static method => method.GetParameters()
				.Where(static parameter => parameter.ParameterType == typeof(string))
				.Select(parameter => $"{method.GetCustomAttribute<McpServerPromptAttribute>()!.Name}." +
									 $"{parameter.Name}={parameter.GetCustomAttribute<DisplayAttribute>()?.GetName()}"))
			.Order(StringComparer.Ordinal);
	}

	/// <summary>Every listed prompt argument's title, as <c>prompt.argument=title</c>.</summary>
	internal static IEnumerable<string> ListedTitles(IEnumerable<Prompt> prompts)
	{
		return prompts.SelectMany(static prompt => (prompt.Arguments ?? [])
				.Select(argument => $"{prompt.Name}.{argument.Name}={argument.Title}"))
			.Order(StringComparer.Ordinal);
	}

	private static IEnumerable<MethodInfo> PromptMethods()
	{
		return typeof(CheatEngineWorkflowPrompts).GetMethods(BindingFlags.Public | BindingFlags.Static)
			.Where(static method => method.GetCustomAttribute<McpServerPromptAttribute>() is not null);
	}

	[GeneratedRegex(@"asm_generate_injection\([^)]*symbolName=""\{(?<name>[A-Za-z]+)\}(?<suffix>[A-Za-z0-9_]*)""",
		RegexOptions.CultureInvariant)]
	private static partial Regex InjectionSymbol();

	[GeneratedRegex("^record_create variableType (?<code>[0-9]+)(?<unicode> with unicode true)?$",
		RegexOptions.CultureInvariant)]
	private static partial Regex RecordTypeCode();

	[GeneratedRegex("variableType=(?<code>[0-9]+)", RegexOptions.CultureInvariant)]
	private static partial Regex RecordVariableType();

	[GeneratedRegex(@"memory_read(?:\.size\b|\([^)]*\bsize=)", RegexOptions.CultureInvariant)]
	private static partial Regex MemoryReadSize();

	[GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
	private static partial Regex WhiteSpaceRun();
}
