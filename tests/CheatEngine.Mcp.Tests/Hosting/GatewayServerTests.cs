using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Hosting;

public sealed class GatewayServerTests
{
	private const string Traceparent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

	[Fact]
	public async Task ListTools_WithoutPublishedInstances_ExposesStableRoutedCatalog()
	{
		await using GatewayTestHost host = await GatewayTestHost.StartAsync();
		await using McpClient client = await host.ConnectAsync();

		IList<McpClientTool> tools =
			await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

		// The exact catalog is pinned by the gateway-tools golden; here the live listing must match the composition.
		Assert.Equal(TestComposition.GatewayTools.Select(static tool => tool.Name),
			tools.Select(static tool => tool.Name));
		Assert.Equal(GatewayToolCatalog.InstanceListToolName, tools[0].Name);
		Assert.Single(tools, static tool => tool.Name == GatewayToolCatalog.InstanceListToolName);
		foreach (string name in new[]
				 {
					 "scan_first", "scan_next", "scan_list_results", "scan_reset", "scan_get_status"
				 })
		{
			Tool tool = Assert.Single(TestComposition.GatewayTools, tool => tool.Name == name);
			Assert.Equal("main",
				tool.InputSchema.GetProperty("properties").GetProperty("scannerName").GetProperty("default")
					.GetString());
			Assert.DoesNotContain("scannerName",
				tool.InputSchema.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
		}
	}

	[Fact]
	public async Task CallTool_TwoPublishedInstances_RoutesOnlyToRequestedBackendAndStripsInstanceId()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend first = await FakeBackend.StartAsync(gateway.Registry, "first");
		await using FakeBackend second = await FakeBackend.StartAsync(gateway.Registry, "second");
		await using McpClient client = await gateway.ConnectAsync();

		JsonObject listed = await CallJsonAsync(client, GatewayToolCatalog.InstanceListToolName,
			new Dictionary<string, object?>());
		string listedText = listed.ToJsonString();
		Assert.Contains(first.Descriptor.InstanceId, listedText, StringComparison.Ordinal);
		Assert.Contains(second.Descriptor.InstanceId, listedText, StringComparison.Ordinal);
		Assert.DoesNotContain(first.Descriptor.AccessToken, listedText, StringComparison.Ordinal);
		Assert.DoesNotContain(second.Descriptor.AccessToken, listedText, StringComparison.Ordinal);
		JsonArray entries = listed["instances"]!.AsArray();
		Assert.Equal(first.Descriptor.InstanceId, entries[0]!["instanceId"]!.GetValue<string>());
		Assert.Equal("first", entries[0]!["name"]!.GetValue<string>());
		Assert.Equal(first.Descriptor.ProcessId, entries[0]!["processId"]!.GetValue<int>());

		JsonObject firstResult = await CallJsonAsync(client, "runtime_get_info",
			new Dictionary<string, object?>
			{
				[GatewayToolCatalog.InstanceIdArgumentName] = first.Descriptor.InstanceId,
				["marker"] = "first payload"
			});
		JsonObject secondResult = await CallJsonAsync(client, "runtime_get_info",
			new Dictionary<string, object?>
			{
				[GatewayToolCatalog.InstanceIdArgumentName] = second.Descriptor.InstanceId,
				["marker"] = "second payload"
			});

		Assert.Equal(first.Descriptor.InstanceId, firstResult["backend"]?.GetValue<string>());
		Assert.Equal(second.Descriptor.InstanceId, secondResult["backend"]?.GetValue<string>());
		Assert.Equal("first payload", first.LastArguments["marker"].GetString());
		Assert.Equal("second payload", second.LastArguments["marker"].GetString());
		Assert.DoesNotContain(GatewayToolCatalog.InstanceIdArgumentName, first.LastArguments.Keys);
		Assert.DoesNotContain(GatewayToolCatalog.InstanceIdArgumentName, second.LastArguments.Keys);
		Assert.Equal(1, first.ForwardedCallCount);
		Assert.Equal(1, second.ForwardedCallCount);
	}

