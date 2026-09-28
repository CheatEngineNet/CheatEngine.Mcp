// Adapted from CheatEngine.Client (MIT); see NOTICE.md.

using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>
///     Verifies the source Cheat Engine installation without writing to it, copies it into the run's sandbox, verifies the
///     copy again, and proves after the run that the source is unchanged. Nothing here ever writes below the source.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class CheatEngineInstallation
{
	/// <summary>The folder whose Lua files Cheat Engine runs at startup.</summary>
	internal const string AutorunFolder = "autorun";

	/// <summary>Every way <paramref name="directory" /> differs from <paramref name="profile" />; empty when it matches.</summary>
	internal static IReadOnlyList<string> Verify(string directory, CheatEngineProfile profile,
		IExecutableInspector inspector)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		ArgumentNullException.ThrowIfNull(profile);
		ArgumentNullException.ThrowIfNull(inspector);

		List<string> problems = [];
		string host = Path.Combine(directory, profile.HostExecutable);
		if (!File.Exists(host))
		{
			problems.Add($"{profile.HostExecutable} is missing.");
		}
		else
		{
			string sha256 = Sha256(host);
			if (!string.Equals(sha256, profile.HostSha256, StringComparison.Ordinal))
			{
				problems.Add($"{profile.HostExecutable} has SHA-256 {sha256}, expected {profile.HostSha256}.");
			}

			ExecutableFacts facts;
			try
			{
				facts = inspector.Describe(host);
			}
			catch (BadImageFormatException)
			{
				facts = new ExecutableFacts("NotAPortableExecutable", null);
			}

			if (!string.Equals(facts.Machine, profile.HostMachine, StringComparison.Ordinal))
			{
				problems.Add($"{profile.HostExecutable} is built for {facts.Machine}, expected {profile.HostMachine}.");
			}

			if (!string.Equals(facts.FileVersion, profile.HostFileVersion, StringComparison.Ordinal))
			{
				problems.Add(
					$"{profile.HostExecutable} has file version {facts.FileVersion ?? "(none)"}, expected {profile.HostFileVersion}.");
			}
		}

		foreach ((string target, string expected) in profile.TargetSha256.OrderBy(static pair => pair.Key,
					 StringComparer.Ordinal))
		{
			string path = Path.Combine(directory, target);
			if (!File.Exists(path))
			{
				problems.Add($"{target} is missing.");
				continue;
			}

			string sha256 = Sha256(path);
			if (!string.Equals(sha256, expected, StringComparison.Ordinal))
			{
				problems.Add($"{target} has SHA-256 {sha256}, expected {expected}.");
			}
		}

		return problems;
	}

	/// <summary>The host hash and the autorun listing of <paramref name="directory" />.</summary>
	internal static InstallationFingerprint Fingerprint(string directory, CheatEngineProfile profile)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		ArgumentNullException.ThrowIfNull(profile);

		string host = Path.Combine(directory, profile.HostExecutable);
		string autorun = Path.Combine(directory, AutorunFolder);
		List<string> listing = [];
		if (Directory.Exists(autorun))
		{
			foreach (string file in Directory.EnumerateFiles(autorun, "*", SearchOption.AllDirectories))
			{
				string relative = Path.GetRelativePath(autorun, file).Replace('\\', '/');
				listing.Add(string.Create(CultureInfo.InvariantCulture,
					$"{relative} {new FileInfo(file).Length} {Sha256(file)}"));
			}
		}

		listing.Sort(StringComparer.Ordinal);
		return new InstallationFingerprint(File.Exists(host) ? Sha256(host) : "missing", listing);
	}

	/// <summary>Every difference between two fingerprints; empty when the installation is unchanged.</summary>
	internal static IReadOnlyList<string> Compare(InstallationFingerprint before, InstallationFingerprint after)
	{
		ArgumentNullException.ThrowIfNull(before);
		ArgumentNullException.ThrowIfNull(after);

		List<string> differences = [];
		if (!string.Equals(before.HostSha256, after.HostSha256, StringComparison.Ordinal))
		{
			differences.Add($"host executable SHA-256 {before.HostSha256} became {after.HostSha256}");
		}

		differences.AddRange(before.Autorun.Except(after.Autorun, StringComparer.Ordinal)
			.Select(static line => $"autorun lost or changed: {line}"));
		differences.AddRange(after.Autorun.Except(before.Autorun, StringComparer.Ordinal)
			.Select(static line => $"autorun gained or changed: {line}"));
		return differences;
	}

	/// <summary>
	///     Copies the whole source installation into <paramref name="destination" /> (which must not exist yet), then
	///     verifies the copy against the profile and proves that its autorun folder equals the source's.
	/// </summary>
	internal static void CopyTo(string source, string destination, CheatEngineProfile profile,
		IExecutableInspector inspector)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(source);
		ArgumentException.ThrowIfNullOrWhiteSpace(destination);
		ArgumentNullException.ThrowIfNull(profile);
		if (Directory.Exists(destination) || File.Exists(destination))
		{
			throw new IOException($"The sandbox '{destination}' already exists.");
		}

		string root = Path.GetFullPath(source);
		Directory.CreateDirectory(destination);
		foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
		{
			Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(root, directory)));
		}

		foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
		{
			string copy = Path.Combine(destination, Path.GetRelativePath(root, file));
			File.Copy(file, copy);
			File.SetAttributes(copy, File.GetAttributes(copy) & ~FileAttributes.ReadOnly);
		}

		IReadOnlyList<string> problems = Verify(destination, profile, inspector);
		if (problems.Count > 0)
		{
			throw new InvalidOperationException(
				$"The sandbox copy differs from the profile: {string.Join(" ", problems)}");
		}

		IReadOnlyList<string> autorun = Compare(Fingerprint(root, profile), Fingerprint(destination, profile));
		if (autorun.Count > 0)
		{
			throw new InvalidOperationException(
				$"The sandbox copy differs from its source: {string.Join("; ", autorun)}.");
		}
	}

	/// <summary>The upper-case SHA-256 of a file.</summary>
	internal static string Sha256(string path)
	{
		using FileStream stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}
}
