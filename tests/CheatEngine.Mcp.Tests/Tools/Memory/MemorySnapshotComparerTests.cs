using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Memory;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>
///     The slot comparison behind <c>memory_compare_snapshot</c>: bitwise changes, numeric order per value type, NaN,
///     alignment and slots that touch unreadable bytes.
/// </summary>
public sealed class MemorySnapshotComparerTests
{
	[Theory]
	[InlineData(McpValueType.Int8, "01", "FF", SnapshotChangeFilter.Decreased, "1", "-1")]
	[InlineData(McpValueType.UInt8, "01", "FF", SnapshotChangeFilter.Increased, "1", "255")]
	[InlineData(McpValueType.Int16, "01 00", "FF FF", SnapshotChangeFilter.Decreased, "1", "-1")]
	[InlineData(McpValueType.UInt16, "01 00", "FF FF", SnapshotChangeFilter.Increased, "1", "65535")]
	[InlineData(McpValueType.Int32, "01 00 00 00", "FF FF FF FF", SnapshotChangeFilter.Decreased, "1", "-1")]
	[InlineData(McpValueType.UInt32, "01 00 00 00", "FF FF FF FF", SnapshotChangeFilter.Increased, "1",
		"4294967295")]
	[InlineData(McpValueType.Int64, "01 00 00 00 00 00 00 00", "FF FF FF FF FF FF FF FF",
		SnapshotChangeFilter.Decreased, "1", "-1")]
	[InlineData(McpValueType.UInt64, "01 00 00 00 00 00 00 00", "FF FF FF FF FF FF FF FF",
		SnapshotChangeFilter.Increased, "1", "18446744073709551615")]
	[InlineData(McpValueType.Float, "00 00 80 3F", "00 00 20 C0", SnapshotChangeFilter.Decreased, "1", "-2.5")]
	[InlineData(McpValueType.Double, "00 00 00 00 00 00 F0 3F", "00 00 00 00 00 00 04 40",
		SnapshotChangeFilter.Increased, "1", "2.5")]
	[InlineData(McpValueType.Pointer, "00 10 00 00 00 00 00 00", "00 20 00 00 00 00 00 00",
		SnapshotChangeFilter.Increased, "1000", "2000")]
	public void Compare_NumericOrder_FollowsTheSignednessOfTheType(McpValueType type, string before, string after,
		SnapshotChangeFilter expected, string beforeText, string afterText)
	{
		byte[] first = HexParse.Bytes(before, "before", 16);
		byte[] second = HexParse.Bytes(after, "after", 16);
		SnapshotSlots shape = new(type, first.Length, first.Length, 8);
		SnapshotChangeFilter opposite = expected is SnapshotChangeFilter.Increased
			? SnapshotChangeFilter.Decreased
			: SnapshotChangeFilter.Increased;

		SnapshotMatches matches = MemorySnapshotComparer.Compare(Image(first), Image(second), shape, expected,
			0x400000, 0, 10);
		SnapshotMatches none = MemorySnapshotComparer.Compare(Image(first), Image(second), shape, opposite,
			0x400000, 0, 10);

		Assert.Equal([new SnapshotChange("400000", "0", beforeText, afterText)], matches.Page);
		Assert.Equal((1, 1, 0), (matches.Compared, matches.Total, none.Total));
	}

	[Fact]
	public void Compare_NaNAndSignedZero_ChangeBitsButNeverIncreaseOrDecrease()
	{
		byte[] before = [.. BitConverter.GetBytes(float.NaN), .. BitConverter.GetBytes(0F)];
		byte[] after = [.. BitConverter.GetBytes(1F), .. BitConverter.GetBytes(-0F)];
		SnapshotSlots shape = new(McpValueType.Float, 4, 4, 8);

		int Total(SnapshotChangeFilter change)
		{
			return MemorySnapshotComparer.Compare(Image(before), Image(after), shape, change, 0, 0, 10).Total;
		}

		Assert.Equal((2, 0, 0, 0), (Total(SnapshotChangeFilter.Changed), Total(SnapshotChangeFilter.Unchanged),
			Total(SnapshotChangeFilter.Increased), Total(SnapshotChangeFilter.Decreased)));
	}

