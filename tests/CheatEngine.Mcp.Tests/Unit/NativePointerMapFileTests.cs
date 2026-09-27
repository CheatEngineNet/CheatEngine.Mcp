using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

using CheatEngine.Mcp.Tools;

namespace CheatEngine.Mcp.Tests;

public sealed class NativePointerMapFileTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("CheatEngine.Mcp.NativePointerMap-").FullName;
	private string MapPath => Path.Combine(_directory, "map.scandata");

	[Theory]
	[InlineData(4)]
	[InlineData(8)]
	public void Load_NativeFixture_PreservesWidthChainsAndPerAddressStaticClassification(int width)
	{
		WriteFixture(NativeFixture(width));
		PointerMap map = NativePointerMapFile.Load(MapPath, 100, TestContext.Current.CancellationToken);

		Assert.Equal(width, map.Width);
		Assert.Null(map.Process);
		Assert.True(map.Incomplete);
		Assert.Equal(new ulong[] { 0x1010, 0x1020, 0x2010 }, map.Entries.Select(entry => entry.Address));
		PointerPath path = Assert.Single(map.Search(0x3020, 2, 0x20, true, 100, 1000, TestContext.Current.CancellationToken).Paths);
		Assert.Equal("game.exe", path.Module);
		Assert.Equal(0x20UL, path.ModuleOffset);
		Assert.Equal(new long[] { 0x10, 0x20 }, path.Offsets);
		Assert.True(map.TryResolve(path, out ulong target));
		Assert.Equal(0x3020UL, target);
		Assert.Null(map.GetStaticRoot(0x1010)); // Inside the inferred module bounds, but not a native static root.
	}

	[Fact]
	public void Save_NativeFixture_EmitsSameNativeLayoutIncludingStaticRoots()
	{
		byte[] fixture = NativeFixture(8);
		WriteFixture(fixture);
		PointerMap map = NativePointerMapFile.Load(MapPath, 100, TestContext.Current.CancellationToken);
		NativePointerMapFile.Save(Path.Combine(_directory, "saved.scandata"), map, false, TestContext.Current.CancellationToken);

		using FileStream file = File.OpenRead(Path.Combine(_directory, "saved.scandata"));
		using ZLibStream decompressed = new(file, CompressionMode.Decompress);
		using MemoryStream bytes = new();
		decompressed.CopyTo(bytes);
		// Export sorts each value group by storage address. Read independently of the importer.
		using BinaryReader reader = new(new MemoryStream(bytes.ToArray()), Encoding.UTF8);
		Assert.Equal(0xCE, reader.ReadByte());
		Assert.Equal(1, reader.ReadByte());
		Assert.Equal(1, reader.ReadInt32());
		Assert.Equal("game.exe", Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32())));
		Assert.Equal(0x1000UL, reader.ReadUInt64());
		Assert.Equal(0, reader.ReadByte());
		Assert.Equal(15, reader.ReadInt32());
		Assert.Equal(3UL, reader.ReadUInt64()); // Address count, not group count.
		Assert.Equal(0x2000UL, reader.ReadUInt64());
		Assert.Equal(2, reader.ReadInt32());
		Assert.Equal(0x1010UL, reader.ReadUInt64());
		Assert.Equal(0, reader.ReadByte());
		Assert.Equal(0x1020UL, reader.ReadUInt64());
		Assert.Equal(1, reader.ReadByte());
		Assert.Equal(0, reader.ReadInt32());
		Assert.Equal(0x20U, reader.ReadUInt32());
		Assert.Equal(0x3000UL, reader.ReadUInt64());
		Assert.Equal(1, reader.ReadInt32());
		Assert.Equal(0x2010UL, reader.ReadUInt64());
		Assert.Equal(0, reader.ReadByte());
		Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
	}

	[Theory]
	[InlineData(0, 0)] // Magic.
	[InlineData(1, 2)] // Version.
	[InlineData(26, 2)] // Static flag.
	[InlineData(27, 9)] // Width discriminator.
	[InlineData(47, 0)] // Zero-length group.
	[InlineData(47, 4)] // Group larger than remaining entry count.
	[InlineData(59, 2)] // Entry static flag.
	[InlineData(60, 1)] // Nonexistent module index.
	[InlineData(64, 0x21)] // Module offset disagrees with address.
	public void Load_InvalidNativeField_Rejects(int index, byte value)
	{
		byte[] fixture = NativeFixture(8);
		fixture[index] = value;
		WriteFixture(fixture);
		Assert.Throws<InvalidDataException>(() => NativePointerMapFile.Load(MapPath, 100, TestContext.Current.CancellationToken));
	}

	[Fact]
	public void Load_CountLimitAndDuplicateAddress_RejectBeforeReturningAMap()
	{
		byte[] fixture = NativeFixture(8);
		WriteFixture(fixture);
		Assert.Throws<InvalidDataException>(() => NativePointerMapFile.Load(MapPath, 2, TestContext.Current.CancellationToken));
		BinaryPrimitives.WriteUInt64LittleEndian(fixture.AsSpan(68), 0x1020);
		WriteFixture(fixture);
		Assert.Throws<InvalidDataException>(() => NativePointerMapFile.Load(MapPath, 100, TestContext.Current.CancellationToken));
	}

	[Fact]
	public void Load_TruncatedAndTrailingPayload_Rejects()
	{
		byte[] fixture = NativeFixture(8);
		WriteFixture(fixture[..^2]);
		Assert.Throws<EndOfStreamException>(() => NativePointerMapFile.Load(MapPath, 100, TestContext.Current.CancellationToken));
		WriteFixture([.. fixture, 0]);
		Assert.Throws<InvalidDataException>(() => NativePointerMapFile.Load(MapPath, 100, TestContext.Current.CancellationToken));
	}

	[Fact]
	public void Load_NativeDirectRoot_RemainsAbsoluteAndRoundTrips()
	{
		byte[] fixture = NativeFixture(8);
		BinaryPrimitives.WriteInt32LittleEndian(fixture.AsSpan(60), -1);
		WriteFixture(fixture);
		PointerMap map = NativePointerMapFile.Load(MapPath, 100, TestContext.Current.CancellationToken);
		Assert.Empty(map.Search(0x3020, 2, 0x20, true, 100, 1000, TestContext.Current.CancellationToken).Paths);
		Assert.Equal(-1, map.GetStaticRoot(0x1020)!.Value.ModuleIndex);
		NativePointerMapFile.Save(MapPath, map, true, TestContext.Current.CancellationToken);
		Assert.Equal(-1, NativePointerMapFile.Load(MapPath, 100, TestContext.Current.CancellationToken).GetStaticRoot(0x1020)!.Value.ModuleIndex);
	}

	[Fact]
	public void LoadSave_SpecificStaticRange_PreservesNativeHeader()
	{
		byte[] fixture = NativeFixture(8);
		WriteFixture([.. fixture[..26], 1, .. Convert.FromHexString("0010000000000000FF20000000000000"), .. fixture[27..]]);
		PointerMap map = NativePointerMapFile.Load(MapPath, 100, TestContext.Current.CancellationToken);
		Assert.Equal(new PointerStaticRange(0x1000, 0x20FF), map.StaticRange);
		NativePointerMapFile.Save(MapPath, map, true, TestContext.Current.CancellationToken);
		using FileStream file = File.OpenRead(MapPath);
		using ZLibStream decompressed = new(file, CompressionMode.Decompress);
		using BinaryReader reader = new(decompressed);
		Assert.Equal(26, reader.ReadBytes(26).Length);
		Assert.Equal(1, reader.ReadByte());
		Assert.Equal(0x1000UL, reader.ReadUInt64());
		Assert.Equal(0x20FFUL, reader.ReadUInt64());
		Assert.Equal(15, reader.ReadInt32());
	}

	[Fact]
	public void StaticRoots_UnsortedModules_KeepOriginalIndexes()
	{
		PointerMap map = new(null, 8, [], [new("high.dll", 0x5000, 0x100), new("low.dll", 0x1000, 0x100), new("inner.dll", 0x1050, 0x20)], false, 0, 0);
		Assert.Equal(new PointerStaticRoot(1, 0x20), map.GetStaticRoot(0x1020));
		Assert.Equal(new PointerStaticRoot(2, 0x10), map.GetStaticRoot(0x1060));
		Assert.Equal(new PointerStaticRoot(0, 8), map.GetStaticRoot(0x5008));
		Assert.Null(map.GetStaticRoot(0x5100));
	}

	[Fact]
	public void Save_EmptyMapOrUnrepresentableStaticOffset_PreservesExistingFile()
	{
		File.WriteAllText(MapPath, "original");
		PointerMap empty = new(null, 8, [], [], false, 0, 0);
		Assert.Throws<InvalidDataException>(() => NativePointerMapFile.Save(MapPath, empty, true, TestContext.Current.CancellationToken));
		PointerMap oversized = new(null, 8, [new(0x100001000, 0x2000)], [new("game.exe", 0x1000, 0x200000000)], false, 0, 0);
		Assert.Throws<InvalidDataException>(() => NativePointerMapFile.Save(MapPath, oversized, true, TestContext.Current.CancellationToken));
		Assert.Equal("original", File.ReadAllText(MapPath));
		Assert.Single(Directory.GetFiles(_directory));
	}

	private void WriteFixture(byte[] decompressed)
	{
		using FileStream file = File.Create(MapPath);
		using ZLibStream compressed = new(file, CompressionLevel.Fastest);
		compressed.Write(decompressed);
	}

	// Independent CE v1 wire fixture: one module, two value groups, three pointer addresses.
	private static byte[] NativeFixture(int width) => Convert.FromHexString(
		"CE01" + "01000000" + "08000000" + "67616D652E657865" + "0010000000000000" + "00" +
		(width == 8 ? "0F000000" : "07000000") + "0300000000000000" +
		"0020000000000000" + "02000000" + "2010000000000000" + "01" + "00000000" + "20000000" +
		"1010000000000000" + "00" + "0030000000000000" + "01000000" + "1020000000000000" + "00");

	public void Dispose() => Directory.Delete(_directory, recursive: true);
}
