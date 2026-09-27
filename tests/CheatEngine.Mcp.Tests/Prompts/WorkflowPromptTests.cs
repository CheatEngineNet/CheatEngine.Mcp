using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Prompts;
using CheatEngine.Mcp.Prompts.Workflows;
using CheatEngine.Mcp.Resources.Knowledge;
using CheatEngine.Mcp.Tests.Resources;
using CheatEngine.Mcp.Tests.Support;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Prompts;

/// <summary>The 22 guided workflow prompts: names, bodies, links, arguments, budget and cited tools.</summary>
public sealed partial class WorkflowPromptTests
{
	private static readonly string[] PlannedPrompts =
	[
		"attach_and_orient", "find_known_value", "find_unknown_value", "find_float_value", "freeze_value",
		"find_writer", "nop_patch", "manual_pointer_chain", "pointer_scan", "aob_injection", "shared_code_filter",
		"injection_copy_base", "make_aob_signature", "dissect_structure", "trace_logic", "unity_mono_recon",
		"dotnet_recon", "speedhack", "build_robust_table", "repair_after_update", "cleanup_session",
		"ce_tutorial_walkthrough"
	];

	/// <summary>A value for every required argument, so each prompt renders.</summary>
	private static readonly Dictionary<string, string> RequiredSamples = new(StringComparer.Ordinal)
	{
		["address"] = "7FF6A1B2C3D0",
		["baseAddress"] = "game.exe+1A2B40",
		["instructionAddress"] = "game.exe+1A2B",
		["currentValue"] = "100",
		["valueType"] = "int32",
		["goal"] = "double the damage dealt",
		["speed"] = "0.5",
		["step"] = "2"
	};

	/// <summary>Snake-case identifiers in bodies that name something other than a tool.</summary>
	private static readonly HashSet<string> NonToolIdentifiers = new(StringComparer.Ordinal)
	{
		// A symbol CE's speedhack registers in the target.
		"speedhack_wantedspeed"
	};

	[Fact]
	public void Prompts_Names_AreTheTwentyTwoPlannedWorkflows()
	{
		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(KnowledgeResourceTests.KnowledgeManifest);

		Assert.Equal(PlannedPrompts.Order(StringComparer.Ordinal), catalog.Prompts.Select(static prompt => prompt.Name));
		Assert.Equal(PlannedPrompts, CheatEngineWorkflows.All.Select(static workflow => workflow.Prompt));
	}

	[Fact]
	public void Prompts_BijectWithTheEmbeddedWorkflowBodies()
	{
		Assert.All(CheatEngineWorkflows.All, static workflow =>
			Assert.Equal(workflow.Prompt.Replace('_', '-'), workflow.Workflow));
		Assert.Equal(CheatEngineWorkflows.All.Select(static workflow => workflow.Workflow).Order(StringComparer.Ordinal),
			WorkflowPrompt.EmbeddedWorkflows.Order(StringComparer.Ordinal));
		Assert.Equal(CheatEngineKnowledge.WorkflowNames, WorkflowPrompt.EmbeddedWorkflows.Order(StringComparer.Ordinal));
		Assert.All(CheatEngineWorkflows.All, static workflow =>
			Assert.Equal(CheatEngineKnowledge.TryReadWorkflow(workflow.Workflow, out string? served) ? served : null,
				WorkflowPrompt.Body(workflow.Workflow)));
	}

	[Fact]
	public void Prompts_Titles_AreTheWorkflowHeadings()
	{
		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(KnowledgeResourceTests.KnowledgeManifest);

		foreach (WorkflowDefinition workflow in CheatEngineWorkflows.All)
		{
			Prompt prompt = Assert.Single(catalog.Prompts, candidate => candidate.Name == workflow.Prompt);
			Assert.Equal(KnowledgeResourceTests.Heading(WorkflowPrompt.Body(workflow.Workflow)), prompt.Title);
			Assert.False(string.IsNullOrWhiteSpace(prompt.Description));
			Assert.All(prompt.Arguments ?? [], static argument =>
				Assert.False(string.IsNullOrWhiteSpace(argument.Description)));
		}
	}

