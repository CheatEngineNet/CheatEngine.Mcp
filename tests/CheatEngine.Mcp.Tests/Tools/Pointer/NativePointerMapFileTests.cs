using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

using CheatEngine.Mcp.Tools.Pointer;

namespace CheatEngine.Mcp.Tests.Tools.Pointer;

/// <summary>CE v1 <c>.scandata</c> import and export, including its exact per-address static-root metadata.</summary>
public sealed class NativePointerMapFileTests
{
	[Theory]
	[InlineData(4)]
	[InlineData(8)]
	public void Load_IndependentNativeFixture_PreservesWidthAndExactStaticRoots(int width)
	{
		using MemoryStream input = Compressed(NativeFixture(width));

		PointerMap map = NativePointerMapFile.Load(input, 3, CancellationToken.None);

		Assert.Equal(width, map.Width);
		Assert.Null(map.ProcessId);
		Assert.True(map.Incomplete);
		Assert.Equal(new PointerEntry[]
		{
			new(0x1010, 0x2000), new(0x1020, 0x2000), new(0x2010, 0x3000)
		}, map.Entries);
		Assert.Equal(new PointerModule("game.exe", 0x1000, 0x21), Assert.Single(map.Modules));
		Assert.Equal(new PointerStaticRoot(0, 0x20), map.GetStaticRoot(0x1020));
		Assert.Null(map.GetStaticRoot(0x1010));
		Assert.Null(map.FindModule(0x1010));
		Assert.Same(map.Modules[0], map.FindModule(0x1020));
	}

	[Theory]
	[InlineData(4)]
	[InlineData(8)]
	public void SaveLoad_RoundTrips32And64BitMapsWithExactStaticRoots(int width)
	{
		ulong moduleBase = width == 4 ? 0x1000UL : 0x1_0000_1000UL;
		ulong directAddress = width == 4 ? 0x7FFF_0100UL : 0x2_0000_0100UL;
		ulong lowValue = width == 4 ? 0x2000UL : 0x1_0000_2000UL;
		ulong highValue = width == 4 ? 0x3000UL : 0x1_0000_3000UL;
		PointerMap original = new(42, width,
		[
			new(moduleBase + 0x40, highValue), new(moduleBase + 0x20, lowValue), new(directAddress, lowValue)
		],
		[
			new PointerModule("game.exe", moduleBase, 0x200),
			new PointerModule("unused.dll", moduleBase + 0x1000, 0x500)
		],
		false, 123, 7,
		new Dictionary<ulong, PointerStaticRoot>
		{
			[moduleBase + 0x40] = new(0, 0x40),
			[directAddress] = new(-1, 0x7F)
		},
		new PointerStaticRange(moduleBase, moduleBase + 0xFF));
		using MemoryStream data = new();

		NativePointerMapFile.Save(data, original, CancellationToken.None);
		data.Position = 0;
		PointerMap imported = NativePointerMapFile.Load(data, 3, CancellationToken.None);

		Assert.Null(imported.ProcessId);
		Assert.Equal(width, imported.Width);
		Assert.True(imported.Incomplete);
		Assert.Equal(0, imported.BytesRead);
		Assert.Equal(0, imported.UnreadableBytes);
		Assert.Equal(new PointerEntry[]
		{
			new(moduleBase + 0x20, lowValue), new(moduleBase + 0x40, highValue), new(directAddress, lowValue)
		}, imported.Entries);
		Assert.Equal(new PointerModule[]
		{
			new("game.exe", moduleBase, 0x41), new("unused.dll", moduleBase + 0x1000, 0)
		}, imported.Modules);
		Assert.Equal(new PointerStaticRange(moduleBase, moduleBase + 0xFF), imported.StaticRange);
		Assert.Equal(new PointerStaticRoot(0, 0x40), imported.GetStaticRoot(moduleBase + 0x40));
		Assert.Equal(new PointerStaticRoot(-1, 0x7F), imported.GetStaticRoot(directAddress));
		Assert.Null(imported.GetStaticRoot(moduleBase + 0x20));
		Assert.Null(imported.FindModule(moduleBase + 0x20));
		Assert.Same(imported.Modules[0], imported.FindModule(moduleBase + 0x40));
	}

	public static IEnumerable<object[]> InvalidNativeFields()
	{
		yield return ["signature", WithByte(0, 0)];
		yield return ["version", WithByte(1, 2)];
		yield return ["static range flag", WithByte(26, 2)];
		yield return ["pointer width", WithUInt32(27, 9)];
		yield return ["empty value group", WithUInt32(47, 0)];
		yield return ["oversized value group", WithUInt32(47, 4)];
		yield return ["entry static flag", WithByte(59, 2)];
		yield return ["unknown module root", WithInt32(60, 1)];
		yield return ["inconsistent module root offset", WithUInt32(64, 0x21)];
		yield return ["trailing entry payload", WithTrailingByte()];
	}

	[Theory]
	[MemberData(nameof(InvalidNativeFields))]
	public void Load_InvalidNativeField_Rejects(string _, byte[] payload)
	{
		using MemoryStream input = Compressed(payload);

		Assert.Throws<InvalidDataException>(() => NativePointerMapFile.Load(input, 3, CancellationToken.None));
	}

