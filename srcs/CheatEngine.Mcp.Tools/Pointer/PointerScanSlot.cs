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
			_incomplete = result.Truncated || result.Cancelled || mapIncomplete;
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
	}

	/// <summary>Replaces the paths after a rescan; the store's budget shrinks with them.</summary>
	/// <param name="paths">The remaining paths.</param>
	/// <param name="incomplete">Whether the scan may miss paths or keeps unverified ones.</param>
	internal void Replace(PointerPath[] paths, bool incomplete)
	{
		ArgumentNullException.ThrowIfNull(paths);
		lock (_lock)
		{
			_paths = paths;
			_count = paths.Length;
			_incomplete = incomplete;
			Volatile.Write(ref _reservedPaths, paths.Length);
		}
	}

	/// <summary>Claims the scan for one rescan at a time.</summary>
	/// <returns><see langword="false" /> when another rescan holds it.</returns>
	internal bool TryBeginRescan()
	{
		return Interlocked.CompareExchange(ref _rescanning, 1, 0) == 0;
	}

	/// <summary>Releases the rescan claim.</summary>
	internal void EndRescan()
	{
		Volatile.Write(ref _rescanning, 0);
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
}