	[Fact]
	public void Prompts_LinkedDocuments_FollowTheWorkflowIndex()
	{
		string index = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "skills", "cheatengine-mcp", "references",
			"workflows.md"));
		Dictionary<string, string[]> rows = IndexRow().Matches(index).ToDictionary(
			static row => row.Groups["prompt"].Value,
			static row => DocLink().Matches(row.Groups["docs"].Value).Select(static link => link.Groups["slug"].Value)
				.ToArray(), StringComparer.Ordinal);

		Assert.Equal(PlannedPrompts.Order(StringComparer.Ordinal), rows.Keys.Order(StringComparer.Ordinal));
		Assert.All(CheatEngineWorkflows.All, workflow => Assert.Equal(rows[workflow.Prompt], workflow.Documents));
		Assert.All(CheatEngineWorkflows.All.SelectMany(static workflow => workflow.Documents),
			static slug => Assert.Contains(slug, CheatEngineKnowledge.DocumentSlugs));
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

	[Fact]
	public void Prompts_CitedParameters_ExistInTheServedV2Schemas()
	{
		// Only v2 tools the composition serves can be checked; the rest are verified as their batches land.
		Dictionary<string, HashSet<string>> schemas = McpPrimitiveCatalog.Create(TestComposition.BackendManifest).Tools
			.Where(static tool => !McpContractRules.IsLegacy(tool))
			.ToDictionary(static tool => tool.Name, static tool => Properties(tool.InputSchema), StringComparer.Ordinal);
		foreach (WorkflowDefinition workflow in CheatEngineWorkflows.All)
		{
			foreach (Match call in ToolCall().Matches(WorkflowPrompt.Body(workflow.Workflow)))
			{
				if (!schemas.TryGetValue(call.Groups["tool"].Value, out HashSet<string>? properties))
				{
					continue;
				}

				foreach (string parameter in TopLevelParameters(call.Groups["arguments"].Value))
				{
					Assert.True(properties.Contains(parameter),
						$"{workflow.Workflow} passes '{parameter}' to {call.Groups["tool"].Value}, which has no such " +
						"parameter.");
				}
			}
		}
	}

	[Theory]
	[InlineData("find_writer", "trigger", "read")]
	[InlineData("find_writer", "size", "3")]
	[InlineData("speedhack", "speed", "0")]
	[InlineData("speedhack", "speed", "fast")]
	[InlineData("pointer_scan", "maxOffset", "-4")]
	[InlineData("injection_copy_base", "symbolName", "player base")]
	[InlineData("find_known_value", "description", "two\nlines")]
	[InlineData("ce_tutorial_walkthrough", "tutorial", "gtutorial")]
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
			foreach (PromptArgument argument in (prompt.Arguments ?? []).Where(static argument => argument.Required == true))
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

		Assert.Equal(16, checkedArguments);
	}

	[Fact]
	public async Task Prompts_EveryAllowedValue_RendersAndAnythingElseIsRefused()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);
		int checkedValues = 0;
		foreach (MethodInfo method in typeof(CheatEngineWorkflowPrompts).GetMethods(BindingFlags.Public |
					 BindingFlags.Static))
		{
			string prompt = method.GetCustomAttribute<McpServerPromptAttribute>()!.Name!;
			foreach (ParameterInfo parameter in method.GetParameters())
			{
				if (parameter.GetCustomAttribute<AllowedValuesAttribute>() is not { } allowed)
				{
					continue;
				}

				foreach (string value in allowed.Values.Cast<string>())
				{
					// gtutorial only has levels 1 to 3.
					Dictionary<string, object?> arguments = new(StringComparer.Ordinal)
					{
						[parameter.Name!] = value
					};
					if (prompt == "ce_tutorial_walkthrough" && parameter.Name == "tutorial")
					{
						arguments["step"] = "1";
					}

					await GetAsync(pipeline, prompt, arguments);
					checkedValues++;
				}

				await Assert.ThrowsAsync<McpProtocolException>(async () => await GetAsync(pipeline, prompt,
					new Dictionary<string, object?>(StringComparer.Ordinal) { [parameter.Name!] = "not-a-value" }));
			}
		}

		Assert.True(checkedValues > 40, $"Only {checkedValues} allowed values were rendered.");
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

		Assert.Equal(["access"], trigger.Completion.Values);
		Assert.Equal(["yes", "no"], keep.Completion.Values);
	}

	[Fact]
	public async Task Prompts_Get_RendersPreambleInputsBodyAndServedDocumentLinks()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeResourceTests.KnowledgeManifest, McpPrimitiveBinding.Catalog);

		GetPromptResult result = await GetAsync(pipeline, "find_writer",
			new Dictionary<string, object?> { ["trigger"] = "ACCESS" });

		string text = Assert.IsType<TextContentBlock>(result.Messages[0].Content).Text;
		Assert.StartsWith("# Find what writes or accesses an address\n\n" + WorkflowPrompt.Preamble, text,
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
			Assert.False(string.IsNullOrWhiteSpace(link.Description));
		});
	}

	[Fact]
	public async Task Prompts_WithoutTheResources_LinkByUriAndNameOnly()
	{
		CheatEngineMcpPrimitiveOptions manifest =
			CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog, static builder => builder.AddPrompts());
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync(manifest, McpPrimitiveBinding.Catalog);

		GetPromptResult result = await GetAsync(pipeline, "speedhack", new Dictionary<string, object?>());

		ResourceLinkBlock link = Assert.IsType<ResourceLinkBlock>(result.Messages[1].Content);
		Assert.Equal("cheatengine://docs/speedhack", link.Uri);
		Assert.Equal("doc_speedhack", link.Name);
		Assert.Null(link.Title);
		Assert.Null(link.Size);
	}

	private static async Task<GetPromptResult> GetAsync(TestMcpPipeline pipeline, string prompt,
		Dictionary<string, object?> arguments)
	{
		McpPrimitiveCatalog catalog = PromptCatalog.Value;
		foreach (PromptArgument argument in catalog.Prompts.Single(candidate => candidate.Name == prompt).Arguments ?? [])
		{
			if (argument.Required == true && !arguments.ContainsKey(argument.Name))
			{
				arguments[argument.Name] = RequiredSamples[argument.Name];
			}
		}

		return await pipeline.Client.GetPromptAsync(prompt, arguments,
			cancellationToken: TestContext.Current.CancellationToken);
	}

	private static readonly Lazy<McpPrimitiveCatalog> PromptCatalog =
		new(static () => McpPrimitiveCatalog.Create(KnowledgeResourceTests.KnowledgeManifest));

	private static int RenderedLength(GetPromptResult result)
	{
		return result.Messages.Sum(static message => message.Content switch
		{
			TextContentBlock text => text.Text.Length,
			ResourceLinkBlock link => link.Uri.Length + link.Name.Length + (link.Title?.Length ?? 0) +
									  (link.Description?.Length ?? 0),
			_ => 0
		});
	}

	private static HashSet<string> Properties(JsonElement schema)
	{
		return schema.TryGetProperty("properties", out JsonElement properties)
			? properties.EnumerateObject().Select(static property => property.Name).ToHashSet(StringComparer.Ordinal)
			: [];
	}

	/// <summary>The <c>name=</c> arguments at the top level of a cited call, outside nested lists and objects.</summary>
	private static IEnumerable<string> TopLevelParameters(string arguments)
	{
		int depth = 0;
		int start = 0;
		for (int index = 0; index <= arguments.Length; index++)
		{
			char character = index < arguments.Length ? arguments[index] : ',';
			depth += character switch
			{
				'[' or '{' or '(' => 1,
				']' or '}' or ')' => -1,
				_ => 0
			};
			if (character == ',' && depth == 0)
			{
				string part = arguments[start..index].Trim();
				int equals = part.IndexOf('=', StringComparison.Ordinal);
				if (equals > 0 && part[..equals].All(char.IsAsciiLetterOrDigit))
				{
					yield return part[..equals];
				}

				start = index + 1;
			}
		}
	}

	private static bool IsToolDomain(string segment)
	{
		return McpContractRules.Domains.Contains(segment) || McpContractRules.GatewayDomains.Contains(segment);
	}

	[GeneratedRegex("(?<![A-Za-z0-9_.])[a-z][a-z0-9]*(?:_[a-z0-9]+)+(?![A-Za-z0-9_])", RegexOptions.CultureInvariant)]
	private static partial Regex SnakeToken();

	[GeneratedRegex(@"(?<tool>[a-z][a-z0-9]*(?:_[a-z0-9]+)+)\((?<arguments>[^()]*(?:\([^()]*\)[^()]*)*)\)",
		RegexOptions.CultureInvariant)]
	private static partial Regex ToolCall();

	[GeneratedRegex(@"^\| \[(?<prompt>[a-z_]+)\]\(workflows/[a-z-]+\.md\) \|[^|]*\|[^|]*\|(?<docs>[^|]*)\|\s*$",
		RegexOptions.CultureInvariant | RegexOptions.Multiline)]
	private static partial Regex IndexRow();

	[GeneratedRegex(@"\[[^\]]+\]\((?<slug>[a-z0-9-]+)\.md\)", RegexOptions.CultureInvariant)]
	private static partial Regex DocLink();
}