	[Theory]
	[InlineData(1, 5)]
	[InlineData(2, 3)]
	[InlineData(4, 2)]
	public void Compare_Alignment_StartsASlotEveryAlignmentBytesFromOffsetZero(int alignment, int slots)
	{
		byte[] bytes = new byte[8];
		SnapshotSlots shape = new(McpValueType.Int32, 4, alignment, 8);

		SnapshotMatches matches = MemorySnapshotComparer.Compare(Image(bytes), Image(bytes), shape,
			SnapshotChangeFilter.Unchanged, 0, 0, 10);

		Assert.Equal((slots, slots), (matches.Compared, matches.Total));
		Assert.Equal(Enumerable.Range(0, slots).Select(index => HexFormat.Offset(index * alignment)),
			matches.Page.Select(static change => change.Offset));
	}

	[Fact]
	public void Compare_UnreadableBytesOnEitherSide_SkipTheSlotsTheyTouch()
	{
		byte[] before = new byte[16];
		byte[] after = [.. Enumerable.Repeat((byte) 1, 16)];
		MemoryImage first = Image(before, (2, 1));
		MemoryImage second = Image(after, (9, 2));
		SnapshotSlots shape = new(McpValueType.Int32, 4, 4, 8);

		SnapshotMatches matches = MemorySnapshotComparer.Compare(first, second, shape, SnapshotChangeFilter.Changed,
			0x1000, 0, 10);

		Assert.Equal((2, 2, 2), (matches.Compared, matches.Skipped, matches.Total));
		Assert.Equal(["1004", "100C"], matches.Page.Select(static change => change.Address));
	}

	[Fact]
	public void Compare_Pointer32_DecodesAndOrdersFourBytes()
	{
		byte[] before = [0xFF, 0xFF, 0xFF, 0xFF];
		byte[] after = [0x00, 0x00, 0x00, 0x80];
		SnapshotSlots shape = new(McpValueType.Pointer, 4, 4, 4);

		SnapshotMatches matches = MemorySnapshotComparer.Compare(Image(before), Image(after), shape,
			SnapshotChangeFilter.Decreased, 0, 0, 10);

		Assert.Equal([new SnapshotChange("0", "0", "FFFFFFFF", "80000000")], matches.Page);
	}

	[Fact]
	public void Read_ChunksWithZeroRanges_MergeAdjacentRangesAcrossChunks()
	{
		MemoryFileTools.Chunk first = new(new byte[MemoryTargets.ChunkBytes], [(MemoryTargets.ChunkBytes - 16, 16)]);
		MemoryFileTools.Chunk second = new(new byte[32], [(0, 8), (16, 4)]);

		MemoryImage image = MemoryImage.Read(MemoryTargets.ChunkBytes + 32, first, (offset, length) =>
		{
			Assert.Equal((MemoryTargets.ChunkBytes, 32), (offset, length));
			return second;
		});

		Assert.Equal([(MemoryTargets.ChunkBytes - 16, 24), (MemoryTargets.ChunkBytes + 16, 4)], image.Unreadable);
		Assert.Equal(28, image.UnreadableBytes);
		UnreadableRange[] listed = image.Listed(out bool truncated);
		Assert.False(truncated);
		Assert.Equal(new UnreadableRange("FFFF0", 24), listed[0]);
	}

	[Fact]
	public void Listed_MoreThan256Ranges_IsTruncated()
	{
		(int Offset, int Length)[] zeros = [.. Enumerable.Range(0, 300).Select(static index => (index * 2, 1))];
		MemoryImage image = MemoryImage.Read(600, new MemoryFileTools.Chunk(new byte[600], zeros),
			static (_, _) => throw new XunitException("One chunk only."));

		UnreadableRange[] listed = image.Listed(out bool truncated);

		Assert.True(truncated);
		Assert.Equal(MemoryFileTools.MaximumZeroRanges, listed.Length);
		Assert.Equal(300, image.UnreadableBytes);
	}

	private static MemoryImage Image(byte[] bytes, params (int Offset, int Length)[] unreadable)
	{
		return MemoryImage.Read(bytes.Length, new MemoryFileTools.Chunk(bytes, unreadable),
			static (_, _) => throw new XunitException("One chunk only."));
	}
}
