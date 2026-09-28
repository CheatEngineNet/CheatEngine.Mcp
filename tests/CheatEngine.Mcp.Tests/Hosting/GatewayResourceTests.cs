using System.ComponentModel;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Prompts.Workflows;
using CheatEngine.Mcp.Resources.Knowledge;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Runtime;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Hosting;

/// <summary>
///     The gateway's resources, prompts and completions: Local documents and prompts served without any backend, the
///     instance list, and routed live resources proved with both a probe template and the composed module projection.
/// </summary>
public sealed class GatewayResourceTests
{
	private const string RoutedTemplate = "cheatengine://instances/{instanceId}/probes/{name}";
	private const string RoutedModules = "cheatengine://instances/{instanceId}/modules{?offset,limit}";

	/// <summary>Every composed live template in its backend form, after <c>cheatengine://instance/</c>.</summary>
	private static readonly string[] LivePaths =
	[
		"debugger", "debugger/breakpoints{?limit}", "disassembly/{address}{?count}", "jobs",
		"memory/{address}{?size}", "modules/{module}", "modules/{module}/exports{?offset,limit}",
		"modules{?offset,limit}", "patches", "pointer-maps", "pointer-scans",
		"pointer-scans/{scanName}/paths{?offset,limit}", "process", "records/{recordId}", "records{?offset,limit}",
		"regions{?offset,limit}", "resources", "runtime", "scanners", "scanners/{scannerName}", "speedhack",
		"structures/{structure}{?offset,limit}", "structures{?offset,limit}", "symbols{?offset,limit}", "threads"
	];

	private static readonly CheatEngineMcpPrimitive[] RoutedProbe =
	[
		new(CheatEngineMcpPrimitiveKind.Resource, typeof(RoutedProbeResource))
	];

	[Fact]
	public async Task ListResources_WithoutInstances_ServesTheDocumentsAndTheInstanceList()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using McpClient client = await gateway.ConnectAsync();

		ListResourcesResult resources = await client.ListResourcesAsync(new ListResourcesRequestParams(),
			TestContext.Current.CancellationToken);
		ListResourceTemplatesResult templates = await client.ListResourceTemplatesAsync(
			new ListResourceTemplatesRequestParams(), TestContext.Current.CancellationToken);

