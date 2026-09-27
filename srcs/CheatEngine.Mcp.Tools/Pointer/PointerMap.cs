using System.Buffers.Binary;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>
///     A bounded, managed snapshot of the pointers of one process. It owns no target memory or Cheat Engine handle, is
///     immutable once built, and costs 20 bytes per pointer: the entries sorted by address and an index sorted by value.
/// </summary>
internal sealed class PointerMap
{
	/// <summary>How many visited nodes separate two cancellation checks and progress reports of a search.</summary>
	internal const int SearchReportInterval = 65_536;

	private readonly PointerModule[] _byBase;
	private readonly int[] _byValue;

	/// <summary>Creates a map; the entries are sorted by address when they are not already.</summary>
	/// <param name="processId">The process the map was captured from.</param>
	/// <param name="width">The target pointer width, 4 or 8 bytes.</param>
	/// <param name="entries">The captured pointers; the map takes ownership of the array.</param>
	/// <param name="modules">The modules of the process.</param>
	/// <param name="incomplete">Whether the capture skipped memory or hit a limit.</param>
	/// <param name="bytesRead">How many bytes were read.</param>
	/// <param name="unreadableBytes">How many requested bytes could not be read.</param>
	internal PointerMap(int processId, int width, PointerEntry[] entries, PointerModule[] modules, bool incomplete,
		long bytesRead, long unreadableBytes)
	{
		ArgumentNullException.ThrowIfNull(entries);
		ArgumentNullException.ThrowIfNull(modules);
		if (width is not (4 or 8))
		{
			throw new ArgumentOutOfRangeException(nameof(width), width, "A pointer is 4 or 8 bytes wide.");
		}

		if (!IsSortedByAddress(entries))
		{
			Array.Sort(entries, static (left, right) => left.Address.CompareTo(right.Address));
		}

		ProcessId = processId;
		Width = width;
		Entries = entries;
		Modules = modules;
		Incomplete = incomplete;
		BytesRead = bytesRead;
		UnreadableBytes = unreadableBytes;
		_byBase = [.. modules.OrderBy(static module => module.BaseAddress)];
		_byValue = SortByValue(entries);
	}

	/// <summary>The process the map was captured from.</summary>
	internal int ProcessId
	{
		get;
	}

	/// <summary>The target pointer width, 4 or 8 bytes.</summary>
	internal int Width
	{
		get;
	}

	/// <summary>The captured pointers, sorted by address.</summary>
	internal PointerEntry[] Entries
	{
		get;
	}

	/// <summary>The modules of the captured process.</summary>
	internal PointerModule[] Modules
	{
		get;
	}

	/// <summary>Whether the capture skipped memory or hit a limit.</summary>
	internal bool Incomplete
	{
		get;
	}

	/// <summary>How many bytes were read.</summary>
	internal long BytesRead
	{
		get;
	}

	/// <summary>How many requested bytes could not be read.</summary>
	internal long UnreadableBytes
	{
		get;
	}

	/// <summary>
	///     Adds the nonzero aligned pointers of one read to a capture. The alignment is absolute, so a chunk that starts
	///     at an unaligned address still reads the same slots as its neighbours.
	/// </summary>
	/// <param name="entries">The capture's entries.</param>
	/// <param name="address">The address of the first byte.</param>
	/// <param name="bytes">The bytes read.</param>
	/// <param name="width">The pointer width, 4 or 8.</param>
	/// <param name="alignment">The slot alignment, 1, 2, 4 or 8.</param>
	/// <param name="maximumEntries">The capture's pointer limit.</param>
	/// <param name="filter">Keeps only values that point into memory, or <see langword="null" /> to keep every value.</param>
	internal static void CopyEntries(PointerEntryBuffer entries, ulong address, ReadOnlySpan<byte> bytes, int width,
		int alignment, int maximumEntries, PointerValueFilter? filter)
	{
		ArgumentNullException.ThrowIfNull(entries);
		int first = (int) (((ulong) alignment - (address % (ulong) alignment)) % (ulong) alignment);
		for (int index = first; index <= bytes.Length - width && entries.Count < maximumEntries; index += alignment)
		{
			ulong value = width == 8
				? BinaryPrimitives.ReadUInt64LittleEndian(bytes[index..])
				: BinaryPrimitives.ReadUInt32LittleEndian(bytes[index..]);
			if (value != 0 && (filter is null || filter.Contains(value)))
			{
				entries.Add(new PointerEntry(address + (ulong) index, value));
			}
		}
	}

