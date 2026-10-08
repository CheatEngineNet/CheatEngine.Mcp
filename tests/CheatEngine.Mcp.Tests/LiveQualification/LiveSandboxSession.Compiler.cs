using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Compiler-only lifecycle hooks; main session integration supplies the private-copy and driver call sites.</summary>
internal sealed partial class LiveSandboxSession
{
	private static readonly TimeSpan CompilerHoldTimeout = TimeSpan.FromSeconds(10);
	private readonly List<PrivateCompilerQuarantine> _privateCompilerQuarantines = [];

	internal static void ValidateCompilerVariant(LiveCompilerVariant variant, CompilerTempTopology topology,
		bool enableManagedInjection)
	{
		if (variant == LiveCompilerVariant.HeldExportAfterB && topology != CompilerTempTopology.Shared)
		{
			throw new InvalidOperationException("The held export variant requires the reviewed shared temporary topology.");
		}
		if (enableManagedInjection && variant != LiveCompilerVariant.HeldExportAfterB)
		{
			throw new InvalidOperationException("Managed injection requires the reviewed held-export variant.");
		}
	}

	/// <summary>Arms exactly one successful original compileCS result to publish an owned hold receipt.</summary>
	internal Task<JsonNode> ArmCompilerHoldAsync(string hostName) => CompilerProbeAsync(hostName, "armHold");

	/// <summary>Waits for the fixed bridge receipt without issuing a Lua request while CE is deliberately held.</summary>
	internal async Task<string> WaitForCompilerHoldAsync(string hostName, CancellationToken cancellationToken = default)
	{
		OwnedHost host = GetRunningHost(hostName);
		string receipt = CompilerHoldReadyPath(hostName);
		RequireInsideRun(receipt);
		Stopwatch elapsed = Stopwatch.StartNew();
		while (!File.Exists(receipt))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (host.Process is null || host.Process.HasExited || elapsed.Elapsed > CompilerHoldTimeout)
			{
				throw new TimeoutException("The fixed compiler hold receipt was not published before its deadline.");
			}
			await Task.Delay(50, cancellationToken);
		}

		RequireNoReparseAncestors(receipt);
		using FileStream stream = new(receipt, FileMode.Open, FileAccess.Read, FileShare.Read);
		RequireNoReparseAncestors(receipt);
		if (stream.Length is <= 0 or > 4096)
		{
			throw new InvalidOperationException("The fixed compiler hold receipt has an invalid bounded length.");
		}
		using StreamReader reader = new(stream, new UTF8Encoding(false, true), false);
		char[] buffer = new char[4097];
		int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
		if (read == buffer.Length || await reader.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) != 0)
		{
			throw new InvalidOperationException("The fixed compiler hold receipt exceeds its bounded text length.");
		}
		string rawPath = new string(buffer, 0, read);
		if (rawPath.Length == 0 || rawPath.IndexOfAny(['\r', '\n']) >= 0)
		{
			throw new InvalidOperationException("The fixed compiler hold receipt did not contain one exact assembly path.");
		}
		return rawPath;
	}

	/// <summary>Publishes the one owned release file directly because the held CE timer cannot service another request.</summary>
	internal async Task ReleaseCompilerHoldAsync(string hostName, CancellationToken cancellationToken = default)
	{
		_ = GetRunningHost(hostName);
		string release = CompilerHoldReleasePath(hostName);
		RequireInsideRun(release);
		RequireNoReparseAncestors(release);
		if (File.Exists(release))
		{
			throw new InvalidOperationException("The fixed compiler hold was already released.");
		}

		string partial = release + ".partial";
		RequireInsideRun(partial);
		await File.WriteAllTextAsync(partial, "release", cancellationToken);
		File.Move(partial, release, false);
	}

	internal string CompilerHoldReadyPath(string hostName) =>
		Path.Combine(CompilerRoots(hostName).Temp, $"hold-ready-{hostName}.txt");

	internal string CompilerHoldReleasePath(string hostName) =>
		Path.Combine(CompilerRoots(hostName).Temp, $"hold-release-{hostName}.txt");

	/// <summary>Removes only the reviewed compiler DLL from private host A before CE starts.</summary>
	internal void ApplyPrivateCompilerAbsentVariant(string hostName, string installation)
	{
		if (_options.Variant != LiveCompilerVariant.PrivateCompilerAbsent || hostName != "A")
		{
			return;
		}

		string installed = Path.Combine(_source ?? throw new InvalidOperationException("The CE source was not prepared."), "CSCompiler.dll");
		string privateCopy = Path.Combine(installation, "CSCompiler.dll");
		string quarantineRoot = Path.Combine(_layout.RunDirectory, "compiler", "quarantine", hostName);
		string quarantine = Path.Combine(quarantineRoot, "CSCompiler.dll");
		RequireInsideRun(privateCopy);
		RequireInsideRun(quarantine);
		RequireNoReparseAncestors(privateCopy);
		RequireNoReparseAncestors(quarantineRoot);
		if (!File.Exists(installed) || !File.Exists(privateCopy))
		{
			throw new FileNotFoundException("The private compiler-absence variant requires the installed CSCompiler.dll source.", installed);
		}
		string installedHash = CheatEngineInstallation.Sha256(installed);
		string privateHash = CheatEngineInstallation.Sha256(privateCopy);
		if (!string.Equals(installedHash, privateHash, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("The private CSCompiler.dll does not match the installed source identity.");
		}

		Directory.CreateDirectory(quarantineRoot);
		if (File.Exists(quarantine))
		{
			throw new IOException("The run-owned CSCompiler.dll quarantine path is unexpectedly occupied.");
		}
		File.Move(privateCopy, quarantine, false);
		_privateCompilerQuarantines.Add(new PrivateCompilerQuarantine(privateCopy, quarantine, privateHash));
		Record("compiler_private_component_quarantined", new
		{
			hostName,
			sha256 = privateHash
		});
	}

	/// <summary>Restores a private compiler component only when its quarantine identity still matches.</summary>
	internal void RestorePrivateCompilerVariants()
	{
		foreach (PrivateCompilerQuarantine item in _privateCompilerQuarantines)
		{
			RequireInsideRun(item.PrivatePath);
			RequireInsideRun(item.QuarantinePath);
			RequireNoReparseAncestors(item.QuarantinePath);
			if (!File.Exists(item.QuarantinePath) || File.Exists(item.PrivatePath) ||
				!string.Equals(CheatEngineInstallation.Sha256(item.QuarantinePath), item.Sha256, StringComparison.Ordinal))
			{
				throw new InvalidOperationException("The private compiler quarantine identity changed; it is retained for recovery.");
			}
			File.Move(item.QuarantinePath, item.PrivatePath, false);
		}
		if (_privateCompilerQuarantines.Count != 0)
		{
			Record("compiler_private_component_restored", true);
			_privateCompilerQuarantines.Clear();
		}
	}

	private OwnedHost GetRunningHost(string hostName)
	{
		if (!_hosts.TryGetValue(hostName, out OwnedHost? host) || host.Process is null || host.Stopped)
		{
			throw new InvalidOperationException($"Owned host '{hostName}' is not running.");
		}
		return host;
	}

	private sealed record PrivateCompilerQuarantine(string PrivatePath, string QuarantinePath, string Sha256);
}
