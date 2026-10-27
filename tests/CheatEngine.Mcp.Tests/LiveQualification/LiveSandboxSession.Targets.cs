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
	internal async Task TerminateExecutionTargetsBeforeHostCleanupAsync(string reason, List<Exception> failures)
	{
		if (!_options.UsesTargetFirstExecutionShutdown)
		{
			return;
		}

		await LiveExecutionCleanup.AttemptAllAsync(_hosts.Keys.Order(StringComparer.Ordinal).ToArray(),
			name => TerminateOwnedTargetBeforeHostCleanupAsync(name, reason), failures);
	}

	internal async Task TerminateOwnedTargetBeforeHostCleanupAsync(string name, string reason)
	{
		if (!_options.UsesTargetFirstExecutionShutdown || !_hosts.TryGetValue(name, out OwnedHost? host))
		{
			throw new InvalidOperationException("Target-first termination is available only to the admitted execution scenario.");
		}

		if (host.TargetExitConfirmed)
		{
			try
			{
				PublishTargetExitConfirmation(name, host, reason);
				return;
			}
			catch
			{
				host.HostCleanupBlocked = true;
				throw;
			}
		}

		bool exitPublished = false;
		try
		{
			bool forced = false;
			List<Exception> markerFailures = [];
			Process target = host.Target ?? throw new InvalidOperationException("The owned target is unavailable before exit confirmation.");
			await LiveExecutionCleanup.ConfirmTargetExitAsync(
				() => File.WriteAllText(host.TargetManifest + ".stop", "stop"),
				() => target.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)),
				() => { forced = true; target.Kill(true); },
				() => { target.Refresh(); return target.HasExited; }, markerFailures);

			target.Dispose();
			host.Target = null;
			host.TargetExitConfirmed = true;
			host.TargetTerminationForced = forced;
			PublishTargetExitConfirmation(name, host, reason);
			exitPublished = true;
			if (markerFailures.Count != 0)
			{
				throw new AggregateException("The owned target exited, but writing its stop marker failed.", markerFailures);
			}
		}
		catch when (exitPublished)
		{
			throw;
		}
		catch
		{
			host.HostCleanupBlocked = true;
			Record($"target_{name}_termination_failed_before_host_cleanup", new
			{
				reason,
				host.Public.TargetProcessId
			});
			throw;
		}
	}

	private void PublishTargetExitConfirmation(string name, OwnedHost host, string reason)
	{
		File.WriteAllText(host.TargetExitPath, "exited");
		host.HostCleanupBlocked = false;
		if (!host.TargetTerminationReceiptEmitted)
		{
			Record($"target_{name}_terminated_before_host_cleanup",
				new
				{
					reason,
					host.Public.TargetProcessId,
					forced = host.TargetTerminationForced
				});
			host.TargetTerminationReceiptEmitted = true;
		}
	}

	/// <summary>Runs one fixed compiler availability or compile probe through the test-owned private autorun bridge.</summary>

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
			LiveTargetManifest facts = ReadTargetManifest(await ReadPublishedTargetManifestAsync(manifest, target, name), target.Id, name);
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
				UnreadableRootAddress = facts.UnreadableRootAddress,
				ManagedObjectAddress = facts.ManagedObjectAddress,
				ManagedObjectType = facts.ManagedObjectType,
				ManagedObjectMarker = facts.ManagedObjectMarker,
				ManagedObjectMirror = facts.ManagedObjectMirror,
				ManagedObjectToggle = facts.ManagedObjectToggle,
				DebuggerExecution = facts.DebuggerExecution
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
		if (TargetArchitecture == "x86")
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
		else if (_options.DebuggerQualification || _options.ActivePluginLifecycleQualification)
		{
			start.ArgumentList.Add("--debugger-loop");
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
		LiveDebuggerExecutionQualification.Manifest? debuggerExecution = manifest["debuggerExecution"] is { } loop
			? LiveDebuggerExecutionQualification.ReadManifest(loop, processId) : null;
		if ((_options.DebuggerQualification || _options.ActivePluginLifecycleQualification) && debuggerExecution is null)
		{
			throw new InvalidDataException("The debugger target omitted its owned advancing thread manifest.");
		}
		return width != (TargetArchitecture == "x86" ? 32 : 64)
			? throw new InvalidOperationException($"Target manifest {name} reported pointer width {width}, not the requested {TargetArchitecture} width.")
			: new LiveTargetManifest(manifest["address"]!.GetValue<string>(),
			manifest["pointerRootAddress"]!.GetValue<string>(), manifest["pointerTargetAddress"]!.GetValue<string>(),
			manifest["zeroPointerRootAddress"]!.GetValue<string>(), manifest["unreadableRootAddress"]!.GetValue<string>(),
			manifest["managedObjectAddress"]?.GetValue<string>() ?? string.Empty,
			manifest["managedObjectType"]?.GetValue<string>() ?? string.Empty,
			manifest["managedObjectMarker"]?.GetValue<int>(), manifest["managedObjectMirror"]?.GetValue<int>(),
			manifest["managedObjectToggle"]?.GetValue<int>(), debuggerExecution);
	}

	private async Task<JsonNode> ReadPublishedTargetManifestAsync(string path, Process target, string name)
	{
		RequireInsideRun(path);
		LiveTargetManifestReader.Result read = await LiveTargetManifestReader.ReadAsync(path, target,
			TimeSpan.FromSeconds(10), CancellationToken.None);
		Record($"target_{name}_manifest_ready", new
		{
			processId = target.Id,
			read.Attempts,
			read.SharingRetries,
			read.ElapsedMs
		});
		return read.Node;
	}
}
