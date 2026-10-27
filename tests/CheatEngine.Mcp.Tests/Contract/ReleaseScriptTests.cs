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
	[InlineData("wrong-case readme", "Unexpected distribution entries")]
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
			case "wrong-case readme":
				string readme = Path.Combine(fixture.Distribution, "README.md");
				string temporaryReadme = Path.Combine(fixture.Distribution, "README.temporary");
				File.Move(readme, temporaryReadme);
				File.Move(temporaryReadme, Path.Combine(fixture.Distribution, "ReadMe.md"));
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
	public async Task Package_PrebuildUploadWithoutReviewedManifest_RefusesBeforeGitHubAccess()
	{
		await using DistributionFixture fixture = new();

		RunResult run = await fixture.RunPrebuildUploadWithoutManifestAsync();

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains("prebuilt distribution upload requires DistributionManifestPath", run.Output, StringComparison.Ordinal);
		Assert.DoesNotContain(DistributionFixture.GitHubReached, run.Output, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Package_ReviewedPrebuildUpload_ReachesGitHubOnlyAfterSourceAndDistributionVerification()
	{
		await using DistributionFixture fixture = new();
		await fixture.WriteReviewedManifestAsync();

		RunResult run = await fixture.RunReviewedPrebuildUploadAsync();

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains(DistributionFixture.GitHubReached, run.Output, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(ReviewedPrebuildMutation.ManifestDigest)]
	[InlineData(ReviewedPrebuildMutation.Dll)]
	[InlineData(ReviewedPrebuildMutation.Readme)]
	[InlineData(ReviewedPrebuildMutation.Dependency)]
	[InlineData(ReviewedPrebuildMutation.SourceCommit)]
	[InlineData(ReviewedPrebuildMutation.BuildEligibility)]
	public async Task Package_ReviewedPrebuildUpload_TamperedProvenanceRefusesBeforeGitHubAccess(
		ReviewedPrebuildMutation mutation)
	{
		await using DistributionFixture fixture = new();
		await fixture.WriteReviewedManifestAsync();
		string expected = await fixture.MutateReviewedManifestAsync(mutation);

		RunResult run = await fixture.RunReviewedPrebuildUploadAsync(mutation == ReviewedPrebuildMutation.ManifestDigest);

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains(expected, run.Output, StringComparison.Ordinal);
		Assert.DoesNotContain(DistributionFixture.GitHubReached, run.Output, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(false, "clean checkout")]
	[InlineData(true, "clean recorded submodules")]
	public async Task Package_ReviewedPrebuildUpload_DirtySourceRefusesBeforeGitHubAccess(bool submodule,
		string expected)
	{
		await using DistributionFixture fixture = new();
		await fixture.WriteReviewedManifestAsync();

		RunResult run = await fixture.RunReviewedPrebuildUploadAsync(dirtySource: submodule ? " M externals/CheatEngine.Client" : " M srcs/changed.cs");

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains(expected, run.Output, StringComparison.Ordinal);
		Assert.DoesNotContain(DistributionFixture.GitHubReached, run.Output, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Package_ReviewedPrebuildUpload_SourceDriftAfterTagLookup_RefusesBeforeReleaseMutation()
	{
		await using DistributionFixture fixture = new();
		await fixture.WriteReviewedManifestAsync();

		RunResult run = await fixture.RunReviewedPrebuildUploadAsync(driftAfterTagLookup: true);

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains("clean checkout", run.Output, StringComparison.Ordinal);
		Assert.DoesNotContain(DistributionFixture.GitHubMutationReached, run.Output, StringComparison.Ordinal);
	}

	[Fact]
	public async Task FrozenReleaseInputs_RejectWriteAndRenameWhileSnapshotsAreHeld()
	{
		await using FrozenAssetFixture fixture = new();

		RunResult run = await fixture.RunHelperAsync(corruptChecksum: false);

		Assert.Equal(0, run.ExitCode);
		Assert.Contains("FROZEN_RELEASE_INPUTS_CONFIRMED", run.Output, StringComparison.Ordinal);
		Assert.True(File.Exists(fixture.Zip));
		Assert.True(File.Exists(fixture.Checksums));
	}

	[Fact]
	public async Task FrozenReleaseInputs_ChecksumMismatchIsRejectedBeforeAnyExternalOperation()
	{
		await using FrozenAssetFixture fixture = new();

		RunResult run = await fixture.RunHelperAsync(corruptChecksum: true);

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains("SHA256SUMS.txt does not match the frozen ZIP input", run.Output, StringComparison.Ordinal);
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

	public enum ReviewedPrebuildMutation
	{
		ManifestDigest,
		Dll,
		Readme,
		Dependency,
		SourceCommit,
		BuildEligibility
	}

	private sealed class DistributionFixture : IAsyncDisposable
	{
		internal const string GitHubReached = "RELEASE_SCRIPT_GITHUB_REACHED";
		internal const string GitHubMutationReached = "RELEASE_SCRIPT_GITHUB_MUTATION_REACHED";
		private readonly string _root = Path.Combine(RepositoryPaths.Root, "artifacts", "test-results", "release-script",
			Guid.NewGuid().ToString("N"));
		private readonly string _sourceCommit;

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
			string productVersion = FileVersionInfo.GetVersionInfo(assembly).ProductVersion!;
			_sourceCommit = productVersion.Split('+')[1];
			string version = productVersion.Split('+')[0];
			ZipName = $"CheatEngine.Mcp-{version}-win-x64.zip";
		}

		internal string Distribution => Path.Combine(_root, "distribution");
		internal string Output => Path.Combine(_root, "output");
		internal string Zip => Path.Combine(Output, ZipName);
		internal string ZipName
		{
			get;
		}

		private string Manifest => Distribution + ".release-manifest.json";

		private string SourceCommit => _sourceCommit;

		public ValueTask DisposeAsync()
		{
			return new ValueTask(TestDirectory.DeleteAsync(_root));
		}

		internal Task<RunResult> RunWithFailedCheckoutAsync()
		{
			return RunAsync(true, ["-Upload"], false, null);
		}

		internal async Task WriteReviewedManifestAsync()
		{
			string readme = Path.Combine(RepositoryPaths.Root, "srcs", "CheatEngine.Mcp.Plugin", "Distribution", "README.md");
			await File.WriteAllTextAsync(Path.Combine(Distribution, "README.md"), await File.ReadAllTextAsync(readme),
				TestContext.Current.CancellationToken);
			foreach (string name in new[] { "LICENSE", "THIRD-PARTY-NOTICES.md" })
			{
				await File.WriteAllTextAsync(Path.Combine(Distribution, name),
					await File.ReadAllTextAsync(Path.Combine(RepositoryPaths.Root, name), TestContext.Current.CancellationToken),
					TestContext.Current.CancellationToken);
			}
			object dependencySnapshot = new
			{
				cheatEngineClient = new string('A', 40),
				cheatEngineSdk = new string('B', 40)
			};
			object buildSnapshot = new
			{
				clean = true,
				sourceCommit = SourceCommit,
				dependencies = dependencySnapshot
			};
			object manifest = new
			{
				schema = 1,
				sourceCommit = SourceCommit,
				dependencies = dependencySnapshot,
				releaseEligibility = new
				{
					eligible = true,
					beforeBuild = buildSnapshot,
					afterBuild = buildSnapshot
				},
				sourceFiles = new[]
				{
					new { distributionPath = "README.md", sourcePath = "srcs/CheatEngine.Mcp.Plugin/Distribution/README.md", sha256 = FileHash(readme) },
					new { distributionPath = "LICENSE", sourcePath = "LICENSE", sha256 = FileHash(Path.Combine(RepositoryPaths.Root, "LICENSE")) },
					new { distributionPath = "THIRD-PARTY-NOTICES.md", sourcePath = "THIRD-PARTY-NOTICES.md", sha256 = FileHash(Path.Combine(RepositoryPaths.Root, "THIRD-PARTY-NOTICES.md")) }
				},
				files = Directory.GetFiles(Distribution).Select(path => new { path = Path.GetFileName(path), sha256 = FileHash(path) })
					.OrderBy(entry => entry.path, StringComparer.Ordinal).ToArray()
			};
			await File.WriteAllTextAsync(Manifest, System.Text.Json.JsonSerializer.Serialize(manifest),
				TestContext.Current.CancellationToken);
		}

		internal async Task<string> MutateReviewedManifestAsync(ReviewedPrebuildMutation mutation)
		{
			if (mutation == ReviewedPrebuildMutation.ManifestDigest)
			{
				return "digest does not match";
			}

			System.Text.Json.Nodes.JsonObject manifest = System.Text.Json.Nodes.JsonNode.Parse(
				await File.ReadAllTextAsync(Manifest, TestContext.Current.CancellationToken))!.AsObject();
			string expected;
			switch (mutation)
			{
				case ReviewedPrebuildMutation.Dll:
					await File.AppendAllTextAsync(Path.Combine(Distribution, "CheatEngine.Mcp.dll"), "\0", TestContext.Current.CancellationToken);
					return "does not match CheatEngine.Mcp.dll";
				case ReviewedPrebuildMutation.Readme:
					await File.WriteAllTextAsync(Path.Combine(Distribution, "README.md"), "tampered", TestContext.Current.CancellationToken);
					return "does not bind README.md";
				case ReviewedPrebuildMutation.Dependency:
					manifest["dependencies"]!["cheatEngineClient"] = new string('D', 40);
					expected = "does not match the checked-out Client and SDK commits";
					break;
				case ReviewedPrebuildMutation.SourceCommit:
					manifest["sourceCommit"] = new string('E', 40);
					expected = "does not match the binaries source commit";
					break;
				case ReviewedPrebuildMutation.BuildEligibility:
					manifest["releaseEligibility"]!["eligible"] = false;
					expected = "was not built from one clean, stable source";
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(mutation));
			}
			await File.WriteAllTextAsync(Manifest, manifest.ToJsonString(), TestContext.Current.CancellationToken);
			return expected;
		}

		internal Task<RunResult> RunReviewedPrebuildUploadAsync(bool wrongDigest = false, string? dirtySource = null,
			bool driftAfterTagLookup = false)
		{
			string manifestHash = wrongDigest ? new string('F', 64) : FileHash(Manifest);
			return RunAsync(false, ["-Upload", "-DistributionManifestPath", Manifest,
				"-DistributionManifestSha256", manifestHash], true, dirtySource, driftAfterTagLookup);
		}

		internal Task<RunResult> RunPrebuildUploadWithoutManifestAsync()
		{
			return RunAsync(false, ["-Upload"], true, null, false);
		}

		internal Task<RunResult> RunAsync(params string[] extra)
		{
			return RunAsync(false, extra, false, null, false);
		}

		private async Task<RunResult> RunAsync(bool invalidCheckout, string[] extra, bool reviewedPrebuild, string? dirtySource,
			bool driftAfterTagLookup = false)
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
			if (reviewedPrebuild)
			{
				string wrapper = Path.Combine(_root, "reviewed-prebuild.ps1");
				string dirtyStatus = dirtySource?.Replace("'", "''", StringComparison.Ordinal) ?? string.Empty;
				string statusCommand = dirtySource is null ? "return" : $"'{dirtyStatus}'; return";
				string tagResponse = System.Text.Json.JsonSerializer.Serialize(new
				{
					@object = new
					{
						type = "commit",
						sha = SourceCommit
					}
				});
				string githubBody = driftAfterTagLookup
					? $"$global:releaseTestGhCalls += 1\nif ($global:releaseTestGhCalls -eq 1) {{ '{tagResponse}'; return }}\nthrow '{GitHubMutationReached}'"
					: $"throw '{GitHubReached}'";
				string text = $$"""
$global:releaseTestStatusCalls = 0
$global:releaseTestGhCalls = 0
function git {
	param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)
	$global:LASTEXITCODE = 0
	if ($Arguments[0] -eq 'status') {
		$global:releaseTestStatusCalls += 1
		if (${{driftAfterTagLookup.ToString().ToLowerInvariant()}} -and $global:releaseTestStatusCalls -gt 2) { ' M srcs/changed.cs'; return }
		{{statusCommand}}
	}
	if ($Arguments[0] -eq '-C' -and $Arguments[2] -eq 'rev-parse') {
		if ($Arguments[1] -like '*CheatEngine.Client') { '{{new string('A', 40)}}'; return }
		if ($Arguments[1] -like '*CheatEngine.SDK') { '{{new string('B', 40)}}'; return }
		'{{SourceCommit}}'; return
	}
	throw "Unexpected git invocation: $($Arguments -join ' ')"
}
function gh { {{githubBody}} }
& '{{script.Replace("'", "''", StringComparison.Ordinal)}}' @args
""";
				await File.WriteAllTextAsync(wrapper, text, TestContext.Current.CancellationToken);
				script = wrapper;
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

	private sealed class FrozenAssetFixture : IAsyncDisposable
	{
		private readonly string _root = Path.Combine(RepositoryPaths.Root, "artifacts", "test-results", "release-frozen-inputs",
			Guid.NewGuid().ToString("N"));

		internal FrozenAssetFixture()
		{
			Directory.CreateDirectory(_root);
			Zip = Path.Combine(_root, "CheatEngine.Mcp-2.0.0-win-x64.zip");
			Checksums = Path.Combine(_root, "SHA256SUMS.txt");
			File.WriteAllBytes(Zip, [0x50, 0x4B, 0x03, 0x04, 0x00]);
			File.WriteAllText(Checksums, $"{FileHash(Zip).ToLowerInvariant()}  {Path.GetFileName(Zip)}\n", new UTF8Encoding(false));
		}

		internal string Zip
		{
			get;
		}
		internal string Checksums
		{
			get;
		}

		public ValueTask DisposeAsync()
		{
			return new ValueTask(TestDirectory.DeleteAsync(_root));
		}

		internal async Task<RunResult> RunHelperAsync(bool corruptChecksum)
		{
			if (corruptChecksum)
			{
				string checksum = await File.ReadAllTextAsync(Checksums, TestContext.Current.CancellationToken);
				char replacement = checksum[0] == '0' ? '1' : '0';
				await File.WriteAllTextAsync(Checksums, replacement + checksum[1..], TestContext.Current.CancellationToken);
			}
			string releaseScript = await File.ReadAllTextAsync(Path.Combine(RepositoryPaths.Root, "eng", "Release.ps1"),
				TestContext.Current.CancellationToken);
			int start = releaseScript.IndexOf("function New-ReleaseFileSnapshot", StringComparison.Ordinal);
			int end = start < 0 ? -1 : releaseScript.IndexOf("function Assert-CosturaResourceInventory", start,
				StringComparison.Ordinal);
			Assert.True(start >= 0 && end > start, "Could not extract the frozen-release-input helpers.");
			string helperFunctions = releaseScript[start..end];
			string helper = Path.Combine(_root, "frozen-release-inputs.ps1");
			string script = $$"""
$ErrorActionPreference = 'Stop'
{{helperFunctions}}
$zipSnapshot = New-ReleaseFileSnapshot '{{Zip.Replace("'", "''", StringComparison.Ordinal)}}'
$checksumsSnapshot = New-ReleaseFileSnapshot '{{Checksums.Replace("'", "''", StringComparison.Ordinal)}}'
try {
	$writeRejected = $false
	try { [IO.File]::WriteAllText($zipSnapshot.Path, 'tampered') } catch [IO.IOException] { $writeRejected = $true }
	$renameRejected = $false
	try { [IO.File]::Move($checksumsSnapshot.Path, "$($checksumsSnapshot.Path).renamed") } catch [IO.IOException] { $renameRejected = $true }
	if (-not $writeRejected -or -not $renameRejected) { throw 'Frozen release inputs accepted a write or rename.' }
	Assert-ChecksumSnapshot $zipSnapshot $checksumsSnapshot
	'FROZEN_RELEASE_INPUTS_CONFIRMED'
}
finally {
	$zipSnapshot.Stream.Dispose()
	$checksumsSnapshot.Stream.Dispose()
}
""";
			await File.WriteAllTextAsync(helper, script, TestContext.Current.CancellationToken);
			ProcessStartInfo startInfo = new("pwsh")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				WorkingDirectory = _root
			};
			startInfo.ArgumentList.Add("-NoProfile");
			startInfo.ArgumentList.Add("-File");
			startInfo.ArgumentList.Add(helper);
			using Process process = Process.Start(startInfo)!;
			string output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
			string error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
			await process.WaitForExitAsync(TestContext.Current.CancellationToken);
			return new RunResult(process.ExitCode, output + error);
		}
	}
}
