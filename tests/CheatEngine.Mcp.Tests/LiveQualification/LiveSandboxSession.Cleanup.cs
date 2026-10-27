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
	public async ValueTask DisposeAsync()
	{
		await _disposeGate.WaitAsync();
		List<Exception> failures = [];
		try
		{
			if (_disposed)
			{
				return;
			}

			if (_symbolSourceContainment is not null)
			{
				try
				{
					await _symbolSourceCleanupGate.EnsureCleanedAsync(
						StopSourceOwnedProcessesAsync,
						() => _symbolSourceContainment.Dispose());
					Record("symbol_source_containment_cleanup", _symbolSourceContainment.CleanupReceipt ??
						throw new InvalidOperationException("Source containment returned without a verified cleanup receipt."));
				}
				catch (Exception exception)
				{
					RecordRetryableSourceCleanupFailure(exception);
					throw;
				}
			}

			_disposed = true;
			try
			{
				if (_options.UsesTargetFirstExecutionShutdown)
				{
					await TerminateExecutionTargetsBeforeHostCleanupAsync("sandbox disposal", failures);
				}

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
				if ((_options.SymbolFixtureQualification || _options.SymbolSourcesQualification) && _hosts.Values.All(host => host.Stopped))
				{
					TryCleanup(() =>
					{
						string root = Path.Combine(_layout.RunDirectory, "native-symbol-fixture");
						RequireInsideRun(root);
						if (!Directory.Exists(root))
						{
							return;
						}

						RequireNoReparseAncestors(root);
						CleanupCompilerDirectory(root);
						Directory.Delete(root, false);
						if (Directory.Exists(root))
						{
							throw new InvalidOperationException("Native fixture root remained after cleanup.");
						}

						Record("native_symbol_fixture_cleanup", new
						{
							root,
							absent = true
						});
					}, failures);
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
		finally
		{
			_disposeGate.Release();
		}
	}

	private async Task StopSourceOwnedProcessesAsync()
	{
		List<Exception> failures = [];
		await TerminateExecutionTargetsBeforeHostCleanupAsync("symbol-source containment cleanup", failures);
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

		if (failures.Count != 0)
		{
			throw new AggregateException(
				"Owned source processes did not stop before symbol-source containment cleanup.", failures);
		}
	}

	private void RecordRetryableSourceCleanupFailure(Exception exception)
	{
		_report["passed"] = false;
		_report["symbolSourceContainmentCleanupVerified"] = false;
		_report["cleanupFailures"] = new[] { exception.ToString() };
		File.WriteAllText(_layout.SummaryPath, JsonSerializer.Serialize(_report, ReportJson));
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

	private sealed class OwnedHost(
		Process? process,
		Process? target,
		string stopPath,
		string targetManifest,
		string targetExitPath,
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

		public string TargetExitPath
		{
			get;
		} = targetExitPath;

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

		public bool TargetExitConfirmed
		{
			get;
			set;
		}

		public bool HostCleanupBlocked
		{
			get;
			set;
		}

		public bool TargetTerminationForced
		{
			get;
			set;
		}

		public bool TargetTerminationReceiptEmitted
		{
			get;
			set;
		}
	}

	private sealed record LiveTargetManifest(string Address, string PointerRootAddress, string PointerTargetAddress,
		string ZeroPointerRootAddress, string UnreadableRootAddress, string ManagedObjectAddress, string ManagedObjectType,
		int? ManagedObjectMarker, int? ManagedObjectMirror, int? ManagedObjectToggle,
		LiveDebuggerExecutionQualification.Manifest? DebuggerExecution);
}
