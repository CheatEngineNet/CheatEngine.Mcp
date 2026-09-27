namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>
///     The committed, readable address ranges of the captured process: a captured value is a pointer only when it
///     points into one of them. The ranges are sorted and merged, so a lookup is one binary search.
/// </summary>
internal sealed class PointerValueFilter
{
	private readonly ulong[] _lasts;
	private readonly ulong[] _starts;

	/// <summary>Creates the filter from inclusive ranges in any order.</summary>
	/// <param name="ranges">The ranges, as first and last address.</param>
	internal PointerValueFilter(IEnumerable<(ulong Start, ulong Last)> ranges)
	{
		ArgumentNullException.ThrowIfNull(ranges);
		List<ulong> starts = [];
		List<ulong> lasts = [];
		foreach ((ulong start, ulong last) in ranges.Where(static range => range.Last >= range.Start)
					 .OrderBy(static range => range.Start))
		{
			// Merge a range that touches or overlaps the previous one.
			if (lasts.Count > 0 && (lasts[^1] == ulong.MaxValue || start <= lasts[^1] + 1))
			{
				lasts[^1] = Math.Max(lasts[^1], last);
				continue;
			}

			starts.Add(start);
			lasts.Add(last);
		}

		_starts = [.. starts];
		_lasts = [.. lasts];
	}

	/// <summary>How many merged ranges the filter holds.</summary>
	internal int RangeCount => _starts.Length;

	/// <summary>Whether a value points into one of the ranges.</summary>
	/// <param name="value">The value.</param>
	/// <returns><see langword="true" /> inside a range.</returns>
	internal bool Contains(ulong value)
	{
		if (_starts.Length == 0 || value < _starts[0] || value > _lasts[^1])
		{
			return false;
		}

		int low = 0;
		int high = _starts.Length;
		while (low < high)
		{
			int middle = low + ((high - low) / 2);
			if (_starts[middle] <= value)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low > 0 && value <= _lasts[low - 1];
	}
}
