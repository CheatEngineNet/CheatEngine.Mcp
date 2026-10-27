using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveCompilerCandidateTests
{
	private const string Commit = "1f987742b7fcd7760057a19508ddacbf2aad4bd8";

	[Theory]
	[InlineData(nameof(LiveQualificationScenario.Compiler), "compiler-probe-policy.md")]
	[InlineData(nameof(LiveQualificationScenario.CompilerExtended), "compiler-extended-policy.md")]
	[InlineData(nameof(LiveQualificationScenario.CompilerInjection), "compiler-extended-policy.md")]
	public void PolicyPath_CompilerScenario_UsesMaintainedAllToolsPolicyDirectory(string scenario,
		string expectedFile)
	{
		string path = LiveCompilerCandidate.PolicyPath(RepositoryPaths.Root, Enum.Parse<LiveQualificationScenario>(scenario));

		Assert.Equal(Path.Combine(RepositoryPaths.Root, "docs", "qualification", "all-tools", expectedFile), path);
		Assert.True(File.Exists(path));
	}

	[Fact]
	public void VerifyFrozenInputManifest_AcceptsPinnedSourceSubmoduleAndBinaryInputs()
	{
		using FrozenManifestFixture fixture = FrozenManifestFixture.Create();

		int count = LiveCompilerCandidate.VerifyFrozenInputManifest(fixture.Repository, fixture.Manifest,
			fixture.ManifestSha256, Commit, fixture.Distribution);

		Assert.Equal(3, count);
	}

	[Fact]
	public void VerifyFrozenInputManifest_RejectsChangedInput()
	{
		using FrozenManifestFixture fixture = FrozenManifestFixture.Create();
		File.WriteAllText(Path.Combine(fixture.Repository, "artifacts", "candidate.dll"), "changed candidate");

		Assert.Throws<InvalidDataException>(() => LiveCompilerCandidate.VerifyFrozenInputManifest(fixture.Repository,
			fixture.Manifest, fixture.ManifestSha256, Commit, fixture.Distribution));
	}

	[Fact]
	public void VerifyFrozenInputManifest_RejectsChangedManifestDigest()
	{
		using FrozenManifestFixture fixture = FrozenManifestFixture.Create();
		string replacement = fixture.ManifestSha256[0] == 'A' ? "B" : "A";
		string wrongDigest = replacement + fixture.ManifestSha256[1..];

		Assert.Throws<InvalidDataException>(() => LiveCompilerCandidate.VerifyFrozenInputManifest(fixture.Repository,
			fixture.Manifest, wrongDigest, Commit, fixture.Distribution));
	}

	[Fact]
	public void VerifyFrozenInputManifest_RejectsInputPathOutsideRepository()
	{
		using FrozenManifestFixture fixture = FrozenManifestFixture.Create(binaryPath: "..\\outside.dll");

		Assert.Throws<InvalidDataException>(() => LiveCompilerCandidate.VerifyFrozenInputManifest(fixture.Repository,
			fixture.Manifest, fixture.ManifestSha256, Commit, fixture.Distribution));
	}

	private sealed class FrozenManifestFixture : IDisposable
	{
		private FrozenManifestFixture(string repository, string distribution, string manifest, string manifestSha256)
		{
			Repository = repository;
			Distribution = distribution;
			Manifest = manifest;
			ManifestSha256 = manifestSha256;
		}

		internal string Repository
		{
			get;
		}

		internal string Distribution
		{
			get;
		}

		internal string Manifest
		{
			get;
		}

		internal string ManifestSha256
		{
			get;
		}

		internal static FrozenManifestFixture Create(string binaryPath = "artifacts/candidate.dll")
		{
			string repository = Path.Combine(Path.GetTempPath(), "cemcp-frozen-inputs-" + Guid.NewGuid().ToString("N"));
			string source = Path.Combine(repository, "src", "candidate.cs");
			string submoduleSource = Path.Combine(repository, "externals", "candidate", "candidate.cs");
			string binary = Path.GetFullPath(Path.Combine(repository, binaryPath));
			string distribution = Path.Combine(repository, "artifacts", "dist");
			Directory.CreateDirectory(Path.GetDirectoryName(source)!);
			Directory.CreateDirectory(Path.GetDirectoryName(submoduleSource)!);
			Directory.CreateDirectory(Path.Combine(repository, "artifacts", "test-results"));
			Directory.CreateDirectory(distribution);
			File.WriteAllText(source, "source input");
			File.WriteAllText(submoduleSource, "submodule input");
			bool binaryInsideRepository = LiveQualificationOptIn.IsSameOrBelow(binary, repository);
			if (binaryInsideRepository)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
				File.WriteAllText(binary, "binary input");
			}
			string manifest = Path.Combine(repository, "artifacts", "test-results", "manifest.json");
			string json = JsonSerializer.Serialize(new
			{
				repository,
				baseCommit = Commit,
				distribution,
				source = new[] { new { path = "src/candidate.cs", sha256 = Hash(source) } },
				submodules = new[]
				{
					new { source = new[] { new { path = "externals/candidate/candidate.cs", sha256 = Hash(submoduleSource) } } }
				},
				binaries = new[] { new { path = binaryPath, sha256 = binaryInsideRepository ? Hash(binary) : new string('F', 64) } }
			});
			File.WriteAllText(manifest, json);
			return new FrozenManifestFixture(repository, distribution, manifest, Hash(manifest));
		}

		public void Dispose()
		{
			if (Directory.Exists(Repository))
			{
				Directory.Delete(Repository, recursive: true);
			}
		}
	}

	private static string Hash(string path)
	{
		using FileStream stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}
}
