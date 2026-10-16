using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Hosting;

/// <summary>
///     Gateway-only qualification for every composed backend tool. The loopback backends deliberately echo requests;
///     these cases prove routing semantics, not a Cheat Engine tool's native behavior.
/// </summary>
[Collection(nameof(SerialTestGroup))]
public sealed class AllToolGatewayQualificationTests(AllToolGatewayFixture fixture) : IClassFixture<AllToolGatewayFixture>
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>The data source is the production backend catalog, so every catalog addition gets these routing cases.</summary>
	public static IEnumerable<object[]> BackendTools => BackendToolNames().Select(static name => new object[] { name });

	[Fact]
	public void BackendToolCases_AreCatalogDerivedAndCoverTheCurrentBackendSurface()
	{
		string[] names = BackendToolNames();

		Assert.Equal(193, names.Length);
		Assert.Equal(names.Distinct(StringComparer.Ordinal).Count(), names.Length);
		Assert.Equal(names, TestComposition.GatewayTools.Skip(1).Select(static tool => tool.Name));
	}

	[Theory]
	[MemberData(nameof(BackendTools))]
	public async Task CallTool_MissingInstanceId_RejectsEveryBackendToolBeforeAnyBackendContact(string tool)
	{
		BackendCounts firstBefore = BackendCounts.Capture(fixture.First);
		BackendCounts secondBefore = BackendCounts.Capture(fixture.Second);
		await using McpClient client = await fixture.Gateway.ConnectAsync();

		CallToolResult result = await client.CallToolAsync(tool, new Dictionary<string, object?>(), cancellationToken: Token);

		AssertError(result, tool, ToolErrorKind.InvalidArgument);
		AssertUnchanged(fixture.First, firstBefore);
		AssertUnchanged(fixture.Second, secondBefore);
	}

	[Theory]
	[MemberData(nameof(BackendTools))]
	public async Task CallTool_UnknownInstanceId_RejectsEveryBackendToolWithoutFallback(string tool)
	{
		BackendCounts firstBefore = BackendCounts.Capture(fixture.First);
		BackendCounts secondBefore = BackendCounts.Capture(fixture.Second);
		await using McpClient client = await fixture.Gateway.ConnectAsync();

		CallToolResult result = await client.CallToolAsync(tool,
			new Dictionary<string, object?>
			{
				[GatewayToolCatalog.InstanceIdArgumentName] = "ce-404-00000000000000000000000000000000"
			}, cancellationToken: Token);

		AssertError(result, tool, ToolErrorKind.InstanceUnavailable);
		AssertUnchanged(fixture.First, firstBefore);
		AssertUnchanged(fixture.Second, secondBefore);
	}

	[Theory]
	[MemberData(nameof(BackendTools))]
	public async Task CallTool_StaleIdentity_RejectsEveryBackendToolBeforeMutation(string tool)
	{
		BackendCounts firstBefore = BackendCounts.Capture(fixture.First);
		BackendCounts secondBefore = BackendCounts.Capture(fixture.Second);
		fixture.First.ReportedIdentity = fixture.First.Descriptor with
		{
			ActivationId = Guid.NewGuid()
		};
		try
		{
			await using McpClient client = await fixture.Gateway.ConnectAsync();
			CallToolResult result = await client.CallToolAsync(tool, RoutedArguments(fixture.First, tool, "stale"), cancellationToken: Token);

			AssertError(result, tool, ToolErrorKind.InstanceUnavailable);
			Assert.Equal(firstBefore.ForwardedCalls, fixture.First.ForwardedCallCount);
			Assert.Equal(firstBefore.InitializeCalls, fixture.First.InitializeCount);
			Assert.Equal(firstBefore.IdentityProbes + 1, fixture.First.IdentityProbeCount);
			AssertUnchanged(fixture.Second, secondBefore);
		}
		finally
		{
			fixture.First.ReportedIdentity = fixture.First.Descriptor;
		}
	}

	[Theory]
	[MemberData(nameof(BackendTools))]
	public async Task CallTool_TwoInstances_RoutesEveryBackendToolWithOnlyInstanceIdRemoved(string tool)
	{
		BackendCounts firstBefore = BackendCounts.Capture(fixture.First);
		BackendCounts secondBefore = BackendCounts.Capture(fixture.Second);
		await using McpClient client = await fixture.Gateway.ConnectAsync();

		CallToolResult first = await client.CallToolAsync(tool, RoutedArguments(fixture.First, tool, "first"), cancellationToken: Token);
		AssertForwarded(tool, fixture.First, "first", first);
		Assert.Equal(firstBefore.ForwardedCalls + 1, fixture.First.ForwardedCallCount);
		Assert.Equal(firstBefore.InitializeCalls + (firstBefore.InitializeCalls == 0 ? 1 : 0),
			fixture.First.InitializeCount);
		Assert.Equal(firstBefore.IdentityProbes + 1, fixture.First.IdentityProbeCount);
		AssertUnchanged(fixture.Second, secondBefore);

		CallToolResult second = await client.CallToolAsync(tool, RoutedArguments(fixture.Second, tool, "second"), cancellationToken: Token);
		AssertForwarded(tool, fixture.Second, "second", second);
		Assert.Equal(secondBefore.ForwardedCalls + 1, fixture.Second.ForwardedCallCount);
		Assert.Equal(secondBefore.InitializeCalls + (secondBefore.InitializeCalls == 0 ? 1 : 0),
			fixture.Second.InitializeCount);
		Assert.Equal(secondBefore.IdentityProbes + 1, fixture.Second.IdentityProbeCount);
	}

	[Fact]
	public async Task InstanceList_EmptyRegistry_ReturnsLocalEmptyDiscoveryResult()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using McpClient client = await gateway.ConnectAsync();

		CallToolResult result = await client.CallToolAsync(GatewayToolCatalog.InstanceListToolName,
			new Dictionary<string, object?>(), cancellationToken: Token);

		Assert.NotEqual(true, result.IsError);
		JsonObject payload = StructuredPayload(result);
		Assert.Empty(payload["instances"]!.AsArray());
		Assert.False(payload["discoveryIncomplete"]!.GetValue<bool>());
	}

	[Fact]
	public async Task InstanceList_PublishedDoubles_ReturnsBothVerifiedInstancesWithoutTokens()
	{
		await using McpClient client = await fixture.Gateway.ConnectAsync();

		CallToolResult result = await client.CallToolAsync(GatewayToolCatalog.InstanceListToolName,
			new Dictionary<string, object?>(), cancellationToken: Token);

		Assert.NotEqual(true, result.IsError);
		JsonObject payload = StructuredPayload(result);
		JsonArray instances = payload["instances"]!.AsArray();
		Assert.Equal([fixture.First.Descriptor.InstanceId, fixture.Second.Descriptor.InstanceId],
			instances.Select(static instance => instance!["instanceId"]!.GetValue<string>()));
		string text = payload.ToJsonString();
		Assert.DoesNotContain(fixture.First.Descriptor.AccessToken, text, StringComparison.Ordinal);
		Assert.DoesNotContain(fixture.Second.Descriptor.AccessToken, text, StringComparison.Ordinal);
	}

	private static string[] BackendToolNames()
	{
		return McpPrimitiveCatalog.Create(TestComposition.BackendManifest).Tools.Select(static tool => tool.Name).ToArray();
	}

	private static Dictionary<string, object?> RoutedArguments(FakeBackend backend, string tool, string marker)
	{
		return new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			[GatewayToolCatalog.InstanceIdArgumentName] = backend.Descriptor.InstanceId,
			["marker"] = $"{tool}:{marker}",
			["nested"] = new Dictionary<string, object?> { ["route"] = marker, ["tool"] = tool }
		};
	}

	private static void AssertError(CallToolResult result, string tool, ToolErrorKind kind)
	{
		Assert.True(result.IsError);
		Assert.Null(result.StructuredContent);
		Assert.True(ToolErrorResults.TryRead(result, out ToolError? error));
		Assert.Equal(kind, error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
		Assert.Equal(tool, error.Operation);
	}

	private static void AssertForwarded(string tool, FakeBackend backend, string marker, CallToolResult result)
	{
		Assert.NotEqual(true, result.IsError);
		Assert.Equal(tool, backend.LastToolName);
		Assert.DoesNotContain(GatewayToolCatalog.InstanceIdArgumentName, backend.LastArguments.Keys);
		Assert.Equal($"{tool}:{marker}", backend.LastArguments["marker"].GetString());
		Assert.Equal(marker, backend.LastArguments["nested"].GetProperty("route").GetString());
		Assert.Equal(tool, backend.LastArguments["nested"].GetProperty("tool").GetString());

		JsonObject payload = StructuredPayload(result);
		Assert.Equal(backend.Descriptor.InstanceId, payload["backend"]!.GetValue<string>());
		Assert.Equal($"{tool}:{marker}", payload["arguments"]!["marker"]!.GetValue<string>());
		Assert.Equal(marker, payload["arguments"]!["nested"]!["route"]!.GetValue<string>());
		Assert.Equal(tool, payload["arguments"]!["nested"]!["tool"]!.GetValue<string>());
		Assert.Equal(["marker", "nested"], payload["arguments"]!.AsObject().Select(static argument => argument.Key)
			.Order(StringComparer.Ordinal));
	}

	private static JsonObject StructuredPayload(CallToolResult result)
	{
		Assert.True(result.StructuredContent is JsonElement);
		JsonElement structured = result.StructuredContent!.Value;
		Assert.Equal(structured.GetRawText(), Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
		return JsonNode.Parse(structured.GetRawText())!.AsObject();
	}

	private static void AssertUnchanged(FakeBackend backend, BackendCounts before)
	{
		Assert.Equal(before.ForwardedCalls, backend.ForwardedCallCount);
		Assert.Equal(before.InitializeCalls, backend.InitializeCount);
		Assert.Equal(before.IdentityProbes, backend.IdentityProbeCount);
	}

	private sealed record BackendCounts(int ForwardedCalls, int InitializeCalls, int IdentityProbes)
	{
		internal static BackendCounts Capture(FakeBackend backend)
		{
			return new BackendCounts(backend.ForwardedCallCount, backend.InitializeCount, backend.IdentityProbeCount);
		}
	}
}

public sealed class AllToolGatewayFixture : IAsyncLifetime
{
	internal GatewayTestHost Gateway
	{
		get;
		private set;
	} = null!;

	internal FakeBackend First
	{
		get;
		private set;
	} = null!;

	internal FakeBackend Second
	{
		get;
		private set;
	} = null!;

	public async ValueTask InitializeAsync()
	{
		Gateway = await GatewayTestHost.StartAsync();
		First = await FakeBackend.StartAsync(Gateway.Registry, "first");
		Second = await FakeBackend.StartAsync(Gateway.Registry, "second");
	}

	public async ValueTask DisposeAsync()
	{
		if (Second is not null)
		{
			await Second.DisposeAsync();
		}

		if (First is not null)
		{
			await First.DisposeAsync();
		}

		if (Gateway is not null)
		{
			await Gateway.DisposeAsync();
		}
	}
}
