using System.Text.Json;

using CheatEngine.Mcp.Tools;

namespace CheatEngine.Mcp.Tests;

public sealed class PointerScanFileTests : IDisposable
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.PointerScanFileTests-{Guid.NewGuid():N}");

	[Fact]
	public void SaveLoad_ValidSnapshot_RoundTripsAndMarksPathsUnresolved()
	{
		string path = Path.Combine(_directory, "scan.json");
		PointerScanSnapshot original = new(8, [new(0x1234, "game.exe", 0x234, [0L, 16L]) { Verification = "liveMatch" }], true);
		Directory.CreateDirectory(_directory);

		PointerScanFile.Save(path, original, false, CancellationToken.None);
		PointerScanSnapshot loaded = PointerScanFile.Load(path, CancellationToken.None);

		Assert.Equal(8, loaded.Width);
		Assert.True(loaded.Incomplete);
		PointerPath result = Assert.Single(loaded.Paths);
		Assert.Equal(0x1234UL, result.BaseAddress);
		Assert.Equal("game.exe", result.Module);
		Assert.Equal(new long[] { 0, 16 }, result.Offsets);
		Assert.Equal("unresolved", result.Verification);
	}

	[Fact]
	public void Load_UnknownVersion_Rejects()
	{
		string path = Path.Combine(_directory, "unknown-version.json");
		Directory.CreateDirectory(_directory);
		File.WriteAllText(path, "{\"version\":2,\"width\":8,\"paths\":[],\"incomplete\":false}");

		Assert.Throws<InvalidDataException>(() => PointerScanFile.Load(path, CancellationToken.None));
	}

	[Fact]
	public void Load_MissingRequiredField_Rejects()
	{
		string path = Path.Combine(_directory, "missing.json");
		Directory.CreateDirectory(_directory);
		File.WriteAllText(path, "{\"version\":1,\"width\":8,\"paths\":[]}");

		Assert.Throws<InvalidDataException>(() => PointerScanFile.Load(path, CancellationToken.None));
	}

	[Fact]
	public void Load_MalformedFixture_Rejects()
	{
		string path = Path.Combine(_directory, "malformed.json");
		Directory.CreateDirectory(_directory);
		File.WriteAllText(path, "{broken");

		Assert.ThrowsAny<JsonException>(() => PointerScanFile.Load(path, CancellationToken.None));
	}

	[Theory]
	[InlineData("{\"version\":1,\"width\":3,\"paths\":[],\"incomplete\":false}")]
	[InlineData("{\"version\":1,\"width\":4,\"paths\":[{\"baseAddress\":4294967296,\"module\":null,\"moduleOffset\":0,\"offsets\":[0]}],\"incomplete\":false}")]
	public void Load_MalformedOrOutOfBoundsFixture_Rejects(string json)
	{
		string path = Path.Combine(_directory, "invalid.json");
		Directory.CreateDirectory(_directory);
		File.WriteAllText(path, json);

		Assert.Throws<ArgumentException>(() => PointerScanFile.Load(path, CancellationToken.None));
	}

	[Theory]
	[InlineData(10_001, 1)]
	[InlineData(1, 9)]
	[InlineData(1, 0)]
	public void Load_ArraysExceedBounds_RejectsBeforeMaterializingPaths(int pathCount, int offsetCount)
	{
		string path = Path.Combine(_directory, "arrays.json");
		Directory.CreateDirectory(_directory);
		string offsets = string.Join(',', Enumerable.Repeat("0", offsetCount));
		string entry = "{\"baseAddress\":4096,\"module\":null,\"moduleOffset\":0,\"offsets\":[" + offsets + "]}";
		File.WriteAllText(path, "{\"version\":1,\"width\":8,\"incomplete\":false,\"paths\":[" + string.Join(',', Enumerable.Repeat(entry, pathCount)) + "]}");
		Assert.Throws<InvalidDataException>(() => PointerScanFile.Load(path, CancellationToken.None));
	}

	[Theory]
	[InlineData(3, 0)]
	[InlineData(8, -1)]
	[InlineData(8, 1_048_577)]
	public void Save_InvalidWidthOrOffset_RejectsBeforeWriting(int width, long offset)
	{
		Directory.CreateDirectory(_directory);
		PointerScanSnapshot invalid = new(width, [new(1, null, 0, [offset])], false);

		Assert.Throws<ArgumentException>(() => PointerScanFile.Save(Path.Combine(_directory, "scan.json"), invalid, false, CancellationToken.None));
		Assert.False(File.Exists(Path.Combine(_directory, "scan.json")));
	}

	[Fact]
	public void Save_ModuleNameExceedsUtf8Limit_RejectsBeforeWriting()
	{
		Directory.CreateDirectory(_directory);
		PointerScanSnapshot invalid = new(8, [new(1, new string('x', 4097), 0, [0L])], false);

		Assert.Throws<ArgumentException>(() => PointerScanFile.Save(Path.Combine(_directory, "scan.json"), invalid, false, CancellationToken.None));
	}

	[Fact]
	public void Save_TooManyPaths_RejectsBeforeWriting()
	{
		Directory.CreateDirectory(_directory);
		PointerPath path = new(1, null, 0, [0L]);
		PointerScanSnapshot invalid = new(8, Enumerable.Repeat(path, 10_001).ToArray(), false);

		Assert.Throws<ArgumentException>(() => PointerScanFile.Save(Path.Combine(_directory, "scan.json"), invalid, false, CancellationToken.None));
	}

	[Fact]
	public void Save_JsonWouldExceedFileLimit_RejectsAndCleansTemporary()
	{
		string path = Path.Combine(_directory, "scan.json");
		Directory.CreateDirectory(_directory);
		PointerPath pathWithMaximumModuleName = new(1, new string('x', 4096), 0, [0L]);
		PointerScanSnapshot oversized = new(8, Enumerable.Repeat(pathWithMaximumModuleName, 10_000).ToArray(), false);

		Assert.Throws<InvalidDataException>(() => PointerScanFile.Save(path, oversized, false, CancellationToken.None));

		Assert.False(File.Exists(path));
		Assert.Empty(Directory.EnumerateFiles(_directory, ".scan.json.*.tmp"));
	}

	[Fact]
	public void Save_InvalidPathOrSnapshot_RejectsBeforeWriting()
	{
		Directory.CreateDirectory(_directory);
		PointerScanSnapshot invalid = new(8, [new(1, null, 1, [0L])], false);

		Assert.Throws<ArgumentException>(() => PointerScanFile.Save(Path.Combine(_directory, "scan.json"), invalid, false, CancellationToken.None));
		Assert.Throws<ArgumentException>(() => PointerScanFile.Save(Path.Combine(_directory, "scan.ptr"), ValidScan(), false, CancellationToken.None));
		Assert.False(File.Exists(Path.Combine(_directory, "scan.json")));
	}

	[Fact]
	public void Save_ExistingFileWithoutOverwrite_PreservesExistingFileAndCleansTemporary()
	{
		string path = Path.Combine(_directory, "scan.json");
		Directory.CreateDirectory(_directory);
		File.WriteAllText(path, "existing");

		Assert.Throws<IOException>(() => PointerScanFile.Save(path, ValidScan(), false, CancellationToken.None));

		Assert.Equal("existing", File.ReadAllText(path));
		Assert.Empty(Directory.EnumerateFiles(_directory, ".scan.json.*.tmp"));
	}

	[Fact]
	public void Save_OverwriteExistingFile_ReplacesItWithLoadableSnapshot()
	{
		string path = Path.Combine(_directory, "scan.json");
		Directory.CreateDirectory(_directory);
		File.WriteAllText(path, "existing");

		PointerScanFile.Save(path, ValidScan(), true, CancellationToken.None);

		Assert.Single(PointerScanFile.Load(path, CancellationToken.None).Paths);
	}

	[Fact]
	public void Write_CancellationBeforeMove_PreservesExistingFileAndCleansTemporary()
	{
		string path = Path.Combine(_directory, "scan.json");
		Directory.CreateDirectory(_directory);
		File.WriteAllText(path, "existing");
		using CancellationTokenSource stopping = new();

		Assert.Throws<OperationCanceledException>(() => PointerDataFile.Write(path, ".json", true, stream =>
		{
			stream.WriteByte(1);
			stopping.Cancel();
		}, stopping.Token));

		Assert.Equal("existing", File.ReadAllText(path));
		Assert.Empty(Directory.EnumerateFiles(_directory, ".scan.json.*.tmp"));
	}

	[Fact]
	public void Write_CallbackFailure_PreservesExistingFileAndCleansTemporary()
	{
		string path = Path.Combine(_directory, "scan.json");
		Directory.CreateDirectory(_directory);
		File.WriteAllText(path, "existing");

		Assert.Throws<InvalidOperationException>(() => PointerDataFile.Write(path, ".json", true, stream =>
		{
			stream.WriteByte(1);
			throw new InvalidOperationException("write failed");
		}, CancellationToken.None));

		Assert.Equal("existing", File.ReadAllText(path));
		Assert.Empty(Directory.EnumerateFiles(_directory, ".scan.json.*.tmp"));
	}

	[Fact]
	public void Load_TooLargeFile_RejectsWithoutParsing()
	{
		string path = Path.Combine(_directory, "large.json");
		Directory.CreateDirectory(_directory);
		using (FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
		{
			file.SetLength((16 * 1024 * 1024) + 1);
		}

		Assert.Throws<InvalidDataException>(() => PointerScanFile.Load(path, CancellationToken.None));
	}

	[Fact]
	public void ValidatePath_AlternativeDataStreamAndUncPaths_Rejects()
	{
		Assert.Throws<ArgumentException>(() => PointerDataFile.ValidatePath(Path.Combine(_directory, "scan.json:stream"), ".json"));
		Assert.Throws<ArgumentException>(() => PointerDataFile.ValidatePath("\\\\server\\share\\scan.json", ".json"));
	}

	[Fact]
	public void Load_IndependentHighAddressFixture_PreservesUnsignedAddresses()
	{
		string path = Path.Combine(_directory, "high-address.json");
		Directory.CreateDirectory(_directory);
		File.WriteAllText(path, "{\"version\":1,\"width\":8,\"paths\":[{\"baseAddress\":18446744073709551615,\"module\":\"game.exe\",\"moduleOffset\":9223372036854775807,\"offsets\":[1048576]}],\"incomplete\":false}");

		PointerPath result = Assert.Single(PointerScanFile.Load(path, CancellationToken.None).Paths);

		Assert.Equal(ulong.MaxValue, result.BaseAddress);
		Assert.Equal((ulong) long.MaxValue, result.ModuleOffset);
		Assert.Equal("unresolved", result.Verification);
	}

	private static PointerScanSnapshot ValidScan() => new(8, [new(0x1000, null, 0, [0L])], false);

	public void Dispose()
	{
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, recursive: true);
		}
	}
}
