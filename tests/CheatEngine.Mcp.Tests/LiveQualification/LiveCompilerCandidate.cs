using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Read-only reviewed-candidate checks performed before a compiler run creates its sandbox.</summary>
[SupportedOSPlatform("windows")]
internal static partial class LiveCompilerCandidate
{
	private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(5);
	internal static string PolicyPath(string repositoryRoot, LiveQualificationScenario scenario)
	{
		string policyName = scenario is LiveQualificationScenario.CompilerExtended or LiveQualificationScenario.CompilerInjection
			? "compiler-extended-policy.md"
			: "compiler-probe-policy.md";
		return Path.Combine(repositoryRoot, "docs", "qualification", "all-tools", policyName);
	}

	internal static object Verify(LiveQualificationInputs inputs)
	{
		RequireReleaseBuild();
		RequireNoReparseAncestors(inputs.RunRoot);
		LiveCompilerPayload.RequireReviewedHashes();
		string commit = Git(inputs.RepositoryRoot, "rev-parse", "HEAD").Trim();
		bool cleanTree = string.IsNullOrWhiteSpace(Git(inputs.RepositoryRoot, "status", "--porcelain"));
		if (!FullCommit().IsMatch(commit))
		{
			throw new InvalidOperationException("Compiler qualification requires a full Git commit.");
		}
		int frozenInputCount = 0;
		if (inputs.FrozenInputManifestPath is not null && inputs.FrozenInputManifestSha256 is not null)
		{
			frozenInputCount = VerifyFrozenInputManifest(inputs.RepositoryRoot, inputs.FrozenInputManifestPath,
				inputs.FrozenInputManifestSha256, commit, LiveSandboxSession.DistributionDirectory(inputs.RepositoryRoot, "release"));
		}
		else if (!cleanTree)
		{
			throw new InvalidOperationException("Compiler qualification requires a clean checkout or a fully verified frozen-input manifest.");
		}

		string policy = PolicyPath(inputs.RepositoryRoot, inputs.Scenario);
		string distribution = LiveSandboxSession.DistributionDirectory(inputs.RepositoryRoot, "release");
		string plugin = Path.Combine(distribution, "CheatEngine.Mcp.dll");
		string gateway = Path.Combine(distribution, "CheatEngine.Mcp.Gateway.exe");
		if (!File.Exists(policy) || !File.Exists(plugin) || !File.Exists(gateway))
		{
			throw new FileNotFoundException("Compiler qualification requires the reviewed policy and Release distribution.");
		}

		string pluginVersion = RequireCandidateVersion(plugin, commit);
		string gatewayVersion = RequireCandidateVersion(gateway, commit);
		if (!string.Equals(pluginVersion, gatewayVersion, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("The plugin DLL and gateway executable do not have the same candidate ProductVersion.");
		}
		return new
		{
			commit,
			cleanTree,
			frozenInputManifestSha256 = frozenInputCount == 0 ? null : inputs.FrozenInputManifestSha256,
			frozenInputCount,
			productVersion = pluginVersion,
			policy = Path.GetFileName(policy),
			policySha256 = CheatEngineInstallation.Sha256(policy),
			bridgeTemplateSha256 = LiveCompilerBridge.TemplateSha256,
			windowsVersion = Environment.OSVersion.VersionString,
			testRuntime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
			pluginSha256 = CheatEngineInstallation.Sha256(plugin),
			gatewaySha256 = CheatEngineInstallation.Sha256(gateway),
			payloads = new
			{
				valid = LiveCompilerPayload.ValidSha256,
				invalidSource = LiveCompilerPayload.InvalidSourceSha256,
				invalidReference = LiveCompilerPayload.InvalidReferenceSha256
			}
		};
	}

