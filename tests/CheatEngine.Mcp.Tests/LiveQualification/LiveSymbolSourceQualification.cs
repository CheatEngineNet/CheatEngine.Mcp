using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Effect-only oracle for the private Windows symbol source: no module registration and no kernel source.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveSymbolSourceQualification
{
	private static readonly (string Name, string Offset, string ValueType)[] ExpectedLayout =
	[
		("Magic", "0", "uint32"), ("Delta", "4", "uint16"), ("Flags", "6", "uint16"), ("Counter", "8", "uint64")
	];

	internal static async Task RunAsync(LiveSandboxSession sandbox, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		await using LiveMcpClient gateway = await LiveMcpClient.ConnectGatewayAsync(sandbox.GatewayExecutablePath,
			sandbox.InstanceDirectory, cancellationToken);
		await QualifyHostAsync(sandbox, gateway.Bind(sandbox.HostA.InstanceId), sandbox.HostA, cancellationToken);
		await QualifyHostAsync(sandbox, gateway.Bind(sandbox.HostB.InstanceId), sandbox.HostB, cancellationToken);
		string previousInstance = sandbox.HostA.InstanceId;
		await sandbox.TerminateOwnedTargetBeforeHostCleanupAsync("A", "symbol-source qualification restart");
		await sandbox.RestartHostAsync("A");
		Assert.NotEqual(previousInstance, sandbox.HostA.InstanceId);
		await QualifyHostAsync(sandbox, gateway.Bind(sandbox.HostA.InstanceId), sandbox.HostA, cancellationToken);
		sandbox.Record("live_symbol_sources_windows", new
		{
			windows = true,
			kernel = false,
			hosts = new[] { sandbox.HostA.InstanceId, sandbox.HostB.InstanceId },
			restartedInstance = previousInstance,
			clientQualification = "Unknown/NotExecuted; MCP receipts do not qualify the Client profile"
		});
	}

	private static async Task QualifyHostAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance,
		LiveSandboxHost host, CancellationToken cancellationToken)
	{
		JsonNode attached = await CallAsync(instance, CheatEngineToolNames.ProcessAttach,
			new Dictionary<string, object?> { ["process"] = host.TargetProcessId.ToString(CultureInfo.InvariantCulture) });
		Assert.Equal(host.TargetProcessId, attached["processId"]!.GetValue<int>());
		await AssertGatesAsync(instance);
		await sandbox.VerifySymbolSourceNetworkAsync(host, "before-enable", cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();

		// Each host incarnation gets a fresh owned target; verify module presence independently before loading the DLL.
		using (System.Diagnostics.Process target = System.Diagnostics.Process.GetProcessById(host.TargetProcessId))
		{
			bool loaded = target.Modules.Cast<System.Diagnostics.ProcessModule>().Any(module =>
				string.Equals(module.FileName, sandbox.NativeSymbolFixture.DllPath, StringComparison.OrdinalIgnoreCase));
			if (!loaded)
			{
				JsonNode injected = await CallAsync(instance, CheatEngineToolNames.ExecInjectLibrary,
					new Dictionary<string, object?> { ["libraryPath"] = sandbox.NativeSymbolFixture.DllPath, ["skipSymbolReloadWait"] = true });
				Assert.Equal(sandbox.NativeSymbolFixture.DllPath, injected["libraryPath"]!.GetValue<string>(), ignoreCase: true);
			}
		}
		await AssertPdbAbsentAsync(instance);

		JsonNode enabled = await CallAsync(instance, CheatEngineToolNames.SymbolEnableSources,
			new Dictionary<string, object?> { ["windows"] = true, ["kernel"] = false });
		Assert.True(enabled["windowsEnabled"]!.GetValue<bool>());
		Assert.False(enabled["kernelEnabled"]!.GetValue<bool>());
		Assert.True(enabled["externalAccess"]!.GetValue<bool>());

		JsonNode layout = await ReadPdbLayoutAsync(instance);
		for (int attempt = 0; !layout["found"]!.GetValue<bool>() && attempt < 50; attempt++)
		{
			await Task.Delay(100, cancellationToken);
			layout = await ReadPdbLayoutAsync(instance);
		}
		AssertExactLayout(layout);
		await sandbox.VerifySymbolSourceNetworkAsync(host, "after-enable", cancellationToken);
		sandbox.Record("symbol_source_effect", new
		{
			host = host.Name,
			fixture = sandbox.SymbolSourceContainment.Fixture.Manifest,
			enabled = enabled.DeepClone(),
			layout = layout.DeepClone()
		});
	}

	private static async Task AssertGatesAsync(LiveMcpInstanceClient instance)
	{
		JsonNode runtime = await CallAsync(instance, CheatEngineToolNames.RuntimeGetInfo);
		JsonNode gates = runtime["gates"] ?? throw new InvalidDataException("The source host did not report gates.");
		Assert.True(gates["targetCodeExecution"]!.GetValue<bool>());
		Assert.False(gates["kernelAccess"]!.GetValue<bool>());
	}

	private static async Task AssertPdbAbsentAsync(LiveMcpInstanceClient instance)
	{
		JsonNode before = await ReadPdbLayoutAsync(instance);
		Assert.False(before["found"]!.GetValue<bool>());
		Assert.Empty(before["elements"]!.AsArray());
	}

	private static Task<JsonNode> ReadPdbLayoutAsync(LiveMcpInstanceClient instance) => CallAsync(instance,
		CheatEngineToolNames.StructureGetPdbLayout,
		new Dictionary<string, object?> { ["typeName"] = "CemcpPdbFixtureLayout", ["limit"] = ExpectedLayout.Length });

	private static void AssertExactLayout(JsonNode layout)
	{
		Assert.True(layout["found"]!.GetValue<bool>());
		Assert.False(layout["truncated"]!.GetValue<bool>());
		JsonArray fields = layout["elements"]!.AsArray();
		Assert.Equal(ExpectedLayout.Length, fields.Count);
		for (int index = 0; index < ExpectedLayout.Length; index++)
		{
			Assert.Equal(ExpectedLayout[index], (fields[index]!["name"]!.GetValue<string>(),
				fields[index]!["offset"]!.GetValue<string>(), fields[index]!["valueType"]!.GetValue<string>()));
		}
	}

	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool,
		IReadOnlyDictionary<string, object?> arguments = null!) =>
		await instance.CallToolAsync(tool, arguments) ?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
}
