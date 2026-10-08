using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

using CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Read-only reviewed-candidate checks performed before a compiler run creates its sandbox.</summary>
[SupportedOSPlatform("windows")]
internal static partial class LiveCompilerCandidate
{
	private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(5);

	internal static object Verify(LiveQualificationInputs inputs)
	{
		RequireReleaseBuild();
		RequireNoReparseAncestors(inputs.RunRoot);
		LiveCompilerPayload.RequireReviewedHashes();
		string commit = Git(inputs.RepositoryRoot, "rev-parse", "HEAD").Trim();
		if (!FullCommit().IsMatch(commit) || !string.IsNullOrWhiteSpace(Git(inputs.RepositoryRoot, "status", "--porcelain")))
		{
			throw new InvalidOperationException("Compiler qualification requires one clean checkout at a full Git commit.");
		}

		string policyName = inputs.Scenario is LiveQualificationScenario.CompilerExtended or LiveQualificationScenario.CompilerInjection
			? "compiler-extended-policy.md"
			: "compiler-probe-policy.md";
		string policy = Path.Combine(inputs.RepositoryRoot, "docs", "qualification", "v2.0.0", policyName);
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
			cleanTree = true,
			productVersion = pluginVersion,
			policy = policyName,
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
