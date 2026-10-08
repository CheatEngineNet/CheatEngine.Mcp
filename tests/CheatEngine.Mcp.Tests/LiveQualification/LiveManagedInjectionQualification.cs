using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Closed-world preparation for the separately admitted managed-injection qualification.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveManagedInjectionQualification
{
	internal const string TypeName = "CheatEngineMcpCompileCsProbe";
	internal const string MethodName = "Run";
	internal const string Parameter = "CEMCP-COMPILECS-PROBE-V1";
	internal const string ExpectedResult = "7319";
	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		if (inputs.Scenario != LiveQualificationScenario.CompilerInjection)
		{
			throw new InvalidOperationException("Managed injection is admitted only by compiler-injection.");
		}
		object candidate = LiveCompilerCandidate.Verify(inputs);
		LiveFrameworkFixture fixture = RequireFixture(inputs);
		LiveSandboxOptions options = new(true, true)
		{
			EnableManagedInjection = true,
			Variant = LiveCompilerVariant.HeldExportAfterB,
			TempTopology = CompilerTempTopology.Shared
		};
		await using LiveSandboxSession sandbox = await LiveSandboxSession.StartAsync(inputs, options);
		LiveFrameworkTargetSession target = await LiveFrameworkTargetSession.StartAsync(sandbox.RunDirectory, fixture,
			TestContext.Current.CancellationToken);
		try
		{
			await using LiveMcpClient gateway = await LiveMcpClient.ConnectGatewayAsync(sandbox.GatewayExecutablePath,
				sandbox.InstanceDirectory, TestContext.Current.CancellationToken);
			LiveMcpInstanceClient instanceA = gateway.Bind(sandbox.HostA.InstanceId);
			LiveMcpInstanceClient instanceB = gateway.Bind(sandbox.HostB.InstanceId);
			await AttachAsync(instanceA, target.ProcessId);
			await AttachAsync(instanceB, sandbox.HostB.TargetProcessId);
			await AssertAttachedAsync(instanceB, sandbox.HostB.TargetProcessId);
			JsonNode? runtime = await instanceA.CallToolAsync(CheatEngineToolNames.RuntimeGetInfo);
			JsonNode gates = runtime?["gates"] ?? throw new InvalidDataException("Injection runtime did not report gates.");
			if (!gates["targetCodeExecution"]!.GetValue<bool>() || gates["unsafeLua"]!.GetValue<bool>() ||
				gates["autoAssembler"]!.GetValue<bool>() || gates["kernelAccess"]!.GetValue<bool>())
			{
				throw new InvalidOperationException("Injection runtime gates differ from the reviewed admission.");
			}

			LiveHeldCompilerExport export = await LiveCompilerQualification.CompileHeldExportAsync(sandbox, gateway, instanceA)
				?? throw new InvalidOperationException("B shutdown expired the raw assembly; injection was not attempted and remains unqualified.");
			await AssertAttachedAsync(instanceA, target.ProcessId);
			await VerifyOneFixedInjectionAsync(sandbox, instanceA, export, fixture);
			target.RequireAlive();
			sandbox.Record("managed_injection_scenario", new
			{
				candidate,
				fixture.Architecture,
				fixture.Sha256,
				target.ProcessId,
				bClosedBeforeExport = true,
				separateInjection = true,
				fixtureRuntime = target.RuntimeVersion
			});
		}
		finally
		{
			await target.DisposeAsync();
		}
		sandbox.MarkPassed();
	}

	private static async Task AttachAsync(LiveMcpInstanceClient instance, int processId)
	{
		JsonNode? opened = await instance.CallToolAsync(CheatEngineToolNames.ProcessAttach,
			new Dictionary<string, object?> { ["process"] = processId.ToString(CultureInfo.InvariantCulture) });
		if (opened?["processId"]?.GetValue<int>() != processId)
		{
			throw new InvalidOperationException("A did not attach to the owned Framework fixture.");
		}
	}

	private static async Task AssertAttachedAsync(LiveMcpInstanceClient instance, int processId)
	{
		JsonNode? overview = await instance.CallToolAsync(CheatEngineToolNames.RuntimeGetOverview);
		if (overview?["process"]?["processId"]?.GetValue<int>() != processId)
		{
			throw new InvalidOperationException("The isolation target changed unexpectedly.");
		}
	}

	/// <summary>Checks the reviewed Framework fixture before a caller starts an owned injection session.</summary>
	internal static LiveFrameworkFixture RequireFixture(LiveQualificationInputs inputs)
	{
		if (inputs.Scenario != LiveQualificationScenario.CompilerInjection)
		{
			throw new InvalidOperationException("The Framework fixture is admitted only to compiler-injection.");
		}
		string root = Path.Combine(inputs.RepositoryRoot, "artifacts", "live-framework-target", inputs.TargetArchitecture);
		string executable = Path.Combine(root, "CheatEngine.Mcp.LiveFrameworkTarget.exe");
		string identity = Path.Combine(root, "fixture-identity.json");
		if (!File.Exists(executable) || !File.Exists(identity))
		{
			throw new FileNotFoundException("Build the fixed Framework fixture with eng/Build-LiveFrameworkTarget.ps1 before injection qualification.", executable);
		}
		LiveCompilerCandidate.RequireNoReparseAncestors(executable);
		LiveCompilerCandidate.RequireNoReparseAncestors(identity);
		if (new FileInfo(identity).Length > 4096 || new FileInfo(executable).Length is <= 0 or > 1024 * 1024)
		{
			throw new InvalidDataException("The fixed Framework fixture or identity exceeds its size bound.");
		}
		JsonNode document = JsonNode.Parse(File.ReadAllText(identity))
			?? throw new InvalidDataException("The fixed Framework fixture identity file is empty.");
		string architecture = document["architecture"]?.GetValue<string>() ?? string.Empty;
		string expectedHash = document["targetSha256"]?.GetValue<string>() ?? string.Empty;
		string expectedSource = document["sourceSha256"]?.GetValue<string>() ?? string.Empty;
		string source = Path.Combine(inputs.RepositoryRoot, "eng", "LiveFrameworkTarget", "Program.cs");
		string actualSource = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
		string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable)));
		if (architecture != inputs.TargetArchitecture || !string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(expectedSource, actualSource, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("The fixed Framework fixture identity does not match its selected architecture or bytes.");
		}
		using FileStream image = File.OpenRead(executable);
		using PEReader pe = new(image);
		if (pe.PEHeaders.CorHeader is not { } cor ||
			(architecture == "x86" && (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || (cor.Flags & CorFlags.Requires32Bit) == 0)) ||
			(architecture == "x64" && pe.PEHeaders.CoffHeader.Machine != Machine.Amd64))
		{
			throw new InvalidDataException("The Framework fixture PE architecture does not match its declared identity.");
		}
		return new LiveFrameworkFixture(executable, actualHash, inputs.TargetArchitecture);
	}

	/// <summary>Issues the one approved fixed method call; callers must have compiled and held the artifact already.</summary>
	internal static async Task VerifyOneFixedInjectionAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance,
		LiveHeldCompilerExport export, LiveFrameworkFixture fixture)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(fixture);
		string exportedAssembly = export.Path;
		if (!Path.IsPathFullyQualified(exportedAssembly) || !File.Exists(exportedAssembly))
		{
			throw new FileNotFoundException("The fixed compiler export was not retained for injection.", exportedAssembly);
		}
		if (!LiveQualificationOptIn.IsSameOrBelow(exportedAssembly, sandbox.CompilerA.Output))
		{
			throw new InvalidDataException("The injection assembly escaped the owned compiler output root.");
		}
		LiveCompilerCandidate.RequireNoReparseAncestors(exportedAssembly);
		using FileStream heldAssembly = new(exportedAssembly, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (heldAssembly.Length != export.Length || Convert.ToHexString(SHA256.HashData(heldAssembly)) != export.Sha256)
		{
			throw new InvalidDataException("The fixed compiler export changed before injection.");
		}
		LiveMcpToolResult result = await instance.CallToolRawAsync(CheatEngineToolNames.ExecInjectDotNet,
			new Dictionary<string, object?>
			{
				["assemblyPath"] = exportedAssembly,
				["className"] = TypeName,
				["methodName"] = MethodName,
				["parameter"] = Parameter
			});
		if (result.IsError)
		{
			throw new InvalidOperationException("The fixed managed injection did not succeed: " + result.ErrorText);
		}
		JsonNode payload = result.Payload ?? throw new InvalidDataException("Managed injection returned no structured payload.");
		if (payload["assemblyPath"]?.GetValue<string>() != exportedAssembly ||
			payload["className"]?.GetValue<string>() != TypeName || payload["methodName"]?.GetValue<string>() != MethodName ||
			payload["result"]?.GetValue<string>() != ExpectedResult)
		{
			throw new InvalidDataException("Managed injection did not confirm the one reviewed entry point and return value.");
		}
		sandbox.Record("managed_injection_fixed_result", new
		{
			fixture.Architecture,
			fixture.Sha256,
			assemblyPath = exportedAssembly,
			TypeName,
			MethodName,
			Parameter,
			result = ExpectedResult
		});
	}
}