	[Fact]
	public async Task CallTool_ReservedUpstreamMeta_IsNotForwarded()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "meta");
		await using McpClient client = await gateway.ConnectAsync();

		CallToolResult result = await client.CallToolAsync(
			new CallToolRequestParams
			{
				Name = "runtime_get_info",
				Arguments = Arguments(backend),
				Meta = new JsonObject
				{
					["io.modelcontextprotocol/related-task"] = new JsonObject { ["taskId"] = "upstream-task" },
					["progressToken"] = "p1",
					["traceparent"] = Traceparent,
					["tracestate"] = "vendor=upstream",
					["baggage"] = "session=upstream",
					["vendor.example/secret"] = "upstream-only"
				}
			}, TestContext.Current.CancellationToken);

		Assert.NotEqual(true, result.IsError);
		JsonObject forwarded = Assert.IsType<JsonObject>(Assert.Single(backend.ReceivedMeta));
		Assert.Equal(["baggage", "traceparent", "tracestate"],
			forwarded.Select(static property => property.Key).Order(StringComparer.Ordinal));
		Assert.Equal(Traceparent, forwarded["traceparent"]!.GetValue<string>());
		Assert.Equal("vendor=upstream", forwarded["tracestate"]!.GetValue<string>());
		Assert.Equal("session=upstream", forwarded["baggage"]!.GetValue<string>());
	}

	[Fact]
	public async Task CallTool_Upstream2026Client_BackendSeesNoForeignProtocolMeta()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "july-2026");
		await using McpClient client = await gateway.ConnectAsync("2026-07-28");
		Assert.Equal("2026-07-28", client.NegotiatedProtocolVersion);

		CallToolResult result = await client.CallToolAsync(
			new CallToolRequestParams
			{
				Name = "runtime_get_info",
				Arguments = Arguments(backend),
				Meta = new JsonObject { ["traceparent"] = Traceparent, ["progressToken"] = "p1" }
			}, TestContext.Current.CancellationToken);

		Assert.NotEqual(true, result.IsError);
		Assert.Equal(backend.Descriptor.InstanceId,
			JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!["backend"]!.GetValue<string>());
		JsonObject forwarded = Assert.IsType<JsonObject>(Assert.Single(backend.ReceivedMeta));
		Assert.Equal(["traceparent"], forwarded.Select(static property => property.Key));
		Assert.Equal(Traceparent, forwarded["traceparent"]!.GetValue<string>());
		// The upstream negotiation never changes the pinned backend hop.
		Assert.Equal(["2025-06-18"], backend.InitializeProtocolVersions);
	}

	[Fact]
	public async Task CallTool_WithoutTraceContext_ForwardsNoMeta()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "no-meta");
		await using McpClient client = await gateway.ConnectAsync();

		await CallJsonAsync(client, "runtime_get_info", ObjectArguments(backend));

		Assert.Null(Assert.Single(backend.ReceivedMeta));
	}

	[Fact]
	public async Task CallTool_CancelledForward_DoesNotRetryAnotherInstance()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend selected = await FakeBackend.StartAsync(gateway.Registry, "selected");
		await using FakeBackend other = await FakeBackend.StartAsync(gateway.Registry, "other");
		await using McpClient client = await gateway.ConnectAsync();
		selected.HangUntilCancelled = true;
		using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));

		Task<CallToolResult> call = client.CallToolAsync("runtime_get_info", ObjectArguments(selected),
			cancellationToken: cancellation.Token).AsTask();
		await selected.WaitUntilForwardedAsync(TestContext.Current.CancellationToken);
		await cancellation.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await call);
		Assert.Equal(1, selected.ForwardedCallCount);
		Assert.Equal(0, other.ForwardedCallCount);
	}

	[Fact]
	public async Task ListTools_Result_CarriesPrivateOneHourCacheHint()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using McpClient client = await gateway.ConnectAsync();

		ListToolsResult result = await client.ListToolsAsync(new ListToolsRequestParams(),
			TestContext.Current.CancellationToken);

		Assert.Equal(TimeSpan.FromHours(1), result.TimeToLive);
		Assert.Equal(CacheScope.Private, result.CacheScope);
		Assert.Equal(GatewayToolCatalog.InstanceListToolName, result.Tools[0].Name);
	}

	[Fact]
	public async Task CallTool_InstanceList_ReturnsTypedStructuredResultWithMatchingText()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "listed");
		await using McpClient client = await gateway.ConnectAsync();

		CallToolResult result = await client.CallToolAsync(GatewayToolCatalog.InstanceListToolName,
			new Dictionary<string, object?>(), cancellationToken: TestContext.Current.CancellationToken);

		Assert.NotEqual(true, result.IsError);
		string structured = result.StructuredContent!.Value.GetRawText();
		Assert.Equal(structured, Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
		Assert.Equal(
			"{\"instances\":[{\"instanceId\":\"" + backend.Descriptor.InstanceId + "\",\"name\":\"listed\"," +
			"\"processId\":" + backend.Descriptor.ProcessId + ",\"pluginVersion\":\"2.0.0\"}]," +
			"\"discoveryIncomplete\":false}", structured);
	}

	[Fact]
	public async Task CallTool_TwoCallsSameInstance_ReuseOneBackendHandshake()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "pooled");
		await using McpClient client = await gateway.ConnectAsync();

		await CallJsonAsync(client, "runtime_get_info", backend.RoutedArguments());
		await CallJsonAsync(client, "runtime_get_info", backend.RoutedArguments());

		Assert.Equal(2, backend.ForwardedCallCount);
		Assert.Equal(1, backend.InitializeCount);
	}

	[Fact]
	public async Task CallTool_RepublishedRecord_OpensNewBackendClient()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "republished");
		await using McpClient client = await gateway.ConnectAsync();
		await CallJsonAsync(client, "runtime_get_info", backend.RoutedArguments());

		// Same instance and activation, new token: the old client must never be reused.
		backend.RepublishWithNewToken();
		await CallJsonAsync(client, "runtime_get_info", backend.RoutedArguments());

		Assert.Equal(2, backend.ForwardedCallCount);
		Assert.Equal(2, backend.InitializeCount);
		Assert.Equal(1, gateway.Services.GetRequiredService<BackendConnectionPool>().Count);
	}

	[Fact]
	public async Task CallTool_BackendHang_ReturnsTimeoutUnknownAndNeverRetries()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(TimeSpan.FromSeconds(1));
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "hanging");
		await using McpClient client = await gateway.ConnectAsync();
		backend.HangUntilCancelled = true;

		CallToolResult result = await client.CallToolAsync("runtime_get_info", backend.RoutedArguments(),
			cancellationToken: TestContext.Current.CancellationToken);

		ToolError error = AssertEnvelope(result, ToolErrorKind.Timeout, ToolHostEffect.Unknown);
		Assert.False(error.Retryable);
		Assert.Equal("Do not repeat a mutation; inspect state with a read-only tool first.", error.Hint);
		Assert.Equal("runtime_get_info", error.Operation);
		await backend.Cancelled.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
		Assert.Equal(1, backend.ForwardedCallCount);

		// The timed-out client was evicted: the next call connects again and is sent exactly once.
		backend.HangUntilCancelled = false;
		await CallJsonAsync(client, "runtime_get_info", backend.RoutedArguments());
		Assert.Equal(2, backend.ForwardedCallCount);
		Assert.Equal(2, backend.InitializeCount);
	}

	[Fact]
	public async Task CallTool_MissingInstanceId_ReturnsInvalidArgumentEnvelope()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "only");
		await using McpClient client = await gateway.ConnectAsync();

		CallToolResult missing = await client.CallToolAsync("runtime_get_info", new Dictionary<string, object?>(),
			cancellationToken: TestContext.Current.CancellationToken);
		CallToolResult numeric = await client.CallToolAsync("runtime_get_info",
			new Dictionary<string, object?> { [GatewayToolCatalog.InstanceIdArgumentName] = 42 },
			cancellationToken: TestContext.Current.CancellationToken);

		foreach (CallToolResult result in new[] { missing, numeric })
		{
			ToolError error = AssertEnvelope(result, ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted);
			Assert.Equal("runtime_get_info", error.Operation);
			Assert.Equal(GatewayToolCatalog.InstanceIdArgumentName,
				error.Details!.Value.GetProperty("parameter").GetString());
		}

		Assert.Equal(0, backend.ForwardedCallCount);
	}

	[Fact]
	public async Task CallTool_UnknownInstanceId_ReturnsInstanceUnavailableNotStarted()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "only");
		await using McpClient client = await gateway.ConnectAsync();

		CallToolResult result = await client.CallToolAsync("runtime_get_info",
			new Dictionary<string, object?>
			{
				[GatewayToolCatalog.InstanceIdArgumentName] = "ce-404-00000000000000000000000000000000"
			}, cancellationToken: TestContext.Current.CancellationToken);

		ToolError error = AssertEnvelope(result, ToolErrorKind.InstanceUnavailable, ToolHostEffect.NotStarted);
		Assert.Contains("instance_list", error.Hint, StringComparison.Ordinal);
		Assert.Equal(0, backend.ForwardedCallCount);
	}

	[Fact]
	public async Task CallTool_StaleIdentity_ReturnsInstanceUnavailableNotStartedAndNeverForwards()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "stale");
		await using McpClient client = await gateway.ConnectAsync();
		backend.ReportedIdentity = backend.Descriptor with
		{
			ActivationId = Guid.NewGuid()
		};

		CallToolResult result = await client.CallToolAsync("runtime_get_info", backend.RoutedArguments(),
			cancellationToken: TestContext.Current.CancellationToken);

		AssertEnvelope(result, ToolErrorKind.InstanceUnavailable, ToolHostEffect.NotStarted);
		Assert.Equal(0, backend.ForwardedCallCount);
		Assert.Equal(0, backend.InitializeCount);
	}

	[Fact]
	public async Task CallTool_StoppedBackend_ReportsNotStartedAndEvictsItsClient()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "stopped");
		await using McpClient client = await gateway.ConnectAsync();
		await CallJsonAsync(client, "runtime_get_info", backend.RoutedArguments());
		await backend.StopListeningAsync();

		CallToolResult result = await client.CallToolAsync("runtime_get_info", backend.RoutedArguments(),
			cancellationToken: TestContext.Current.CancellationToken);

		AssertEnvelope(result, ToolErrorKind.InstanceUnavailable, ToolHostEffect.NotStarted);
		Assert.Equal(1, backend.ForwardedCallCount);
		Assert.Equal(0, gateway.Services.GetRequiredService<BackendConnectionPool>().Count);
	}

	[Fact]
	public async Task CallTool_BackendDropsConnection_ReportsUnknownEffectAndNeverRetries()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "dropping");
		await using McpClient client = await gateway.ConnectAsync();
		backend.AbortConnection = true;

		CallToolResult result = await client.CallToolAsync("runtime_get_info", backend.RoutedArguments(),
			cancellationToken: TestContext.Current.CancellationToken);

		ToolError error = AssertEnvelope(result, ToolErrorKind.InstanceUnavailable, ToolHostEffect.Unknown);
		Assert.Equal("Do not repeat a mutation; inspect state with a read-only tool first.", error.Hint);
		Assert.Equal(1, backend.ForwardedCallCount);
		Assert.Equal(0, gateway.Services.GetRequiredService<BackendConnectionPool>().Count);
	}

	[Fact]
	public async Task CallTool_UnknownTool_ReturnsInvalidArgumentEnvelope()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using McpClient client = await gateway.ConnectAsync();

		// The pre-2.0.0 name of instance_list is an unknown tool now.
		CallToolResult result = await client.CallToolAsync("list_instances", new Dictionary<string, object?>(),
			cancellationToken: TestContext.Current.CancellationToken);

		AssertEnvelope(result, ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted);
	}

	[Fact]
	public async Task CallTool_BackendErrorResult_PassesThroughUnchanged()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "failing");
		await using McpClient client = await gateway.ConnectAsync();
		CallToolResult backendError = ToolErrorResults.Create(new ToolError(ToolErrorKind.NotAttached,
			"No process is attached.", "Memory.ReadBytes", ToolHostEffect.NotStarted, false,
			"Attach a process with process_attach."));
		backend.Respond = () => backendError;

		CallToolResult result = await client.CallToolAsync("runtime_get_info", backend.RoutedArguments(),
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(result.IsError);
		Assert.Null(result.StructuredContent);
		Assert.Equal(Assert.IsType<TextContentBlock>(Assert.Single(backendError.Content)).Text,
			Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
		Assert.Equal(1, backend.InitializeCount);
		Assert.Equal(1, gateway.Services.GetRequiredService<BackendConnectionPool>().Count);
	}

	[Fact]
	public async Task CallTool_BackendProtocolError_PassesThroughWithItsCode()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "protocol");
		await using McpClient client = await gateway.ConnectAsync();
		backend.Respond = static () => throw new McpProtocolException("Backend rejected the parameters.",
			McpErrorCode.InvalidParams);

		McpProtocolException failure = await Assert.ThrowsAsync<McpProtocolException>(async () =>
			await client.CallToolAsync("runtime_get_info", backend.RoutedArguments(),
				cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal(McpErrorCode.InvalidParams, failure.ErrorCode);
		Assert.Contains("Backend rejected the parameters.", failure.Message, StringComparison.Ordinal);
		Assert.Equal(1, backend.ForwardedCallCount);
	}

	private static ToolError AssertEnvelope(CallToolResult result, ToolErrorKind kind, ToolHostEffect effect)
	{
		Assert.True(result.IsError);
		Assert.Null(result.StructuredContent);
		Assert.True(ToolErrorResults.TryRead(result, out ToolError? error),
			"A gateway failure must carry only the {\"error\":{...}} envelope.");
		Assert.Equal(kind, error.Kind);
		Assert.Equal(effect, error.HostEffect);
		Assert.False(string.IsNullOrWhiteSpace(error.Hint));
		return error;
	}

	private static Dictionary<string, JsonElement> Arguments(FakeBackend backend)
	{
		return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[GatewayToolCatalog.InstanceIdArgumentName] =
				JsonElement.Parse(JsonValue.Create(backend.Descriptor.InstanceId).ToJsonString())
		};
	}

	private static Dictionary<string, object?> ObjectArguments(FakeBackend backend)
	{
		return new Dictionary<string, object?>
		{
			[GatewayToolCatalog.InstanceIdArgumentName] = backend.Descriptor.InstanceId
		};
	}

	private static async Task<JsonObject> CallJsonAsync(McpClient client, string name,
		IReadOnlyDictionary<string, object?> arguments)
	{
		CallToolResult result =
			await client.CallToolAsync(name, arguments, cancellationToken: TestContext.Current.CancellationToken);
		Assert.NotEqual(true, result.IsError);
		Assert.True(result.StructuredContent is JsonElement);
		return JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!.AsObject();
	}
}
