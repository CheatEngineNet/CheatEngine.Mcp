namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>
///     The growing list of a running capture, in fixed segments, so a large map never doubles its memory while it grows.
///     Only the capture job's thread writes it.
/// </summary>
internal sealed class PointerEntryBuffer
{
	internal const int SegmentLength = 65_536;

	private readonly List<PointerEntry[]> _segments = [];
	private int _last = SegmentLength;

	/// <summary>How many entries were added.</summary>
	internal int Count
	{
		get;
		private set;
	}

	/// <summary>Adds one entry.</summary>
	/// <param name="entry">The entry.</param>
	internal void Add(PointerEntry entry)
	{
		if (_last == SegmentLength)
		{
			_segments.Add(new PointerEntry[SegmentLength]);
			_last = 0;
		}

		_segments[^1][_last++] = entry;
		Count++;
	}

	/// <summary>Copies the entries, in the order they were added, into one exact array.</summary>
	/// <returns>The entries.</returns>
	internal PointerEntry[] ToArray()
	{
		PointerEntry[] entries = new PointerEntry[Count];
		int copied = 0;
		foreach (PointerEntry[] segment in _segments)
		{
			int length = Math.Min(SegmentLength, Count - copied);
			Array.Copy(segment, 0, entries, copied, length);
			copied += length;
		}

		return entries;
	}
}
