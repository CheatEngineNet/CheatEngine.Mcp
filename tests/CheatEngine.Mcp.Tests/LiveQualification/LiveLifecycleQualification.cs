using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Identity checks shared by live lifecycle scenarios and their portable regression tests.</summary>
internal static class LiveLifecycleQualification
{
	[SupportedOSPlatform("windows")]
	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.True(inputs.Scenario is LiveQualificationScenario.Lifecycle or LiveQualificationScenario.Workflows);
		bool reloadPlugins = inputs.Scenario == LiveQualificationScenario.Lifecycle;
		LiveSandboxSession? sandbox = null;
		LiveLifecycleSession? connection = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			sandbox = await LiveSandboxSession.StartAsync(inputs, LiveSandboxOptions.Smoke with
			{
				LifecycleQualification = reloadPlugins,
				WorkflowQualification = !reloadPlugins
			});
			connection = await LiveLifecycleSession.ConnectAsync(sandbox);
			await connection.AttachBothAsync();
			await connection.VerifyBothAsync();
			sandbox.Record("all_tools_process_attach", new
			{
				processA = sandbox.HostA.TargetProcessId,
				processB = sandbox.HostB.TargetProcessId,
				knownValuesVerified = true
			});
			await LiveResourceQualification.VerifyAsync(sandbox, connection.Gateway);
			await LiveAdditionalReadQualification.VerifyAsync(sandbox, connection.Gateway);
			int noteTargetProcessId = sandbox.HostA.TargetProcessId;
			await LiveWorkflowQualification.VerifyAsync(sandbox, connection.Gateway);
			await LiveDotNetMetadataQualification.VerifyAsync(sandbox,
				connection.Gateway.Bind(sandbox.HostA.InstanceId), sandbox.TargetManagedAssemblyPath);
			await LiveManagedObjectQualification.VerifyAsync(sandbox,
				connection.Gateway.Bind(sandbox.HostA.InstanceId), sandbox.TargetManagedAssemblyPath);
			await LiveProcessFileQualification.VerifyAsync(sandbox, connection);
			await connection.RequireInstancesAsync(sandbox.HostA.InstanceId, sandbox.HostB.InstanceId);
			await LiveSignatureQualification.VerifyAsync(sandbox, connection.Gateway.Bind(sandbox.HostA.InstanceId));
			await LiveProcessCreateQualification.VerifyAsync(sandbox, connection.Gateway.Bind(sandbox.HostA.InstanceId),
				inputs.RepositoryRoot);
			for (int cycle = 0; cycle < (reloadPlugins ? 3 : 0); cycle++)
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
			await LiveRoadmapQualification.VerifyNotesAfterRestartAsync(sandbox,
				connection.Gateway.Bind(sandbox.HostA.InstanceId), noteTargetProcessId);
			sandbox.Record(reloadPlugins ? "lifecycle_workload" : "workflow_restart_workload", new
			{
				pluginCycles = reloadPlugins ? 3 : 0,
				targetRestarts = 1,
				gatewayRestarts = 1,
				hostRestarts = 2,
				fileTargetRecoveryHostRestarts = 1
			});
		}
		catch (Exception exception)
		{
			failure = exception;
			throw;
		}
		finally
		{
			await DisposeOrderedAsync(failure,
				connection is null ? null : connection.DisposeAsync,
				sandbox is null ? null : exceptions =>
				{
					sandbox.Record("lifecycle_connection_cleanup_failure", exceptions.Select(exception => exception.ToString()).ToArray());
					return ValueTask.CompletedTask;
				},
				sandbox is null ? null : () => { sandbox.MarkPassed(); return ValueTask.CompletedTask; },
				sandbox is null ? null : sandbox.DisposeAsync,
				cleanup);
			ThrowWithCleanup(failure, cleanup);
		}
	}

	internal static async Task DisposeOrderedAsync(Exception? failure, Func<ValueTask>? disposeConnection,
		Func<IReadOnlyList<Exception>, ValueTask>? recordConnectionCleanup, Func<ValueTask>? markPassed,
		Func<ValueTask>? disposeSandbox, List<Exception> cleanup)
	{
		if (disposeConnection is not null)
		{
			try
			{
				await disposeConnection();
			}
			catch (Exception exception)
			{
				cleanup.Add(exception);
			}
		}
		if (cleanup.Count != 0 && recordConnectionCleanup is not null)
		{
			try
			{
				await recordConnectionCleanup(cleanup);
			}
			catch (Exception exception)
			{
				cleanup.Add(exception);
			}
		}
		if (failure is null && cleanup.Count == 0 && markPassed is not null)
		{
			try
			{
				await markPassed();
			}
			catch (Exception exception)
			{
				cleanup.Add(exception);
			}
		}
		if (disposeSandbox is not null)
		{
			try
			{
				await disposeSandbox();
			}
			catch (Exception exception)
			{
				cleanup.Add(exception);
			}
		}
	}

	internal static void ThrowWithCleanup(Exception? failure, List<Exception> cleanup)
	{
		if (cleanup.Count == 0)
		{
			return;
		}
		if (failure is null)
		{
			throw new AggregateException("Live lifecycle cleanup failed.", cleanup);
		}
		throw new AggregateException("Live lifecycle failed and cleanup also failed.", [failure, .. cleanup]);
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
