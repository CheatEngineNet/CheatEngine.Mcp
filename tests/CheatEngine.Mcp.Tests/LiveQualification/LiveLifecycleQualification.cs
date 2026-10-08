using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Identity checks shared by live lifecycle scenarios and their portable regression tests.</summary>
internal static class LiveLifecycleQualification
{
	[SupportedOSPlatform("windows")]
	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.Equal(LiveQualificationScenario.Lifecycle, inputs.Scenario);
		await using LiveSandboxSession sandbox = await LiveSandboxSession.StartAsync(inputs,
			LiveSandboxOptions.Smoke with
			{
				LifecycleQualification = true
			});
		await using LiveLifecycleSession connection = await LiveLifecycleSession.ConnectAsync(sandbox);
		await connection.AttachBothAsync();
		await connection.VerifyBothAsync();
		await LiveResourceQualification.VerifyAsync(sandbox, connection.Gateway);
		await LiveWorkflowQualification.VerifyAsync(sandbox, connection.Gateway);
		for (int cycle = 0; cycle < 3; cycle++)
		{
			await connection.CyclePluginAsync("A");
		}
		await sandbox.RestartTargetAsync("A");
		await connection.AttachBothAsync();
		await connection.VerifyBothAsync();
		await connection.RestartGatewayAsync();
		await connection.VerifyBothAsync();
		LiveSandboxHost before = sandbox.HostA;
		LiveSandboxHost unaffected = sandbox.HostB;
		await sandbox.RestartHostAsync("A");
		RequireHostRestart(before, sandbox.HostA, sandbox.HostB);
		Assert.Equal(unaffected, sandbox.HostB);
		await connection.RequireInstancesAsync(sandbox.HostA.InstanceId, sandbox.HostB.InstanceId);
		await connection.RejectStaleAsync(before.InstanceId);
		await connection.AttachBothAsync();
		await connection.VerifyBothAsync();
		sandbox.Record("lifecycle_workload", new
		{
			pluginCycles = 3,
			targetRestarts = 1,
			gatewayRestarts = 1,
			hostRestarts = 1
		});
		sandbox.MarkPassed();
	}

	internal static void RequirePluginReload(LiveSandboxHost before, LiveSandboxHost after)
	{
		ArgumentNullException.ThrowIfNull(before);
		ArgumentNullException.ThrowIfNull(after);
		if (before.ProcessId != after.ProcessId || before.TargetProcessId != after.TargetProcessId
			|| string.Equals(before.InstanceId, after.InstanceId, StringComparison.Ordinal)
			|| string.Equals(before.Endpoint, after.Endpoint, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("An in-process plugin reload must retain the CE/target PIDs and refresh discovery identity.");
		}
	}

	internal static void RequireHostRestart(LiveSandboxHost before, LiveSandboxHost after, LiveSandboxHost unaffected)
	{
		ArgumentNullException.ThrowIfNull(before);
		ArgumentNullException.ThrowIfNull(after);
		ArgumentNullException.ThrowIfNull(unaffected);
		if (before.ProcessId == after.ProcessId || before.TargetProcessId == after.TargetProcessId)
		{
			throw new InvalidOperationException("A host restart must replace both the owned CE and target processes.");
		}
		if (unaffected.Name == after.Name || unaffected.ProcessId <= 0 || unaffected.TargetProcessId <= 0)
		{
			throw new InvalidOperationException("The independently owned second host was not preserved during the host restart.");
		}
	}
}

internal sealed record LiveProcessMetrics(DateTimeOffset CapturedAt, int ProcessId, int HandleCount, long PrivateBytes,
	long WorkingSetBytes, string? CollectionError);
