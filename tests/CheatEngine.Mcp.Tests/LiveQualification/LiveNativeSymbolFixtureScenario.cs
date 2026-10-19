using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
internal static class LiveNativeSymbolFixtureScenario
{
	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.Equal(LiveQualificationScenario.SymbolFixture, inputs.Scenario);
		Assert.Equal("x64", inputs.TargetArchitecture);
		LiveSandboxSession sandbox = await LiveSandboxSession.StartAsync(inputs, LiveSandboxOptions.Smoke with
		{
			SymbolFixtureQualification = true,
			EnableTargetCodeExecution = true
		});
		LiveMcpClient? gateway = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			gateway = await LiveMcpClient.ConnectGatewayAsync(sandbox.GatewayExecutablePath, sandbox.InstanceDirectory, TestContext.Current.CancellationToken);
			LiveMcpInstanceClient instanceA = gateway.Bind(sandbox.HostA.InstanceId);
			JsonNode? attachedA = await instanceA.CallToolAsync(CheatEngineToolNames.ProcessAttach,
				new Dictionary<string, object?> { ["process"] = sandbox.HostA.TargetProcessId.ToString(CultureInfo.InvariantCulture) });
			Assert.Equal(sandbox.HostA.TargetProcessId, attachedA!["processId"]!.GetValue<int>());
			bool structureReceiptAlreadyEmitted = await LiveNativeSymbolFixtureQualification.VerifyAsync(sandbox, instanceA,
				sandbox.NativeSymbolFixture);

			LiveMcpInstanceClient instanceB = gateway.Bind(sandbox.HostB.InstanceId);
			JsonNode? attachedB = await instanceB.CallToolAsync(CheatEngineToolNames.ProcessAttach,
				new Dictionary<string, object?> { ["process"] = sandbox.HostB.TargetProcessId.ToString(CultureInfo.InvariantCulture) });
			Assert.Equal(sandbox.HostB.TargetProcessId, attachedB!["processId"]!.GetValue<int>());
			await LiveNativeSymbolFixtureQualification.VerifyAddModuleOnFreshHostAsync(sandbox, instanceB, sandbox.HostB,
				sandbox.NativeSymbolFixture, structureReceiptAlreadyEmitted);
		}
		catch (Exception exception) { failure = exception; }
		finally
		{
			if (gateway is not null)
			{
				try
				{
					await gateway.DisposeAsync();
				}
				catch (Exception exception) { cleanup.Add(exception); }
			}
			await sandbox.TerminateExecutionTargetsBeforeHostCleanupAsync("native symbol fixture finalization", cleanup);
			if (failure is null && cleanup.Count == 0)
			{
				sandbox.MarkPassed();
			}

			try
			{
				await sandbox.DisposeAsync();
			}
			catch (Exception exception) { cleanup.Add(exception); }
		}
		if (failure is not null)
		{
			cleanup.Insert(0, failure);
		}

		if (cleanup.Count != 0)
		{
			throw new AggregateException(cleanup);
		}
	}
}
