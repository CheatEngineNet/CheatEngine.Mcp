using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Owns one gateway connection while the private hosts undergo fixed lifecycle transitions.</summary>
[SupportedOSPlatform("windows")]
internal sealed class LiveLifecycleSession(LiveSandboxSession sandbox, LiveMcpClient gateway,
	CancellationToken? pollingCancellation = null) : IAsyncDisposable
{
	internal LiveMcpClient Gateway { get; private set; } = gateway;
	private bool _ownsGateway = true;

	internal static async Task<LiveLifecycleSession> ConnectAsync(LiveSandboxSession sandbox)
	{
		LiveMcpClient client = await LiveMcpClient.ConnectGatewayAsync(sandbox.GatewayExecutablePath,
			sandbox.InstanceDirectory, TestContext.Current.CancellationToken);
		return new LiveLifecycleSession(sandbox, client);
	}

	public async ValueTask DisposeAsync()
	{
		if (!_ownsGateway)
		{
			return;
		}

		_ownsGateway = false;
		await Gateway.DisposeAsync();
	}

	internal async Task RestartGatewayAsync()
	{
		await DisposeAsync();
		Gateway = await LiveMcpClient.ConnectGatewayAsync(sandbox.GatewayExecutablePath,
			sandbox.InstanceDirectory, TestContext.Current.CancellationToken);
		_ownsGateway = true;
		await RequireInstancesAsync(sandbox.HostA.InstanceId, sandbox.HostB.InstanceId);
	}

	internal async Task AttachBothAsync()
	{
		foreach (LiveSandboxHost host in new[] { sandbox.HostA, sandbox.HostB })
		{
			JsonNode opened = (await Gateway.Bind(host.InstanceId).CallToolAsync(CheatEngineToolNames.ProcessAttach,
				new Dictionary<string, object?> { ["process"] = host.TargetProcessId.ToString(CultureInfo.InvariantCulture) }))!;
			Assert.Equal(host.TargetProcessId, opened["processId"]!.GetValue<int>());
		}
	}

	internal async Task VerifyBothAsync(bool expectAssembler = false)
	{
		foreach (LiveSandboxHost host in new[] { sandbox.HostA, sandbox.HostB })
		{
			LiveMcpInstanceClient instance = Gateway.Bind(host.InstanceId);
			JsonNode runtime = (await instance.CallToolAsync(CheatEngineToolNames.RuntimeGetInfo))!;
			Assert.Equal(expectAssembler, runtime["gates"]!["unsafeLua"]!.GetValue<bool>());
			Assert.Equal(expectAssembler, runtime["gates"]!["autoAssembler"]!.GetValue<bool>());
			Assert.False(runtime["gates"]!["kernelAccess"]!.GetValue<bool>());
			Assert.False(runtime["gates"]!["targetCodeExecution"]!.GetValue<bool>());
			Assert.Equal(Path.GetFileName(host.PluginPath), runtime["pluginFileName"]!.GetValue<string>());
			Assert.Equal(Path.GetFileName(host.PluginPath), runtime["runtimeFileName"]!.GetValue<string>());
			Assert.True(runtime["epoch"]!.GetValue<long>() > 0);
			JsonNode value = (await instance.CallToolAsync(CheatEngineToolNames.MemoryRead,
				new Dictionary<string, object?> { ["address"] = host.TargetAddress, ["valueType"] = "int32" }))!;
			Assert.Equal("20260926", value["value"]!.GetValue<string>());
		}
	}

	internal async Task<LivePluginReloadCycleEvidence> CyclePluginAsync(string name)
	{
		LiveSandboxHost before = name == "A" ? sandbox.HostA : sandbox.HostB;
		LiveSandboxHost unaffected = name == "A" ? sandbox.HostB : sandbox.HostA;
		LivePluginCycle disabled = await sandbox.ReloadPluginAsync(name, false, TestContext.Current.CancellationToken);
		Assert.Equal((before.ProcessId, before.TargetProcessId, false, before.InstanceId, before.InstanceId),
			(disabled.ProcessId, disabled.TargetProcessId, disabled.Enabled,
				disabled.PreviousInstanceId, disabled.CurrentInstanceId));
		await RequireInstancesAsync(unaffected.InstanceId);
		await RejectStaleAsync(before.InstanceId);
		JsonNode remaining = (await Gateway.Bind(unaffected.InstanceId).CallToolAsync(CheatEngineToolNames.MemoryRead,
			new Dictionary<string, object?> { ["address"] = unaffected.TargetAddress, ["valueType"] = "int32" }))!;
		Assert.Equal("20260926", remaining["value"]!.GetValue<string>());
		LivePluginCycle enabled = await sandbox.ReloadPluginAsync(name, true, TestContext.Current.CancellationToken);
		LiveSandboxHost after = name == "A" ? sandbox.HostA : sandbox.HostB;
		LiveLifecycleQualification.RequirePluginReload(before, after);
		Assert.True(enabled.Enabled);
		Assert.True(enabled.InstanceIdChanged);
		Assert.True(enabled.EndpointChanged);
		Assert.True(enabled.AccessTokenChanged);
		Assert.Equal((before.InstanceId, after.InstanceId, before.Endpoint, after.Endpoint),
			(enabled.PreviousInstanceId, enabled.CurrentInstanceId, enabled.PreviousEndpoint, enabled.CurrentEndpoint));
		Assert.Equal(unaffected, name == "A" ? sandbox.HostB : sandbox.HostA);
		await RequireInstancesAsync(sandbox.HostA.InstanceId, sandbox.HostB.InstanceId);
		await RejectStaleAsync(before.InstanceId);
		await AttachBothAsync();
		await VerifyBothAsync();
		LivePluginReloadCycleEvidence evidence = new(disabled.Nonce, enabled.Nonce, before.ProcessId,
			before.TargetProcessId, before.InstanceId, after.InstanceId, before.Endpoint, after.Endpoint,
			enabled.AccessTokenChanged);
		sandbox.Record($"plugin_{name}_cycle", evidence);
		return evidence;
	}

	internal async Task RequireInstancesAsync(params string[] expected)
	{
		Stopwatch deadline = Stopwatch.StartNew();
		do
		{
			JsonNode result = (await Gateway.CallToolAsync(CheatEngineToolNames.InstanceList))!;
			string[] actual = result["instances"]!.AsArray().Select(item => item!["instanceId"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
			if (actual.SequenceEqual(expected.Order(StringComparer.Ordinal)))
			{
				return;
			}

			await Task.Delay(100, pollingCancellation ?? TestContext.Current.CancellationToken);
		} while (deadline.Elapsed < TimeSpan.FromSeconds(10));
		Assert.Fail("Discovery did not converge to the exact owned activation identities.");
	}

	internal async Task RejectStaleAsync(string instanceId)
	{
		LiveMcpToolResult result = await Gateway.Bind(instanceId).CallToolRawAsync(CheatEngineToolNames.RuntimeGetInfo);
		Assert.True(result.IsError);
		Assert.Equal("instance_unavailable", result.Payload!["error"]!["kind"]!.GetValue<string>());
		Assert.Equal("not_started", result.Payload["error"]!["hostEffect"]!.GetValue<string>());
	}
}

internal sealed record LivePluginReloadCycleEvidence(long DisableNonce, long EnableNonce, int ProcessId,
	int TargetProcessId, string BeforeInstanceId, string AfterInstanceId, string BeforeEndpoint,
	string AfterEndpoint, bool AccessTokenChanged);
