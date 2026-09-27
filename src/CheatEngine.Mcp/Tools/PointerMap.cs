using System.Buffers.Binary;
using System.Diagnostics;

using CheatEngine.Client.Processes;

namespace CheatEngine.Mcp.Tools;

/// <summary>A bounded, managed memory snapshot. It owns no target memory or CE handles.</summary>
internal sealed class PointerMap(ProcessSnapshot? process, int width, PointerEntry[] entries, PointerModule[] modules,
	bool incomplete, ulong bytesRead, ulong unreadableBytes, Dictionary<ulong, PointerStaticRoot>? staticRoots = null,
	PointerStaticRange? staticRange = null)
{
	internal ProcessSnapshot? Process { get; } = process;
	internal int Width { get; } = width;
	internal PointerEntry[] Entries { get; } = entries;
	internal PointerModule[] Modules { get; } = modules;
	internal bool Incomplete { get; } = incomplete;
	internal ulong BytesRead { get; } = bytesRead;
	internal ulong UnreadableBytes { get; } = unreadableBytes;
	// Native maps classify individual roots; module sizes are not stored in .scandata.
	internal Dictionary<ulong, PointerStaticRoot>? StaticRoots { get; } = staticRoots;
	internal PointerStaticRange? StaticRange { get; } = staticRange;
	private readonly PointerEntry[] _byValue = entries.OrderBy(entry => entry.Value).ThenBy(entry => entry.Address).ToArray();
	internal ReadOnlySpan<PointerEntry> EntriesByValue => _byValue;
	private readonly (PointerModule Module, int Index)[] _byBase = modules.Select((module, index) => (Module: module, Index: index))
		.OrderBy(entry => entry.Module.BaseAddress).ToArray();

	internal static void CopyEntries(List<PointerEntry> entries, ulong address, ReadOnlySpan<byte> bytes,
		int width, int alignment, int maximumEntries)
	{
		int first = (int) (((ulong) alignment - (address % (ulong) alignment)) % (ulong) alignment);
		for (int index = first; index <= bytes.Length - width && entries.Count < maximumEntries; index += alignment)
		{
			ulong value = width == 8 ? BinaryPrimitives.ReadUInt64LittleEndian(bytes[index..]) : BinaryPrimitives.ReadUInt32LittleEndian(bytes[index..]);
			if (value != 0)
			{
				entries.Add(new PointerEntry(address + (ulong) index, value));
			}
		}
	}

	internal PointerSearchResult Search(ulong target, int maximumDepth, int maximumOffset, bool moduleRootsOnly,
		int maximumResults, int maximumNodes, CancellationToken cancellationToken)
	{
		List<PointerPath> paths = [];
		List<long> reversedOffsets = [];
		HashSet<ulong> visited = [target];
		int nodes = 0;
		bool truncated = false;
		Stopwatch clock = Stopwatch.StartNew();
		Visit(target);
		return new PointerSearchResult(paths.ToArray(), truncated, nodes);

		void Visit(ulong destination)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ulong minimum = destination >= (ulong) maximumOffset ? destination - (ulong) maximumOffset : 0;
			int index = LowerBound(minimum);
			while (index < _byValue.Length && _byValue[index].Value <= destination)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (nodes >= maximumNodes || paths.Count >= maximumResults || clock.Elapsed > TimeSpan.FromSeconds(1))
				{
					truncated = true;
					return;
				}
				nodes++;
				PointerEntry entry = _byValue[index++];
				if (!visited.Add(entry.Address))
				{
					continue;
				}
				reversedOffsets.Add((long) (destination - entry.Value));
				PointerStaticRoot? root = GetStaticRoot(entry.Address);
				PointerModule? module = root is { ModuleIndex: >= 0 } ? Modules[root.Value.ModuleIndex] : null;
				if (!moduleRootsOnly || module is not null)
				{
					paths.Add(new PointerPath(entry.Address, module?.Name,
						module is null ? 0 : root!.Value.Offset, reversedOffsets.AsEnumerable().Reverse().ToArray()));
				}
				if (reversedOffsets.Count < maximumDepth)
				{
					Visit(entry.Address);
				}
				reversedOffsets.RemoveAt(reversedOffsets.Count - 1);
				visited.Remove(entry.Address);
				if (truncated)
				{
					return;
				}
			}
		}
	}

	internal PointerStaticRoot? GetStaticRoot(ulong address)
	{
		if (StaticRoots is not null)
		{
			return StaticRoots.TryGetValue(address, out PointerStaticRoot root) ? root : null;
		}
		int low = 0;
		int high = _byBase.Length;
		while (low < high)
		{
			int middle = low + ((high - low) / 2);
			if (_byBase[middle].Module.BaseAddress <= address)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}
		if (low == 0 || !_byBase[low - 1].Module.Contains(address))
		{
			return null;
		}
		(PointerModule module, int index) = _byBase[low - 1];
		return new PointerStaticRoot(index, address - module.BaseAddress);
	}

	internal bool TryResolve(PointerPath path, out ulong destination)
	{
		destination = 0;
		if (!TryRebase(path, Modules, out ulong address))
		{
			return false;
		}
		foreach (long offset in path.Offsets)
		{
			int index = Array.BinarySearch(Entries, new PointerEntry(address, 0), PointerAddressComparer.Instance);
			if (index < 0 || Entries[index].Value > ulong.MaxValue - (ulong) offset)
			{
				return false;
			}
			address = Entries[index].Value + (ulong) offset;
			if (Width == 4 && address > uint.MaxValue)
			{
				return false;
			}
		}
		destination = address;
		return true;
	}

	internal static bool TryRebase(PointerPath path, PointerModule[] modules, out ulong address)
	{
		address = path.BaseAddress;
		if (path.Module is null)
		{
			return true;
		}
		PointerModule[] matches = modules.Where(module => string.Equals(module.Name, path.Module, StringComparison.OrdinalIgnoreCase)).ToArray();
		if (matches.Length != 1 || path.ModuleOffset >= matches[0].Size || matches[0].BaseAddress > ulong.MaxValue - path.ModuleOffset)
		{
			return false;
		}
		address = matches[0].BaseAddress + path.ModuleOffset;
		return true;
	}

	private int LowerBound(ulong value)
	{
		int low = 0;
		int high = _byValue.Length;
		while (low < high)
		{
			int middle = low + ((high - low) / 2);
			if (_byValue[middle].Value < value)
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

	private sealed class PointerAddressComparer : IComparer<PointerEntry>
	{
		internal static readonly PointerAddressComparer Instance = new();
		public int Compare(PointerEntry left, PointerEntry right) => left.Address.CompareTo(right.Address);
	}
}

internal readonly record struct PointerEntry(ulong Address, ulong Value);
internal readonly record struct PointerStaticRoot(int ModuleIndex, ulong Offset);
internal readonly record struct PointerStaticRange(ulong Start, ulong End);
internal sealed record PointerModule(string Name, ulong BaseAddress, ulong Size)
{
	internal bool Contains(ulong address) => address >= BaseAddress && address - BaseAddress < Size;
}
internal sealed record PointerPath(ulong BaseAddress, string? Module, ulong ModuleOffset, long[] Offsets)
{
	internal string Verification { get; init; } = "snapshotMatch";
}
internal sealed record PointerSearchResult(PointerPath[] Paths, bool Truncated, int VisitedNodes);
