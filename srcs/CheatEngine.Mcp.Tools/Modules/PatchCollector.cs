namespace CheatEngine.Mcp.Tools.Modules;

/// <summary>One differing range found by <see cref="PatchCollector" />.</summary>
/// <param name="Rva">The relative virtual address of the first differing byte.</param>
/// <param name="Section">The section that holds the range.</param>
/// <param name="Length">The length of the range.</param>
/// <param name="FileBytes">The file's bytes, at most <see cref="PatchCollector.MaximumShownBytes" />.</param>
/// <param name="MemoryBytes">The bytes in memory, at most <see cref="PatchCollector.MaximumShownBytes" />.</param>
internal sealed record CollectedPatch(uint Rva, string Section, int Length, byte[] FileBytes, byte[] MemoryBytes);

/// <summary>
///     Turns a stream of compared bytes into differing ranges: a range starts at a differing byte, absorbs runs of at most
///     <see cref="MergeGap" /> equal bytes between differing ones, and ends at a longer equal run or at a break (an
///     unreadable gap or a section end). Chunks of one contiguous range may arrive in several calls.
/// </summary>
internal sealed class PatchCollector
{
	/// <summary>The most equal bytes merged between two differing runs.</summary>
	internal const int MergeGap = 3;

	/// <summary>The most bytes of each side kept per range.</summary>
	internal const int MaximumShownBytes = 64;

	private readonly int _limit;
	private readonly List<CollectedPatch> _patches = [];
	private Open? _open;

	/// <summary>Creates a collector that keeps at most <paramref name="limit" /> ranges.</summary>
	/// <param name="limit">The most ranges to keep.</param>
	internal PatchCollector(int limit)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
		_limit = limit;
	}

	/// <summary>Whether a range beyond the limit was found; the scan should stop.</summary>
	internal bool IsFull
	{
		get;
		private set;
	}

	/// <summary>The closed ranges, in address order.</summary>
	internal IReadOnlyList<CollectedPatch> Patches => _patches;

	/// <summary>Compares one contiguous run of bytes.</summary>
	/// <param name="rva">The relative virtual address of the first byte.</param>
	/// <param name="section">The section that holds the run.</param>
	/// <param name="file">The file's bytes, after relocation.</param>
	/// <param name="memory">The bytes in memory.</param>
	/// <param name="excluded">Addresses the loader writes, compared as equal; <see langword="null" /> for none.</param>
	/// <returns>The number of bytes inspected before the collector reached its range limit.</returns>
	internal int Compare(uint rva, string section, ReadOnlySpan<byte> file, ReadOnlySpan<byte> memory,
		Func<uint, bool>? excluded = null)
	{
		if (file.Length != memory.Length)
		{
			throw new ArgumentException("Both runs must have the same length.", nameof(memory));
		}

		if (_open is not null && _open.Next != rva)
		{
			Break();
		}

		int compared = 0;
		for (int index = 0; index < file.Length && !IsFull; index++)
		{
			compared++;
			uint address = rva + (uint) index;
			bool differs = file[index] != memory[index] && excluded?.Invoke(address) != true;
			if (_open is null)
			{
				if (differs)
				{
					Start(address, section, file[index], memory[index]);
				}

				continue;
			}

			_open.Next = address + 1;
			if (differs)
			{
				_open.Extend(file[index], memory[index]);
			}
			else if (!_open.Hold(file[index], memory[index]))
			{
				Break();
			}
		}

		return compared;
	}

	/// <summary>Closes the open range, if any, at its last differing byte.</summary>
	internal void Break()
	{
		if (_open is null)
		{
			return;
		}

		_patches.Add(new CollectedPatch(_open.Start, _open.Section, _open.Length, [.. _open.FileBytes],
			[.. _open.MemoryBytes]));
		_open = null;
	}

	private void Start(uint address, string section, byte file, byte memory)
	{
		if (_patches.Count >= _limit)
		{
			IsFull = true;
			return;
		}

		_open = new Open(address, section);
		_open.Extend(file, memory);
	}

	private sealed class Open(uint start, string section)
	{
		private readonly List<byte> _heldFile = [];
		private readonly List<byte> _heldMemory = [];

		internal uint Start => start;

		internal string Section => section;

		internal uint Next
		{
			get;
			set;
		} = start + 1;

		internal int Length
		{
			get;
			private set;
		}

		internal List<byte> FileBytes
		{
			get;
		} = [];

		internal List<byte> MemoryBytes
		{
			get;
		} = [];

		internal void Extend(byte file, byte memory)
		{
			// The equal bytes held since the last difference belong to the range after all.
			for (int index = 0; index < _heldFile.Count; index++)
			{
				Append(_heldFile[index], _heldMemory[index]);
			}

			_heldFile.Clear();
			_heldMemory.Clear();
			Append(file, memory);
		}

		internal bool Hold(byte file, byte memory)
		{
			if (_heldFile.Count == MergeGap)
			{
				return false;
			}

			_heldFile.Add(file);
			_heldMemory.Add(memory);
			return true;
		}

		private void Append(byte file, byte memory)
		{
			Length++;
			if (FileBytes.Count < MaximumShownBytes)
			{
				FileBytes.Add(file);
				MemoryBytes.Add(memory);
			}
		}
	}
}
