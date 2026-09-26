// Adapted from CheatEngine.Client; see NOTICE.md and licenses/CheatEngine.Client.LICENSE.
using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>The exact Cheat Engine installation a live run qualifies against.</summary>
/// <param name="Profile">The profile id a qualification result names.</param>
/// <param name="HostExecutable">The file name of the host executable.</param>
/// <param name="HostSha256">The upper-case SHA-256 of the host executable.</param>
/// <param name="HostFileVersion">The file version resource of the host executable.</param>
/// <param name="HostMachine">The COFF machine of the host executable.</param>
/// <param name="TargetSha256">The disposable targets, by file name, with their upper-case SHA-256.</param>
[SupportedOSPlatform("windows")]
internal sealed record CheatEngineProfile(
	string Profile,
	string HostExecutable,
	string HostSha256,
	string HostFileVersion,
	string HostMachine,
	IReadOnlyDictionary<string, string> TargetSha256)
{
	/// <summary>The x64 gtutorial target.</summary>
	internal const string Target64 = "gtutorial-x86_64.exe";

	/// <summary>The x86 gtutorial target.</summary>
	internal const string Target32 = "gtutorial-i386.exe";

	/// <summary>
	///     Cheat Engine 7.7.0.10621 x64 with its two gtutorial targets, the profile the harness gate also pins
	///     (<c>QualificationAuthorization.ExactCheatEngineSha256</c>).
	/// </summary>
	internal static CheatEngineProfile CheatEngine77
	{
		get;
	} = new("ce-7.7.0.10621-x64-managed-hostfxr", "cheatengine-x86_64.exe",
		"9727076DA50924E4A097B49A02155E4B34759269C3017FF31375364B8826EB4D", "7.7.0.10621", "Amd64",
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			[Target64] = "2DABEFFD5DD45A3DA79697D6B8A3B7942A9EFF0EA78519A313ECB29AD5A865E3",
			[Target32] = "9131B1CA916D6AC1FB67224A67CF0F578105D43AEEEBB03137E94D73CBE11BCA"
		});
}

/// <summary>The PE facts the verification needs about an executable.</summary>
/// <param name="Machine">The COFF machine, for example <c>Amd64</c>.</param>
/// <param name="FileVersion">The file version resource, or <see langword="null" />.</param>
internal readonly record struct ExecutableFacts(string Machine, string? FileVersion);

/// <summary>Reads <see cref="ExecutableFacts" />; tests substitute one for fake files.</summary>
internal interface IExecutableInspector
{
	/// <summary>Describes the executable at <paramref name="path" />.</summary>
	public ExecutableFacts Describe(string path);
}

/// <summary>Reads the COFF machine with <see cref="PEReader" /> and the version resource with <see cref="FileVersionInfo" />.</summary>
[SupportedOSPlatform("windows")]
internal sealed class PortableExecutableInspector : IExecutableInspector
{
	/// <summary>The shared instance.</summary>
	internal static PortableExecutableInspector Instance
	{
		get;
	} = new();

	/// <inheritdoc />
	public ExecutableFacts Describe(string path)
	{
		using FileStream stream = File.OpenRead(path);
		using PEReader reader = new(stream);
		return new ExecutableFacts(reader.PEHeaders.CoffHeader.Machine.ToString(), FileVersionInfo.GetVersionInfo(path).FileVersion);
	}
}

/// <summary>What must not change in the source installation: the host executable and the autorun folder.</summary>
/// <param name="HostSha256">The SHA-256 of the host executable.</param>
/// <param name="Autorun">One <c>relative/path length SHA-256</c> line per autorun file, ordinally sorted.</param>
internal sealed record InstallationFingerprint(string HostSha256, IReadOnlyList<string> Autorun);

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
	internal static IReadOnlyList<string> Verify(string directory, CheatEngineProfile profile, IExecutableInspector inspector)
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
				problems.Add($"{profile.HostExecutable} has file version {facts.FileVersion ?? "(none)"}, expected {profile.HostFileVersion}.");
			}
		}

		foreach ((string target, string expected) in profile.TargetSha256.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
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
				listing.Add(string.Create(CultureInfo.InvariantCulture, $"{relative} {new FileInfo(file).Length} {Sha256(file)}"));
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

		differences.AddRange(before.Autorun.Except(after.Autorun, StringComparer.Ordinal).Select(static line => $"autorun lost or changed: {line}"));
		differences.AddRange(after.Autorun.Except(before.Autorun, StringComparer.Ordinal).Select(static line => $"autorun gained or changed: {line}"));
		return differences;
	}

	/// <summary>
	///     Copies the whole source installation into <paramref name="destination" /> (which must not exist yet), then
	///     verifies the copy against the profile and proves that its autorun folder equals the source's.
	/// </summary>
	internal static void CopyTo(string source, string destination, CheatEngineProfile profile, IExecutableInspector inspector)
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
			throw new InvalidOperationException($"The sandbox copy differs from the profile: {string.Join(" ", problems)}");
		}

		IReadOnlyList<string> autorun = Compare(Fingerprint(root, profile), Fingerprint(destination, profile));
		if (autorun.Count > 0)
		{
			throw new InvalidOperationException($"The sandbox copy differs from its source: {string.Join("; ", autorun)}.");
		}
	}

	/// <summary>The upper-case SHA-256 of a file.</summary>
	internal static string Sha256(string path)
	{
		using FileStream stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}
}
