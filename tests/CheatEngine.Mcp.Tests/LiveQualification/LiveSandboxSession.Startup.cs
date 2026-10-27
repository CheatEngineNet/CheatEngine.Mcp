using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

namespace CheatEngine.Mcp.Tests.LiveQualification;

internal sealed partial class LiveSandboxSession
{
	public static async Task<LiveSandboxSession> StartAsync(LiveQualificationInputs inputs,
		LiveSandboxOptions? options = null)
	{
		LiveSandboxOptions selected = options ?? LiveSandboxOptions.Smoke;
		selected.ValidateFor(inputs.Scenario);
		if (selected.UsesTargetFirstExecutionShutdown && inputs.TargetArchitecture != "x64")
		{
			throw new InvalidOperationException("The fixed execution qualification supports only the owned x64 target.");
		}
		if (selected.CompilerQualification)
		{
			LiveCompilerPayload.RequireReviewedHashes();
			LiveCompilerBridge.RequireReviewedHash();
		}

		RequireNoCheatEngine();
		FileStream runLock = new(Path.Combine(Path.GetTempPath(), "CheatEngine.Mcp.LiveTests.lock"),
			FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		LiveSandboxSession? sandbox = null;
		try
		{
			SandboxLayout layout = SandboxLayout.Create(inputs.RunRoot, DateTimeOffset.UtcNow);
			sandbox = new LiveSandboxSession(layout, runLock, selected);
			Console.WriteLine($"Live evidence: {layout.RunDirectory}");
			await sandbox.StartCoreAsync(inputs);
			return sandbox;
		}
		catch (Exception exception)
		{
			if (sandbox is not null)
			{
				try
				{
					sandbox.Record("startupFailure", exception.ToString());
				}
				finally
				{
					await sandbox.DisposeAsync();
				}
			}
			else
			{
				runLock.Dispose();
			}

			throw;
		}
	}

	private async Task StartCoreAsync(LiveQualificationInputs inputs)
	{
		string repository = inputs.RepositoryRoot;
		_repository = repository;
		TargetArchitecture = inputs.TargetArchitecture;
		_targetPath = TargetExecutablePath(repository, TargetArchitecture);
		if (!File.Exists(_targetPath))
		{
			throw new FileNotFoundException("Build the selected disposable live target before live qualification.", _targetPath);
		}
		if (TargetArchitecture == "x86")
		{
			_targetHostPath = X86DotnetHostPath();
			if (!File.Exists(_targetHostPath))
			{
				throw new FileNotFoundException("The x86 .NET host is required for the selected disposable target.",
					_targetHostPath);
			}
			using FileStream targetStream = File.OpenRead(_targetPath);
			using PEReader targetImage = new(targetStream);
			if (PortableExecutableInspector.Instance.Describe(_targetHostPath).Machine != "I386"
				|| targetImage.PEHeaders.CoffHeader.Machine != Machine.I386
				|| targetImage.PEHeaders.CorHeader is not { } managedHeader
				|| (managedHeader.Flags & CorFlags.Requires32Bit) == 0)
			{
				throw new InvalidOperationException("The selected x86 fixture must require 32-bit execution and use an x86 .NET host.");
			}
		}
		else if (PortableExecutableInspector.Instance.Describe(_targetPath).Machine != "Amd64")
		{
			throw new InvalidOperationException("The selected x64 fixture must use an x64 application host.");
		}
		if (_options.LifecycleQualification || _options.WorkflowQualification)
		{
			// Validate the metadata oracle before launching a host or changing any user settings.
			_ = LiveDotNetMetadataQualification.ReadExpectation(TargetManagedAssemblyPath);
		}
		_source = inputs.CheatEngineDirectory;
		if (LiveQualificationOptIn.IsSameOrBelow(_layout.RunRoot, _source)
			|| LiveQualificationOptIn.IsSameOrBelow(_source, _layout.RunRoot))
		{
			throw new InvalidOperationException("The CE installation and live-run directory must be separate.");
		}

		string hostPath = Path.Combine(_source, "cheatengine-x86_64.exe");
		ExecutableFacts facts = PortableExecutableInspector.Instance.Describe(hostPath);
		if (facts.Machine != "Amd64" || !Version.TryParse(facts.FileVersion, out Version? version) ||
			version < new Version(7, 7))
		{
			throw new InvalidOperationException("Live smoke tests require Cheat Engine 7.7 or later, x64.");
		}

		_profile = new CheatEngineProfile("mcp-installed-host", "cheatengine-x86_64.exe",
			CheatEngineInstallation.Sha256(hostPath), facts.FileVersion!, "Amd64", new Dictionary<string, string>());
		_fingerprint = CheatEngineInstallation.Fingerprint(_source, _profile);
		_runtimeHash = HashIfExists(Path.Combine(_source, "ce.runtimeconfig.json"));
		Record("host", _profile);
		Record("owned_target", new
		{
			architecture = TargetArchitecture,
			imageFileName = Path.GetFileName(TargetImagePath),
			imageSha256 = CheatEngineInstallation.Sha256(TargetImagePath),
			managedAssemblyFileName = Path.GetFileName(TargetManagedAssemblyPath),
			managedAssemblySha256 = CheatEngineInstallation.Sha256(TargetManagedAssemblyPath)
		});
		if (_options.CompilerQualification)
		{
			CreateCompilerRoots("A");
			CreateCompilerRoots("B");
			Record("compiler_configuration", new
			{
				targetCodeExecution = _options.EnableTargetCodeExecution,
				unsafeLua = false,
				autoAssembler = false,
				kernelAccess = false
			});
		}
		if (_options.SymbolFixtureQualification || _options.SymbolSourcesQualification)
		{
			string fixtureRoot = Path.Combine(_layout.RunDirectory, "native-symbol-fixture");
			_nativeSymbolFixture = await LiveNativeSymbolFixture.BuildAsync(repository, fixtureRoot, CancellationToken.None);
			Record("native_symbol_fixture", new
			{
				_nativeSymbolFixture.DllSha256,
				_nativeSymbolFixture.PdbSha256,
				_nativeSymbolFixture.CodeView
			});
		}
		if (_options.SymbolSourcesQualification)
		{
			PrepareSymbolSourceFixture(inputs);
		}
		Directory.CreateDirectory(InstanceDirectory);
		PrepareDistribution(repository);
		PrepareHostInstallation("A");
		PrepareHostInstallation("B");
		if (_options.SymbolSourcesQualification)
		{
			LiveSymbolSourceNetworkProbeStage probe = await LiveSymbolSourceNetworkProbe.StageAsync(repository,
				_layout.RunDirectory, TestContext.Current.CancellationToken);
			probe.CopyToPrivateHost(HostInstallationDirectory("A"));
			probe.CopyToPrivateHost(HostInstallationDirectory("B"));
			Record("symbol_source_network_probe_stage", new
			{
				probe.ManifestPath,
				probe.SourceSha256,
				probe.DllSha256
			});
			ActivateSymbolSourceContainment(inputs);
		}
		RequireNoCheatEngine();
		RegistryTreeSnapshot current =
			RegistrySnapshot.Capture(CheatEngineUserStateLocations.CheatEngineRegistrySubKey);
		string[] pluginKeys = current.Root?.Keys.Select(key => key.Name)
			.Where(name => name.StartsWith("Plugin", StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
		string[] pluginValues = current.Root?.Values.Select(value => value.Name)
			.Where(name => name.StartsWith("Plugin", StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
		_stateGuard = new CheatEngineRegistryGuard(CheatEngineUserStateLocations.Workstation, pluginKeys, pluginValues);
		_debugOutput = DebugOutputCapture.Start(DebugOutputBuffer.SystemAnsiEncoding());
		_userState = _stateGuard.Begin(_layout);
		await StartHostAsync(repository, "A");
		await StartHostAsync(repository, "B");
		Record("instances",
			_hosts.Values.Select(host => new
			{
				host.Public.Name,
				host.Public.InstanceId,
				host.Public.ProcessId,
				host.Public.TargetProcessId,
				port = new Uri(host.Public.Endpoint).Port
			}).ToArray());
	}
}
