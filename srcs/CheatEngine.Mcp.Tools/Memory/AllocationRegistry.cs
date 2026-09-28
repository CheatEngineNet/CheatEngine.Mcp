using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using CheatEngine.Client.Allocations;
using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>
///     The activation's named allocations. A name is reserved before its dispatch, so the count and total caps hold under
///     concurrent calls without a lock across a dispatch, and it is removed when its lease is released here or through
///     <c>runtime_release_resources</c>.
/// </summary>
public sealed class AllocationRegistry
{
	/// <summary>The most allocations an activation owns.</summary>
	internal const int MaximumAllocations = 128;

	/// <summary>The largest single allocation.</summary>
	internal const long MaximumAllocationBytes = 16 * 1024 * 1024;

	/// <summary>The largest total of an activation's allocations.</summary>
	internal const long MaximumTotalBytes = 64 * 1024 * 1024;

	/// <summary>The longest allocation name.</summary>
	internal const int MaximumNameLength = 256;

	private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
	private readonly Lock _lock = new();

	/// <summary>How many names are allocated or being allocated.</summary>
	internal int Count
	{
		get
		{
			lock (_lock)
			{
				return _entries.Count;
			}
		}
	}

	/// <summary>Reserves a name and its size before the allocation's dispatch.</summary>
	/// <param name="name">A checked name.</param>
	/// <param name="size">A checked size.</param>
	/// <exception cref="CheatEngineToolException">The name is taken or a cap would be exceeded.</exception>
	internal void Reserve(string name, long size)
	{
		lock (_lock)
		{
			if (_entries.ContainsKey(name))
			{
				throw CheatEngineToolException.InvalidArgument("name",
					$"'{name}' already names an allocation; free it with memory_free or choose another name.");
			}

			if (_entries.Count >= MaximumAllocations)
			{
				throw CheatEngineToolException.LimitExceeded("name",
					$"this activation already owns {MaximumAllocations} allocations; free one with memory_free first.");
			}

			long total = _entries.Values.Sum(static entry => entry.Size);
			if (total > MaximumTotalBytes - size)
			{
				throw CheatEngineToolException.LimitExceeded("size",
					$"the activation's allocations would total {(total + size).ToString(CultureInfo.InvariantCulture)} bytes; the limit is {MaximumTotalBytes}.");
			}

			_entries.Add(name, new Entry(size, null, null));
		}
	}

	/// <summary>Publishes the lease of a reserved name.</summary>
	/// <param name="name">The reserved name.</param>
	/// <param name="lease">The allocation's lease.</param>
	/// <param name="resource">The tracked resource of the lease.</param>
	internal void Commit(string name, ITargetMemoryLease lease, ITargetResource resource)
	{
		lock (_lock)
		{
			_entries[name] = new Entry(_entries[name].Size, lease, resource);
		}
	}

	/// <summary>Drops a reservation whose allocation failed.</summary>
	/// <param name="name">The reserved name.</param>
	internal void Cancel(string name)
	{
		lock (_lock)
		{
			if (_entries.TryGetValue(name, out Entry? entry) && entry.Lease is null)
			{
				_entries.Remove(name);
			}
		}
	}

	/// <summary>Finds a published allocation.</summary>
	/// <param name="name">The name.</param>
	/// <param name="lease">The allocation's lease.</param>
	/// <param name="resource">The tracked resource of the lease.</param>
	/// <returns><see langword="true" /> for a published allocation.</returns>
	/// <exception cref="CheatEngineToolException">The name is still being allocated.</exception>
	internal bool TryGet(string name, [NotNullWhen(true)] out ITargetMemoryLease? lease,
		[NotNullWhen(true)] out ITargetResource? resource)
	{
		lock (_lock)
		{
			if (!_entries.TryGetValue(name, out Entry? entry))
			{
				lease = null;
				resource = null;
				return false;
			}

			if (entry.Lease is null || entry.Resource is null)
			{
				throw CheatEngineToolException.Busy($"The allocation '{name}' is still being made.",
					"Repeat memory_free once memory_allocate returns.");
			}

			lease = entry.Lease;
			resource = entry.Resource;
			return true;
		}
	}

	/// <summary>Removes a name once its lease no longer holds memory, unless the name now holds another lease.</summary>
	/// <param name="name">The name.</param>
	/// <param name="lease">The released lease.</param>
	internal void Remove(string name, ITargetMemoryLease lease)
	{
		lock (_lock)
		{
			if (_entries.TryGetValue(name, out Entry? entry) && ReferenceEquals(entry.Lease, lease))
			{
				_entries.Remove(name);
			}
		}
	}

	private sealed record Entry(long Size, ITargetMemoryLease? Lease, ITargetResource? Resource);
}
