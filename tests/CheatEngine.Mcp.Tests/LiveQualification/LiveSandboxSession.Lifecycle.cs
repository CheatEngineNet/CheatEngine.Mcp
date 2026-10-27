using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

using CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

using Microsoft.Win32;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
internal sealed partial class LiveSandboxSession
{
	private long _pluginControlNonce;
	private readonly Dictionary<string, PendingPluginIdentity> _pendingPluginIdentities = new(StringComparer.Ordinal);

	/// <summary>Issues one fixed Settings/Plugins enable or disable transition to an already-loaded owned plugin.</summary>
	internal async Task<LivePluginCycle> ReloadPluginAsync(string hostName, bool enabled,
		CancellationToken cancellationToken = default)
	{
		if (!_hosts.TryGetValue(hostName, out OwnedHost? host) || host.Process is null || host.Stopped)
		{
			throw new InvalidOperationException($"Owned host '{hostName}' is not running.");
		}
		PendingPluginIdentity? previous;
		if (enabled)
		{
			previous = _pendingPluginIdentities.GetValueOrDefault(hostName);
		}
		else
		{
			InstanceDescriptor descriptor = new InstanceRegistry(InstanceDirectory).ReadActive(cancellationToken).Single(candidate =>
				candidate.ProcessId == host.Public.ProcessId && candidate.InstanceId == host.Public.InstanceId);
			previous = new PendingPluginIdentity(descriptor.InstanceId, descriptor.Endpoint, descriptor.AccessToken);
		}
		if (previous is null)
		{
			throw new InvalidOperationException(enabled
				? "Plugin enable requires a successful prior disable transition."
				: "The owned plugin discovery identity is unavailable before disable.");
		}
		PrepareOwnedPluginRegistryForSettingsReload(hostName, currentEnabled: !enabled);
		long nonce = Interlocked.Increment(ref _pluginControlNonce);
		string request = Path.Combine(_layout.RunDirectory, $"plugin-request-{hostName}.txt");
		string response = Path.Combine(_layout.RunDirectory, $"plugin-response-{hostName}.txt");
		RequireInsideRun(request);
		RequireInsideRun(response);
		RequireNoReparseAncestors(request);
		RequireNoReparseAncestors(response);
		if (File.Exists(response))
		{
			File.Delete(response);
		}
		string temporary = request + ".tmp";
		RequireNoReparseAncestors(temporary);
		await File.WriteAllTextAsync(temporary, $"{nonce}\n{(enabled ? "enable" : "disable")}\n", cancellationToken);
		if (!enabled)
		{
			// The UI request is about to become observable. Retain this exact identity only after all preparation has
			// succeeded, so malformed/missing replies can recover an unload without treating preflight failure as one.
			_pendingPluginIdentities[hostName] = previous;
		}
		File.Move(temporary, request, false);
		await WaitUntilAsync(() => File.Exists(response), host.Process, TimeSpan.FromSeconds(15));
		RequireNoReparseAncestors(response);
		FileInfo responseFacts = new(response);
		if (responseFacts.Length is <= 0 or > 4096)
		{
			throw new InvalidOperationException("The fixed CE Plugin Manager bridge response has an invalid bounded length.");
		}
		using FileStream responseStream = new(response, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (responseStream.Length is <= 0 or > 4096)
		{
			throw new InvalidDataException("Plugin response exceeds its byte bound.");
		}

		using StreamReader responseReader = new(responseStream, new System.Text.UTF8Encoding(false, true), false);
		string[] fields = (await responseReader.ReadToEndAsync(cancellationToken)).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		string action = enabled ? "enable" : "disable";
		if (fields.Length == 4 && fields[0] == "error" && long.TryParse(fields[1], out long failedNonce)
			&& failedNonce == nonce && fields[2] == action && fields[3].Length is > 0 and <= 1024)
		{
			throw new InvalidOperationException($"The fixed CE Plugin Manager bridge rejected {action}: {fields[3]}");
		}
		string expectedBeforeToggle = enabled ? "unchecked" : "checked";
		string expectedAfterToggle = enabled ? "checked" : "unchecked";
		if (fields.Length != 6 || fields[0] != "ok" || !long.TryParse(fields[1], out long received)
			|| received != nonce || fields[2] != action || fields[3] != expectedBeforeToggle
			|| fields[4] != expectedAfterToggle || fields[5] != expectedAfterToggle)
		{
			throw new InvalidOperationException("The fixed CE Plugin Manager bridge returned an invalid response.");
		}
		if (host.Process.HasExited)
		{
			throw new InvalidOperationException("The owned CE process exited during the plugin lifecycle transition.");
		}
		bool accessTokenChanged = false;
		if (enabled)
		{
			InstanceDescriptor descriptor = await WaitForInstanceAsync(host, TimeSpan.FromSeconds(45));
			if (descriptor.InstanceId == previous.InstanceId || descriptor.Endpoint == previous.Endpoint
				|| descriptor.AccessToken == previous.AccessToken)
			{
				throw new InvalidOperationException("Plugin re-enable did not publish a fresh discovery identity.");
			}
			accessTokenChanged = !string.Equals(previous.AccessToken, descriptor.AccessToken, StringComparison.Ordinal);
			host.Public = host.Public with
			{
				InstanceId = descriptor.InstanceId,
				Endpoint = descriptor.Endpoint
			};
			_pendingPluginIdentities.Remove(hostName);
		}

		string currentInstanceId = host.Public.InstanceId;
		string currentEndpoint = host.Public.Endpoint;
		LivePluginCycle cycle = new(host.Public.ProcessId, host.Public.TargetProcessId, nonce, enabled,
			previous.InstanceId, currentInstanceId, previous.Endpoint, currentEndpoint,
			enabled && previous.InstanceId != currentInstanceId,
			enabled && previous.Endpoint != currentEndpoint,
			accessTokenChanged,
			DateTimeOffset.UtcNow);
		Record($"plugin_{hostName}_{(enabled ? "enable" : "disable")}", cycle);
		return cycle;
	}

	/// <summary>Uses the fixed CE Settings/Plugins bridge to observe disabled plugin and cleanup state.</summary>
	internal async Task<LivePluginDisabledObservation> ObservePluginDisabledAsync(string hostName, ulong breakpointAddress,
		CancellationToken cancellationToken = default)
	{
		Stopwatch elapsed = Stopwatch.StartNew();
		int observations = 0;
		LivePluginDisabledObservation observation;
		do
		{
			observation = await ObservePluginStateAsync(hostName, breakpointAddress, cancellationToken);
			observations++;
			if (observation.PluginChecked || !observation.StatusMenuPresent || !observation.StatusMenuDisabled)
			{
				break;
			}

			if (!observation.BreakpointPresent)
			{
				Record($"plugin_{hostName}_disabled_cleanup_wait", new
				{
					elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
					observations,
					breakpointAbsent = true,
					observation
				});
				return observation;
			}

			if (elapsed.Elapsed >= TimeSpan.FromSeconds(8))
			{
				break;
			}

			await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
		}
		while (true);

		Record($"plugin_{hostName}_disabled_cleanup_wait", new
		{
			elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
			observations,
			breakpointAbsent = false,
			observation
		});
		throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
			$"The fixed CE Plugin Manager bridge did not observe the plugin unchecked, its status menu marked Disabled, and its owned breakpoint absent within {elapsed.Elapsed.TotalMilliseconds:F0} ms (checked={observation.PluginChecked}, statusMenuPresent={observation.StatusMenuPresent}, statusMenuDisabled={observation.StatusMenuDisabled}, breakpointPresent={observation.BreakpointPresent})."));
	}

	internal async Task<LivePluginDisabledObservation> ObservePluginStateAsync(string hostName, ulong breakpointAddress,
		CancellationToken cancellationToken = default)
	{
		if (!_hosts.TryGetValue(hostName, out OwnedHost? host) || host.Process is null || host.Stopped)
		{
			throw new InvalidOperationException($"Owned host '{hostName}' is not running.");
		}
		long nonce = Interlocked.Increment(ref _pluginControlNonce);
		string request = Path.Combine(_layout.RunDirectory, $"plugin-request-{hostName}.txt");
		string response = Path.Combine(_layout.RunDirectory, $"plugin-response-{hostName}.txt");
		RequireInsideRun(request);
		RequireInsideRun(response);
		RequireNoReparseAncestors(request);
		RequireNoReparseAncestors(response);
		if (File.Exists(response))
		{
			File.Delete(response);
		}
		string temporary = request + ".tmp";
		RequireNoReparseAncestors(temporary);
		string address = $"0x{breakpointAddress:X}";
		await File.WriteAllTextAsync(temporary, $"{nonce}\nobserve\n{address}\n", cancellationToken);
		File.Move(temporary, request, false);
		await WaitUntilAsync(() => File.Exists(response), host.Process, TimeSpan.FromSeconds(15));
		RequireNoReparseAncestors(response);
		FileInfo responseFacts = new(response);
		if (responseFacts.Length is <= 0 or > 4096)
		{
			throw new InvalidOperationException("The fixed CE Plugin Manager observation response has an invalid bounded length.");
		}
		LivePluginDisabledObservation observation = LivePluginDisabledObservation.ParseResponse(
			await File.ReadAllTextAsync(response, cancellationToken), nonce, address, host.Public.TargetProcessId);
		Record($"plugin_{hostName}_observed", observation);
		Record($"plugin_{hostName}_observed_{nonce}", observation);
		return observation;
	}

	/// <summary>Temporarily wraps the native breakpoint remover inside an owned CE host for one cleanup observation.</summary>
	internal async Task SetBreakpointRemovalProbeAsync(string hostName, bool installed,
		CancellationToken cancellationToken = default)
	{
		if (!_hosts.TryGetValue(hostName, out OwnedHost? host) || host.Process is null || host.Stopped)
		{
			throw new InvalidOperationException($"Owned host '{hostName}' is not running.");
		}

		string action = installed ? "install_probe" : "remove_probe";
		long nonce = Interlocked.Increment(ref _pluginControlNonce);
		string request = Path.Combine(_layout.RunDirectory, $"plugin-request-{hostName}.txt");
		string response = Path.Combine(_layout.RunDirectory, $"plugin-response-{hostName}.txt");
		RequireInsideRun(request);
		RequireInsideRun(response);
		RequireNoReparseAncestors(request);
		RequireNoReparseAncestors(response);
		if (File.Exists(response))
		{
			File.Delete(response);
		}

		string temporary = request + ".tmp";
		RequireNoReparseAncestors(temporary);
		await File.WriteAllTextAsync(temporary, $"{nonce}\n{action}\n", cancellationToken);
		File.Move(temporary, request, false);
		await WaitUntilAsync(() => File.Exists(response), host.Process, TimeSpan.FromSeconds(15));
		RequireNoReparseAncestors(response);
		FileInfo responseFacts = new(response);
		if (responseFacts.Length is <= 0 or > 4096)
		{
			throw new InvalidOperationException("The fixed CE Plugin Manager cleanup-probe response has an invalid bounded length.");
		}

		string[] fields = (await File.ReadAllTextAsync(response, cancellationToken))
			.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		if (fields.Length == 4 && fields[0] == "error" && long.TryParse(fields[1], out long failedNonce)
			&& failedNonce == nonce && fields[2] == action && fields[3].Length is > 0 and <= 1024)
		{
			throw new InvalidOperationException($"The fixed CE Plugin Manager bridge rejected {action}: {fields[3]}");
		}
		if (fields.Length != 3 || fields[0] != "ok" || !long.TryParse(fields[1], out long received)
			|| received != nonce || fields[2] != action)
		{
			throw new InvalidOperationException("The fixed CE Plugin Manager cleanup-probe bridge returned an invalid response.");
		}

		Record($"plugin_{hostName}_removal_probe_{(installed ? "installed" : "removed")}", new
		{
			nonce,
			action,
			processId = host.Public.ProcessId,
			completedAt = DateTimeOffset.UtcNow
		});
	}

	internal void ClearPendingPluginIdentity(string hostName) => _pendingPluginIdentities.Remove(hostName);
	/// <summary>Restarts one private CE installation and its owned target without disturbing the second host.</summary>
	public async Task RestartHostAsync(string name)
	{
		if (!_hosts.TryGetValue(name, out OwnedHost? previous) || previous.Stopped)
		{
			throw new InvalidOperationException($"Owned host '{name}' is not running.");
		}

		string repository = _repository ?? throw new InvalidOperationException("The sandbox repository was not prepared.");
		LiveSandboxHost before = previous.Public;
		await StopHostAsync(name);
		if (!previous.Stopped)
		{
			throw new InvalidOperationException($"Owned host '{name}' did not stop before restart.");
		}

		PrepareRestartArtifacts(name, previous);
		NeutralizeOwnedPluginRegistry();
		_hosts.Remove(name);
		try
		{
			await StartHostAsync(repository, name);
			LiveSandboxHost after = GetHost(name);
			Record($"host_{name}_restart", new
			{
				before = new
				{
					before.ProcessId,
					before.TargetProcessId,
					before.InstanceId,
					before.Endpoint
				},
				after = new
				{
					after.ProcessId,
					after.TargetProcessId,
					after.InstanceId,
					after.Endpoint
				}
			});
		}
		catch (Exception exception)
		{
			Record($"host_{name}_restart_failure", exception.ToString());
			throw;
		}
	}

	/// <summary>Captures an owned CE process measurement without treating an unavailable metric as zero.</summary>
	public LiveProcessMetrics CaptureMetrics(string name)
	{
		if (!_hosts.TryGetValue(name, out OwnedHost? host) || host.Process is null || host.Stopped)
		{
			throw new InvalidOperationException($"Owned host '{name}' is not running.");
		}

		DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
		try
		{
			host.Process.Refresh();
			if (host.Process.HasExited)
			{
				throw new InvalidOperationException("The owned CE process exited before metrics were captured.");
			}

			return new LiveProcessMetrics(capturedAt, host.Process.Id, host.Process.HandleCount,
				host.Process.PrivateMemorySize64, host.Process.WorkingSet64, null);
		}
		catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
		{
			return new LiveProcessMetrics(capturedAt, host.Public.ProcessId, 0, 0, 0, exception.Message);
		}
	}

	/// <summary>Removes only validated owned plugin registry entries before the next private-host transition.</summary>
	internal void NeutralizeOwnedPluginRegistry()
	{
		if (_userState is null || _stateGuard is null)
		{
			throw new InvalidOperationException("The guarded CE user-state scope was not established.");
		}

		const string pluginsSubKey = CheatEngineUserStateLocations.CheatEngineRegistrySubKey + @"\Plugins64";
		using RegistryKey plugins = Registry.CurrentUser.CreateSubKey(pluginsSubKey, true)
			?? throw new InvalidOperationException("The guarded CE Plugins64 registry key could not be created.");
		if (plugins.GetSubKeyNames().Length != 0)
		{
			throw new InvalidOperationException("The CE Plugins64 registry key has unexpected subkeys; it is retained for recovery.");
		}

		string[] names = plugins.GetValueNames();
		Dictionary<string, (string? Path, object? Enabled)> pairs = new(StringComparer.Ordinal);
		foreach (string valueName in names)
		{
			if (valueName.Length != 10 || valueName[8] != ' ' || valueName[9] is not ('A' or 'B')
				|| !valueName[..8].All(character => character is (>= '0' and <= '9') or (>= 'A' and <= 'F')))
			{
				throw new InvalidOperationException("The CE Plugins64 registry key has an unknown value name; it is retained for recovery.");
			}

			string pair = valueName[..8];
			(string? Path, object? Enabled) existing = pairs.GetValueOrDefault(pair);
			RegistryValueKind kind = plugins.GetValueKind(valueName);
			if (valueName[9] == 'A')
			{
				if (kind != RegistryValueKind.String || existing.Path is not null)
				{
					throw new InvalidOperationException("The CE Plugins64 registry key has an invalid plugin path pair; it is retained for recovery.");
				}
				pairs[pair] = ((string?) plugins.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames), existing.Enabled);
			}
			else
			{
				if (kind != RegistryValueKind.DWord || existing.Enabled is not null)
				{
					throw new InvalidOperationException("The CE Plugins64 registry key has an invalid enabled pair; it is retained for recovery.");
				}
				pairs[pair] = (existing.Path, plugins.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
			}
		}

		HashSet<string> expectedPaths = _hosts.Values.Select(host => Path.GetFullPath(host.Public.PluginPath))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		HashSet<string> observedPaths = new(StringComparer.OrdinalIgnoreCase);
		foreach ((string? path, object? enabled) in pairs.Values)
		{
			if (string.IsNullOrWhiteSpace(path) || enabled is not int enabledValue || enabledValue is not (0 or 1)
				|| !Path.IsPathFullyQualified(path) || !expectedPaths.Contains(Path.GetFullPath(path))
				|| !observedPaths.Add(Path.GetFullPath(path)))
			{
				throw new InvalidOperationException("The CE Plugins64 registry key names a plugin outside this run; it is retained for recovery.");
			}
		}

		foreach (string valueName in names)
		{
			plugins.DeleteValue(valueName, false);
		}
		Record("plugins64_neutralized", new
		{
			count = pairs.Count,
			keyPresentForSettingsReload = true
		});
	}

	private void PrepareOwnedPluginRegistryForSettingsReload(string hostName, bool currentEnabled)
	{
		if (!_hosts.TryGetValue(hostName, out OwnedHost? host) || host.Stopped)
		{
			throw new InvalidOperationException($"Owned host '{hostName}' is unavailable for plugin settings reload.");
		}

		LivePluginRegistrySeed seed = LivePluginRegistrySeed.Create(host.Public.PluginPath, currentEnabled);
		RequireInsideRun(seed.PluginPath);
		RequireNoReparseAncestors(seed.PluginPath);
		NeutralizeOwnedPluginRegistry();
		const string pluginsSubKey = CheatEngineUserStateLocations.CheatEngineRegistrySubKey + @"\Plugins64";
		using RegistryKey plugins = Registry.CurrentUser.OpenSubKey(pluginsSubKey, writable: true)
			?? throw new InvalidOperationException("The guarded CE Plugins64 key disappeared before the owned plugin pair could be seeded.");
		OwnedPluginRegistryReadback readback = SetAndVerifyOwnedPluginRegistryPair(plugins, seed);
		Record("plugins64_seeded_for_settings_reload", new
		{
			host = hostName,
			action = currentEnabled ? "disable" : "enable",
			pluginPath = seed.PluginPath,
			registryValueCount = 2,
			currentEnabled,
			keyPresentForSettingsReload = true,
			readback
		});
	}

	private void SeedOwnedPluginChecklistAtStartup(string hostName, string pluginPath)
	{
		if (!_hosts.TryGetValue(hostName, out OwnedHost? host) || host.Stopped || host.Process is not null
			|| !string.Equals(Path.GetFullPath(host.Public.PluginPath), Path.GetFullPath(pluginPath),
				StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Startup plugin seeding requires the exact owned plugin before its CE host starts.");
		}

		LivePluginRegistrySeed seed = LivePluginRegistrySeed.Create(pluginPath, currentEnabled: false);
		RequireInsideRun(seed.PluginPath);
		RequireNoReparseAncestors(seed.PluginPath);
		NeutralizeOwnedPluginRegistry();
		const string pluginsSubKey = CheatEngineUserStateLocations.CheatEngineRegistrySubKey + @"\Plugins64";
		using RegistryKey plugins = Registry.CurrentUser.OpenSubKey(pluginsSubKey, writable: true)
			?? throw new InvalidOperationException("The guarded CE Plugins64 key disappeared before startup seeding.");
		OwnedPluginRegistryReadback readback = SetAndVerifyOwnedPluginRegistryPair(plugins, seed);
		Record("plugins64_seeded_before_host_start", new
		{
			host = hostName,
			pluginPath = seed.PluginPath,
			registryValueCount = 2,
			currentEnabled = false,
			hostProcessStarted = false,
			readback
		});
	}

	private static OwnedPluginRegistryReadback SetAndVerifyOwnedPluginRegistryPair(RegistryKey plugins,
		LivePluginRegistrySeed seed)
	{
		if (plugins.GetSubKeyNames().Length != 0)
		{
			throw new InvalidOperationException("The guarded CE Plugins64 key has unexpected subkeys before seeding.");
		}

		plugins.SetValue(seed.PathValueName, seed.PluginPath, RegistryValueKind.String);
		plugins.SetValue(seed.EnabledValueName, seed.EnabledValue, RegistryValueKind.DWord);
		plugins.Flush();

		string[] names = plugins.GetValueNames().Order(StringComparer.Ordinal).ToArray();
		string[] expectedNames = [seed.PathValueName, seed.EnabledValueName];
		if (!names.SequenceEqual(expectedNames, StringComparer.Ordinal))
		{
			throw new InvalidOperationException("The guarded CE Plugins64 key does not contain exactly the expected owned pair.");
		}

		RegistryValueKind pathKind = plugins.GetValueKind(seed.PathValueName);
		RegistryValueKind enabledKind = plugins.GetValueKind(seed.EnabledValueName);
		string? path = plugins.GetValue(seed.PathValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
		object? enabled = plugins.GetValue(seed.EnabledValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
		if (plugins.GetSubKeyNames().Length != 0 || pathKind != RegistryValueKind.String
			|| enabledKind != RegistryValueKind.DWord || !string.Equals(path, seed.PluginPath, StringComparison.OrdinalIgnoreCase)
			|| enabled is not int enabledValue || enabledValue != seed.EnabledValue)
		{
			throw new InvalidOperationException("The guarded CE Plugins64 pair failed exact type/value readback.");
		}

		return new OwnedPluginRegistryReadback(names, pathKind.ToString(), enabledKind.ToString(), path!, enabledValue,
			plugins.GetValueNames().Length, true);
	}

	private void PrepareRestartArtifacts(string name, OwnedHost previous)
	{
		string pluginRoot = Path.Combine(_layout.PluginsDirectory, name);
		string currentManifest = Path.Combine(_layout.RunDirectory, $"target-{name}.json");
		RequireInsideRun(pluginRoot);
		RequireInsideRun(currentManifest);
		RequireInsideRun(previous.TargetManifest);
		RequireNoReparseAncestors(pluginRoot);
		foreach (string path in new[] { previous.StopPath, currentManifest, previous.TargetManifest,
			previous.TargetManifest + ".stop", currentManifest + ".stop" })
		{
			RequireInsideRun(path);
			if (File.Exists(path))
			{
				RequireNoReparseAncestors(path);
				File.Delete(path);
			}
		}
		if (Directory.Exists(pluginRoot))
		{
			Directory.Delete(pluginRoot, true);
		}
	}
}

internal sealed record LivePluginRegistrySeed(string PathValueName, string EnabledValueName, string PluginPath,
	int EnabledValue)
{
	internal static LivePluginRegistrySeed Create(string pluginPath, bool currentEnabled)
	{
		if (string.IsNullOrWhiteSpace(pluginPath) || !Path.IsPathFullyQualified(pluginPath)
			|| !string.Equals(Path.GetExtension(pluginPath), ".dll", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The owned plugin registry seed requires one absolute DLL path.");
		}

		return new LivePluginRegistrySeed("00000000 A", "00000000 B", Path.GetFullPath(pluginPath), currentEnabled ? 1 : 0);
	}
}

internal sealed record OwnedPluginRegistryReadback(string[] ValueNames, string PathValueKind, string EnabledValueKind,
	string PluginPath, int EnabledValue, int ValueCount, bool ExactMatch);

internal sealed record LivePluginCycle(int ProcessId, int TargetProcessId, long Nonce, bool Enabled,
	string PreviousInstanceId, string CurrentInstanceId, string PreviousEndpoint, string CurrentEndpoint,
	bool InstanceIdChanged, bool EndpointChanged, bool AccessTokenChanged, DateTimeOffset CompletedAt);

internal sealed record LivePluginDisabledObservation(long Nonce, string BreakpointAddress, bool PluginChecked,
	bool StatusMenuPresent, bool StatusMenuDisabled, bool BreakpointPresent, int OpenedProcessId, int BreakpointCount,
	string[] BreakpointSamples, string[] CleanupProbeFields, string RawBreakpointSnapshot, DateTimeOffset ObservedAt)
{
	internal bool CleanupRemovalSucceeded => CleanupProbeFields.Length == 12
		&& CleanupProbeFields[0] == "called" && CleanupProbeFields[3] == "true"
		&& CleanupProbeFields[4] == "boolean" && CleanupProbeFields[5] == "true"
		&& CleanupProbeFields[6].Length == 0;

	internal static LivePluginDisabledObservation ParseResponse(string response, long expectedNonce, string expectedAddress,
		int expectedProcessId)
	{
		string[] fields = response.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		if (fields.Length != 23 || fields[0] != "ok" || !long.TryParse(fields[1], out long received) || received != expectedNonce
			|| fields[2] != "observe" || fields[3] != expectedAddress || fields[4] is not ("enabled" or "disabled")
			|| fields[5] is not ("disabled" or "other" or "absent") || fields[6] is not ("present" or "absent")
			|| !int.TryParse(fields[7], out int openedProcessId) || openedProcessId < 0
			|| !int.TryParse(fields[8], out int breakpointCount) || breakpointCount < 0 || fields[9].Length > 2048
			|| fields[10] is not ("uninstalled" or "installed-no-call" or "called")
			|| fields[18].Length > 1024 || fields[20].Length > 1024 || fields[21].Length > 1536 || fields[22].Length > 1200
			|| !fields[22].StartsWith("raw:", StringComparison.Ordinal))
		{
			throw new InvalidOperationException("The fixed CE Plugin Manager bridge returned an invalid bounded lifecycle observation.");
		}
		if (expectedProcessId <= 0 || openedProcessId != expectedProcessId)
		{
			throw new InvalidOperationException("The fixed CE Plugin Manager observation did not match the owned target process.");
		}

		string[] breakpointSamples = fields[9].Length == 0 ? [] : fields[9].Split(';');
		if (breakpointSamples.Length > 16 || breakpointSamples.Length > breakpointCount
			|| breakpointSamples.Any(static sample => sample.Length is 0 or > 128))
		{
			throw new InvalidOperationException("The fixed CE Plugin Manager breakpoint sample is invalid.");
		}
		string[] cleanupProbeFields = fields[10..22];
		if (fields[10] == "called"
			&& (fields[11].Length is 0 or > 32 || fields[12].Length is 0 or > 32
				|| fields[13] is not ("true" or "false") || fields[14].Length is 0 or > 32
				|| fields[15].Length > 32 || fields[16].Length > 32
				|| !int.TryParse(fields[17], out int beforeCount) || beforeCount < -1
				|| !int.TryParse(fields[19], out int afterCount) || afterCount < -1))
		{
			throw new InvalidOperationException("The fixed CE Plugin Manager cleanup receipt is invalid.");
		}
		if (fields[10] != "called" && fields[11..21].Any(static field => field.Length != 0))
		{
			throw new InvalidOperationException("The fixed CE Plugin Manager returned receipt fields without a remover call.");
		}

		return new LivePluginDisabledObservation(expectedNonce, expectedAddress, fields[4] == "enabled",
			fields[5] != "absent", fields[5] == "disabled", fields[6] == "present", openedProcessId,
			breakpointCount, breakpointSamples, cleanupProbeFields, fields[22], DateTimeOffset.UtcNow);
	}
}

internal sealed record PendingPluginIdentity(string InstanceId, string Endpoint, string AccessToken);