	[Fact]
	public void Load_TruncatedPayload_Rejects()
	{
		byte[] payload = NativeFixture(8)[..^2];
		using MemoryStream input = Compressed(payload);

		Assert.Throws<EndOfStreamException>(() => NativePointerMapFile.Load(input, 3, CancellationToken.None));
	}

	[Fact]
	public void Load_DeclaredPointerCountExceedingRequestedLimit_Rejects()
	{
		using MemoryStream input = Compressed(NativeFixture(8));

		Assert.Throws<InvalidDataException>(() => NativePointerMapFile.Load(input, 2, CancellationToken.None));
	}

	[Fact]
	public void Load_DeclaredModuleCountAboveFormatLimit_Rejects()
	{
		using MemoryStream input = Compressed(WithUInt32(2, 4097));

		Assert.Throws<InvalidDataException>(() => NativePointerMapFile.Load(input, 3, CancellationToken.None));
	}

	[Fact]
	public void Load_UnsupportedMaximumPointerBound_Rejects()
	{
		using MemoryStream first = Compressed(NativeFixture(8));
		using MemoryStream second = Compressed(NativeFixture(8));

		Assert.Throws<ArgumentOutOfRangeException>(() => NativePointerMapFile.Load(first, 0, CancellationToken.None));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			NativePointerMapFile.Load(second, NativePointerMapFile.MaximumEntries + 1, CancellationToken.None));
	}

	[Fact]
	public void Load_DuplicatePointerStorageAddress_Rejects()
	{
		byte[] payload = NativeFixture(8);
		BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(68), 0x1020);
		using MemoryStream input = Compressed(payload);

		Assert.Throws<InvalidDataException>(() => NativePointerMapFile.Load(input, 3, CancellationToken.None));
	}

	[Fact]
	public void Save_EmptyMap_RejectsBeforeWriting()
	{
		PointerMap empty = new(null, 8, [], [], false, 0, 0);
		using MemoryStream output = new();

		Assert.Throws<InvalidDataException>(() => NativePointerMapFile.Save(output, empty, CancellationToken.None));
		Assert.Empty(output.ToArray());
	}

	[Fact]
	public void Save_StaticRootOffsetAboveUInt32_Rejects()
	{
		const ulong address = (ulong) uint.MaxValue + 1;
		PointerMap map = new(null, 8, [new PointerEntry(address, 0x2000)], [new PointerModule("game.exe", 0, 0)],
			false, 0, 0, new Dictionary<ulong, PointerStaticRoot> { [address] = new(0, address) });
		using MemoryStream output = new();

		Assert.Throws<InvalidDataException>(() => NativePointerMapFile.Save(output, map, CancellationToken.None));
	}

	[Fact]
	public void Load_WithCancelledToken_Throws()
	{
		using CancellationTokenSource stopping = new();
		stopping.Cancel();
		using MemoryStream input = Compressed(NativeFixture(8));

		Assert.ThrowsAny<OperationCanceledException>(() => NativePointerMapFile.Load(input, 3, stopping.Token));
	}

	[Fact]
	public void Save_WithCancelledToken_Throws()
	{
		PointerMap map = new(null, 8, [new PointerEntry(0x1010, 0x2000)], [], false, 0, 0);
		using CancellationTokenSource stopping = new();
		stopping.Cancel();
		using MemoryStream output = new();

		Assert.ThrowsAny<OperationCanceledException>(() => NativePointerMapFile.Save(output, map, stopping.Token));
	}

	private static MemoryStream Compressed(byte[] payload)
	{
		MemoryStream stream = new();
		using (ZLibStream compressed = new(stream, CompressionLevel.Fastest, leaveOpen: true))
		{
			compressed.Write(payload);
		}
		stream.Position = 0;
		return stream;
	}

	// Independent CE v1 wire fixture: one module, two value groups, three pointer addresses.
	private static byte[] NativeFixture(int width) => Convert.FromHexString(
		"CE01" + "01000000" + "08000000" + "67616D652E657865" + "0010000000000000" + "00" +
		(width == 8 ? "0F000000" : "07000000") + "0300000000000000" +
		"0020000000000000" + "02000000" + "2010000000000000" + "01" + "00000000" + "20000000" +
		"1010000000000000" + "00" + "0030000000000000" + "01000000" + "1020000000000000" + "00");

	private static byte[] WithByte(int index, byte value)
	{
		byte[] payload = NativeFixture(8);
		payload[index] = value;
		return payload;
	}

	private static byte[] WithUInt32(int index, uint value)
	{
		byte[] payload = NativeFixture(8);
		BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(index), value);
		return payload;
	}

	private static byte[] WithInt32(int index, int value)
	{
		byte[] payload = NativeFixture(8);
		BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(index), value);
		return payload;
	}

	private static byte[] WithTrailingByte()
	{
		byte[] payload = NativeFixture(8);
		Array.Resize(ref payload, payload.Length + 1);
		return payload;
	}
}
