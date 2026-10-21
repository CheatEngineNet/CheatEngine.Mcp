using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Identity checks shared by live lifecycle scenarios and their portable regression tests.</summary>
internal static class LiveLifecycleQualification
{
	[SupportedOSPlatform("windows")]
	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.True(inputs.Scenario is LiveQualificationScenario.Lifecycle or LiveQualificationScenario.PluginLifecycle or LiveQualificationScenario.Workflows or LiveQualificationScenario.Inspection or LiveQualificationScenario.StateWorkflows);
		bool pluginLifecycle = inputs.Scenario == LiveQualificationScenario.PluginLifecycle;
		bool reloadPlugins = inputs.Scenario is LiveQualificationScenario.Lifecycle or LiveQualificationScenario.PluginLifecycle;
		bool stateWorkflows = inputs.Scenario == LiveQualificationScenario.StateWorkflows;
		LiveSandboxSession? sandbox = null;
		LiveLifecycleSession? connection = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			sandbox = await LiveSandboxSession.StartAsync(inputs, LiveSandboxOptions.Smoke with
			{
				LifecycleQualification = reloadPlugins,
				SeedPluginChecklistAtStartup = pluginLifecycle,
				WorkflowQualification = inputs.Scenario == LiveQualificationScenario.Workflows,
				InspectionQualification = inputs.Scenario == LiveQualificationScenario.Inspection,
				StateWorkflowsQualification = stateWorkflows
			});
			connection = await LiveLifecycleSession.ConnectAsync(sandbox);
			await connection.AttachBothAsync();
			await connection.VerifyBothAsync();
			if (pluginLifecycle)
			{
				List<LivePluginReloadCycleEvidence> cycles = [];
				int processId = sandbox.HostA.ProcessId;
				int targetProcessId = sandbox.HostA.TargetProcessId;
				for (int cycle = 0; cycle < 3; cycle++)
				{
					cycles.Add(await connection.CyclePluginAsync("A"));
				}
				RequirePluginReloadCycles(cycles, processId, targetProcessId);
				sandbox.Record("plugin_lifecycle_completed", new
				{
					cycleCount = cycles.Count,
					processId,
					targetProcessId,
					cycles
				});
				return;
			}
			if (inputs.Scenario == LiveQualificationScenario.Inspection)
			{
				await LiveWorkflowQualification.VerifyGatesAsync(sandbox, connection.Gateway.Bind(sandbox.HostA.InstanceId));
			}
			sandbox.Record("all_tools_process_attach", new
			{
				processA = sandbox.HostA.TargetProcessId,
				processB = sandbox.HostB.TargetProcessId,
				knownValuesVerified = true
			});
			await LiveResourceQualification.VerifyAsync(sandbox, connection.Gateway);
			await LiveAdditionalReadQualification.VerifyAsync(sandbox, connection.Gateway);
			if (inputs.Scenario == LiveQualificationScenario.Inspection)
			{
				LiveMcpInstanceClient instance = connection.Gateway.Bind(sandbox.HostA.InstanceId);
				await LiveUtilitySymbolQualification.VerifyAsync(sandbox, instance);
				await connection.VerifyBothAsync();
				await connection.RestartGatewayAsync();
				await connection.VerifyBothAsync();
				sandbox.Record("inspection_qualification_completed", new
				{
					gatewayRestarted = true,
					bothOwnedTargetsHealthy = true
				});
				return;
			}
			int noteTargetProcessId = sandbox.HostA.TargetProcessId;
			if (stateWorkflows)
			{
				LiveMcpInstanceClient instance = connection.Gateway.Bind(sandbox.HostA.InstanceId);
				await LiveWorkflowQualification.VerifyGatesAsync(sandbox, instance);
				await LiveCatalogStateQualification.VerifyAsync(sandbox, instance);
				await LiveInspectionQualification.VerifyAsync(sandbox, instance);
				await LiveRoadmapQualification.VerifyAsync(sandbox, instance);
				await LiveFileRecordQualification.VerifyAsync(sandbox, instance);
				await LiveCodeAnalysisQualification.VerifyAsync(sandbox, instance);
				await LiveModuleExportQualification.VerifyAsync(sandbox, instance);
				await LiveDissectionQualification.VerifyAsync(sandbox, instance);
			}
			else
			{
				await LiveWorkflowQualification.VerifyAsync(sandbox, connection.Gateway);
				await LiveDotNetMetadataQualification.VerifyAsync(sandbox,
					connection.Gateway.Bind(sandbox.HostA.InstanceId), sandbox.TargetManagedAssemblyPath);
				await LiveManagedObjectQualification.VerifyAsync(sandbox,
					connection.Gateway.Bind(sandbox.HostA.InstanceId), sandbox.TargetManagedAssemblyPath);
				await LiveProcessFileQualification.VerifyAsync(sandbox, connection);
			}
			await connection.RequireInstancesAsync(sandbox.HostA.InstanceId, sandbox.HostB.InstanceId);
			await LiveSignatureQualification.VerifyAsync(sandbox, connection.Gateway.Bind(sandbox.HostA.InstanceId));
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
			sandbox.Record(stateWorkflows ? "state_workflow_restart_workload" : reloadPlugins ? "lifecycle_workload" : "workflow_restart_workload", new
			{
				pluginCycles = reloadPlugins ? 3 : 0,
				targetRestarts = 1,
				gatewayRestarts = 1,
				hostRestarts = stateWorkflows ? 1 : 2,
				fileTargetRecoveryHostRestarts = stateWorkflows ? 0 : 1
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

	internal static void RequirePluginReloadCycles(IReadOnlyList<LivePluginReloadCycleEvidence> cycles,
		int expectedProcessId, int expectedTargetProcessId)
	{
		ArgumentNullException.ThrowIfNull(cycles);
		if (expectedProcessId <= 0 || expectedTargetProcessId <= 0 || cycles.Count != 3)
		{
			throw new InvalidOperationException("Plugin lifecycle qualification requires exactly three cycles on the same owned host and target.");
		}

		HashSet<long> nonces = [];
		HashSet<string> instanceIds = new(StringComparer.Ordinal);
		HashSet<string> endpoints = new(StringComparer.Ordinal);
		string? expectedBeforeInstanceId = null;
		string? expectedBeforeEndpoint = null;
		foreach (LivePluginReloadCycleEvidence cycle in cycles)
		{
			if (cycle.ProcessId != expectedProcessId || cycle.TargetProcessId != expectedTargetProcessId
				|| cycle.DisableNonce <= 0 || cycle.EnableNonce <= 0 || cycle.DisableNonce == cycle.EnableNonce
				|| !nonces.Add(cycle.DisableNonce) || !nonces.Add(cycle.EnableNonce)
				|| string.IsNullOrWhiteSpace(cycle.BeforeInstanceId) || string.IsNullOrWhiteSpace(cycle.AfterInstanceId)
				|| string.IsNullOrWhiteSpace(cycle.BeforeEndpoint) || string.IsNullOrWhiteSpace(cycle.AfterEndpoint)
				|| string.Equals(cycle.BeforeInstanceId, cycle.AfterInstanceId, StringComparison.Ordinal)
				|| string.Equals(cycle.BeforeEndpoint, cycle.AfterEndpoint, StringComparison.Ordinal)
				|| !cycle.AccessTokenChanged)
			{
				throw new InvalidOperationException("A plugin lifecycle cycle lacks unique actions or a fresh instance, endpoint and access token.");
			}
			if (expectedBeforeInstanceId is not null
				&& (!string.Equals(expectedBeforeInstanceId, cycle.BeforeInstanceId, StringComparison.Ordinal)
					|| !string.Equals(expectedBeforeEndpoint, cycle.BeforeEndpoint, StringComparison.Ordinal)))
			{
				throw new InvalidOperationException("Plugin lifecycle cycles do not form one continuous identity sequence.");
			}
			if (expectedBeforeInstanceId is null
				&& (!instanceIds.Add(cycle.BeforeInstanceId) || !endpoints.Add(cycle.BeforeEndpoint)))
			{
				throw new InvalidOperationException("Plugin lifecycle qualification began with a duplicate discovery identity or endpoint.");
			}
			if (!instanceIds.Add(cycle.AfterInstanceId) || !endpoints.Add(cycle.AfterEndpoint))
			{
				throw new InvalidOperationException("A plugin lifecycle cycle reused an earlier discovery identity or endpoint.");
			}
			expectedBeforeInstanceId = cycle.AfterInstanceId;
			expectedBeforeEndpoint = cycle.AfterEndpoint;
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