		// The gateway's own entry comes first; the SDK appends the Local documents in its collection order.
		Assert.Equal(McpResourceUris.GatewayInstances, resources.Resources[0].Uri);
		Assert.Equal(CheatEngineKnowledge.DocumentSlugs.Select(McpResourceUris.Doc).Order(StringComparer.Ordinal),
			resources.Resources.Skip(1).Select(static resource => resource.Uri).Order(StringComparer.Ordinal));
		Assert.All(resources.Resources.Skip(1), static resource => Assert.True(resource.Size > 0));
		Assert.Equal(
		[
			"cheatengine://docs/workflows/{workflow}",
			.. LivePaths.Select(static path => "cheatengine://instances/{instanceId}/" + path)
		], templates.ResourceTemplates.Select(static template => template.UriTemplate).Order(StringComparer.Ordinal));
		Assert.Equal(TimeSpan.Zero, resources.TimeToLive);
	}

	[Fact]
	public async Task ReadResourceAndGetPrompt_Local_AreServedWithoutAnyBackend()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using McpClient client = await gateway.ConnectAsync();

		ReadResourceResult safety = await client.ReadResourceAsync(McpResourceUris.Doc("safety"),
			cancellationToken: TestContext.Current.CancellationToken);
		IList<McpClientPrompt> prompts =
			await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);
		GetPromptResult prompt = await client.GetPromptAsync("find_writer",
			new Dictionary<string, object?> { ["address"] = "game.exe+10" },
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(CheatEngineKnowledge.ReadDocument("safety"),
			Assert.IsType<TextResourceContents>(Assert.Single(safety.Contents)).Text);
		Assert.Equal(CheatEngineWorkflows.All.Count, prompts.Count);
		Assert.Contains(CheatEngineToolNames.InstanceList,
			Assert.IsType<TextContentBlock>(prompt.Messages[0].Content).Text, StringComparison.Ordinal);
		ResourceLinkBlock link = Assert.IsType<ResourceLinkBlock>(prompt.Messages[1].Content);
		Assert.Equal("Debugger methods", link.Title);
	}

	[Fact]
	public async Task ListResourceTemplates_RoutedLiveTemplate_IsTheBackendTemplateWithThePrefixSwapped()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(extraPrimitives: RoutedProbe);
		await using McpClient client = await gateway.ConnectAsync();

		IList<McpClientResourceTemplate> templates =
			await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);

		ResourceTemplate routed = Assert.Single(templates.Select(static template => template.ProtocolResourceTemplate),
			static template => template.UriTemplate == RoutedTemplate);
		Assert.Equal("instance_probe", routed.Name);
		Assert.Equal("Live probe", routed.Title);
		Assert.Equal(McpResourceUris.JsonMimeType, routed.MimeType);
		Assert.Contains(templates, static template =>
			template.UriTemplate == "cheatengine://docs/workflows/{workflow}");
	}

	[Fact]
	public async Task ListResourceTemplates_LiveTemplates_KeepTheirAnnotationsAndSourceTool()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using McpClient client = await gateway.ConnectAsync();

		IList<McpClientResourceTemplate> templates =
			await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);

		ResourceTemplate[] routed =
		[
			.. templates.Select(static template => template.ProtocolResourceTemplate)
				.Where(static template => template.UriTemplate.StartsWith(McpResourceUris.GatewayInstancesPrefix,
					StringComparison.Ordinal))
		];
		Assert.Equal(LivePaths.Length, routed.Length);
		Assert.All(routed, static template =>
		{
			Assert.Equal([Role.Assistant], template.Annotations!.Audience);
			Assert.Equal(0.3f, template.Annotations.Priority);
			Assert.Contains(template.Meta![McpSourceToolAttribute.MetaKey]!.GetValue<string>(),
				(IEnumerable<string>) CheatEngineToolNames.Backend);
		});
		ResourceTemplate modules = Assert.Single(routed, static template => template.UriTemplate == RoutedModules);
		Assert.Equal(CheatEngineToolNames.ModuleList,
			modules.Meta![McpSourceToolAttribute.MetaKey]!.GetValue<string>());
		Assert.Equal("instance_modules", modules.Name);
	}

	[Fact]
	public async Task ReadResource_QueryTemplate_ForwardsTheUriVerbatimAndRewritesItsContentUri()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "paged");
		await using McpClient client = await gateway.ConnectAsync();
		string prefix = $"cheatengine://instances/{backend.Descriptor.InstanceId}/";

		ReadResourceResult page = await client.ReadResourceAsync(prefix + "modules?offset=0&limit=1",
			cancellationToken: TestContext.Current.CancellationToken);
		ReadResourceResult limitOnly = await client.ReadResourceAsync(prefix + "regions?limit=5",
			cancellationToken: TestContext.Current.CancellationToken);
		ReadResourceResult encoded = await client.ReadResourceAsync(prefix + "memory/game.exe%2B10?size=16",
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(
		[
			"cheatengine://instance/modules?offset=0&limit=1", "cheatengine://instance/regions?limit=5",
			"cheatengine://instance/memory/game.exe%2B10?size=16"
		], backend.ReadUris);
		Assert.Equal(prefix + "modules?offset=0&limit=1",
			Assert.IsType<TextResourceContents>(page.Contents[0]).Uri);
		Assert.Equal(prefix + "regions?limit=5", Assert.IsType<TextResourceContents>(limitOnly.Contents[0]).Uri);
		Assert.Equal(prefix + "memory/game.exe%2B10?size=16",
			Assert.IsType<TextResourceContents>(encoded.Contents[0]).Uri);
	}

	[Theory]
	[InlineData("2025-06-18", McpErrorCode.ResourceNotFound)]
	[InlineData("2026-07-28", McpErrorCode.InvalidParams)]
	public async Task ReadResource_QueryOutOfOrderOrUnknown_IsNotFoundWithoutContactingTheBackend(string version,
		McpErrorCode code)
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "strict");
		await using McpClient client = await gateway.ConnectAsync(version);
		string prefix = $"cheatengine://instances/{backend.Descriptor.InstanceId}/";

		foreach (string path in new[] { "modules?limit=1&offset=0", "modules?foo=1", "runtime?offset=0" })
		{
			McpProtocolException failure = await ReadFailureAsync(client, prefix + path);

			Assert.Equal(code, failure.ErrorCode);
			Assert.Equal("not_found", failure.Data["kind"]);
			Assert.Contains("query parameters must follow the template's order",
				Assert.IsType<string>(failure.Data["hint"]), StringComparison.Ordinal);
		}

		Assert.Equal(0, backend.IdentityProbeCount);
		Assert.Empty(backend.ReadUris);
	}

	[Fact]
	public async Task ReadResource_TwoInstances_RoutesToTheNamedBackendAndRewritesContentUris()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(extraPrimitives: RoutedProbe);
		await using FakeBackend first = await FakeBackend.StartAsync(gateway.Registry, "first");
		await using FakeBackend second = await FakeBackend.StartAsync(gateway.Registry, "second");
		await using McpClient client = await gateway.ConnectAsync();
		string uri = $"cheatengine://instances/{second.Descriptor.InstanceId}/probes/alpha";

		ReadResourceResult result = await client.ReadResourceAsync(uri,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(["cheatengine://instance/probes/alpha"], second.ReadUris);
		Assert.Empty(first.ReadUris);
		TextResourceContents[] contents = [.. result.Contents.Cast<TextResourceContents>()];
		Assert.Equal(uri, contents[0].Uri);
		Assert.Equal(second.Descriptor.InstanceId, JsonNode.Parse(contents[0].Text)!["backend"]!.GetValue<string>());
		Assert.Equal($"cheatengine://instances/{second.Descriptor.InstanceId}/probes/nested", contents[1].Uri);
	}

	[Fact]
	public async Task ReadResource_LiveModule_UsesTheComposedPrefixAndVerifiesTheNamedInstance()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "modules");
		await using McpClient client = await gateway.ConnectAsync();
		string uri = $"cheatengine://instances/{backend.Descriptor.InstanceId}/modules";

		ReadResourceResult result = await client.ReadResourceAsync(uri,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(["cheatengine://instance/modules"], backend.ReadUris);
		Assert.Equal(1, backend.IdentityProbeCount);
		TextResourceContents contents = Assert.IsType<TextResourceContents>(result.Contents[0]);
		Assert.Equal(uri, contents.Uri);
		Assert.Equal(backend.Descriptor.InstanceId, JsonNode.Parse(contents.Text)!["backend"]!.GetValue<string>());
	}

	[Fact]
	public async Task ReadResource_UnknownOrStaleInstance_IsRefusedWithoutForwarding()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(extraPrimitives: RoutedProbe);
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "stale");
		await using McpClient client = await gateway.ConnectAsync();
		backend.ReportedIdentity = backend.Descriptor with { ActivationId = Guid.NewGuid() };

		McpProtocolException unknown = await ReadFailureAsync(client,
			"cheatengine://instances/ce-404-00000000000000000000000000000000/probes/alpha");
		McpProtocolException stale = await ReadFailureAsync(client,
			$"cheatengine://instances/{backend.Descriptor.InstanceId}/probes/alpha");

		foreach (McpProtocolException failure in new[] { unknown, stale })
		{
			Assert.Equal(McpErrorCode.InternalError, failure.ErrorCode);
			Assert.Equal("instance_unavailable", failure.Data["kind"]);
			Assert.Equal("not_started", failure.Data["hostEffect"]);
			Assert.Contains(CheatEngineToolNames.InstanceList, Assert.IsType<string>(failure.Data["hint"]),
				StringComparison.Ordinal);
		}

		Assert.Empty(backend.ReadUris);
		Assert.Equal(0, backend.InitializeCount);
	}

	[Theory]
	[InlineData("2025-06-18", McpErrorCode.ResourceNotFound)]
	[InlineData("2026-07-28", McpErrorCode.InvalidParams)]
	public async Task ReadResource_UnknownPathOrBackendForm_IsNotFoundWithoutContactingTheBackend(string version,
		McpErrorCode code)
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(extraPrimitives: RoutedProbe);
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "untouched");
		await using McpClient client = await gateway.ConnectAsync(version);

		McpProtocolException unknownPath = await ReadFailureAsync(client,
			$"cheatengine://instances/{backend.Descriptor.InstanceId}/nothing/here");
		McpProtocolException backendForm = await ReadFailureAsync(client, "cheatengine://instance/probes/alpha");
		McpProtocolException malformed = await ReadFailureAsync(client, "cheatengine://instances/not-an-id/probes/a");
		McpProtocolException trailingList = await ReadFailureAsync(client, McpResourceUris.GatewayInstancesPrefix);

		foreach (McpProtocolException failure in new[] { unknownPath, backendForm, malformed, trailingList })
		{
			Assert.Equal(code, failure.ErrorCode);
			Assert.Equal("not_found", failure.Data["kind"]);
		}

		Assert.Contains("cheatengine://instances/{instanceId}/", Assert.IsType<string>(backendForm.Data["hint"]),
			StringComparison.Ordinal);
		Assert.Equal(0, backend.IdentityProbeCount);
		Assert.Empty(backend.ReadUris);
	}

	[Fact]
	public async Task ReadResource_BackendProtocolError_KeepsItsDataAndChoosesTheUpstreamNotFoundCode()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(extraPrimitives: RoutedProbe);
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "missing");
		await using McpClient client = await gateway.ConnectAsync("2026-07-28");
		backend.RespondToRead = static _ => throw McpResourceErrors.Create(
			CheatEngineToolException.NotFound("No module named game.exe.", "List the modules first.").Error,
			"2025-06-18");

		McpProtocolException failure = await ReadFailureAsync(client,
			$"cheatengine://instances/{backend.Descriptor.InstanceId}/probes/game.exe");

		Assert.Equal(McpErrorCode.InvalidParams, failure.ErrorCode);
		Assert.Equal("not_found", failure.Data["kind"]);
		Assert.Equal("List the modules first.", failure.Data["hint"]);
		Assert.DoesNotContain("remote", failure.Message.Replace("Request failed (remote): ", string.Empty,
			StringComparison.Ordinal), StringComparison.Ordinal);
		Assert.Equal(["cheatengine://instance/probes/game.exe"], backend.ReadUris);
	}

	[Fact]
	public async Task ReadResource_Instances_ListsVerifiedIdsWithoutTokensOrEndpoints()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "listed");
		await using McpClient client = await gateway.ConnectAsync();

		ReadResourceResult result = await client.ReadResourceAsync(McpResourceUris.GatewayInstances,
			cancellationToken: TestContext.Current.CancellationToken);

		TextResourceContents contents = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
		Assert.Equal(McpResourceUris.GatewayInstances, contents.Uri);
		Assert.Equal(McpResourceUris.JsonMimeType, contents.MimeType);
		Assert.Equal(backend.Descriptor.InstanceId,
			JsonNode.Parse(contents.Text)!["instances"]![0]!["instanceId"]!.GetValue<string>());
		Assert.DoesNotContain(backend.Descriptor.AccessToken, contents.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(backend.Descriptor.Endpoint, contents.Text, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Complete_InstanceId_OffersOnlyRecentlyVerifiedIdsByPrefix()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(extraPrimitives: RoutedProbe);
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "completed");
		await using McpClient client = await gateway.ConnectAsync();
		ResourceTemplateReference reference = new() { Uri = RoutedTemplate };

		CompleteResult beforeDiscovery = await client.CompleteAsync(reference, "instanceId", "ce-",
			cancellationToken: TestContext.Current.CancellationToken);
		await client.CallToolAsync(CheatEngineToolNames.InstanceList, new Dictionary<string, object?>(),
			cancellationToken: TestContext.Current.CancellationToken);
		CompleteResult afterDiscovery = await client.CompleteAsync(reference, "instanceId", "ce-",
			cancellationToken: TestContext.Current.CancellationToken);
		CompleteResult otherPrefix = await client.CompleteAsync(reference, "instanceId", "zz",
			cancellationToken: TestContext.Current.CancellationToken);
		CompleteResult otherArgument = await client.CompleteAsync(reference, "name", "a",
			cancellationToken: TestContext.Current.CancellationToken);
		CompleteResult queryTemplate = await client.CompleteAsync(new ResourceTemplateReference { Uri = RoutedModules },
			"instanceId", "ce-", cancellationToken: TestContext.Current.CancellationToken);
		CompleteResult queryArgument = await client.CompleteAsync(new ResourceTemplateReference { Uri = RoutedModules },
			"limit", "1", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Empty(beforeDiscovery.Completion.Values);
		Assert.Equal([backend.Descriptor.InstanceId], afterDiscovery.Completion.Values);
		Assert.Equal(1, afterDiscovery.Completion.Total);
		Assert.Empty(otherPrefix.Completion.Values);
		Assert.Empty(otherArgument.Completion.Values);
		Assert.Equal([backend.Descriptor.InstanceId], queryTemplate.Completion.Values);
		Assert.Empty(queryArgument.Completion.Values);
		Assert.Empty(backend.ReadUris);
	}

	[Fact]
	public async Task Complete_InstanceId_ExcludesAnInstanceWhoseRegistryRecordWasWithdrawn()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(extraPrimitives: RoutedProbe);
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "withdrawn");
		await using McpClient client = await gateway.ConnectAsync();
		ResourceTemplateReference reference = new() { Uri = RoutedTemplate };

		await client.CallToolAsync(CheatEngineToolNames.InstanceList, new Dictionary<string, object?>(),
			cancellationToken: TestContext.Current.CancellationToken);
		File.Delete(Path.Combine(gateway.Registry.DirectoryPath,
			backend.Descriptor.ActivationId.ToString("N") + ".json"));

		CompleteResult completion = await client.CompleteAsync(reference, "instanceId", "ce-",
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.DoesNotContain(backend.Descriptor.InstanceId, completion.Completion.Values);
		Assert.Empty(completion.Completion.Values);
	}

	[Fact]
	public async Task Complete_LocalPromptAndWorkflowTemplate_AreServedFromTheirAllowedValues()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using McpClient client = await gateway.ConnectAsync();

		CompleteResult prompt = await client.CompleteAsync(new PromptReference { Name = "find_writer" },
			"trigger", "a", cancellationToken: TestContext.Current.CancellationToken);
		CompleteResult workflow = await client.CompleteAsync(
			new ResourceTemplateReference { Uri = "cheatengine://docs/workflows/{workflow}" }, "workflow", "nop",
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(["access"], prompt.Completion.Values);
		Assert.Equal(["nop-patch"], workflow.Completion.Values);
	}

	[Fact]
	public async Task CallTool_BackendResourceLinks_AreRewrittenToGatewayUris()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "links");
		await using McpClient client = await gateway.ConnectAsync();
		backend.Respond = static () => new CallToolResult
		{
			Content =
			[
				new ResourceLinkBlock
				{
					Uri = "cheatengine://instance/modules/game.exe",
					Name = "instance_module",
					MimeType = McpResourceUris.JsonMimeType
				},
				new ResourceLinkBlock
				{
					Uri = McpResourceUris.Doc("pointers"),
					Name = "doc_pointers",
					MimeType = McpResourceUris.MarkdownMimeType
				},
				new EmbeddedResourceBlock
				{
					Resource = new TextResourceContents { Uri = "cheatengine://instance/runtime", Text = "{}" }
				}
			]
		};

		CallToolResult result = await client.CallToolAsync("runtime_get_info", backend.RoutedArguments(),
			cancellationToken: TestContext.Current.CancellationToken);

		string prefix = $"cheatengine://instances/{backend.Descriptor.InstanceId}/";
		Assert.Equal(prefix + "modules/game.exe", Assert.IsType<ResourceLinkBlock>(result.Content[0]).Uri);
		Assert.Equal(McpResourceUris.Doc("pointers"), Assert.IsType<ResourceLinkBlock>(result.Content[1]).Uri);
		Assert.Equal(prefix + "runtime", Assert.IsType<EmbeddedResourceBlock>(result.Content[2]).Resource.Uri);
	}

	private static async Task<McpProtocolException> ReadFailureAsync(McpClient client, string uri)
	{
		return await Assert.ThrowsAsync<McpProtocolException>(async () =>
			await client.ReadResourceAsync(uri, cancellationToken: TestContext.Current.CancellationToken));
	}

	/// <summary>A live template the gateway routes; it is never constructed or invoked at the gateway.</summary>
	[McpServerResourceType]
	public sealed class RoutedProbeResource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/probes/{name}", Name = "instance_probe",
			Title = "Live probe", MimeType = "application/json")]
		[McpSourceTool(typeof(RuntimeTools), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("A live probe resource.")]
		public string Read(string name)
		{
			return $"{{\"name\":\"{name}\",\"type\":\"{GetType().Name}\"}}";
		}
	}
}
