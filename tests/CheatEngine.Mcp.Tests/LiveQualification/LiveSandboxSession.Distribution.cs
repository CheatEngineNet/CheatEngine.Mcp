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
		if (_options.MonoQualification)
		{
			Record("MonoDataCollector64.dll", StageMonoCollector(disabledAutorun, autorun, MonoCollectorSha256));
		}
		if (_options.EnableManagedInjection)
		{
			CopyReviewedAutorun(disabledAutorun, autorun, "DotNetInject.lua",
				"31ED4209776DBFAD74EC811326439D26C02B6AB653056D5E171D952C12D3F25B");
		}
		File.WriteAllText(Path.Combine(installation, "ce.runtimeconfig.json"), """
		                                                                       {"runtimeOptions":{"tfm":"net10.0","rollForward":"LatestMinor","frameworks":[
		                                                                       {"name":"Microsoft.NETCore.App","version":"10.0.0"},
		                                                                       {"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"},
		                                                                       {"name":"Microsoft.AspNetCore.App","version":"10.0.0"}]}}
		                                                                       """);
	}

	internal static string DistributionDirectory(string repository, string configuration)
	{
		string? custom = Environment.GetEnvironmentVariable("CHEATENGINE_MCP_LIVE_QUALIFICATION_DISTRIBUTION_DIRECTORY");
		return custom is not null && !Path.IsPathFullyQualified(custom)
			? throw new InvalidOperationException("The live qualification distribution directory must be absolute.")
			: custom ?? Path.Combine(repository, "artifacts", "dist", configuration);
	}

	private void ValidateDistribution(string distribution)
	{
		if (!Path.IsPathFullyQualified(distribution) || !Directory.Exists(distribution))
		{
			throw new DirectoryNotFoundException("The selected live qualification distribution directory is unavailable.");
		}

		LiveCompilerCandidate.RequireNoReparseAncestors(distribution);
		string[] expected = [PluginFileName, "CheatEngine.Mcp.Gateway.exe", "LICENSE", "README.md", "THIRD-PARTY-NOTICES.md"];
		string[] actual = [.. Directory.EnumerateFiles(distribution, "*", SearchOption.TopDirectoryOnly)
			.Select(Path.GetFileName).Where(name => name is not null).Select(name => name!).Order(StringComparer.Ordinal)];
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
		string[] expectedPluginFiles = _options.RequiresCapabilityConfiguration
			? [PluginFileName, "appsettings.json"]
			: [PluginFileName];
		if (!pluginFiles.SequenceEqual(expectedPluginFiles, StringComparer.Ordinal)
			|| CheatEngineInstallation.Sha256(Path.Combine(pluginSource, PluginFileName)) !=
			CheatEngineInstallation.Sha256(Path.Combine(pluginDirectory, PluginFileName)))
		{
			throw new InvalidOperationException($"Live host {name} did not stage exactly the published single plugin DLL.");
		}

		string gatewayDirectory = Path.GetDirectoryName(GatewayExecutablePath)!;
		string[] gatewayFiles = [.. Directory.EnumerateFileSystemEntries(gatewayDirectory)
			.Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal)];
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
		return [.. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
			.Select(path => Path.GetRelativePath(directory, path)).Order(StringComparer.Ordinal)];
	}

	internal static string StageMonoCollector(string sourceAutorun, string destinationAutorun, string expectedHash)
	{
		string source = Path.Combine(sourceAutorun, "dlls", "MonoDataCollector64.dll");
		string destination = Path.Combine(destinationAutorun, "dlls", "MonoDataCollector64.dll");
		LiveCompilerCandidate.RequireNoReparseAncestors(source);
		LiveCompilerCandidate.RequireNoReparseAncestors(destination);
		string hash = CheatEngineInstallation.Sha256(source);
		if (!string.Equals(hash, expectedHash, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("The Mono collector differs from the reviewed live-test version.");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
		LiveCompilerCandidate.RequireNoReparseAncestors(destination);
		File.Copy(source, destination);
		return hash;
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
}
