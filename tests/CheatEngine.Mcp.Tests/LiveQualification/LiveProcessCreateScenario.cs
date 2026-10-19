using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Isolated all-gates-disabled evidence for the reversible owned process-create fixture.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveProcessCreateScenario
{
	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.Equal(LiveQualificationScenario.ProcessCreate, inputs.Scenario);
		LiveSandboxSession? sandbox = null;
		LiveLifecycleSession? connection = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			sandbox = await LiveSandboxSession.StartAsync(inputs, LiveSandboxOptions.Smoke with
			{
				ProcessCreateQualification = true
			});
			connection = await LiveLifecycleSession.ConnectAsync(sandbox);
			await connection.AttachBothAsync();
			await connection.VerifyBothAsync();
			await LiveProcessCreateQualification.VerifyAsync(sandbox,
				connection.Gateway.Bind(sandbox.HostA.InstanceId), inputs.RepositoryRoot);
			await connection.AttachBothAsync();
			await connection.VerifyBothAsync();
			sandbox.Record("process_create_workload", new
			{
				gatesDisabled = true,
				processA = sandbox.HostA.TargetProcessId,
				processB = sandbox.HostB.TargetProcessId,
				bothHostsVerifiedAfter = true
			});
		}
		catch (Exception exception)
		{
			failure = exception;
			throw;
		}
		finally
		{
			await LiveLifecycleQualification.DisposeOrderedAsync(failure,
				connection is null ? null : connection.DisposeAsync,
				sandbox is null ? null : exceptions =>
				{
					sandbox.Record("process_create_connection_cleanup_failure", exceptions.Select(exception => exception.ToString()).ToArray());
					return ValueTask.CompletedTask;
				},
				sandbox is null ? null : () => { sandbox.MarkPassed(); return ValueTask.CompletedTask; },
				sandbox is null ? null : sandbox.DisposeAsync,
				cleanup);
			LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
		}
	}
}
