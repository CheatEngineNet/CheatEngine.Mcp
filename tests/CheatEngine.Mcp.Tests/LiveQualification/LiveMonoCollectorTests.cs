using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveMonoCollectorTests
{
	[Fact]
	public void StageMonoCollector_CopiesOnlyReviewedCollectorAndPreservesSource()
	{
		WithFixture((source, destination, collector, hash) =>
		{
			File.WriteAllText(Path.Combine(source, "dlls", "unrelated.dll"), "unrelated");
			File.WriteAllText(Path.Combine(source, "unrelated.lua"), "unrelated");

			Assert.Equal(hash, LiveSandboxSession.StageMonoCollector(source, destination, hash));

			string staged = Assert.Single(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
			Assert.Equal(Path.Combine(destination, "dlls", "MonoDataCollector64.dll"), staged);
			Assert.Equal(File.ReadAllBytes(collector), File.ReadAllBytes(staged));
			Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(collector))));
			Assert.True(File.Exists(Path.Combine(source, "dlls", "unrelated.dll")));
			Assert.True(File.Exists(Path.Combine(source, "unrelated.lua")));
		});
	}

	[Fact]
	public void StageMonoCollector_ChangedBytesRefuseBeforeCreatingDestination()
	{
		WithFixture((source, destination, collector, hash) =>
		{
			File.WriteAllText(collector, "changed collector");

			Assert.Throws<InvalidOperationException>(() => LiveSandboxSession.StageMonoCollector(source, destination, hash));

			Assert.False(Directory.Exists(destination));
			Assert.Equal("changed collector", File.ReadAllText(collector));
		});
	}

	[Fact]
	public void StageMonoCollector_ExistingDestinationIsNotOverwritten()
	{
		WithFixture((source, destination, _, hash) =>
		{
			string staged = Path.Combine(destination, "dlls", "MonoDataCollector64.dll");
			Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
			File.WriteAllText(staged, "existing collector");

			Assert.Throws<IOException>(() => LiveSandboxSession.StageMonoCollector(source, destination, hash));

			Assert.Equal("existing collector", File.ReadAllText(staged));
		});
	}

	[Fact]
	public void StageMonoCollector_MissingCollectorRefusesBeforeCreatingDestination()
	{
		WithFixture((source, destination, collector, hash) =>
		{
			File.Delete(collector);

			Assert.Throws<FileNotFoundException>(() => LiveSandboxSession.StageMonoCollector(source, destination, hash));

			Assert.False(Directory.Exists(destination));
		});
	}

	private static void WithFixture(Action<string, string, string, string> action)
	{
		string root = Path.Combine(Path.GetTempPath(), "mcp-mono-collector-" + Guid.NewGuid().ToString("N"));
		string source = Path.Combine(root, "autorun.disabled");
		string destination = Path.Combine(root, "autorun");
		string collector = Path.Combine(source, "dlls", "MonoDataCollector64.dll");
		Directory.CreateDirectory(Path.GetDirectoryName(collector)!);
		try
		{
			File.WriteAllText(collector, "reviewed collector");
			string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(collector)));
			action(source, destination, collector, hash);
		}
		finally { Directory.Delete(root, true); }
	}
}
