using System.Diagnostics;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>
///     One stored pointer scan: its search's progress while the job runs, then its paths. The search job writes it from
///     a thread-pool thread and tools read it; a short lock guards every field, and a rescan replaces the paths at once.
/// </summary>
internal sealed class PointerScanSlot
{
	private readonly Lock _lock = new();
	private readonly long _started = Stopwatch.GetTimestamp();
	private int _count;
	private bool _coverageIncomplete = true;
	private bool _deleting;
	private long? _elapsedMs;
	private string? _error;
	private bool _incomplete = true;
	private McpJob? _job;
	private PointerPath[] _paths = [];
	private int _rescanning;
	private int _reservedPaths;
	private PointerJobState _state = PointerJobState.Running;
	private bool _traversalLimited;
	private int _visitedNodes;

	internal PointerScanSlot(string name, string mapName, ulong target, int width, int reservedPaths)
	{
		Name = name;
		MapName = mapName;
		Target = target;
		Width = width;
		_reservedPaths = reservedPaths;
	}

	/// <summary>The scan name.</summary>
	internal string Name
	{
		get;
	}

	/// <summary>The searched map.</summary>
	internal string MapName
	{
		get;
	}

	/// <summary>The target address of the search.</summary>
	internal ulong Target
	{
		get;
	}

	/// <summary>The pointer width of the searched map.</summary>
	internal int Width
	{
		get;
	}

	/// <summary>The paths the scan holds or may still find, counted against the store's budget.</summary>
	internal int ReservedPaths => Volatile.Read(ref _reservedPaths);

	/// <summary>The search job, once started.</summary>
	internal McpJob? Job
	{
		get
		{
			lock (_lock)
			{
				return _job;
			}
		}
	}

	/// <summary>Records the search job before its work starts.</summary>
	/// <param name="job">The job.</param>
	/// <returns><see langword="false" /> when deletion won the race before the job was published.</returns>
	internal bool TryAttach(McpJob job)
	{
		ArgumentNullException.ThrowIfNull(job);
		lock (_lock)
		{
			if (_deleting)
			{
				return false;
			}

			_job = job;
			return true;
		}
	}

	/// <summary>Prevents any later job attachment and returns the job already attached, if any.</summary>
	/// <returns>The running or registered job, or <see langword="null" /> while search creation has not reached it.</returns>
	internal McpJob? BeginDelete()
	{
		lock (_lock)
		{
			_deleting = true;
			return _job;
		}
	}

	/// <summary>Records the search's progress.</summary>
	/// <param name="visitedNodes">The candidate pointers visited so far.</param>
	/// <param name="count">The paths found so far.</param>
	internal void Report(int visitedNodes, int count)
	{
		lock (_lock)
		{
			_visitedNodes = visitedNodes;
			_count = count;
		}
	}

	/// <summary>Stores the paths of a search that ended, finished or stopped early.</summary>
	/// <param name="state">Ready, stopped or expired.</param>
	/// <param name="result">What the search found.</param>
	/// <param name="mapIncomplete">Whether the searched map was incomplete.</param>
	/// <param name="message">Why the search ended early, if it did.</param>
	internal void End(PointerJobState state, PointerSearchResult result, bool mapIncomplete, string? message)
	{
		ArgumentNullException.ThrowIfNull(result);
		lock (_lock)
		{
			_state = state;
			_error = message;
			_paths = result.Paths;
			_count = result.Paths.Length;
			_visitedNodes = result.VisitedNodes;
			_traversalLimited = result.Truncated;
			_coverageIncomplete = result.Truncated || result.Cancelled || mapIncomplete;
			_incomplete = _coverageIncomplete;
			_elapsedMs = (long) Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
			Volatile.Write(ref _reservedPaths, result.Paths.Length);
		}
	}

	/// <summary>Ends a search without usable paths.</summary>
	/// <param name="state">Failed or cancelled.</param>
	/// <param name="message">Why.</param>
	internal void Fail(PointerJobState state, string message)
	{
		lock (_lock)
		{
			_state = state;
			_error = message;
			_paths = [];
			_count = 0;
			_incomplete = true;
			_coverageIncomplete = true;
			_elapsedMs = (long) Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
			Volatile.Write(ref _reservedPaths, 0);
		}
	}

	/// <summary>The stored paths, once the search ended with some.</summary>
	/// <param name="incomplete">Whether the scan may miss paths or keeps unverified ones.</param>
	/// <returns>The paths.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_state</c> while the search runs or when it failed.</exception>
	internal PointerPath[] GetUsablePaths(out bool incomplete)
	{
		lock (_lock)
		{
			return GetUsablePathsUnderLock(out incomplete);
		}
	}

	/// <summary>
	/// Copies a coherent snapshot for persistence. A snapshot cannot be taken while a rescan may replace its paths.
	/// </summary>
	/// <exception cref="CheatEngineToolException"><c>busy</c> while a rescan holds the scan.</exception>
	internal PointerScanSnapshot SnapshotForSave()
	{
		lock (_lock)
		{
			if (_rescanning != 0)
			{
				throw CheatEngineToolException.Busy($"The pointer scan {Name} is being rescanned.",
					"Repeat the save once pointer_rescan_paths completes.");
			}

			PointerPath[] paths = CopyPaths(GetUsablePathsUnderLock(out bool incomplete), unresolved: false);
			return new PointerScanSnapshot(MapName, Target, Width, paths, incomplete, _coverageIncomplete);
		}
	}