internal sealed record LiveFrameworkFixture(string ExecutablePath, string Sha256, string Architecture);

/// <summary>Owns one no-input Framework fixture below the active run and always sends its adjacent stop marker.</summary>
[SupportedOSPlatform("windows")]
internal sealed class LiveFrameworkTargetSession : IAsyncDisposable
{
	private readonly string _stop;
	private readonly Process _process;
	private bool _disposed;
	private LiveFrameworkTargetSession(Process process, string stop)
	{
		_process = process;
		_stop = stop;
	}
	internal int ProcessId => _process.Id;
	internal void RequireAlive()
	{
		if (_disposed || _process.HasExited)
		{
			throw new InvalidOperationException("The owned Framework fixture exited before its explicit graceful stop.");
		}
	}
	internal string RuntimeVersion { get; private set; } = string.Empty;

	internal static async Task<LiveFrameworkTargetSession> StartAsync(string runDirectory, LiveFrameworkFixture fixture, CancellationToken token)
	{
		string manifest = Path.Combine(runDirectory, "framework-injection-target.json");
		if (!Path.IsPathFullyQualified(manifest) || !LiveQualificationOptIn.IsSameOrBelow(manifest, runDirectory))
		{
			throw new InvalidOperationException("Framework fixture manifest escaped the live run.");
		}

		string executable = Path.Combine(runDirectory, "CheatEngine.Mcp.LiveFrameworkTarget.exe");
		LiveCompilerCandidate.RequireNoReparseAncestors(manifest);
		LiveCompilerCandidate.RequireNoReparseAncestors(executable);
		if (File.Exists(manifest) || File.Exists(manifest + ".tmp") || File.Exists(manifest + ".stop"))
		{
			throw new InvalidOperationException("The Framework fixture paths must be unused before startup.");
		}
		File.Copy(fixture.ExecutablePath, executable, false);
		using (FileStream copied = File.OpenRead(executable))
		{
			if (Convert.ToHexString(SHA256.HashData(copied)) != fixture.Sha256)
			{
				throw new InvalidDataException("The owned Framework fixture copy changed before startup.");
			}
		}
		ProcessStartInfo start = new(executable)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			WorkingDirectory = runDirectory
		};
		foreach (string key in start.Environment.Keys.Where(key => key.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)
			|| key.StartsWith("MCP_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("CHEATENGINE_", StringComparison.OrdinalIgnoreCase)
			|| key.StartsWith("COR_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase)
			|| key.StartsWith("COMPLUS_", StringComparison.OrdinalIgnoreCase)).ToArray())
		{
			start.Environment.Remove(key);
		}

		start.ArgumentList.Add(manifest);
		Process process = Process.Start(start) ?? throw new InvalidOperationException("The owned Framework fixture did not start.");
		LiveFrameworkTargetSession session = new(process, manifest + ".stop");
		try
		{
			Stopwatch lifetime = Stopwatch.StartNew();
			while (!File.Exists(manifest))
			{
				if (process.HasExited || lifetime.Elapsed >= TimeSpan.FromSeconds(10))
				{
					throw new TimeoutException("The owned Framework fixture did not publish its manifest.");
				}

				await Task.Delay(50, token);
			}
			LiveCompilerCandidate.RequireNoReparseAncestors(manifest);
			using FileStream input = new(manifest, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (input.Length is <= 0 or > 4096)
			{
				throw new InvalidDataException("The Framework fixture manifest exceeds its bound.");
			}

			using StreamReader reader = new(input, new UTF8Encoding(false, true), false);
			JsonNode identity = JsonNode.Parse(await reader.ReadToEndAsync(token))
				?? throw new InvalidDataException("The Framework fixture manifest is empty.");
			string runtime = identity["runtime"]?.GetValue<string>() ?? string.Empty;
			if (identity["processId"]?.GetValue<int>() != process.Id ||
				identity["pointerWidth"]?.GetValue<int>() != (fixture.Architecture == "x86" ? 32 : 64) ||
				identity["sha256"]?.GetValue<string>() != fixture.Sha256 ||
				identity["assembly"]?.GetValue<string>() != "CheatEngine.Mcp.LiveFrameworkTarget" ||
				!Version.TryParse(runtime, out Version? parsedRuntime) || parsedRuntime.Major != 4)
			{
				throw new InvalidDataException("The Framework target process, architecture, CLR or executable hash differs from the reviewed fixture.");
			}
			session.RuntimeVersion = runtime;
			return session;
		}
		catch { await session.DisposeAsync(); throw; }
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		try
		{
			if (_process.HasExited)
			{
				throw new InvalidOperationException("The owned Framework fixture exited prematurely; cleanup cannot qualify it.");
			}
			try
			{
				LiveCompilerCandidate.RequireNoReparseAncestors(_stop);
				File.WriteAllText(_stop, "stop");
				await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
				if (_process.ExitCode != 0)
				{
					throw new InvalidOperationException("The owned Framework fixture returned a nonzero exit code.");
				}
			}
			catch
			{
				if (!_process.HasExited)
				{
					_process.Kill(true);
				}
				await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
				throw;
			}
		}
		finally { _process.Dispose(); }
	}
}
