using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[Collection(nameof(SerialTestGroup))]
public sealed class LiveMcpCleanupCancellationTests
{
	[Fact]
	public async Task WithCancellation_AfterScenarioCancellation_RoutesCleanupAndHonorsItsOwnDeadline()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "cleanup");
		using CancellationTokenSource scenario = new();
		await using LiveMcpClient client = await LiveMcpClient.ConnectAsync(gateway.Endpoint, scenario.Token);
		LiveMcpInstanceClient bound = client.Bind(backend.Descriptor.InstanceId);
		await scenario.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bound.CallToolRawAsync(CheatEngineToolNames.RuntimeGetInfo));
		Assert.Equal(0, backend.ForwardedCallCount);

		using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(10));
		LiveMcpInstanceClient cleanupClient = bound.WithCancellation(cleanup.Token);
		LiveMcpToolResult result = await cleanupClient.CallToolRawAsync(CheatEngineToolNames.RuntimeGetInfo);
		Assert.False(result.IsError, result.ErrorText);
		Assert.Equal(1, backend.ForwardedCallCount);
		Assert.Equal(CheatEngineToolNames.RuntimeGetInfo, backend.LastToolName);
		Assert.DoesNotContain("instanceId", backend.LastArguments.Keys);

		await cleanup.CancelAsync();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cleanupClient.CallToolRawAsync(CheatEngineToolNames.RuntimeGetInfo));
		Assert.Equal(1, backend.ForwardedCallCount);
	}
}
