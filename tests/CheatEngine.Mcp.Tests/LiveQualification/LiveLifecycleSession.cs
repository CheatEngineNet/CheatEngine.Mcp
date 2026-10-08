using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Owns one gateway connection while the private hosts undergo fixed lifecycle transitions.</summary>
[SupportedOSPlatform("windows")]
internal sealed class LiveLifecycleSession(LiveSandboxSession sandbox, LiveMcpClient gateway) : IAsyncDisposable
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

	internal async Task VerifyBothAsync()
	{
		foreach (LiveSandboxHost host in new[] { sandbox.HostA, sandbox.HostB })
		{
			LiveMcpInstanceClient instance = Gateway.Bind(host.InstanceId);
			JsonNode runtime = (await instance.CallToolAsync(CheatEngineToolNames.RuntimeGetInfo))!;
			foreach (string gate in new[] { "unsafeLua", "autoAssembler", "kernelAccess", "targetCodeExecution" })
			{
				Assert.False(runtime["gates"]![gate]!.GetValue<bool>());
			}
			Assert.Equal(Path.GetFileName(host.PluginPath), runtime["pluginFileName"]!.GetValue<string>());
			Assert.Equal(Path.GetFileName(host.PluginPath), runtime["runtimeFileName"]!.GetValue<string>());
			Assert.True(runtime["epoch"]!.GetValue<long>() > 0);
			JsonNode value = (await instance.CallToolAsync(CheatEngineToolNames.MemoryRead,
				new Dictionary<string, object?> { ["address"] = host.TargetAddress, ["valueType"] = "int32" }))!;
			Assert.Equal("20260926", value["value"]!.GetValue<string>());
		}
	}

	internal async Task CyclePluginAsync(string name)
	{
		LiveSandboxHost before = name == "A" ? sandbox.HostA : sandbox.HostB;
		LiveSandboxHost unaffected = name == "A" ? sandbox.HostB : sandbox.HostA;
		await sandbox.ReloadPluginAsync(name, false, TestContext.Current.CancellationToken);
		await RequireInstancesAsync(unaffected.InstanceId);
		await RejectStaleAsync(before.InstanceId);
		JsonNode remaining = (await Gateway.Bind(unaffected.InstanceId).CallToolAsync(CheatEngineToolNames.MemoryRead,
			new Dictionary<string, object?> { ["address"] = unaffected.TargetAddress, ["valueType"] = "int32" }))!;
		Assert.Equal("20260926", remaining["value"]!.GetValue<string>());
		await sandbox.ReloadPluginAsync(name, true, TestContext.Current.CancellationToken);
		LiveSandboxHost after = name == "A" ? sandbox.HostA : sandbox.HostB;
		LiveLifecycleQualification.RequirePluginReload(before, after);
		Assert.Equal(unaffected, name == "A" ? sandbox.HostB : sandbox.HostA);
		await RequireInstancesAsync(sandbox.HostA.InstanceId, sandbox.HostB.InstanceId);
		await RejectStaleAsync(before.InstanceId);
		await AttachBothAsync();
		await VerifyBothAsync();
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

			await Task.Delay(100, TestContext.Current.CancellationToken);
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
