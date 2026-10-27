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
		if (_options.UsesTargetFirstExecutionShutdown && (!host.TargetExitConfirmed || host.HostCleanupBlocked))
		{
			throw new InvalidOperationException("Refusing CE/plugin cleanup before owned target exit is confirmed.");
		}

		List<Exception> failures = [];
		TryCleanup(() => File.WriteAllText(host.StopPath, "stop"), failures);
		bool hostStopped = await StopOwnedAsync(host.Process, failures);
		if (hostStopped)
		{
			host.Process = null;
			if (!_options.UsesTargetFirstExecutionShutdown)
			{
				TryCleanup(() => File.WriteAllText(host.TargetManifest + ".stop", "stop"), failures);
			}
			bool targetStopped = _options.UsesTargetFirstExecutionShutdown || await StopOwnedAsync(host.Target, failures);
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
		if (_options.RequiresCapabilityConfiguration)
		{
			string[] allowedRoots = _options.CompilerQualification ? [CompilerRoots(name).Output]
				: _options.SymbolFixtureQualification || _options.SymbolSourcesQualification ? [Path.GetDirectoryName(NativeSymbolFixture.DllPath)!]
				: _options.ProcessCreateQualification ? [Path.Combine(_layout.RunDirectory, "roadmap-qualification")]
				: _options.WorkflowQualification || _options.LifecycleQualification || _options.StateWorkflowsQualification
					? [Path.Combine(_layout.RunDirectory, "roadmap-qualification"), Path.Combine(_layout.RunDirectory, "pointer-qualification")]
					: [];
			File.WriteAllText(Path.Combine(pluginDirectory, "appsettings.json"), JsonSerializer.Serialize(new
			{
				Mcp = new
				{
					EnableUnsafeLua = _options.AssemblerQualification,
					EnableAutoAssembler = _options.AssemblerQualification,
					EnableTargetCodeExecution = _options.EnableTargetCodeExecution,
					EnableKernelAccess = _options.KernelStatusQualification,
					Files = new
					{
						AllowedRoots = allowedRoots
					}
				},
				CheatEngineClient = new
				{
					AllowedTableRoots = _options.WorkflowQualification || _options.LifecycleQualification || _options.StateWorkflowsQualification
						? new[] { Path.Combine(_layout.RunDirectory, "roadmap-qualification", "tables") }
						: []
				}
			}));
		}
		string pluginPath = Path.Combine(pluginDirectory, PluginFileName);
		VerifyDistributionStaging(name, pluginSource, pluginDirectory);
		Record($"plugin_{name}_sha256", CheatEngineInstallation.Sha256(pluginPath));
		string stopPath = Path.Combine(_layout.RunDirectory, $"stop-{name}");
		string targetManifest = Path.Combine(_layout.RunDirectory, $"target-{name}.json");
		string targetExitPath = Path.Combine(_layout.RunDirectory, $"target-{name}.exited");
		string driverPath = Path.Combine(HostInstallationDirectory(name), "autorun", "zz_cheatengine_mcp_live.lua");
		Process? target = null;
		Process? host = null;
		OwnedHost? owned = null;
		try
		{
			target = StartTarget(targetManifest, name, _options.UseSoakLifetime);
			owned = new OwnedHost(null, target, stopPath, targetManifest, targetExitPath,
				new LiveSandboxHost(name, 0, target.Id, string.Empty, string.Empty, string.Empty, string.Empty,
					string.Empty, pluginPath, string.Empty, string.Empty));
			_hosts.Add(name, owned);
			if (_options.SeedPluginChecklistAtStartup && name == "A")
			{
				SeedOwnedPluginChecklistAtStartup(name, pluginPath);
			}
			JsonNode manifest = await ReadPublishedTargetManifestAsync(targetManifest, target, name);
			LiveTargetManifest targetFacts = ReadTargetManifest(manifest, target.Id, name);
			string selectionDiagnostic = string.Empty;
			if (Environment.GetEnvironmentVariable("CHEATENGINE_MCP_LIVE_SELECTION_DIAGNOSTIC") ==
				"I_AUTHORIZE_ONE_FIXED_PROCESS_SELECTION_TRACE_ON_OWNED_TARGETS")
			{
				if (!_options.WorkflowQualification)
				{
					throw new InvalidOperationException("The fixed process-selection trace requires the workflows scenario.");
				}
				RenderedProcessSelectionBridge bridge = await LiveProcessSelectionBridge.RenderAsync(targetManifest);
				selectionDiagnostic = bridge.Source;
				Record($"process_selection_diagnostic_{name}", new
				{
					bridge.ProcessId,
					bridge.Address,
					bridge.ReceiptPath
				});
			}
			File.WriteAllText(driverPath, selectionDiagnostic + "\n" + BuildDriver(stopPath, targetExitPath, pluginPath, name));
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
			if (_options.SymbolSourcesQualification)
			{
				hostStart.Environment["_NT_SYMBOL_PATH"] = SymbolSourceContainment.SymbolPath;
				hostStart.Environment.Remove("_NT_ALT_SYMBOL_PATH");
				hostStart.Environment.Remove("_NT_ALTERNATE_SYMBOL_PATH");
			}
			host = _options.SymbolSourcesQualification
				? SymbolSourceContainment.Launch(hostStart)
				: Process.Start(hostStart) ??
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
				UnreadableRootAddress = targetFacts.UnreadableRootAddress,
				ManagedObjectAddress = targetFacts.ManagedObjectAddress,
				ManagedObjectType = targetFacts.ManagedObjectType,
				ManagedObjectMarker = targetFacts.ManagedObjectMarker,
				ManagedObjectMirror = targetFacts.ManagedObjectMirror,
				ManagedObjectToggle = targetFacts.ManagedObjectToggle,
				DebuggerExecution = targetFacts.DebuggerExecution
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
					if (_options.UsesTargetFirstExecutionShutdown)
					{
						await TerminateExecutionTargetsBeforeHostCleanupAsync("host startup failure", cleanupFailures);
					}

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
}
