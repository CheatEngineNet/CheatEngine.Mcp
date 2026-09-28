using System.Globalization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>
///     The activation's memory snapshots. They are managed data, not target resources: they hold no Cheat Engine
///     object, never block a target switch and survive one, so two snapshots can still be compared afterwards; a
///     comparison with live memory is refused once Cheat Engine selected another target. The activation's end discards
///     them.
/// </summary>
/// <remarks>
///     The store is bounded: at most <see cref="MaximumSnapshots" /> snapshots holding <see cref="MaximumTotalBytes" />
///     bytes in total. A name and its size are reserved before the first read, so the caps hold under concurrent calls
///     without a lock across a dispatch; the lock guards the list only.
/// </remarks>
public sealed class MemorySnapshotStore : IDisposable
{
	/// <summary>The most snapshots retained at once.</summary>
	internal const int MaximumSnapshots = 16;

	/// <summary>The largest single snapshot, 16 MiB.</summary>
	internal const int MaximumSnapshotBytes = 16 * 1024 * 1024;

	/// <summary>The most bytes all snapshots hold together, 64 MiB.</summary>
	internal const long MaximumTotalBytes = 64L * 1024 * 1024;

	/// <summary>The longest snapshot name.</summary>
	internal const int MaximumNameLength = 64;

	private readonly List<Entry> _entries = [];
	private readonly Lock _lock = new();
	private bool _disposed;

	/// <summary>The bytes the snapshots hold, including snapshots still being taken.</summary>
	internal long TotalBytes
	{
		get
		{
			lock (_lock)
			{
				return _entries.Sum(static entry => (long) entry.Size);
			}
		}
	}

	/// <summary>Drops every snapshot as the activation ends.</summary>
	public void Dispose()
	{
		lock (_lock)
		{
			_disposed = true;
			_entries.Clear();
		}
	}

	/// <summary>Checks a snapshot name before any Cheat Engine call.</summary>
	/// <param name="name">The caller's name.</param>
	/// <param name="parameter">The parameter that carried it.</param>
	/// <returns>The name.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_argument</c>.</exception>
	internal static string RequireName(string? name, string parameter)
	{
		if (string.IsNullOrEmpty(name) || name.Length > MaximumNameLength ||
			!name.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-'))
		{
			throw CheatEngineToolException.InvalidArgument(parameter, string.Create(CultureInfo.InvariantCulture,
				$"must be 1 to {MaximumNameLength} characters of A-Z, a-z, 0-9, '_', '.' or '-'."));
		}

		return name;
	}

	/// <summary>Reserves a new name and its size before the snapshot's first read.</summary>
	/// <param name="name">A checked name.</param>
	/// <param name="size">A checked size.</param>
	/// <exception cref="CheatEngineToolException">
	///     <c>invalid_state</c> for a taken name, <c>limit_exceeded</c> when full.
	/// </exception>
	internal void Reserve(string name, int size)
	{
		lock (_lock)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_entries.Exists(entry => string.Equals(entry.Name, name, StringComparison.Ordinal)))
			{
				throw CheatEngineToolException.InvalidState($"A memory snapshot named {name} already exists.",
					"Delete it with memory_delete_snapshot, or choose another name.");
			}

			if (_entries.Count >= MaximumSnapshots)
			{
				throw CheatEngineToolException.LimitExceeded("name", string.Create(CultureInfo.InvariantCulture,
					$"at most {MaximumSnapshots} memory snapshots are retained; delete one with memory_delete_snapshot first."));
			}

			long total = _entries.Sum(static entry => (long) entry.Size);
			if (total + size > MaximumTotalBytes)
			{
				throw CheatEngineToolException.LimitExceeded("size", string.Create(CultureInfo.InvariantCulture,
					$"only {MaximumTotalBytes - total} of {MaximumTotalBytes} bytes remain for memory snapshots; delete a snapshot or lower size."));
			}

			_entries.Add(new Entry(name, size));
		}
	}

	/// <summary>Publishes the snapshot of a reserved name; after the activation ended it is dropped.</summary>
	/// <param name="snapshot">The snapshot, whose size is the reserved one.</param>
	internal void Commit(MemorySnapshot snapshot)
	{
		lock (_lock)
		{
			if (Find(snapshot.Name) is { } entry)
			{
				entry.Snapshot = snapshot;
			}
		}
	}

	/// <summary>Drops a reservation whose snapshot failed.</summary>
	/// <param name="name">The reserved name.</param>
	internal void Cancel(string name)
	{
		lock (_lock)
		{
			if (Find(name) is { Snapshot: null } entry)
			{
				_entries.Remove(entry);
			}
		}
	}

	/// <summary>Finds a snapshot by name.</summary>
	/// <param name="name">A checked name.</param>
	/// <param name="parameter">The parameter that carried it.</param>
	/// <returns>The snapshot.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>not_found</c>, or <c>busy</c> while it is still being taken.
	/// </exception>
	internal MemorySnapshot Get(string name, string parameter)
	{
		lock (_lock)
		{
			return Published(name, parameter);
		}
	}

	/// <summary>The published snapshots, oldest first.</summary>
	/// <returns>A copy of the list.</returns>
	internal MemorySnapshot[] List()
	{
		lock (_lock)
		{
			return
			[
				.. _entries.Where(static entry => entry.Snapshot is not null)
					.Select(static entry => entry.Snapshot!)
			];
		}
	}

	/// <summary>Drops a published snapshot and releases its bytes.</summary>
	/// <param name="name">A checked name.</param>
	/// <returns>The dropped snapshot.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>not_found</c>, or <c>busy</c> while it is still being taken.
	/// </exception>
	internal MemorySnapshot Remove(string name)
	{
		lock (_lock)
		{
			MemorySnapshot snapshot = Published(name, "name");
			_entries.RemoveAll(entry => ReferenceEquals(entry.Snapshot, snapshot));
			return snapshot;
		}
	}

	private MemorySnapshot Published(string name, string parameter)
	{
		Entry entry = Find(name) ?? throw CheatEngineToolException.NotFound(
			$"{parameter}: no memory snapshot is named {name}.", "List the snapshots with memory_list_snapshots.");
		return entry.Snapshot ?? throw CheatEngineToolException.Busy(
			$"The memory snapshot {name} is still being taken.",
			"Repeat the call once memory_create_snapshot returns.");
	}

	private Entry? Find(string name)
	{
		return _entries.Find(entry => string.Equals(entry.Name, name, StringComparison.Ordinal));
	}

	/// <summary>A reserved name; its snapshot is set once taken.</summary>
	/// <param name="name">The name.</param>
	/// <param name="size">The reserved size.</param>
	private sealed class Entry(string name, int size)
	{
		internal string Name
		{
			get;
		} = name;

		internal int Size
		{
			get;
		} = size;

		internal MemorySnapshot? Snapshot
		{
			get;
			set;
		}
	}
}
