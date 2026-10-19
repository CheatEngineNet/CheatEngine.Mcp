using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Runs the fixed assembler or Windows-debugger cases in separately configured private hosts.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveSpecializedQualification
{
	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.True(inputs.Scenario is LiveQualificationScenario.Assembler or LiveQualificationScenario.Debugger);
		bool assembler = inputs.Scenario == LiveQualificationScenario.Assembler;
		LiveSandboxSession? sandbox = null;
		LiveLifecycleSession? connection = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			sandbox = await LiveSandboxSession.StartAsync(inputs, LiveSandboxOptions.Smoke with
			{
				AssemblerQualification = assembler,
				DebuggerQualification = !assembler
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
			}
			else
			{
				await LiveDebuggerQualification.VerifyAsync(sandbox, instance);
			}
			await connection.VerifyBothAsync(expectAssembler: assembler);
			await connection.RestartGatewayAsync();
			await connection.VerifyBothAsync(expectAssembler: assembler);
			sandbox.Record("specialized_qualification_completed", new
			{
				scenario = assembler ? "assembler" : "debugger",
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
}
