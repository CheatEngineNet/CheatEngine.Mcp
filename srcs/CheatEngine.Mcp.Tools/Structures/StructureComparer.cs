using System.Buffers.Binary;

using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>One instance compared by <c>structure_compare</c>: the bytes confirmed readable from the compared range.</summary>
/// <param name="Address">The resolved base address.</param>
/// <param name="Bytes">The confirmed prefix of the compared range; cells past it are unreadable.</param>
internal sealed record StructureSample(ulong Address, byte[] Bytes)
{
	/// <summary>Returns a cell, or <see langword="false" /> when it lies past the confirmed prefix.</summary>
	internal bool TryCell(int start, int size, out ReadOnlySpan<byte> cell)
	{
		if (start >= 0 && size > 0 && start + size <= Bytes.Length)
		{
			cell = Bytes.AsSpan(start, size);
			return true;
		}

		cell = default;
		return false;
	}
}

/// <summary>One compared field: where it starts inside the compared range, its size and how to show it.</summary>
/// <param name="Offset">The field's offset from the structure's start.</param>
/// <param name="Start">The field's first byte inside the compared range.</param>
/// <param name="Size">The field's size in bytes.</param>
/// <param name="Name">The element's name when comparing along a structure.</param>
/// <param name="Type">The element's type when comparing along a structure.</param>
internal readonly record struct StructureField(
	long Offset,
	int Start,
	int Size,
	string? Name,
	StructureElementType? Type);

/// <summary>
///     Classifies fields across two groups of instances by their raw bytes, and formats them. It is pure, so its
///     rules are tested without Cheat Engine.
/// </summary>
internal static class StructureComparer
{
	private const ulong LowestPointer = 0x10000;
	private const ulong HighestUserPointer = 0x7FFF_FFFF_FFFF;

	/// <summary>Classifies one field by comparing its bytes inside and between the groups.</summary>
	internal static StructureFieldClassification Classify(StructureSample[] groupA, StructureSample[] groupB,
		StructureField field)
	{
		if (!AllReadable(groupA, field) || !AllReadable(groupB, field))
		{
			return StructureFieldClassification.Unreadable;
		}

		bool constantA = AllEqual(groupA, field);
		bool constantB = AllEqual(groupB, field);
		if (constantA && constantB)
		{
			return groupB.Length == 0 || Cell(groupA[0], field).SequenceEqual(Cell(groupB[0], field))
				? StructureFieldClassification.Constant
				: StructureFieldClassification.Discriminator;
		}

		if (!constantA && !constantB)
		{
			return StructureFieldClassification.VariesBoth;
		}

		return constantA ? StructureFieldClassification.VariesB : StructureFieldClassification.VariesA;
	}

	/// <summary>Whether a mode returns rows of a classification.</summary>
	internal static bool Selects(StructureCompareMode mode, StructureFieldClassification classification)
	{
		return mode switch
		{
			StructureCompareMode.Discriminate => classification is StructureFieldClassification.Discriminator,
			StructureCompareMode.Constant => classification is StructureFieldClassification.Constant,
			StructureCompareMode.Differs => classification is not (StructureFieldClassification.Constant
				or StructureFieldClassification.Unreadable),
			_ => true
		};
	}

	/// <summary>
	///     Returns the element type that shows a raw cell of one row: the requested format, or for <c>auto</c> a pointer
	///     or float when every readable value looks like one, otherwise an integer. <see langword="null" /> means bytes.
	/// </summary>
	internal static StructureElementType? CellType(StructureCompareFormat format, int granularity,
		IEnumerable<StructureSample> samples, StructureField field)
	{
		switch (format)
		{
			case StructureCompareFormat.Hex:
				return null;
			case StructureCompareFormat.Signed:
				return Integer(granularity, true);
			case StructureCompareFormat.Unsigned:
				return Integer(granularity, false);
			case StructureCompareFormat.Float:
				return granularity == 8 ? StructureElementType.Double : StructureElementType.Float;
		}

		if (granularity < 4)
		{
			return Integer(granularity, false);
		}

		bool pointers = granularity == 8, floats = true, anyNonZero = false;
		foreach (StructureSample sample in samples)
		{
			if (!sample.TryCell(field.Start, field.Size, out ReadOnlySpan<byte> cell))
			{
				continue;
			}

			ulong bits = granularity == 8
				? BinaryPrimitives.ReadUInt64LittleEndian(cell)
				: BinaryPrimitives.ReadUInt32LittleEndian(cell);
			if (bits == 0)
			{
				continue;
			}

			anyNonZero = true;
			pointers &= bits is >= LowestPointer and <= HighestUserPointer;
			double value = granularity == 8
				? BitConverter.UInt64BitsToDouble(bits)
				: BitConverter.UInt32BitsToSingle((uint) bits);
			double magnitude = Math.Abs(value);
			floats &= double.IsFinite(value) && magnitude is >= 1e-4 and <= 1e9;
		}

		if (anyNonZero && pointers)
		{
			return StructureElementType.Pointer;
		}

		if (anyNonZero && floats)
		{
			return granularity == 8 ? StructureElementType.Double : StructureElementType.Float;
		}

		return Integer(granularity, true);
	}

	/// <summary>Formats one sample's cell; <see langword="null" /> when it is unreadable.</summary>
	internal static string? Format(StructureSample sample, StructureField field, StructureElementType? type)
	{
		if (!sample.TryCell(field.Start, field.Size, out ReadOnlySpan<byte> cell))
		{
			return null;
		}

		return type is { } shown && StructureValueCodec.IsManaged(shown)
			? StructureValueCodec.Decode(shown, cell) ?? HexFormat.Bytes(cell)
			: HexFormat.Bytes(cell);
	}

	private static StructureElementType Integer(int granularity, bool signed)
	{
		return granularity switch
		{
			1 => signed ? StructureElementType.Int8 : StructureElementType.UInt8,
			2 => signed ? StructureElementType.Int16 : StructureElementType.UInt16,
			4 => signed ? StructureElementType.Int32 : StructureElementType.UInt32,
			_ => signed ? StructureElementType.Int64 : StructureElementType.UInt64
		};
	}

	private static bool AllReadable(StructureSample[] group, StructureField field)
	{
		foreach (StructureSample sample in group)
		{
			if (!sample.TryCell(field.Start, field.Size, out _))
			{
				return false;
			}
		}

		return true;
	}

	private static bool AllEqual(StructureSample[] group, StructureField field)
	{
		for (int index = 1; index < group.Length; index++)
		{
			if (!Cell(group[index], field).SequenceEqual(Cell(group[0], field)))
			{
				return false;
			}
		}

		return true;
	}

	private static ReadOnlySpan<byte> Cell(StructureSample sample, StructureField field)
	{
		return sample.Bytes.AsSpan(field.Start, field.Size);
	}
}
