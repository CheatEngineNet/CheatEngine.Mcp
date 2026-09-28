using System.Buffers.Binary;

using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>
///     Compares two images of one size slot by slot: a slot is <c>width</c> bytes read as one value type, and slots
///     start every <c>alignment</c> bytes from offset 0. A slot that touches an unreadable byte of either image is
///     skipped.
/// </summary>
internal static class MemorySnapshotComparer
{
	/// <summary>Compares two images and keeps one page of the slots that match the change filter.</summary>
	/// <param name="before">The compared snapshot's image.</param>
	/// <param name="after">The other image, of the same size.</param>
	/// <param name="shape">The value type, slot width, alignment and pointer size.</param>
	/// <param name="change">The change filter.</param>
	/// <param name="address">The compared snapshot's address, for the slot addresses.</param>
	/// <param name="offset">The index of the first matching slot to return.</param>
	/// <param name="limit">The most matching slots to return.</param>
	/// <returns>The counts and the page.</returns>
	internal static SnapshotMatches Compare(MemoryImage before, MemoryImage after, SnapshotSlots shape,
		SnapshotChangeFilter change, ulong address, int offset, int limit)
	{
		ArgumentOutOfRangeException.ThrowIfNotEqual(after.Bytes.Length, before.Bytes.Length);
		UnreadableCursor first = new(before.Unreadable);
		UnreadableCursor second = new(after.Unreadable);
		List<SnapshotChange> page = [];
		int compared = 0;
		int skipped = 0;
		int total = 0;
		int width = shape.Width;
		for (int position = 0; position <= before.Bytes.Length - width; position += shape.Alignment)
		{
			// Both cursors must advance, so both are asked.
			bool unreadable = first.Overlaps(position, width) | second.Overlaps(position, width);
			if (unreadable)
			{
				skipped++;
				continue;
			}

			compared++;
			ReadOnlySpan<byte> old = before.Bytes.AsSpan(position, width);
			ReadOnlySpan<byte> current = after.Bytes.AsSpan(position, width);
			if (!Matches(shape, change, old, current))
			{
				continue;
			}

			if (total >= offset && page.Count < limit)
			{
				page.Add(new SnapshotChange(HexFormat.Address(unchecked(address + (ulong) position)),
					HexFormat.Offset(position), MemoryTargets.Decode(shape.Type, old, shape.PointerBytes),
					MemoryTargets.Decode(shape.Type, current, shape.PointerBytes)));
			}

			total++;
		}

		int end = Math.Min(offset, total) + page.Count;
		return new SnapshotMatches(compared, skipped, total, [.. page], end < total ? end : null);
	}

	private static bool Matches(SnapshotSlots shape, SnapshotChangeFilter change, ReadOnlySpan<byte> before,
		ReadOnlySpan<byte> after)
	{
		return change switch
		{
			SnapshotChangeFilter.Changed => !before.SequenceEqual(after),
			SnapshotChangeFilter.Unchanged => before.SequenceEqual(after),
			SnapshotChangeFilter.Increased => Order(shape, before, after) > 0,
			_ => Order(shape, before, after) < 0
		};
	}

	/// <summary>The sign of <c>after - before</c> as numbers of the slot's type; 0 when equal or NaN.</summary>
	private static int Order(SnapshotSlots shape, ReadOnlySpan<byte> before, ReadOnlySpan<byte> after)
	{
		return shape.Type switch
		{
			McpValueType.Int8 => unchecked((sbyte) after[0]).CompareTo(unchecked((sbyte) before[0])),
			McpValueType.UInt8 => after[0].CompareTo(before[0]),
			McpValueType.Int16 => BinaryPrimitives.ReadInt16LittleEndian(after)
				.CompareTo(BinaryPrimitives.ReadInt16LittleEndian(before)),
			McpValueType.UInt16 => BinaryPrimitives.ReadUInt16LittleEndian(after)
				.CompareTo(BinaryPrimitives.ReadUInt16LittleEndian(before)),
			McpValueType.Int32 => BinaryPrimitives.ReadInt32LittleEndian(after)
				.CompareTo(BinaryPrimitives.ReadInt32LittleEndian(before)),
			McpValueType.UInt32 => BinaryPrimitives.ReadUInt32LittleEndian(after)
				.CompareTo(BinaryPrimitives.ReadUInt32LittleEndian(before)),
			McpValueType.Int64 => BinaryPrimitives.ReadInt64LittleEndian(after)
				.CompareTo(BinaryPrimitives.ReadInt64LittleEndian(before)),
			McpValueType.UInt64 => BinaryPrimitives.ReadUInt64LittleEndian(after)
				.CompareTo(BinaryPrimitives.ReadUInt64LittleEndian(before)),
			McpValueType.Float => Order(BinaryPrimitives.ReadSingleLittleEndian(before),
				BinaryPrimitives.ReadSingleLittleEndian(after)),
			McpValueType.Double => Order(BinaryPrimitives.ReadDoubleLittleEndian(before),
				BinaryPrimitives.ReadDoubleLittleEndian(after)),
			McpValueType.Pointer when shape.PointerBytes == 4 => BinaryPrimitives.ReadUInt32LittleEndian(after)
				.CompareTo(BinaryPrimitives.ReadUInt32LittleEndian(before)),
			McpValueType.Pointer => BinaryPrimitives.ReadUInt64LittleEndian(after)
				.CompareTo(BinaryPrimitives.ReadUInt64LittleEndian(before)),
			_ => throw new ArgumentOutOfRangeException(nameof(shape), shape.Type, "Not a fixed-size value type.")
		};
	}

	private static int Order(double before, double after)
	{
		if (double.IsNaN(before) || double.IsNaN(after))
		{
			return 0;
		}

		return after > before ? 1 : after < before ? -1 : 0;
	}

	/// <summary>Answers whether ascending slots touch any of the sorted, merged unreadable ranges.</summary>
	/// <param name="ranges">The unreadable ranges in offset order.</param>
	private sealed class UnreadableCursor((int Offset, int Length)[] ranges)
	{
		private int _index;

		/// <summary>Whether a slot touches an unreadable range; slots must be asked in ascending order.</summary>
		/// <param name="start">The slot's offset.</param>
		/// <param name="width">The slot's width.</param>
		/// <returns><see langword="true" /> when any byte of the slot is unreadable.</returns>
		internal bool Overlaps(int start, int width)
		{
			while (_index < ranges.Length && ranges[_index].Offset + ranges[_index].Length <= start)
			{
				_index++;
			}

			return _index < ranges.Length && ranges[_index].Offset < start + width;
		}
	}
}

/// <summary>How a snapshot comparison cuts the images into slots.</summary>
/// <param name="Type">The fixed-size value type of a slot.</param>
/// <param name="Width">The slot's size in bytes.</param>
/// <param name="Alignment">The distance between two slots.</param>
/// <param name="PointerBytes">The pointer size that decodes a pointer slot, 4 or 8.</param>
internal readonly record struct SnapshotSlots(McpValueType Type, int Width, int Alignment, int PointerBytes);

/// <summary>The counts and one page of a snapshot comparison.</summary>
/// <param name="Compared">How many slots were compared.</param>
/// <param name="Skipped">How many slots touch unreadable bytes.</param>
/// <param name="Total">How many compared slots match the change filter.</param>
/// <param name="Page">The matching slots of the page.</param>
/// <param name="NextOffset">The offset of the next page, when there is one.</param>
internal readonly record struct SnapshotMatches(
	int Compared,
	int Skipped,
	int Total,
	SnapshotChange[] Page,
	int? NextOffset);