	internal static int VerifyFrozenInputManifest(string repositoryRoot, string manifestPath, string expectedManifestSha256,
		string expectedCommit, string expectedDistribution)
	{
		string repository = Path.GetFullPath(repositoryRoot);
		string manifest = Path.GetFullPath(manifestPath);
		if (!Path.IsPathFullyQualified(manifestPath) || !LiveQualificationOptIn.IsSameOrBelow(manifest, repository) ||
			!Regex.IsMatch(expectedManifestSha256, "^[0-9A-Fa-f]{64}$", RegexOptions.CultureInvariant) ||
			!File.Exists(manifest))
		{
			throw new InvalidDataException("The frozen input manifest path or digest is invalid.");
		}
		RequireNoReparseAncestors(manifest);
		if (!string.Equals(Hash(manifest), expectedManifestSha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The frozen input manifest digest changed.");
		}

		using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifest));
		JsonElement root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object ||
			!string.Equals(root.GetProperty("repository").GetString(), repository, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(root.GetProperty("baseCommit").GetString(), expectedCommit, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(Path.GetFullPath(root.GetProperty("distribution").GetString() ?? string.Empty),
				Path.GetFullPath(expectedDistribution), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The frozen input manifest belongs to a different repository, commit, or distribution.");
		}

		List<(string Path, string Sha256)> files = [];
		AddFiles(root.GetProperty("source"), files);
		AddFiles(root.GetProperty("binaries"), files);
		foreach (JsonElement submodule in root.GetProperty("submodules").EnumerateArray())
		{
			AddFiles(submodule.GetProperty("source"), files);
		}
		if (files.Count == 0 || files.Select(static file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
		{
			throw new InvalidDataException("The frozen input manifest is empty or contains duplicate paths.");
		}

		foreach ((string relativePath, string expectedSha256) in files)
		{
			if (Path.IsPathFullyQualified(relativePath) || !Regex.IsMatch(expectedSha256, "^[0-9A-Fa-f]{64}$", RegexOptions.CultureInvariant))
			{
				throw new InvalidDataException("The frozen input manifest contains an invalid relative path or digest.");
			}
			string path = Path.GetFullPath(Path.Combine(repository, relativePath));
			if (!LiveQualificationOptIn.IsSameOrBelow(path, repository) || !File.Exists(path))
			{
				throw new InvalidDataException("A frozen input path escapes the repository or is missing.");
			}
			RequireNoReparseAncestors(path);
			if (!string.Equals(Hash(path), expectedSha256, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException($"A frozen input changed: {relativePath}");
			}
		}
		return files.Count;
	}

	private static void AddFiles(JsonElement element, List<(string Path, string Sha256)> files)
	{
		if (element.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidDataException("The frozen input manifest has an invalid file list.");
		}
		foreach (JsonElement file in element.EnumerateArray())
		{
			files.Add((file.GetProperty("path").GetString() ?? string.Empty,
				file.GetProperty("sha256").GetString() ?? string.Empty));
		}
	}

	private static string Hash(string path)
	{
		using FileStream file = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(file));
	}

	private static void RequireReleaseBuild()
	{
#if DEBUG
		throw new InvalidOperationException("Compiler qualification requires the Release test build and Release distribution.");
#endif
	}

	internal static void RequireNoReparseAncestors(string path)
	{
		for (string? component = Path.GetFullPath(path); component is not null; component = Path.GetDirectoryName(component))
		{
			if ((File.Exists(component) || Directory.Exists(component)) &&
				(File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0)
			{
				throw new InvalidOperationException("Compiler qualification paths must not traverse reparse points.");
			}
		}
	}

	private static string RequireCandidateVersion(string path, string commit)
	{
		string? version = FileVersionInfo.GetVersionInfo(path).ProductVersion;
		if (string.IsNullOrWhiteSpace(version) || !version.EndsWith($"+{commit}", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"{Path.GetFileName(path)} does not identify candidate commit {commit}.");
		}

		return version;
	}

	private static string Git(string workingDirectory, params string[] arguments)
	{
		ProcessStartInfo start = new("git")
		{
			WorkingDirectory = workingDirectory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		foreach (string argument in arguments)
		{
			start.ArgumentList.Add(argument);
		}

		using Process process = Process.Start(start) ?? throw new InvalidOperationException("Git did not start for candidate verification.");
		Task<string> output = process.StandardOutput.ReadToEndAsync();
		Task<string> error = process.StandardError.ReadToEndAsync();
		if (!process.WaitForExit((int) GitTimeout.TotalMilliseconds))
		{
			process.Kill(true);
			process.WaitForExit();
			throw new TimeoutException("Git candidate verification exceeded its bounded timeout.");
		}

		Task.WaitAll(output, error);
		if (process.ExitCode != 0)
		{
			throw new InvalidOperationException("Git candidate verification did not complete successfully.");
		}

		return output.Result;
	}

	[GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
	private static partial Regex FullCommit();
}
