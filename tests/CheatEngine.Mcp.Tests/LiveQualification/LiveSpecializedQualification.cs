using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Runs the fixed assembler or Windows-debugger cases in separately configured private hosts.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveSpecializedQualification
{
	internal static bool Handles(LiveQualificationScenario scenario) =>
		scenario is LiveQualificationScenario.Assembler or LiveQualificationScenario.Debugger or LiveQualificationScenario.DebuggerBasic or LiveQualificationScenario.DebuggerEvents or LiveQualificationScenario.DebuggerExecution;

	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.True(Handles(inputs.Scenario));
		bool assembler = inputs.Scenario == LiveQualificationScenario.Assembler;
		bool debuggerBasicOnly = inputs.Scenario == LiveQualificationScenario.DebuggerBasic;
		bool debuggerEventsOnly = inputs.Scenario == LiveQualificationScenario.DebuggerEvents;
		bool debuggerExecutionOnly = inputs.Scenario == LiveQualificationScenario.DebuggerExecution;
		LiveSandboxSession? sandbox = null;
		LiveLifecycleSession? connection = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			sandbox = await LiveSandboxSession.StartAsync(inputs, LiveSandboxOptions.Smoke with
			{
				AssemblerQualification = assembler,
				DebuggerQualification = !assembler,
				DebuggerEventsQualification = debuggerEventsOnly,
				DebuggerTransitionDiagnostic = inputs.DebuggerTransitionDiagnostic
			});
			connection = await LiveLifecycleSession.ConnectAsync(sandbox);
			await connection.AttachBothAsync();
			await connection.VerifyBothAsync(expectAssembler: assembler);
			LiveMcpInstanceClient instance = connection.Gateway.Bind(sandbox.HostA.InstanceId);
			JsonNode runtime = await instance.CallToolAsync(CheatEngineToolNames.RuntimeGetInfo)
				?? throw new InvalidDataException("The private host returned no runtime information.");
			JsonNode gates = runtime["gates"] ?? throw new InvalidDataException("The private host omitted its capability gates.");
			Assert.Equal(assembler, gates["unsafeLua"]!.GetValue<bool>());
			Assert.Equal(assembler, gates["autoAssembler"]!.GetValue<bool>());
			Assert.False(gates["targetCodeExecution"]!.GetValue<bool>());
			Assert.False(gates["kernelAccess"]!.GetValue<bool>());
			sandbox.Record("specialized_qualification_gates", gates.DeepClone());
			if (assembler)
			{
				await LiveAssemblerQualification.VerifyAsync(sandbox, instance);
				await LiveApiHookQualification.VerifyAsync(sandbox, instance);
			}
			else
			{
				LiveDebuggerExecutionQualification.Manifest manifest = sandbox.HostA.DebuggerExecution
					?? throw new InvalidDataException("The owned debugger execution manifest is missing.");
				await RunDebuggerScenarioPlanAsync(debuggerEventsOnly, debuggerExecutionOnly, inputs.DebuggerTransitionDiagnostic, debuggerBasicOnly,
					() => LiveDebuggerQualification.VerifyAsync(sandbox, instance),
					() => LiveDebuggerTransitionDiagnostic.RunAsync(sandbox, instance),
					async () =>
					{
						await LiveDebuggerExecutionQualification.VerifyAsync(sandbox, instance, manifest);
						await LiveDebuggerExecutionQualification.VerifyRemainingAsync(sandbox, instance, manifest);
					},
					() => LiveDebuggerExecutionQualification.VerifyAsync(sandbox, instance, manifest),
					() => LiveDebuggerExecutionQualification.VerifyRemainingAsync(sandbox, instance, manifest));
			}
			await connection.VerifyBothAsync(expectAssembler: assembler);
			await connection.RestartGatewayAsync();
			await connection.VerifyBothAsync(expectAssembler: assembler);
			sandbox.Record("specialized_qualification_completed", new
			{
				scenario = assembler ? "assembler" : debuggerBasicOnly ? "debugger-basic" : debuggerEventsOnly ? "debugger-events" : debuggerExecutionOnly ? "debugger-execution" : "debugger",
				diagnosticOnly = inputs.DebuggerTransitionDiagnostic,
				gatewayRestarted = true,
				bothOwnedTargetsHealthy = true
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
					sandbox.Record("specialized_connection_cleanup_failure", exceptions.Select(exception => exception.ToString()).ToArray());
					return ValueTask.CompletedTask;
				},
				sandbox is null ? null : () => { sandbox.MarkPassed(); return ValueTask.CompletedTask; },
				sandbox is null ? null : sandbox.DisposeAsync,
				cleanup);
			LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
		}
	}

	internal static async Task RunDebuggerPlanAsync(bool transitionDiagnostic, Func<Task> basic,
		Func<Task> transition, Func<Task> execution, bool basicOnly = false)
	{
		if (transitionDiagnostic)
		{
			await transition();
		}
		else
		{
			await basic();
			if (!basicOnly)
			{
				await execution();
			}
		}
	}

	internal static async Task RunDebuggerScenarioPlanAsync(bool eventsOnly, bool executionOnly, bool transitionDiagnostic, bool basicOnly,
		Func<Task> basic, Func<Task> transition, Func<Task> execution, Func<Task> executionOnlyProbe, Func<Task> events)
	{
		if (eventsOnly)
		{
			await events();
			return;
		}
		if (executionOnly)
		{
			await executionOnlyProbe();
			return;
		}
		await RunDebuggerPlanAsync(transitionDiagnostic, basic, transition, execution, basicOnly);
	}
}
