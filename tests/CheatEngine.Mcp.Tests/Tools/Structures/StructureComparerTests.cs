using CheatEngine.Mcp.Tools.Structures;

namespace CheatEngine.Mcp.Tests.Tools.Structures;

/// <summary>The byte-level classification and formatting behind <c>structure_compare</c>.</summary>
public sealed class StructureComparerTests
{
	private static readonly StructureField First = new(0, 0, 4, null, null);

	[Theory]
	[InlineData(new byte[] { 1, 1 }, new byte[] { 1, 1 }, StructureFieldClassification.Constant)]
	[InlineData(new byte[] { 1, 1 }, new byte[] { 2, 2 }, StructureFieldClassification.Discriminator)]
	[InlineData(new byte[] { 1, 2 }, new byte[] { 3, 3 }, StructureFieldClassification.VariesA)]
	[InlineData(new byte[] { 1, 1 }, new byte[] { 2, 3 }, StructureFieldClassification.VariesB)]
	[InlineData(new byte[] { 1, 2 }, new byte[] { 3, 4 }, StructureFieldClassification.VariesBoth)]
	public void Classify_FirstByteOfEachInstance_FollowsTheGroupRules(byte[] a, byte[] b,
		StructureFieldClassification expected)
	{
		Assert.Equal(expected, StructureComparer.Classify(Samples(a), Samples(b), First));
	}

	[Fact]
	public void Classify_OneUnreadableInstance_IsUnreadable()
	{
		StructureSample[] groupA = [new(0x1000, [1, 0, 0, 0]), new(0x2000, [1, 0])];

		Assert.Equal(StructureFieldClassification.Unreadable,
			StructureComparer.Classify(groupA, Samples([2]), First));
	}

	[Fact]
	public void Classify_EmptyGroupB_ComparesInsideGroupAOnly()
	{
		Assert.Equal(StructureFieldClassification.Constant, StructureComparer.Classify(Samples([7, 7]), [], First));
		Assert.Equal(StructureFieldClassification.VariesA, StructureComparer.Classify(Samples([7, 8]), [], First));
	}

	[Theory]
	[InlineData(StructureCompareMode.Discriminate, StructureFieldClassification.Discriminator, true)]
	[InlineData(StructureCompareMode.Discriminate, StructureFieldClassification.Constant, false)]
	[InlineData(StructureCompareMode.Constant, StructureFieldClassification.Constant, true)]
	[InlineData(StructureCompareMode.Constant, StructureFieldClassification.VariesA, false)]
	[InlineData(StructureCompareMode.Differs, StructureFieldClassification.VariesBoth, true)]
	[InlineData(StructureCompareMode.Differs, StructureFieldClassification.Discriminator, true)]
	[InlineData(StructureCompareMode.Differs, StructureFieldClassification.Constant, false)]
	[InlineData(StructureCompareMode.Differs, StructureFieldClassification.Unreadable, false)]
	[InlineData(StructureCompareMode.All, StructureFieldClassification.Unreadable, true)]
	public void Selects_Mode_PicksItsClassifications(StructureCompareMode mode,
		StructureFieldClassification classification, bool expected)
	{
		Assert.Equal(expected, StructureComparer.Selects(mode, classification));
	}

	[Fact]
	public void CellType_Auto_PicksFloatsPointersAndIntegersPerRow()
	{
		StructureSample[] floats = [new(0, BitConverter.GetBytes(100.5f)), new(0, BitConverter.GetBytes(0f))];
		StructureSample[] integers = [new(0, BitConverter.GetBytes(3)), new(0, BitConverter.GetBytes(4))];
		StructureSample[] pointers =
			[new(0, BitConverter.GetBytes(0x7FF6A1B2C3D0UL)), new(0, BitConverter.GetBytes(0x2A0000UL))];
		StructureField wide = new(0, 0, 8, null, null);

		Assert.Equal(StructureElementType.Float,
			StructureComparer.CellType(StructureCompareFormat.Auto, 4, floats, First));
		Assert.Equal(StructureElementType.Int32,
			StructureComparer.CellType(StructureCompareFormat.Auto, 4, integers, First));
		Assert.Equal(StructureElementType.Pointer,
			StructureComparer.CellType(StructureCompareFormat.Auto, 8, pointers, wide));
		Assert.Null(StructureComparer.CellType(StructureCompareFormat.Hex, 4, integers, First));
		Assert.Equal(StructureElementType.UInt16,
			StructureComparer.CellType(StructureCompareFormat.Unsigned, 2, integers, First));
	}

	[Fact]
	public void Format_ReadableAndUnreadableCells_AreTextOrNull()
	{
		StructureSample sample = new(0x1000, [0x9C, 0xFF, 0xFF, 0xFF, 0x01]);

		Assert.Equal("-100", StructureComparer.Format(sample, First, StructureElementType.Int32));
		Assert.Equal("9C FF FF FF", StructureComparer.Format(sample, First, null));
		Assert.Equal("9C FF FF FF", StructureComparer.Format(sample, First, StructureElementType.Binary));
		Assert.Null(StructureComparer.Format(sample, new StructureField(4, 4, 4, null, null),
			StructureElementType.Int32));
	}

	private static StructureSample[] Samples(byte[] firstBytes)
	{
		return [.. firstBytes.Select(static (value, index) => new StructureSample((ulong) (0x1000 * (index + 1)),
			[value, 0, 0, 0]))];
	}
}
