using System.IO.Compression;
using System.Reflection;
using System.Text;

using CheatEngine.Mcp.Bootstrap;

namespace CheatEngine.Mcp.Tests;

public sealed class BundleCacheTests
{
	[Fact]
	public void Extract_RepeatedPayload_ReusesVerifiedVersionWithoutRewriting()
	{
		using CacheFixture fixture = new();
		using MemoryStream payload = Payload(("sub/file.txt", "first"));
		string directory = BundleCache.Extract(payload, fixture.Root);
		string file = Path.Combine(directory, "sub", "file.txt");
		DateTime originalTime = File.GetLastWriteTimeUtc(file);
		payload.Position = 0;
		Assert.Equal(directory, BundleCache.Extract(payload, fixture.Root));
		Assert.Equal("first", File.ReadAllText(file));
		Assert.Equal(originalTime, File.GetLastWriteTimeUtc(file));
		using MemoryStream nextPayload = Payload(("sub/file.txt", "second"));
		string nextDirectory = BundleCache.Extract(nextPayload, fixture.Root);
		Assert.NotEqual(directory, nextDirectory);
		Assert.Equal("first", File.ReadAllText(file));
		Assert.Equal("second", File.ReadAllText(Path.Combine(nextDirectory, "sub", "file.txt")));
	}

	[Fact]
	public async Task Extract_ConcurrentFirstLoads_PublishesOneCompleteVersion()
	{
		using CacheFixture fixture = new();
		using MemoryStream payload = Payload(("nested/content.txt", new string('x', 100_000)), ("root.txt", "complete"));
		byte[] bytes = payload.ToArray();
		Task<string>[] loads = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
		{
			using MemoryStream copy = new(bytes);
			return BundleCache.Extract(copy, fixture.Root);
		})).ToArray();
		string[] directories = await Task.WhenAll(loads);
		Assert.Single(directories.Distinct());
		Assert.Single(Directory.GetDirectories(fixture.Root));
		Assert.Equal(new string('x', 100_000), File.ReadAllText(Path.Combine(directories[0], "nested", "content.txt")));
		Assert.Equal("complete", File.ReadAllText(Path.Combine(directories[0], "root.txt")));
	}

	[Fact]
	public void Extract_TamperedVersion_RefusesItWithoutReplacingLoadedFiles()
	{
		using CacheFixture fixture = new();
		using MemoryStream payload = Payload(("file.txt", "original"));
		string directory = BundleCache.Extract(payload, fixture.Root);
		string file = Path.Combine(directory, "file.txt");
		File.WriteAllText(file, "tampered");
		payload.Position = 0;
		InvalidDataException exception = Assert.Throws<InvalidDataException>(() => BundleCache.Extract(payload, fixture.Root));
		Assert.Contains("integrity check failed", exception.Message, StringComparison.Ordinal);
		Assert.Equal("tampered", File.ReadAllText(file));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Extract_UnexpectedCacheFileOrDirectory_IsRejected(bool directoryAdded)
	{
		using CacheFixture fixture = new();
		using MemoryStream payload = Payload(("file.txt", "original"));
		string directory = BundleCache.Extract(payload, fixture.Root);
		string unexpected = Path.Combine(directory, "unexpected.dll");
		if (directoryAdded)
		{
			Directory.CreateDirectory(unexpected);
		}
		else
		{
			File.WriteAllText(unexpected, "unexpected");
		}
		payload.Position = 0;
		InvalidDataException exception = Assert.Throws<InvalidDataException>(() => BundleCache.Extract(payload, fixture.Root));
		Assert.Contains("Unexpected path", exception.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("../outside.txt")]
	[InlineData("sub/../../outside.txt")]
	[InlineData("/absolute.txt")]
	[InlineData("C:/absolute.txt")]
	[InlineData("sub\\outside.txt")]
	[InlineData("file.txt:stream")]
	[InlineData("sub./file.txt")]
	public void Extract_UnsafeArchivePath_RejectsBeforeWriting(string name)
	{
		using CacheFixture fixture = new();
		using MemoryStream payload = Payload((name, "unsafe"));
		Assert.Throws<InvalidDataException>(() => BundleCache.Extract(payload, fixture.Root));
		Assert.False(Directory.Exists(fixture.Root));
	}

	[Fact]
	public void Extract_CaseInsensitiveDuplicate_RejectsBeforeWriting()
	{
		using CacheFixture fixture = new();
		using MemoryStream payload = Payload(("file.txt", "one"), ("FILE.txt", "two"));
		Assert.Throws<InvalidDataException>(() => BundleCache.Extract(payload, fixture.Root));
		Assert.False(Directory.Exists(fixture.Root));
	}

	[Fact]
	public void Extract_RelativeCacheOverride_IsRejected()
	{
		using MemoryStream payload = Payload(("file.txt", "one"));
		Assert.Throws<ArgumentException>(() => BundleCache.Extract(payload, "relative"));
	}

	[Fact]
	public void EmbeddedPayload_ContainsRuntimeNativeBridgeAndSkillWithoutLocalState()
	{
		Assembly bootstrap = typeof(BundleCache).Assembly;
		Assert.DoesNotContain(bootstrap.GetReferencedAssemblies(), static assembly =>
			assembly.Name!.StartsWith("CheatEngine.", StringComparison.Ordinal));
		using Stream resource = bootstrap.GetManifestResourceStream("CheatEngine.Mcp.Payload")!;
		Assert.NotNull(resource);
		using ZipArchive archive = new(resource);
		string[] files = archive.Entries.Select(static entry => entry.FullName).ToArray();
		Assert.Contains("CheatEngine.Mcp.Runtime.dll", files);
		Assert.Contains("CheatEngine.Mcp.Runtime.deps.json", files);
		Assert.Contains("cheatengine-sdk-lua-bridge.dll", files);
		Assert.Contains("CheatEngine.Client.Core.dll", files);
		Assert.Contains("skills/cheatengine-mcp/SKILL.md", files);
		Assert.Contains("licenses/CheatEngine.Client.LICENSE", files);
		Assert.DoesNotContain("CheatEngine.Mcp.dll", files);
		Assert.DoesNotContain(files, static file => file.Contains("local-cheat-engine.md", StringComparison.Ordinal));
	}

	private static MemoryStream Payload(params (string Name, string Content)[] files)
	{
		MemoryStream stream = new();
		using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			foreach ((string name, string content) in files)
			{
				using Stream entry = archive.CreateEntry(name).Open();
				entry.Write(Encoding.UTF8.GetBytes(content));
			}
		}
		stream.Position = 0;
		return stream;
	}

	private sealed class CacheFixture : IDisposable
	{
		internal string Root { get; } = Path.Combine(Path.GetTempPath(), "CheatEngine.Mcp.BundleTests", Guid.NewGuid().ToString("N"));
		public void Dispose()
		{
			if (Directory.Exists(Root))
			{
				Directory.Delete(Root, recursive: true);
			}
		}
	}
}
