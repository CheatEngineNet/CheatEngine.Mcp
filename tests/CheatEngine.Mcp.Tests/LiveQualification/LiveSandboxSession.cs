using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Owns two isolated CE hosts and targets beneath one guarded live-test run.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class LiveSandboxSession : IAsyncDisposable
{
	private const string PluginFileName = "CheatEngine.Mcp.dll";

	private static readonly string[] RemovedEnvironmentPrefixes =
		["DOTNET_", "MSBUILD", "TESTINGPLATFORM", "VSTEST", "CHEATENGINE_", "CE_SDK_", "CECLIENT_", "MCP_"];

	private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };
	private readonly Dictionary<string, OwnedHost> _hosts = new(StringComparer.Ordinal);
	private readonly SandboxLayout _layout;
	private readonly Dictionary<string, object?> _report = new(StringComparer.Ordinal);
	private readonly FileStream _runLock;
	private readonly LiveSandboxOptions _options;
	private DebugOutputCapture? _debugOutput;
	private bool _disposed;
	private InstallationFingerprint? _fingerprint;
	private bool _passed;
	private CheatEngineProfile? _profile;
	private string? _runtimeHash;
	private string? _repository;
	private string? _targetHostPath;
	private string? _targetPath;
	private string _targetArchitecture = "x64";
	private string? _source;
	private CheatEngineRegistryGuard? _stateGuard;
	private ICheatEngineUserStateScope? _userState;

	private LiveSandboxSession(SandboxLayout layout, FileStream runLock, LiveSandboxOptions options)
	{
		_layout = layout;
		_runLock = runLock;
		_options = options;
		_report["schema"] = "cheatengine-mcp-live-multi-instance/v1";
		_report["runId"] = layout.RunId;
		InstanceDirectory = Path.Combine(layout.RunDirectory, "instances");
		GatewayExecutablePath = Path.Combine(layout.RunDirectory, "gateway", "CheatEngine.Mcp.Gateway.exe");
	}

	public string InstanceDirectory
	{
		get;
	}

	public string GatewayExecutablePath
	{
		get;
	}

	public string TargetArchitecture => _targetArchitecture;

	public string RunDirectory => _layout.RunDirectory;

	public LiveCompilerRoots CompilerA => CompilerRoots("A");

	public LiveCompilerRoots CompilerB => CompilerRoots("B");

	public LiveSandboxHost HostA => GetHost("A");
	public LiveSandboxHost HostB => GetHost("B");

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		List<Exception> failures = [];
		try
		{
			foreach (string name in _hosts.Keys.ToArray())
			{
				try
				{
					await StopHostAsync(name);
				}
				catch (Exception exception)
				{
					failures.Add(exception);
				}
			}

			if (_debugOutput is not null)
			{
				foreach (OwnedHost host in _hosts.Values)
				{
					string debugPath = Path.Combine(_layout.RunDirectory, $"sdk-debug-{host.Public.Name}.log");
					TryCleanup(() => _debugOutput.WriteTo(debugPath, host.Public.ProcessId), failures);
					if (host.Public.ProcessId > 0)
					{
						TryCleanup(() => VerifyLifecycleEvidence(host.Public.Name, debugPath), failures);
					}
				}

				TryCleanup(_debugOutput.Dispose, failures);
			}

			if (_stateGuard is not null && _hosts.Values.All(host => host.Stopped))
			{
				try
				{
					RequireNoCheatEngine();
					RestoreUserState();
				}
				catch (Exception exception)
				{
					failures.Add(exception);
				}
			}
			else if (_stateGuard is not null)
			{
				failures.Add(new InvalidOperationException(
					"An owned CE host remains active; the settings backup and recovery marker are retained until it stops."));
			}

			if (_source is not null && _profile is not null && _fingerprint is not null)
			{
				TryCleanup(() =>
				{
					IReadOnlyList<string> differences = CheatEngineInstallation.Compare(_fingerprint,
						CheatEngineInstallation.Fingerprint(_source, _profile));
					bool unchanged = differences.Count == 0 &&
									 _runtimeHash == HashIfExists(Path.Combine(_source, "ce.runtimeconfig.json"));
					_report["sourceInstallationUnchanged"] = unchanged;
					if (!unchanged)
					{
						throw new InvalidOperationException("The source CE installation changed during the run.");
					}
				}, failures);
			}

			if (_options.CompilerQualification && _hosts.Values.All(host => host.Stopped))
			{
				TryCleanup(RestorePrivateCompilerVariants, failures);
			}

			if (_options.CompilerQualification && failures.Count == 0 && _hosts.Values.All(host => host.Stopped))
			{
				TryCleanup(CleanupCompilerRoots, failures);
			}
		}
		finally
		{
			_report["passed"] = _passed && failures.Count == 0;
			_report["cleanupFailures"] = failures.Select(exception => exception.ToString()).ToArray();
			try
			{
				File.WriteAllText(_layout.SummaryPath, JsonSerializer.Serialize(_report, ReportJson));
			}
			finally
			{
				_runLock.Dispose();
			}
		}

		if (failures.Count != 0)
		{
			throw new AggregateException(
				"Live test cleanup failed; inspect summary.json and any retained recovery marker.", failures);
		}
	}

	public static async Task<LiveSandboxSession> StartAsync(LiveQualificationInputs inputs,
		LiveSandboxOptions? options = null)
	{
		LiveSandboxOptions selected = options ?? LiveSandboxOptions.Smoke;
		selected.ValidateFor(inputs.Scenario);
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

	public LiveSandboxHost GetHost(string name)
	{
		return _hosts.TryGetValue(name, out OwnedHost? host)
			? host.Public
			: throw new InvalidOperationException($"Owned host '{name}' has not started.");
	}

	public async Task StopHostAsync(string name)
	{
		if (!_hosts.TryGetValue(name, out OwnedHost? host))
		{
			throw new InvalidOperationException($"Owned host '{name}' has not started.");
		}

		if (host.Stopped)
		{
			return;
		}

		List<Exception> failures = [];
		TryCleanup(() => File.WriteAllText(host.StopPath, "stop"), failures);
		bool hostStopped = await StopOwnedAsync(host.Process, failures);
		if (hostStopped)
		{
			host.Process = null;
			TryCleanup(() => File.WriteAllText(host.TargetManifest + ".stop", "stop"), failures);
			bool targetStopped = await StopOwnedAsync(host.Target, failures);
			if (targetStopped)
			{
				host.Target = null;
			}

			host.Stopped = targetStopped;
		}

		Record($"host_{name}_stopped", new
		{
			host.Public.ProcessId,
			host.Public.TargetProcessId
		});
		if (failures.Count != 0)
		{
			throw new AggregateException($"Live host {name} did not stop cleanly.", failures);
		}
	}

	/// <summary>Runs one fixed compiler availability or compile probe through the test-owned private autorun bridge.</summary>
	public async Task<JsonNode> CompilerProbeAsync(string hostName, string command)
	{
		if (!_options.CompilerQualification || command is not ("status" or "compile" or "armHold"))
		{
			throw new ArgumentOutOfRangeException(nameof(command));
		}

		if (!_hosts.TryGetValue(hostName, out OwnedHost? host) || host.Process is null || host.Stopped)
		{
			throw new InvalidOperationException($"Owned host '{hostName}' is not running.");
		}

		string request = Path.Combine(_layout.RunDirectory, $"compiler-request-{hostName}.txt");
		string response = Path.Combine(_layout.RunDirectory, $"compiler-response-{hostName}.txt");
		RequireInsideRun(request);
		RequireInsideRun(response);
		File.Delete(response);
		await File.WriteAllTextAsync(request + ".tmp", command);
		File.Move(request + ".tmp", request);
		await WaitUntilAsync(() => File.Exists(response), host.Process, TimeSpan.FromSeconds(10));
		return await LiveCompilerProbeResponse.ReadAsync(response, command);
	}

	/// <summary>Restarts only the disposable target for one still-running private CE host and publishes a fresh manifest.</summary>
	public async Task RestartTargetAsync(string name)
	{
		if (!_hosts.TryGetValue(name, out OwnedHost? host) || host.Stopped || host.Process is null || host.Process.HasExited)
		{
			throw new InvalidOperationException($"Owned host '{name}' is not running.");
		}

		List<Exception> failures = [];
		TryCleanup(() => File.WriteAllText(host.TargetManifest + ".stop", "stop"), failures);
		bool previousStopped = await StopOwnedAsync(host.Target, failures);
		if (previousStopped)
		{
			host.Target = null;
		}
		if (!previousStopped || failures.Count != 0)
		{
			throw new AggregateException($"Disposable target {name} did not stop cleanly.", failures);
		}

		int incarnation = host.TargetIncarnation + 1;
		string manifest = Path.Combine(_layout.RunDirectory, $"target-{name}-{incarnation}.json");
		RequireInsideRun(manifest);
		Process target = StartTarget(manifest, name, _options.UseSoakLifetime);
		host.Target = target;
		try
		{
			await WaitUntilAsync(() => File.Exists(manifest), target, TimeSpan.FromSeconds(10));
			LiveTargetManifest facts = ReadTargetManifest(JsonNode.Parse(await File.ReadAllTextAsync(manifest))!, target.Id, name);
			host.TargetManifest = manifest;
			host.TargetIncarnation = incarnation;
			host.Public = host.Public with
			{
				TargetIncarnation = incarnation,
				TargetProcessId = target.Id,
				TargetAddress = facts.Address,
				PointerRootAddress = facts.PointerRootAddress,
				PointerTargetAddress = facts.PointerTargetAddress,
				ZeroPointerRootAddress = facts.ZeroPointerRootAddress,
				UnreadableRootAddress = facts.UnreadableRootAddress
			};
			Record($"target_{name}_restart", new
			{
				incarnation,
				processId = target.Id,
				address = facts.Address
			});
		}
		catch (Exception exception)
		{
			List<Exception> cleanupFailures = [];
			bool stopped = await StopOwnedAsync(target, cleanupFailures);
			if (stopped)
			{
				host.Target = null;
			}
			Record($"target_{name}_restart_failure", new
			{
				exception = exception.ToString(),
				cleanupFailures = cleanupFailures.Select(item => item.ToString()).ToArray()
			});
			if (cleanupFailures.Count != 0)
			{
				throw new AggregateException($"Disposable target {name} restart and cleanup failed.", [exception, .. cleanupFailures]);
			}

			throw;
		}
	}

	private async Task StartCoreAsync(LiveQualificationInputs inputs)
	{
		string repository = inputs.RepositoryRoot;
		_repository = repository;
		_targetArchitecture = inputs.TargetArchitecture;
		_targetPath = TargetExecutablePath(repository, _targetArchitecture);
		if (!File.Exists(_targetPath))
		{
			throw new FileNotFoundException("Build the selected disposable live target before live qualification.", _targetPath);
		}
		if (_targetArchitecture == "x86")
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
		Directory.CreateDirectory(InstanceDirectory);
		PrepareDistribution(repository);
		PrepareHostInstallation("A");
		PrepareHostInstallation("B");
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

	private void PrepareDistribution(string repository)
	{
#if DEBUG
		const string configuration = "debug";
#else
		const string configuration = "release";
#endif
		string distribution = _options.DistributionDirectoryOverride ?? DistributionDirectory(repository, configuration);
		ValidateDistribution(distribution);
		string gatewaySource = Path.Combine(distribution, "CheatEngine.Mcp.Gateway.exe");
		if (!File.Exists(gatewaySource))
		{
			throw new FileNotFoundException("Build the self-contained gateway distribution before live qualification.",
				gatewaySource);
		}

		Directory.CreateDirectory(Path.GetDirectoryName(GatewayExecutablePath)!);
		File.Copy(gatewaySource, GatewayExecutablePath);
		Record("gateway_distribution",
			new
			{
				file = Path.GetFileName(GatewayExecutablePath),
				sha256 = CheatEngineInstallation.Sha256(GatewayExecutablePath)
			});
	}

	private void PrepareHostInstallation(string name)
	{
		string installation = HostInstallationDirectory(name);
		CheatEngineInstallation.CopyTo(_source!, installation, _profile!, PortableExecutableInspector.Instance);
		ApplyPrivateCompilerAbsentVariant(name, installation);
		string autorun = Path.Combine(installation, "autorun");
		string disabledAutorun = Path.Combine(installation, "autorun.disabled");
		RequireInsideRun(autorun);
		RequireInsideRun(disabledAutorun);
		if (Directory.Exists(autorun))
		{
			Directory.Move(autorun, disabledAutorun);
		}

		Directory.CreateDirectory(autorun);
		CopyReviewedAutorun(disabledAutorun, autorun, "celib.lua",
			"5871F4E9F6C06B5811C1F4B208B5D6869C5191E7CB5540F2B463D01E5541FAC2");
		CopyReviewedAutorun(disabledAutorun, autorun, "monoscript.lua",
			"F139E50B788C85A15ACFA92B892FCCBC3DECA6D8D609AD9E5428B4C336C90600");
		CopyReviewedAutorun(disabledAutorun, autorun, "SpeedhackV3.lua",
			"69DE7EE3F4563005B5BC34A27F720D9D6E714A14FE7D6E1C47D5CF0AECD93D9C");
		File.WriteAllText(Path.Combine(installation, "ce.runtimeconfig.json"), """
		                                                                       {"runtimeOptions":{"tfm":"net10.0","rollForward":"LatestMinor","frameworks":[
		                                                                       {"name":"Microsoft.NETCore.App","version":"10.0.0"},
		                                                                       {"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"},
		                                                                       {"name":"Microsoft.AspNetCore.App","version":"10.0.0"}]}}
		                                                                       """);
	}

	private async Task StartHostAsync(string repository, string name)
	{
#if DEBUG
		const string configuration = "debug";
#else
		const string configuration = "release";
#endif
		string distribution = _options.DistributionDirectoryOverride ?? DistributionDirectory(repository, configuration);
		ValidateDistribution(distribution);
		string pluginSource = distribution;
		if (!File.Exists(Path.Combine(pluginSource, PluginFileName)))
		{
			throw new FileNotFoundException("Publish the plugin distribution before live qualification.",
				Path.Combine(pluginSource, PluginFileName));
		}

		string pluginDirectory = Path.Combine(_layout.PluginsDirectory, name, "CheatEngine.Mcp");
		RequireInsideRun(pluginDirectory);
		Directory.CreateDirectory(pluginDirectory);
		File.Copy(Path.Combine(pluginSource, PluginFileName), Path.Combine(pluginDirectory, PluginFileName));
		bool capabilityConfiguration = _options.CompilerQualification || _options.LifecycleQualification
			|| _options.PerformanceQualification || _options.SoakQualification;
		if (capabilityConfiguration)
		{
			string[] allowedRoots = _options.CompilerQualification ? [CompilerRoots(name).Output] : [];
			File.WriteAllText(Path.Combine(pluginDirectory, "appsettings.json"), JsonSerializer.Serialize(new
			{
				Mcp = new
				{
					EnableUnsafeLua = false,
					EnableAutoAssembler = false,
					EnableTargetCodeExecution = _options.CompilerQualification && _options.EnableTargetCodeExecution,
					EnableKernelAccess = false,
					Files = new
					{
						AllowedRoots = allowedRoots
					}
				},
				CheatEngineClient = new
				{
					AllowedTableRoots = Array.Empty<string>()
				}
			}));
		}
		string pluginPath = Path.Combine(pluginDirectory, PluginFileName);
		VerifyDistributionStaging(name, pluginSource, pluginDirectory);
		Record($"plugin_{name}_sha256", CheatEngineInstallation.Sha256(pluginPath));
		string stopPath = Path.Combine(_layout.RunDirectory, $"stop-{name}");
		string targetManifest = Path.Combine(_layout.RunDirectory, $"target-{name}.json");
		string driverPath = Path.Combine(HostInstallationDirectory(name), "autorun", "zz_cheatengine_mcp_live.lua");
		File.WriteAllText(driverPath, BuildDriver(stopPath, pluginPath, name));
		Process? target = null;
		Process? host = null;
		OwnedHost? owned = null;
		try
		{
			target = StartTarget(targetManifest, name, _options.UseSoakLifetime);
			owned = new OwnedHost(null, target, stopPath, targetManifest,
				new LiveSandboxHost(name, 0, target.Id, string.Empty, string.Empty, string.Empty, string.Empty,
					string.Empty, pluginPath, string.Empty, string.Empty));
			_hosts.Add(name, owned);
			await WaitUntilAsync(() => File.Exists(targetManifest), target, TimeSpan.FromSeconds(10));
			JsonNode manifest = JsonNode.Parse(await File.ReadAllTextAsync(targetManifest))!;
			LiveTargetManifest targetFacts = ReadTargetManifest(manifest, target.Id, name);
			ProcessStartInfo hostStart =
				CreateStartInfo(Path.Combine(HostInstallationDirectory(name), _profile!.HostExecutable));
			hostStart.Environment["MCP_HOST"] = "127.0.0.1";
			hostStart.Environment["MCP_PORT"] = "0";
			hostStart.Environment["MCP_DATA_DIRECTORY"] = Path.Combine(_layout.RunDirectory, "plugin-data", name);
			hostStart.Environment["MCP_INSTANCE_DIRECTORY"] = InstanceDirectory;
			hostStart.Environment["MCP_INSTANCE_NAME"] = $"Live Qualification {name}";
			if (_options.CompilerQualification)
			{
				LiveCompilerRoots roots = CompilerRoots(name);
				hostStart.Environment["TEMP"] = roots.Temp;
				hostStart.Environment["TMP"] = roots.Temp;
			}
			host = Process.Start(hostStart) ??
				   throw new InvalidOperationException($"Private CE host {name} did not start.");
			owned.Process = host;
			_debugOutput!.Buffer.Track(host.Id);
			owned.Public = owned.Public with
			{
				ProcessId = host.Id,
				TargetAddress = targetFacts.Address,
				PointerRootAddress = targetFacts.PointerRootAddress,
				PointerTargetAddress = targetFacts.PointerTargetAddress,
				ZeroPointerRootAddress = targetFacts.ZeroPointerRootAddress,
				UnreadableRootAddress = targetFacts.UnreadableRootAddress
			};
			InstanceDescriptor descriptor = await WaitForInstanceAsync(owned, TimeSpan.FromSeconds(45));
			owned.Public = owned.Public with
			{
				InstanceId = descriptor.InstanceId,
				Endpoint = descriptor.Endpoint
			};
			Record($"target_{name}", new
			{
				processId = target.Id,
				address = targetFacts.Address,
				pointerRootAddress = targetFacts.PointerRootAddress,
				pointerTargetAddress = targetFacts.PointerTargetAddress
			});
		}
		catch (Exception exception)
		{
			List<Exception> cleanupFailures = [];
			if (owned is not null)
			{
				try
				{
					await StopHostAsync(name);
				}
				catch (Exception cleanupException)
				{
					cleanupFailures.Add(cleanupException);
				}
			}
			else
			{
				if (host is not null)
				{
					await StopOwnedAsync(host, cleanupFailures);
				}

				if (target is not null)
				{
					await StopOwnedAsync(target, cleanupFailures);
				}
			}

			if (cleanupFailures.Count != 0)
			{
				throw new AggregateException($"Live host {name} startup and cleanup failed.",
					[exception, .. cleanupFailures]);
			}

			throw;
		}
	}

	private static string TargetExecutablePath(string repository, string architecture)
	{
#if DEBUG
		const string configuration = "debug";
#else
		const string configuration = "release";
#endif
		return architecture == "x86"
			? Path.Combine(repository, "artifacts", "live-target-x86", "bin", "CheatEngine.Mcp.LiveTarget", configuration,
				"CheatEngine.Mcp.LiveTarget.dll")
			: Path.Combine(repository, "artifacts", "bin", "CheatEngine.Mcp.LiveTarget", configuration,
				"CheatEngine.Mcp.LiveTarget.exe");
	}

	private static string X86DotnetHostPath() =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "dotnet.exe");

	private Process StartTarget(string manifest, string name, bool soakLifetime = false)
	{
		string target = _targetPath ?? throw new InvalidOperationException("Live target path was not prepared.");
		ProcessStartInfo start;
		if (_targetArchitecture == "x86")
		{
			start = CreateStartInfo(_targetHostPath ??
				throw new InvalidOperationException("The x86 .NET host was not prepared."));
			start.ArgumentList.Add(target);
		}
		else
		{
			start = CreateStartInfo(target);
		}

		if (soakLifetime)
		{
			start.ArgumentList.Add("--soak");
		}
		start.ArgumentList.Add(manifest);
		return Process.Start(start) ?? throw new InvalidOperationException($"Disposable target {name} did not start.");
	}

	private LiveTargetManifest ReadTargetManifest(JsonNode manifest, int processId, string name)
	{
		if (manifest["processId"]!.GetValue<int>() != processId)
		{
			throw new InvalidOperationException($"Target manifest {name} belongs to a different process.");
		}

		int width = manifest["pointerWidth"]!.GetValue<int>();
		if (width != (_targetArchitecture == "x86" ? 32 : 64))
		{
			throw new InvalidOperationException($"Target manifest {name} reported pointer width {width}, not the requested {_targetArchitecture} width.");
		}

		return new LiveTargetManifest(manifest["address"]!.GetValue<string>(),
			manifest["pointerRootAddress"]!.GetValue<string>(), manifest["pointerTargetAddress"]!.GetValue<string>(),
			manifest["zeroPointerRootAddress"]!.GetValue<string>(), manifest["unreadableRootAddress"]!.GetValue<string>());
	}

	internal static string DistributionDirectory(string repository, string configuration)
	{
		string? custom = Environment.GetEnvironmentVariable("CHEATENGINE_MCP_LIVE_QUALIFICATION_DISTRIBUTION_DIRECTORY");
		if (custom is not null && !Path.IsPathFullyQualified(custom))
		{
			throw new InvalidOperationException("The live qualification distribution directory must be absolute.");
		}

		return custom ?? Path.Combine(repository, "artifacts", "dist", configuration);
	}

	private void ValidateDistribution(string distribution)
	{
		if (!Path.IsPathFullyQualified(distribution) || !Directory.Exists(distribution))
		{
			throw new DirectoryNotFoundException("The selected live qualification distribution directory is unavailable.");
		}

		LiveCompilerCandidate.RequireNoReparseAncestors(distribution);
		string[] expected = [PluginFileName, "CheatEngine.Mcp.Gateway.exe", "LICENSE", "README.md", "THIRD-PARTY-NOTICES.md"];
		string[] actual = Directory.EnumerateFiles(distribution, "*", SearchOption.TopDirectoryOnly)
			.Select(Path.GetFileName).Where(name => name is not null).Select(name => name!).Order(StringComparer.Ordinal).ToArray();
		if (!actual.SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal)
			|| Directory.EnumerateDirectories(distribution, "*", SearchOption.TopDirectoryOnly).Any()
			|| actual.Any(name => new FileInfo(Path.Combine(distribution, name)).Length <= 0))
		{
			throw new InvalidOperationException("The selected distribution must contain exactly the five reviewed flat release files.");
		}

		string plugin = Path.Combine(distribution, PluginFileName);
		string gateway = Path.Combine(distribution, "CheatEngine.Mcp.Gateway.exe");
		string? pluginVersion = FileVersionInfo.GetVersionInfo(plugin).ProductVersion;
		string? gatewayVersion = FileVersionInfo.GetVersionInfo(gateway).ProductVersion;
		if (string.IsNullOrWhiteSpace(pluginVersion) || !string.Equals(pluginVersion, gatewayVersion, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("The selected plugin and gateway do not have the same source identity.");
		}

		Record("selected_distribution", new
		{
			directory = distribution,
			productVersion = pluginVersion,
			pluginSha256 = CheatEngineInstallation.Sha256(plugin),
			gatewaySha256 = CheatEngineInstallation.Sha256(gateway)
		});
	}

	private void VerifyDistributionStaging(string name, string pluginSource, string pluginDirectory)
	{
		string[] pluginFiles = RelativeFiles(pluginDirectory);
		bool capabilityConfiguration = _options.CompilerQualification || _options.LifecycleQualification
			|| _options.PerformanceQualification || _options.SoakQualification;
		string[] expectedPluginFiles = capabilityConfiguration
			? [PluginFileName, "appsettings.json"]
			: [PluginFileName];
		if (!pluginFiles.SequenceEqual(expectedPluginFiles, StringComparer.Ordinal)
			|| CheatEngineInstallation.Sha256(Path.Combine(pluginSource, PluginFileName)) !=
			CheatEngineInstallation.Sha256(Path.Combine(pluginDirectory, PluginFileName)))
		{
			throw new InvalidOperationException($"Live host {name} did not stage exactly the published single plugin DLL.");
		}

		string gatewayDirectory = Path.GetDirectoryName(GatewayExecutablePath)!;
		string[] gatewayFiles = Directory.EnumerateFileSystemEntries(gatewayDirectory)
			.Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray();
		if (!gatewayFiles.SequenceEqual(["CheatEngine.Mcp.Gateway.exe"], StringComparer.Ordinal))
		{
			throw new InvalidOperationException(
				"Live qualification staged files beyond the self-contained gateway executable.");
		}

		Record($"distribution_{name}", new
		{
			pluginFiles,
			gatewayFiles
		});
	}

	private static string[] RelativeFiles(string directory)
	{
		return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
			.Select(path => Path.GetRelativePath(directory, path)).Order(StringComparer.Ordinal).ToArray();
	}

	private async Task<InstanceDescriptor> WaitForInstanceAsync(OwnedHost host, TimeSpan timeout)
	{
		Process process = host.Process ??
						  throw new InvalidOperationException($"CE host {host.Public.Name} did not start.");
		InstanceRegistry registry = new(InstanceDirectory);
		Stopwatch elapsed = Stopwatch.StartNew();
		while (elapsed.Elapsed < timeout)
		{
			if (process.HasExited)
			{
				throw new InvalidOperationException(
					$"CE host {host.Public.Name} exited with {process.ExitCode}. Inspect {_layout.RunDirectory}.");
			}

			InstanceDescriptor? instance = registry.ReadActive().SingleOrDefault(candidate =>
				candidate.ProcessId == process.Id
				&& string.Equals(candidate.Name, $"Live Qualification {host.Public.Name}", StringComparison.Ordinal));
			if (instance is not null)
			{
				return instance;
			}

			await Task.Delay(200);
		}

		throw new TimeoutException(
			$"MCP instance {host.Public.Name} did not publish within {timeout.TotalSeconds} seconds. Inspect {_layout.RunDirectory}.");
	}

	public void Record(string name, object evidence)
	{
		_report[name] = evidence;
		File.AppendAllText(_layout.ReceiptsPath,
			JsonSerializer.Serialize(new
			{
				name,
				evidence
			}) + Environment.NewLine);
	}

	public void MarkPassed()
	{
		_passed = true;
	}

	private void RestoreUserState()
	{
		if (_userState is not null)
		{
			_userState.Restore();
			_report["userStateRestored"] = _userState.Restored;
			return;
		}

		if (File.Exists(_layout.RestoreMarkerPath))
		{
			try
			{
				using ICheatEngineUserStateScope recovery = _stateGuard!.Begin(_layout);
				recovery.Restore();
			}
			catch (InvalidOperationException) when (!File.Exists(_layout.RestoreMarkerPath))
			{
			}
		}

		_report["userStateRestored"] = true;
	}

	private static void TryCleanup(Action action, List<Exception> failures)
	{
		try
		{
			action();
		}
		catch (Exception exception)
		{
			failures.Add(exception);
		}
	}

	private void VerifyLifecycleEvidence(string name, string debugPath)
	{
		string driverLog = File.ReadAllText(Path.Combine(_layout.RunDirectory, $"driver-{name}.log"));
		string pluginLog = File.ReadAllText(Path.Combine(_layout.RunDirectory, "plugin-data", name,
			$"CheatEngine.Mcp.{GetHost(name).ProcessId}.log"));
		bool enabledIndicator = driverLog.Contains("mcp-status MCP: Enabled count=1", StringComparison.Ordinal);
		bool disabledIndicator = pluginLog.Contains("MCP status indicator: Disabled.", StringComparison.Ordinal);
		if (!enabledIndicator || !disabledIndicator)
		{
			throw new InvalidOperationException(
				$"MCP menu status evidence for host {name} is incomplete: expected enabled and disabled indicators.");
		}

		Record($"mcp_status_{name}", new
		{
			enabledIndicator,
			disabledIndicator
		});
		string log = File.ReadAllText(debugPath);
		bool enabled = log.Contains("[CheatEngine.SDK.Hosting] Information: Plugin ", StringComparison.Ordinal)
					   && log.Contains(" enabled (epoch ", StringComparison.Ordinal);
		bool disabled = log.Contains("[CheatEngine.SDK.Hosting] Information: Plugin ", StringComparison.Ordinal)
						&& log.Contains(" disabled.", StringComparison.Ordinal);
		if (!enabled || !disabled)
		{
			throw new InvalidOperationException(
				$"SDK lifecycle evidence for host {name} is incomplete: expected normal plugin enable and disable markers.");
		}

		string[] deactivationFailures =
		[
			"Client deactivation encountered one or more cleanup failures.",
			"deactivation cleanup failed",
			"Plugin disable failed"
		];
		string? failure =
			deactivationFailures.FirstOrDefault(marker => log.Contains(marker, StringComparison.OrdinalIgnoreCase));
		if (failure is not null)
		{
			throw new InvalidOperationException(
				$"SDK lifecycle evidence for host {name} reported deactivation failure marker '{failure}'.");
		}

		Record($"sdk_lifecycle_{name}", new
		{
			enabled,
			disabled
		});
	}

	internal async Task<string[]> ScanUiAsync(string name, string action)
	{
		if (action is not ("prepare" or "snapshot" or "manual" or "hide"))
		{
			throw new ArgumentOutOfRangeException(nameof(action));
		}

		OwnedHost host = _hosts[name];
		string response = Path.Combine(_layout.RunDirectory, $"scan-response-{name}.txt");
		File.Delete(response);
		await File.WriteAllTextAsync(Path.Combine(_layout.RunDirectory, $"scan-request-{name}.txt"), action);
		await WaitUntilAsync(() => File.Exists(response), host.Process!, TimeSpan.FromSeconds(10));
		string[] lines = await File.ReadAllLinesAsync(response);
		if (lines.Length != 8 || lines[0] != "ok")
		{
			throw new InvalidOperationException($"Native scan UI probe failed: {string.Join("; ", lines)}");
		}

		return lines;
	}

	private string BuildDriver(string stopPath, string pluginPath, string name)
	{
		int maximumLifetimeMilliseconds = _options.UseSoakLifetime ? 8_100_000 : 300_000;
		string pluginBridge = LivePluginBridge.Render(Path.Combine(_layout.RunDirectory, $"plugin-request-{name}.txt"),
			Path.Combine(_layout.RunDirectory, $"plugin-response-{name}.txt"), LuaString);
		string compilerProbe = _options.CompilerQualification
			? LiveCompilerBridge.Render(Path.Combine(_layout.RunDirectory, $"compiler-request-{name}.txt"),
				Path.Combine(_layout.RunDirectory, $"compiler-response-{name}.txt"), CompilerHoldReadyPath(name),
				CompilerHoldReleasePath(name), LiveCompilerPayload.ValidSource, LuaString)
			: "local function compilerProbe() end";
		return $$"""
		         local output = {{LuaString(Path.Combine(_layout.RunDirectory, $"driver-{name}.log"))}}
		         local function log(value)
		           local f = assert(io.open(output, 'a')); f:write(tostring(value)..'\n'); f:close()
		         end
		         log('driver-loaded')
		         local started = getTickCount()
		         local loaded = false
		         local pluginEnabled = false
		         {{pluginBridge}}
		         local function scanProbe()
		           local requestPath={{LuaString(Path.Combine(_layout.RunDirectory, $"scan-request-{name}.txt"))}}
		           local request=io.open(requestPath,'r')
		           if not request then return end
		           local action=request:read('*a'); request:close(); os.remove(requestPath)
		           local ok,answer=pcall(function()
		             local f=getMainForm()
		             if action=='prepare' then
		               f.show()
		               local manifest=assert(io.open({{LuaString(Path.Combine(_layout.RunDirectory, $"target-{name}.json"))}},'r'))
		               local contents=manifest:read('*a'); manifest:close()
		               local address=assert(contents:match('"address"%s*:%s*"([^"]+)"')):gsub('^0x','')
		               f.FromAddress.Text=address
		               f.ToAddress.Text=string.format('%X',tonumber(address,16)+63)
		               f.cbFastScan.Checked=false
		             elseif action=='manual' then
		               assert(f.btnNewScan.Enabled, 'scan is busy')
		               if getCurrentMemscan().LastScanType~='stNewScan' then f.btnNewScan.doClick() end
		               f.VarType.ItemIndex=3; f.VarType.OnChange(f.VarType)
		               f.ScanType.ItemIndex=0; f.ScanType.OnChange(f.ScanType)
		               f.cbHexadecimal.Checked=false; f.Scanvalue.Text='20260927'
		               f.btnNewScan.doClick()
		             elseif action=='hide' then f.hide()
		             else assert(action=='snapshot', 'unknown scan probe') end
		             local ms=getCurrentMemscan()
		             return 'ok\n'..tostring(f.Foundlist3.Items.Count)..'\n'..f.FoundCountLabel.Caption..'\n'..
		               f.Scanvalue.Text..'\n'..tostring(f.VarType.ItemIndex)..'\n'..tostring(ms.FoundList.Count)..'\n'..
		               f.FromAddress.Text..'|'..f.ToAddress.Text..'|hex='..tostring(f.cbHexadecimal.Checked)..'|type='..tostring(ms.VarType)..'|value='..tostring(ms.LastScanValue)..'|error='..tostring(ms.ErrorString)..'\n'..tostring(f.Visible)
		           end)
		           local responsePath={{LuaString(Path.Combine(_layout.RunDirectory, $"scan-response-{name}.txt"))}}
		           local response=assert(io.open(responsePath..'.tmp','w'))
		           response:write(ok and answer or ('error: '..tostring(answer))); response:close()
		           os.rename(responsePath..'.tmp',responsePath)
		         end
		         {{compilerProbe}}
		         local timer = createTimer(nil, false)
		         timer.Interval = 100
		         timer.OnTimer = function()
		          local tickOk, tickError = pcall(function()
		           local stop = io.open({{LuaString(stopPath)}}, 'r')
		           if stop or (getTickCount()-started) % 4294967296 > {{maximumLifetimeMilliseconds}} then
		             if stop then stop:close() end
		             log('closing'); timer.Enabled=false
		             pcall(function() getAddressList().clear() end)
		             closeCE(); return
		           end
		           if loaded then pluginControl(); scanProbe(); compilerProbe(); return end
		           if not getMainForm() then return end
		           loaded = true
		           hideAllCEWindows()
		           log('loading-plugin')
		           local ok, value = pcall(loadPlugin, {{LuaString(pluginPath)}})
		           log('loadPlugin ok='..tostring(ok)..' result='..tostring(value))
		           assert(ok and value, 'owned plugin did not load')
		           pluginEnabled=true
		           local menu = getMainForm().Menu
		           local status = assert(menu.findComponentByName('CheatEngineMcpStatus'), 'MCP status menu is missing')
		           local count = 0
		           for i=0,menu.Items.Count-1 do
		             if menu.Items[i].Name == 'CheatEngineMcpStatus' then count=count+1 end
		           end
		           log('mcp-status '..status.Caption..' count='..count)
		          end)
		          if not tickOk then log('timer-error '..tostring(tickError)) end
		         end
		         timer.Enabled = true
		         """;
	}

	private static ProcessStartInfo CreateStartInfo(string executable)
	{
		ProcessStartInfo start = new(executable)
		{
			UseShellExecute = false,
			WindowStyle = ProcessWindowStyle.Hidden,
			WorkingDirectory = Path.GetDirectoryName(executable)!
		};
		foreach (string key in start.Environment.Keys.ToArray())
		{
			if (RemovedEnvironmentPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
			{
				start.Environment.Remove(key);
			}
		}

		return start;
	}

	private static async Task<bool> StopOwnedAsync(Process? process, List<Exception> failures)
	{
		if (process is null)
		{
			return true;
		}

		try
		{
			if (!process.HasExited)
			{
				try
				{
					await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
				}
				catch (TimeoutException)
				{
					process.Kill(true);
					await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
					failures.Add(
						new TimeoutException(
							"An owned process needed forced termination instead of closing normally."));
				}
			}

			if (!process.HasExited)
			{
				failures.Add(
					new InvalidOperationException("An owned process is still running after its shutdown deadline."));
				return false;
			}

			process.Dispose();
			return true;
		}
		catch (Exception exception)
		{
			failures.Add(exception);
			return false;
		}
	}

	private void RequireInsideRun(string path)
	{
		if (!LiveQualificationOptIn.IsSameOrBelow(path, _layout.RunDirectory)
			|| Path.GetFullPath(path)
				.Equals(Path.GetFullPath(_layout.RunDirectory), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("A sandbox file operation escaped the current run directory.");
		}
	}

	private void CopyReviewedAutorun(string source, string destination, string name, string expectedHash)
	{
		string path = Path.Combine(source, name);
		if (!File.Exists(path))
		{
			throw new FileNotFoundException($"The stock {name} is required by the live speedhack scenario.", path);
		}

		string hash = CheatEngineInstallation.Sha256(path);
		if (hash != expectedHash)
		{
			throw new InvalidOperationException(
				$"The stock {name} differs from the reviewed live-test version; review it before updating its test hash.");
		}

		File.Copy(path, Path.Combine(destination, name));
		Record(name, hash);
	}

	private static async Task WaitUntilAsync(Func<bool> ready, Process process, TimeSpan timeout)
	{
		Stopwatch elapsed = Stopwatch.StartNew();
		while (!ready())
		{
			if (process.HasExited || elapsed.Elapsed > timeout)
			{
				throw new TimeoutException("The disposable target did not become ready.");
			}

			await Task.Delay(50);
		}
	}

	private static void RequireNoCheatEngine()
	{
		foreach (Process process in Process.GetProcesses())
		{
			using (process)
			{
				if (process.ProcessName.StartsWith("cheatengine-", StringComparison.OrdinalIgnoreCase)
					|| process.ProcessName.Equals("cheatengine", StringComparison.OrdinalIgnoreCase)
					|| process.ProcessName.Equals("Cheat Engine", StringComparison.OrdinalIgnoreCase)
					|| process.ProcessName.StartsWith("gtutorial", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException(
						$"Close {process.ProcessName} (PID {process.Id}) before live tests; it will not be stopped automatically.");
				}
			}
		}
	}

	internal static string FindRepository()
	{
		for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
			 directory is not null;
			 directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "CheatEngine.Mcp.slnx")))
			{
				return directory.FullName;
			}
		}

		throw new DirectoryNotFoundException("Run live tests from the CheatEngine.Mcp checkout.");
	}

	private string HostInstallationDirectory(string name)
	{
		return Path.Combine(_layout.RunDirectory, $"ce-{name}");
	}

	private LiveCompilerRoots CompilerRoots(string name)
	{
		if (!_options.CompilerQualification)
		{
			throw new InvalidOperationException("Compiler roots were not admitted for this live session.");
		}

		string root = Path.Combine(_layout.RunDirectory, "compiler", name);
		string temp = _options.TempTopology == CompilerTempTopology.Shared
			? Path.Combine(_layout.RunDirectory, "compiler", "shared", "temp")
			: Path.Combine(root, "temp");
		return new LiveCompilerRoots(temp, Path.Combine(root, "references"),
			Path.Combine(root, "output"));
	}

	private void CreateCompilerRoots(string name)
	{
		LiveCompilerRoots roots = CompilerRoots(name);
		foreach (string path in new[] { roots.Temp, roots.References, roots.Output })
		{
			RequireInsideRun(path);
			Directory.CreateDirectory(path);
		}
	}

	private void CleanupCompilerRoots()
	{
		string compilerRoot = Path.Combine(_layout.RunDirectory, "compiler");
		RequireInsideRun(compilerRoot);
		if (!Directory.Exists(compilerRoot))
		{
			return;
		}

		RequireNoReparseAncestors(compilerRoot);
		CleanupCompilerDirectory(compilerRoot);
		Directory.Delete(compilerRoot, false);
		Record("ownedCompilerFilesRemoved", true);
	}

	private void CleanupCompilerDirectory(string directory)
	{
		foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
		{
			RequireInsideRun(entry);
			FileAttributes attributes = File.GetAttributes(entry);
			if ((attributes & FileAttributes.ReparsePoint) != 0)
			{
				throw new IOException("Compiler cleanup refused a reparse-point entry.");
			}

			if ((attributes & FileAttributes.Directory) != 0)
			{
				CleanupCompilerDirectory(entry);
				Directory.Delete(entry, false);
			}
			else
			{
				File.Delete(entry);
			}
		}
	}

	private void RequireNoReparseAncestors(string path)
	{
		RequireInsideRun(path);
		LiveCompilerCandidate.RequireNoReparseAncestors(path);
	}

	private static string? HashIfExists(string path)
	{
		return File.Exists(path) ? CheatEngineInstallation.Sha256(path) : null;
	}

	private static string LuaString(string value)
	{
		return '"' + string.Concat(Encoding.UTF8.GetBytes(value).Select(valueByte =>
			"\\" + valueByte.ToString("D3", CultureInfo.InvariantCulture))) + '"';
	}

	private sealed class OwnedHost(
		Process? process,
		Process? target,
		string stopPath,
		string targetManifest,
		LiveSandboxHost @public)
	{
		public string StopPath
		{
			get;
		} = stopPath;

		public string TargetManifest
		{
			get;
			set;
		} = targetManifest;

		public int TargetIncarnation
		{
			get;
			set;
		}

		public Process? Process
		{
			get;
			set;
		} = process;

		public Process? Target
		{
			get;
			set;
		} = target;

		public LiveSandboxHost Public
		{
			get;
			set;
		} = @public;

		public bool Stopped
		{
			get;
			set;
		}
	}

	private sealed record LiveTargetManifest(string Address, string PointerRootAddress, string PointerTargetAddress,
		string ZeroPointerRootAddress, string UnreadableRootAddress);
}
