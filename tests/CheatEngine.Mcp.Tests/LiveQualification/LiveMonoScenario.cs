using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
internal static class LiveMonoScenario
{
	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.Equal(LiveQualificationScenario.Mono, inputs.Scenario);
		Assert.Equal("x64", inputs.TargetArchitecture);
		LiveSandboxSession? sandbox = null;
		LiveLifecycleSession? connection = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			sandbox = await LiveSandboxSession.StartAsync(inputs, LiveSandboxOptions.Smoke with
			{
				MonoQualification = true,
				EnableTargetCodeExecution = true
			});
			connection = await LiveLifecycleSession.ConnectAsync(sandbox);
			await connection.AttachBothAsync();
			await RequireUnchangedBAsync();
			await LiveMonoQualification.VerifyAsync(sandbox,
				connection.Gateway.Bind(sandbox.HostA.InstanceId), inputs.RepositoryRoot);
			await RequireUnchangedBAsync();
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			await LiveLifecycleQualification.DisposeOrderedAsync(failure,
				connection is null ? null : connection.DisposeAsync,
				sandbox is null ? null : exceptions =>
				{
					sandbox.Record("mono_connection_cleanup_failure", exceptions.Select(exception => exception.ToString()).ToArray());
					return ValueTask.CompletedTask;
				},
				sandbox is null ? null : () => { sandbox.MarkPassed(); return ValueTask.CompletedTask; },
				sandbox is null ? null : sandbox.DisposeAsync, cleanup);
			LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
		}
		return;

		async Task RequireUnchangedBAsync()
		{
			JsonNode read = await connection!.Gateway.Bind(sandbox!.HostB.InstanceId).CallToolAsync(
				CheatEngineToolNames.MemoryRead, new Dictionary<string, object?>
				{
					["address"] = sandbox.HostB.TargetAddress,
					["valueType"] = "int32"
				}) ?? throw new InvalidDataException("The independent host returned no read payload.");
			Assert.Equal(20260926.ToString(CultureInfo.InvariantCulture), read["value"]!.GetValue<string>());
		}
	}
}
