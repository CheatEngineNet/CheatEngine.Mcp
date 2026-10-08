using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Reproduces a bounded AOB dispatch ordering issue in fresh disposable sessions; it is not qualification evidence.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveDispatchDiagnosticQualification
{
	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.Equal(LiveQualificationScenario.DispatchDiagnostic, inputs.Scenario);
		foreach (LiveDispatchDiagnosticCase @case in Enum.GetValues<LiveDispatchDiagnosticCase>())
		{
			await RunCaseAsync(inputs, @case);
		}
	}

	private static async Task RunCaseAsync(LiveQualificationInputs inputs, LiveDispatchDiagnosticCase @case)
	{
		LiveSandboxSession? sandbox = null;
		LiveLifecycleSession? connection = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			sandbox = await LiveSandboxSession.StartAsync(inputs, LiveSandboxOptions.Smoke with
			{
				DispatchDiagnostic = true
			});
			connection = await LiveLifecycleSession.ConnectAsync(sandbox);
			await connection.AttachBothAsync();
			sandbox.Record("dispatch_diagnostic_case", new
			{
				@case = @case.ToString(),
				step = "before_dispatch"
			});
			await LiveWorkflowQualification.VerifyDispatchDiagnosticAsync(sandbox, connection.Gateway, @case);
			foreach (LiveSandboxHost host in new[] { sandbox.HostA, sandbox.HostB })
			{
				JsonNode value = (await connection.Gateway.Bind(host.InstanceId).CallToolAsync(CheatEngineToolNames.MemoryRead,
					new Dictionary<string, object?> { ["address"] = host.TargetAddress, ["valueType"] = "int32" }))!;
				Assert.Equal("20260926", value["value"]!.GetValue<string>());
			}
			sandbox.Record("dispatch_diagnostic_case", new
			{
				@case = @case.ToString(),
				step = "completed"
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
					sandbox.Record("dispatch_diagnostic_gateway_cleanup_failure", exceptions.Select(exception => exception.ToString()).ToArray());
					return ValueTask.CompletedTask;
				},
				sandbox is null ? null : () => { sandbox.MarkPassed(); return ValueTask.CompletedTask; },
				sandbox is null ? null : sandbox.DisposeAsync,
				cleanup);
			LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
		}
	}
}

internal enum LiveDispatchDiagnosticCase
{
	AobOnly,
	NamedScanThenAob,
	MemoryNamedScanThenAob
}
