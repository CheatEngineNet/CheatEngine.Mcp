using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>
///     A copied target range: its bytes, where unreadable memory holds zeros, and those unreadable ranges, merged and
///     in offset order.
/// </summary>
internal sealed class MemoryImage
{
	private MemoryImage(byte[] bytes, (int Offset, int Length)[] unreadable, int unreadableBytes)
	{
		Bytes = bytes;
		Unreadable = unreadable;
		UnreadableBytes = unreadableBytes;
	}

	/// <summary>The copied bytes; unreadable ranges hold zeros.</summary>
	internal byte[] Bytes
	{
		get;
	}

	/// <summary>The unreadable ranges, merged when adjacent, in offset order.</summary>
	internal (int Offset, int Length)[] Unreadable
	{
		get;
	}

	/// <summary>How many bytes were unreadable.</summary>
	internal int UnreadableBytes
	{
		get;
	}

	/// <summary>Assembles an image from consecutive chunks, the first already read.</summary>
	/// <param name="size">The image's size in bytes.</param>
	/// <param name="first">The first chunk, at offset 0.</param>
	/// <param name="next">
	///     Reads the chunk at an offset with a length of at most <see cref="MemoryTargets.ChunkBytes" />.
	/// </param>
	/// <returns>The image.</returns>
	internal static MemoryImage Read(int size, MemoryFileTools.Chunk first, Func<int, int, MemoryFileTools.Chunk> next)
	{
		byte[] bytes = new byte[size];
		List<(int Offset, int Length)> unreadable = [];
		int unreadableBytes = 0;
		MemoryFileTools.Chunk chunk = first;
		int offset = 0;
		while (true)
		{
			chunk.Bytes.CopyTo(bytes, offset);
			foreach ((int start, int length) in chunk.Zeros)
			{
				int position = offset + start;
				unreadableBytes += length;
				if (unreadable.Count > 0 && unreadable[^1].Offset + unreadable[^1].Length == position)
				{
					unreadable[^1] = (unreadable[^1].Offset, unreadable[^1].Length + length);
				}
				else
				{
					unreadable.Add((position, length));
				}
			}

			offset += chunk.Bytes.Length;
			if (offset >= size)
			{
				break;
			}

			chunk = next(offset, Math.Min(size - offset, MemoryTargets.ChunkBytes));
		}

		return new MemoryImage(bytes, [.. unreadable], unreadableBytes);
	}

	/// <summary>The contract form of the unreadable ranges, listed up to a bound.</summary>
	/// <param name="truncated">Whether more ranges exist than listed.</param>
	/// <returns>The listed ranges.</returns>
	internal UnreadableRange[] Listed(out bool truncated)
	{
		int count = Math.Min(Unreadable.Length, MemoryFileTools.MaximumZeroRanges);
		truncated = count < Unreadable.Length;
		UnreadableRange[] listed = new UnreadableRange[count];
		for (int index = 0; index < count; index++)
		{
			listed[index] = new UnreadableRange(HexFormat.Offset(Unreadable[index].Offset), Unreadable[index].Length);
		}

		return listed;
	}
}

/// <summary>A named copy of a target range, held in this activation's managed memory.</summary>
/// <param name="Name">The snapshot's name.</param>
/// <param name="Address">The copied range's start address.</param>
/// <param name="ProcessId">The process the range was copied from.</param>
/// <param name="SelectionEpoch">The target-selection epoch the range was copied in.</param>
/// <param name="PointerBytes">The target's pointer size, when Cheat Engine reported it.</param>
/// <param name="CreatedUtc">When the range was copied.</param>
/// <param name="Image">The copied bytes and their unreadable ranges.</param>
internal sealed record MemorySnapshot(
	string Name,
	ulong Address,
	int ProcessId,
	long SelectionEpoch,
	int? PointerBytes,
	DateTimeOffset CreatedUtc,
	MemoryImage Image)
{
	/// <summary>The number of bytes copied.</summary>
	internal int Size => Image.Bytes.Length;

	/// <summary>The contract form of the snapshot.</summary>
	/// <returns>The description.</returns>
	internal MemorySnapshotInfo Describe()
	{
		UnreadableRange[] unreadable = Image.Listed(out bool truncated);
		return new MemorySnapshotInfo(Name, HexFormat.Address(Address), Size, ProcessId, SelectionEpoch, CreatedUtc,
			Image.UnreadableBytes, unreadable, truncated, PointerBytes);
	}
}
