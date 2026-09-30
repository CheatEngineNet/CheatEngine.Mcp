using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using CheatEngine.Mcp.Plugin;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>Executes the release packager against owned fixtures, without building or contacting GitHub.</summary>
public sealed class ReleaseScriptTests
{
	[Fact]
	public async Task Package_CompleteDistribution_ContainsDllGatewayDependenciesAndMatchingChecksum()
	{
		await using DistributionFixture fixture = new();

		RunResult run = await fixture.RunAsync();

		Assert.Equal(0, run.ExitCode);
		Assert.Equal([fixture.ZipName, "SHA256SUMS.txt"],
			Directory.GetFiles(fixture.Output).Select(Path.GetFileName).Order(StringComparer.Ordinal));
		using ZipArchive archive = ZipFile.OpenRead(fixture.Zip);
		Assert.Contains(archive.Entries, static entry => entry.FullName == "CheatEngine.Mcp.dll");
		Assert.Contains(archive.Entries, static entry => entry.FullName == "CheatEngine.Mcp.Gateway.exe");
		Assert.DoesNotContain(archive.Entries, static entry => entry.FullName.Contains('/'));
		Assert.Equal(Directory.GetFiles(fixture.Distribution, "*", SearchOption.AllDirectories).Length,
			archive.Entries.Count);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			using Stream content = entry.Open();
			Assert.Equal(FileHash(Path.Combine(fixture.Distribution, entry.FullName)),
				Convert.ToHexString(SHA256.HashData(content)));
		}
		Assert.Equal($"{FileHash(fixture.Zip).ToLowerInvariant()}  {fixture.ZipName}\n",
			await File.ReadAllTextAsync(Path.Combine(fixture.Output, "SHA256SUMS.txt"), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Package_RepeatedAndFreshRuns_KeepIdenticalZipBytesAndRemoveObsoleteAssets()
	{
		await using DistributionFixture fixture = new();
		Assert.Equal(0, (await fixture.RunAsync()).ExitCode);
		string original = FileHash(fixture.Zip);
		await File.WriteAllTextAsync(Path.Combine(fixture.Output, "CheatEngine.Mcp.Plugin.dll"), "obsolete",
			TestContext.Current.CancellationToken);

		Assert.Equal(0, (await fixture.RunAsync()).ExitCode);

		Assert.Equal(original, FileHash(fixture.Zip));
		Assert.Equal(2, Directory.GetFiles(fixture.Output).Length);
		File.Delete(fixture.Zip);
		Assert.Equal(0, (await fixture.RunAsync()).ExitCode);
		Assert.Equal(original, FileHash(fixture.Zip));
	}

	[Theory]
	[InlineData("missing plugin", "Incomplete distribution")]
	[InlineData("missing embedded dependency", "Missing embedded plugin dependencies")]
	[InlineData("loose dependency", "Unexpected distribution entries")]
	[InlineData("folder deployment", "Unexpected distribution entries")]
	[InlineData("foreign file", "Unexpected distribution entries")]
	[InlineData("mixed binaries", "same version and commit")]
	public async Task Package_InvalidDistribution_RefusesAndPreservesExistingAssets(string condition, string message)
	{
		await using DistributionFixture fixture = new();
		Assert.Equal(0, (await fixture.RunAsync()).ExitCode);
		string original = FileHash(fixture.Zip);
		switch (condition)
		{
			case "missing plugin":
				File.Delete(Path.Combine(fixture.Distribution, "CheatEngine.Mcp.dll"));
				break;
			case "missing embedded dependency":
				string plugin = Path.Combine(fixture.Distribution, "CheatEngine.Mcp.dll");
				byte[] contents = File.ReadAllBytes(plugin);
				// Remove the complete name from both Costura's lookup data and the manifest resource table.
				byte[] resource = Encoding.UTF8.GetBytes("costura.cheatengine.sdk.hosting.dll.compressed\0");
				int offset = contents.AsSpan().IndexOf(resource);
				Assert.True(offset >= 0, "The fixture has no SDK.Hosting resource to corrupt.");
				while (offset >= 0)
				{
					contents[offset] = (byte) 'x';
					offset = contents.AsSpan().IndexOf(resource);
				}
				File.WriteAllBytes(plugin, contents);
				break;
			case "loose dependency":
				File.Copy(typeof(ToolDispatch).Assembly.Location, Path.Combine(fixture.Distribution, "Dependency.dll"));
				break;
			case "folder deployment":
				Directory.CreateDirectory(Path.Combine(fixture.Distribution, "CheatEngine.Mcp"));
				break;
			case "foreign file":
				await File.WriteAllTextAsync(Path.Combine(fixture.Distribution, "private.txt"),
					"private", TestContext.Current.CancellationToken);
				break;
			case "mixed binaries":
				File.Copy(typeof(string).Assembly.Location, Path.Combine(fixture.Distribution, "CheatEngine.Mcp.Gateway.exe"), true);
				break;
		}

		RunResult run = await fixture.RunAsync();

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains(message, run.Output, StringComparison.Ordinal);
		Assert.Equal(original, FileHash(fixture.Zip));
	}

	[Fact]
	public async Task Package_CorruptArchive_RebuildsAndVerifiesTheCompleteDistribution()
	{
		await using DistributionFixture fixture = new();
		Directory.CreateDirectory(fixture.Output);
		await File.WriteAllTextAsync(fixture.Zip, "not a ZIP", TestContext.Current.CancellationToken);

		Assert.Equal(0, (await fixture.RunAsync()).ExitCode);

		using ZipArchive archive = ZipFile.OpenRead(fixture.Zip);
		Assert.Contains(archive.Entries, static entry => entry.FullName == "CheatEngine.Mcp.dll");
		Assert.DoesNotContain(Directory.GetFiles(fixture.Output), static path => path.EndsWith(".tmp", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Package_WhatIf_CreatesNoReleaseOutput()
	{
		await using DistributionFixture fixture = new();

		RunResult run = await fixture.RunAsync("-WhatIf");

		Assert.Equal(0, run.ExitCode);
		Assert.False(Directory.Exists(fixture.Output));
	}

	[Fact]
	public async Task Package_FailedCheckoutInspection_RefusesBeforeBuildOrOutput()
	{
		await using DistributionFixture fixture = new();

		RunResult run = await fixture.RunWithFailedCheckoutAsync();

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains("git status failed; cannot confirm a clean checkout", run.Output, StringComparison.Ordinal);
		Assert.False(Directory.Exists(fixture.Output));
	}

	[Fact]
	public async Task Package_OutputInsideDistribution_RefusesWithoutWritingAssets()
	{
		await using DistributionFixture fixture = new();

		RunResult run = await fixture.RunAsync("-OutputDirectory", Path.Combine(fixture.Distribution, "release"));

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains("outside the distribution", run.Output, StringComparison.Ordinal);
		Assert.False(Directory.Exists(Path.Combine(fixture.Distribution, "release")));
	}

	private static string FileHash(string path)
	{
		using Stream stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}

	private sealed record RunResult(int ExitCode, string Output);

	private sealed class DistributionFixture : IAsyncDisposable
	{
		private readonly string _root = Path.Combine(RepositoryPaths.Root, "artifacts", "test-results", "release-script",
			Guid.NewGuid().ToString("N"));

		internal DistributionFixture()
		{
			Directory.CreateDirectory(Distribution);
			// Metadata fixtures only: the gateway copy is not an executable qualification.
			string assembly = typeof(CheatEngineMcpPlugin).Assembly.Location;
			File.Copy(assembly, Path.Combine(Distribution, "CheatEngine.Mcp.dll"));
			File.Copy(assembly, Path.Combine(Distribution, "CheatEngine.Mcp.Gateway.exe"));
			foreach (string name in new[] { "LICENSE", "THIRD-PARTY-NOTICES.md" })
			{
				File.WriteAllText(Path.Combine(Distribution, name), "fixture notices");
			}
			File.WriteAllText(Path.Combine(Distribution, "README.md"), "fixture README");
			string version = FileVersionInfo.GetVersionInfo(assembly).ProductVersion!.Split('+')[0];
			ZipName = $"CheatEngine.Mcp-{version}-win-x64.zip";
		}

		internal string Distribution => Path.Combine(_root, "distribution");
		internal string Output => Path.Combine(_root, "output");
		internal string Zip => Path.Combine(Output, ZipName);
		internal string ZipName
		{
			get;
		}

		public ValueTask DisposeAsync()
		{
			return new ValueTask(TestDirectory.DeleteAsync(_root));
		}

		internal Task<RunResult> RunWithFailedCheckoutAsync()
		{
			return RunAsync(true, ["-Upload"]);
		}

		internal Task<RunResult> RunAsync(params string[] extra)
		{
			return RunAsync(false, extra);
		}

		private async Task<RunResult> RunAsync(bool invalidCheckout, string[] extra)
		{
			ProcessStartInfo start = new("pwsh")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				WorkingDirectory = RepositoryPaths.Root
			};
			string script = Path.Combine(RepositoryPaths.Root, "eng", "Release.ps1");
			if (invalidCheckout)
			{
				string scripts = Directory.CreateDirectory(Path.Combine(_root, "eng")).FullName;
				string copy = Path.Combine(scripts, "Release.ps1");
				File.Copy(script, copy);
				script = copy;
				start.Environment["GIT_DIR"] = Path.Combine(_root, "missing-git-directory");
			}
			string[] arguments = ["-NoProfile", "-File", script];
			foreach (string argument in arguments)
			{
				start.ArgumentList.Add(argument);
			}
			if (!invalidCheckout)
			{
				start.ArgumentList.Add("-DistributionPath");
				start.ArgumentList.Add(Distribution);
			}
			if (!extra.Contains("-OutputDirectory", StringComparer.Ordinal))
			{
				start.ArgumentList.Add("-OutputDirectory");
				start.ArgumentList.Add(Output);
			}
			foreach (string argument in extra)
			{
				start.ArgumentList.Add(argument);
			}
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
			timeout.CancelAfter(TimeSpan.FromSeconds(60));
			using Process process = Process.Start(start)!;
			Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
			Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
			try
			{
				await process.WaitForExitAsync(timeout.Token);
				return new RunResult(process.ExitCode, await output + await error);
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
	}
}