	/// <summary>
	///     Searches the map depth first for paths that end at <see cref="PointerSearchOptions.Target" />. A path never
	///     visits the same pointer twice. Cancellation stops the search and returns what it found so far.
	/// </summary>
	/// <param name="options">The target and limits.</param>
	/// <param name="progress">Receives the visited nodes and found paths every <see cref="SearchReportInterval" /> nodes.</param>
	/// <param name="cancellationToken">Stops the search.</param>
	/// <returns>The paths found.</returns>
	internal PointerSearchResult Search(PointerSearchOptions options, Action<int, int>? progress,
		CancellationToken cancellationToken)
	{
		List<PointerPath> paths = [];
		List<long> reversedOffsets = [];
		HashSet<ulong> visited = [options.Target];
		ulong maximumOffset = (ulong) options.MaximumOffset;
		int nodes = 0;
		bool truncated = false;
		bool cancelled = false;
		Visit(options.Target);
		progress?.Invoke(nodes, paths.Count);
		return new PointerSearchResult([.. paths], truncated, nodes, cancelled);

		void Visit(ulong destination)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				cancelled = true;
				return;
			}

			ulong minimum = destination >= maximumOffset ? destination - maximumOffset : 0;
			ulong maximum = !options.AllowNegativeOffsets
				? destination
				: destination <= ulong.MaxValue - maximumOffset
					? destination + maximumOffset
					: ulong.MaxValue;
			for (int index = LowerBound(minimum); index < _byValue.Length; index++)
			{
				PointerEntry entry = Entries[_byValue[index]];
				if (entry.Value > maximum)
				{
					return;
				}

				if (nodes >= options.MaximumNodes || paths.Count >= options.MaximumResults)
				{
					truncated = true;
					return;
				}

				if (nodes % SearchReportInterval == 0)
				{
					if (cancellationToken.IsCancellationRequested)
					{
						cancelled = true;
						return;
					}

					progress?.Invoke(nodes, paths.Count);
				}

				nodes++;
				if (!visited.Add(entry.Address))
				{
					continue;
				}

				reversedOffsets.Add(unchecked((long) (destination - entry.Value)));
				PointerModule? module = FindModule(entry.Address);
				if (!options.StaticRootsOnly || module is not null)
				{
					long[] offsets = new long[reversedOffsets.Count];
					for (int offset = 0; offset < offsets.Length; offset++)
					{
						offsets[offset] = reversedOffsets[^(offset + 1)];
					}

					paths.Add(new PointerPath(entry.Address, module?.Name,
						module is null ? 0 : entry.Address - module.BaseAddress, offsets));
				}

				if (reversedOffsets.Count < options.MaximumDepth)
				{
					Visit(entry.Address);
				}

				reversedOffsets.RemoveAt(reversedOffsets.Count - 1);
				visited.Remove(entry.Address);
				if (truncated || cancelled)
				{
					return;
				}
			}
		}
	}

	/// <summary>
	///     Finds the pointers whose value lies in <c>[target - maximumOffset, target]</c>, nearest first, optionally only
	///     those held inside one module.
	/// </summary>
	/// <param name="target">The address the pointers lead to.</param>
	/// <param name="maximumOffset">The largest distance below the target.</param>
	/// <param name="module">The module that must hold the pointers, or <see langword="null" />.</param>
	/// <param name="limit">The most pointers copied into <paramref name="found" />.</param>
	/// <param name="found">Receives the pointers, nearest first, then by address.</param>
	/// <returns>How many pointers match, beyond the limit too.</returns>
	internal int FindReferences(ulong target, int maximumOffset, PointerModule? module, int limit,
		List<PointerEntry> found)
	{
		ArgumentNullException.ThrowIfNull(found);
		ulong minimum = target >= (ulong) maximumOffset ? target - (ulong) maximumOffset : 0;
		int count = 0;
		// Walk value runs from nearest to farthest. Each run is already sorted by holder address.
		int end = target == ulong.MaxValue ? _byValue.Length : LowerBound(target + 1);
		while (end > 0)
		{
			PointerEntry last = Entries[_byValue[end - 1]];
			if (last.Value < minimum)
			{
				break;
			}

			int start = LowerBound(last.Value);
			for (int index = start; index < end; index++)
			{
				PointerEntry entry = Entries[_byValue[index]];
				if (module is not null && !module.Contains(entry.Address))
				{
					continue;
				}

				count++;
				if (found.Count < limit)
				{
					found.Add(entry);
				}
			}

			end = start;
		}

		found.Sort(static (left, right) => left.Value != right.Value
			? right.Value.CompareTo(left.Value)
			: left.Address.CompareTo(right.Address));
		return count;
	}

	/// <summary>The module whose image holds an address, if any.</summary>
	/// <param name="address">The address.</param>
	/// <returns>The module, or <see langword="null" />.</returns>
	internal PointerModule? FindModule(ulong address)
	{
		int low = 0;
		int high = _byBase.Length;
		while (low < high)
		{
			int middle = low + ((high - low) / 2);
			if (_byBase[middle].BaseAddress <= address)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low > 0 && _byBase[low - 1].Contains(address) ? _byBase[low - 1] : null;
	}

	/// <summary>
	///     Follows a path through the snapshot: the root rebases onto this map's module of the same name, then every hop
	///     reads a captured pointer.
	/// </summary>
	/// <param name="path">The path.</param>
	/// <param name="destination">The final address when the method returns <see langword="true" />.</param>
	/// <returns><see langword="true" /> when every hop was captured.</returns>
	internal bool TryResolve(PointerPath path, out ulong destination)
	{
		ArgumentNullException.ThrowIfNull(path);
		destination = 0;
		if (!TryRebase(path, Modules, out ulong address))
		{
			return false;
		}

		foreach (long offset in path.Offsets)
		{
			if (!TryFindValue(address, out ulong value) || !TryAdd(value, offset, Width, out address))
			{
				return false;
			}
		}

		destination = address;
		return true;
	}

	/// <summary>
	///     Finds a path's root in a module list: a module root rebases onto the one module of the same name, compared
	///     without case; an absolute root stays absolute.
	/// </summary>
	/// <param name="path">The path.</param>
	/// <param name="modules">The modules of the process now.</param>
	/// <param name="address">The root address when the method returns <see langword="true" />.</param>
	/// <returns><see langword="false" /> when the module is missing, ambiguous or too small.</returns>
	internal static bool TryRebase(PointerPath path, IReadOnlyList<PointerModule> modules, out ulong address)
	{
		ArgumentNullException.ThrowIfNull(path);
		ArgumentNullException.ThrowIfNull(modules);
		address = path.BaseAddress;
		if (path.Module is null)
		{
			return true;
		}

		PointerModule[] matches =
		[
			.. modules.Where(module => string.Equals(module.Name, path.Module, StringComparison.OrdinalIgnoreCase))
		];
		if (matches.Length != 1 || path.ModuleOffset >= matches[0].Size ||
			matches[0].BaseAddress > ulong.MaxValue - path.ModuleOffset)
		{
			return false;
		}

		address = matches[0].BaseAddress + path.ModuleOffset;
		return true;
	}

	/// <summary>Adds a signed offset to a pointer, refusing a result outside the target's address width.</summary>
	/// <param name="pointer">The pointer.</param>
	/// <param name="offset">The offset.</param>
	/// <param name="width">The pointer width, 4 or 8.</param>
	/// <param name="result">The sum.</param>
	/// <returns><see langword="false" /> when the sum wraps or exceeds the width.</returns>
	internal static bool TryAdd(ulong pointer, long offset, int width, out ulong result)
	{
		if (offset >= 0)
		{
			result = pointer + (ulong) offset;
			if (result < pointer)
			{
				return false;
			}
		}
		else
		{
			ulong magnitude = unchecked((ulong) -(offset + 1)) + 1;
			result = pointer - magnitude;
			if (magnitude > pointer)
			{
				return false;
			}
		}

		return width == 8 || result <= uint.MaxValue;
	}

	private bool TryFindValue(ulong address, out ulong value)
	{
		int low = 0;
		int high = Entries.Length - 1;
		while (low <= high)
		{
			int middle = low + ((high - low) / 2);
			ulong current = Entries[middle].Address;
			if (current == address)
			{
				value = Entries[middle].Value;
				return true;
			}

			if (current < address)
			{
				low = middle + 1;
			}
			else
			{
				high = middle - 1;
			}
		}

		value = 0;
		return false;
	}

	private int LowerBound(ulong value)
	{
		int low = 0;
		int high = _byValue.Length;
		while (low < high)
		{
			int middle = low + ((high - low) / 2);
			if (Entries[_byValue[middle]].Value < value)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low;
	}

	private static bool IsSortedByAddress(PointerEntry[] entries)
	{
		for (int index = 1; index < entries.Length; index++)
		{
			if (entries[index - 1].Address > entries[index].Address)
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>The entry indices sorted by value, then by address, which is index order.</summary>
	private static int[] SortByValue(PointerEntry[] entries)
	{
		ulong[] keys = new ulong[entries.Length];
		int[] indices = new int[entries.Length];
		for (int index = 0; index < entries.Length; index++)
		{
			keys[index] = entries[index].Value;
			indices[index] = index;
		}

		Array.Sort(keys, indices);
		// The sort is not stable: restore address order inside each run of equal values.
		int run = 0;
		for (int index = 1; index <= keys.Length; index++)
		{
			if (index == keys.Length || keys[index] != keys[run])
			{
				if (index - run > 1)
				{
					Array.Sort(indices, run, index - run);
				}

				run = index;
			}
		}

		return indices;
	}
}
