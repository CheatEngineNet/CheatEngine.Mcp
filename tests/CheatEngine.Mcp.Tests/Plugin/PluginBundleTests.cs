using System.Diagnostics;

using CheatEngine.Mcp.Plugin;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Plugin;

/// <summary>Exercises the installed one-DLL plugin from a fresh process with no managed deployment sidecars.</summary>
public sealed class PluginBundleTests
{
	[Fact]
	public async Task InstalledPlugin_LoneDll_ColdLoadsAcrossIsolatedContextsWithNativeLuaBridge()
	{
		await using PluginBundleFixture fixture = new();
		Assert.Equal([Path.GetFileName(fixture.PluginPath)],
			Directory.GetFiles(fixture.InstallDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal));

		ProbeResult result = await fixture.RunProbeAsync();

		Assert.True(result.ExitCode == 0, result.Output);
		Assert.Contains("PLUGIN_BUNDLE_PROBE_OK", result.Output, StringComparison.Ordinal);
	}

	[Fact]
	public async Task InstalledPlugin_LoneDll_ReflectedCatalogMatchesReviewedContractInBothContexts()
	{
		await using PluginBundleFixture fixture = new();
		Assert.Equal([Path.GetFileName(fixture.PluginPath)],
			Directory.GetFiles(fixture.InstallDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
		string goldenDirectory = Path.Combine(RepositoryPaths.Root, "tests", "CheatEngine.Mcp.Tests", "Contract", "Golden");

		ProbeResult result = await fixture.RunProbeAsync(goldenDirectory);

		Assert.True(result.ExitCode == 0, result.Output);
		Assert.Equal(2, result.Output.Split('\n').Count(static line =>
			line.StartsWith("PLUGIN_BUNDLE_REFLECTION_OK ", StringComparison.Ordinal)));
		Assert.Contains("PLUGIN_BUNDLE_PROBE_OK", result.Output, StringComparison.Ordinal);
	}

	private sealed class PluginBundleFixture : IAsyncDisposable
	{
		private readonly string _root = Path.Combine(RepositoryPaths.Root, "artifacts", "test-results", "plugin-bundle",
			Guid.NewGuid().ToString("N"));

		internal PluginBundleFixture()
		{
			InstallDirectory = Directory.CreateDirectory(Path.Combine(_root, "installed-plugin")).FullName;
			PluginPath = Path.Combine(InstallDirectory, "CheatEngine.Mcp.dll");
			File.Copy(typeof(CheatEngineMcpPlugin).Assembly.Location, PluginPath);
		}

		internal string InstallDirectory
		{
			get;
		}

		internal string PluginPath
		{
			get;
		}

		public ValueTask DisposeAsync()
		{
			return new ValueTask(TestDirectory.DeleteAsync(_root));
		}

		internal async Task<ProbeResult> RunProbeAsync(string? goldenDirectory = null)
		{
			string target = FindLiveTarget();
			string runtimeConfig = Path.ChangeExtension(typeof(PluginBundleTests).Assembly.Location, ".runtimeconfig.json");
			if (!File.Exists(runtimeConfig))
			{
				throw new FileNotFoundException("The test runtime configuration is missing.", runtimeConfig);
			}

			ProcessStartInfo start = new("dotnet")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				WorkingDirectory = RepositoryPaths.Root
			};
			start.ArgumentList.Add("exec");
			start.ArgumentList.Add("--runtimeconfig");
			start.ArgumentList.Add(runtimeConfig);
			start.ArgumentList.Add(target);
			start.ArgumentList.Add("--probe-plugin");
			start.ArgumentList.Add(PluginPath);
			if (goldenDirectory is not null)
			{
				start.ArgumentList.Add(goldenDirectory);
			}

			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
			timeout.CancelAfter(TimeSpan.FromSeconds(30));
			using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the plugin bundle probe.");
			Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
			Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
			try
			{
				await process.WaitForExitAsync(timeout.Token);
				return new ProbeResult(process.ExitCode, await output + await error);
			}
			finally
			{
				if (!process.HasExited)
				{
					process.Kill(true);
					await process.WaitForExitAsync(CancellationToken.None);
				}
			}
		}

		private static string FindLiveTarget()
		{
			string configuration = new FileInfo(typeof(PluginBundleTests).Assembly.Location).Directory?.Name
				?? throw new DirectoryNotFoundException("The test assembly has no output directory.");
			string target = Path.Combine(RepositoryPaths.Root, "artifacts", "bin", "CheatEngine.Mcp.LiveTarget", configuration,
				"CheatEngine.Mcp.LiveTarget.dll");
			return File.Exists(target)
				? target
				: throw new FileNotFoundException("The LiveTarget probe was not built for the current test configuration.", target);
		}
	}

	private sealed record ProbeResult(int ExitCode, string Output);
}