	/// <summary>
	/// Publishes a fully parsed import in one critical section. Imported verification is always unresolved, independent of
	/// the snapshot's in-memory value.
	/// </summary>
	/// <param name="snapshot">The complete snapshot to publish.</param>
	/// <exception cref="CheatEngineToolException"><c>busy</c> while a rescan holds the scan.</exception>
	internal void LoadImportedSnapshot(PointerScanSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		if (!string.Equals(snapshot.MapName, MapName, StringComparison.Ordinal) || snapshot.Target != Target ||
			snapshot.Width != Width)
		{
			throw new ArgumentException("The imported snapshot does not match this pointer scan's metadata.", nameof(snapshot));
		}

		if (snapshot.Paths is null || snapshot.Paths.Length > PointerScanFile.MaximumPaths)
		{
			throw new ArgumentException($"The imported scan must contain at most {PointerScanFile.MaximumPaths} paths.",
				nameof(snapshot));
		}

		PointerScanFile.Validate(snapshot);
		PointerPath[] imported = CopyPaths(snapshot.Paths, unresolved: true);
		lock (_lock)
		{
			if (_rescanning != 0)
			{
				throw CheatEngineToolException.Busy($"The pointer scan {Name} is being rescanned.",
					"Repeat the import once pointer_rescan_paths completes.");
			}

			if (_deleting)
			{
				throw CheatEngineToolException.InvalidState($"The pointer scan {Name} is being deleted.",
					"Choose another scanName and import again.");
			}

			if (_job is not null)
			{
				throw CheatEngineToolException.InvalidState($"The pointer scan {Name} is attached to a search job.",
					"Wait for the search to finish, or import under a new scanName.");
			}

			_paths = imported;
			_count = imported.Length;
			// Every imported path starts unresolved, which is itself an incomplete result until a rescan verifies it.
			_coverageIncomplete = snapshot.CoverageIncomplete ?? snapshot.Incomplete;
			_incomplete = snapshot.Incomplete || imported.Length != 0;
			_traversalLimited = false;
			_visitedNodes = 0;
			_error = null;
			_state = PointerJobState.Ready;
			_elapsedMs = 0;
			Volatile.Write(ref _reservedPaths, imported.Length);
		}
	}

	/// <summary>Replaces the paths after a rescan; the store's budget shrinks with them.</summary>
	/// <param name="paths">The remaining paths.</param>
	/// <param name="coverageIncomplete">Whether the source scan may have missed paths.</param>
	internal void Replace(PointerPath[] paths, bool coverageIncomplete)
	{
		ArgumentNullException.ThrowIfNull(paths);
		lock (_lock)
		{
			_paths = paths;
			_count = paths.Length;
			_coverageIncomplete = coverageIncomplete;
			_incomplete = coverageIncomplete || paths.Any(static path => path.Verification == PointerVerification.Unresolved);
			Volatile.Write(ref _reservedPaths, paths.Length);
		}
	}

	/// <summary>Claims the scan for one rescan at a time.</summary>
	/// <returns><see langword="false" /> when another rescan holds it.</returns>
	internal bool TryBeginRescan()
	{
		lock (_lock)
		{
			if (_rescanning != 0)
			{
				return false;
			}

			_rescanning = 1;
			return true;
		}
	}

	/// <summary>
	/// Claims the scan and obtains its paths under the same lock, preventing an import from being published between the
	/// caller's snapshot and its rescan claim.
	/// </summary>
	/// <param name="paths">The paths to rescan when this method returns <see langword="true" />.</param>
	/// <param name="incomplete">Whether the source scan may have missed paths, apart from unresolved imports.</param>
	/// <returns><see langword="false" /> when another rescan holds it.</returns>
	internal bool TryBeginRescan(out PointerPath[] paths, out bool incomplete)
	{
		lock (_lock)
		{
			paths = GetUsablePathsUnderLock(out _);
			incomplete = _coverageIncomplete;
			if (_rescanning != 0)
			{
				paths = [];
				incomplete = true;
				return false;
			}

			_rescanning = 1;
			return true;
		}
	}

	/// <summary>Releases the rescan claim.</summary>
	internal void EndRescan()
	{
		lock (_lock)
		{
			_rescanning = 0;
		}
	}

	/// <summary>Describes the scan and its search.</summary>
	/// <returns>The description.</returns>
	internal PointerScanInfo Describe()
	{
		lock (_lock)
		{
			long elapsed = _elapsedMs ?? (long) Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
			return new PointerScanInfo(Name, _job?.Id ?? string.Empty, _state, MapName, HexFormat.Address(Target),
				_count, _incomplete, _traversalLimited, _visitedNodes, elapsed, _error);
		}
	}

	private PointerPath[] GetUsablePathsUnderLock(out bool incomplete)
	{
		if (_state is PointerJobState.Running)
		{
			throw CheatEngineToolException.InvalidState($"The pointer scan {Name} is still searching.",
				"Poll pointer_list_scans until its state is ready.");
		}

		if (_state is not (PointerJobState.Ready or PointerJobState.Stopped or PointerJobState.Expired))
		{
			throw CheatEngineToolException.InvalidState($"The pointer scan {Name} holds no paths: {_error}",
				"Delete it with pointer_delete_scan and search again.");
		}

		incomplete = _incomplete;
		return _paths;
	}

	private static PointerPath[] CopyPaths(PointerPath[] paths, bool unresolved)
	{
		PointerPath[] copy = new PointerPath[paths.Length];
		for (int index = 0; index < paths.Length; index++)
		{
			PointerPath path = paths[index] ?? throw new ArgumentException("Pointer scan paths cannot contain null values.",
				nameof(paths));
			if (path.Offsets is null)
			{
				throw new ArgumentException("Pointer scan path offsets cannot be null.", nameof(paths));
			}

			copy[index] = path with
			{
				Offsets = [.. path.Offsets],
				Verification = unresolved ? PointerVerification.Unresolved : path.Verification
			};
		}

		return copy;
	}
}
