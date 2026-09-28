using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>
///     One stored pointer map: its capture's progress while the job runs, then the map. The capture job writes it from
///     a thread-pool thread and tools read it; a short lock guards every field.
/// </summary>
internal sealed class PointerMapSlot
{
	private readonly Lock _lock = new();
	private long _attempted;
	private Func<PointerMap>? _build;
	private long _bytesRead;
	private bool _deleting;
	private string? _error;
	private bool _incomplete = true;
	private McpJob? _job;
	private PointerMap? _map;
	private long _planned;
	private int _pointers;
	private int? _processId;
	private int _reservedPointers;
	private PointerJobState _state = PointerJobState.Running;
	private long _unreadableBytes;
	private int _width;

	internal PointerMapSlot(string name, int reservedPointers)
	{
		Name = name;
		_reservedPointers = reservedPointers;
	}

	/// <summary>The map name.</summary>
	internal string Name
	{
		get;
	}

	/// <summary>The pointers the map holds or may still capture, counted against the store's budget.</summary>
	internal int ReservedPointers => Volatile.Read(ref _reservedPointers);

	/// <summary>The capture job, once started.</summary>
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

	/// <summary>Where the capture stands.</summary>
	internal PointerJobState State
	{
		get
		{
			lock (_lock)
			{
				return _state;
			}
		}
	}

	/// <summary>Whether a deletion arrived before this map's job could start.</summary>
	internal bool IsDeleting
	{
		get
		{
			lock (_lock)
			{
				return _deleting;
			}
		}
	}

	/// <summary>Records what the capture will read, before its job starts.</summary>
	/// <param name="processId">The captured process.</param>
	/// <param name="width">The pointer width.</param>
	/// <param name="plannedBytes">The bytes the capture plans to read.</param>
	internal void Prepare(int processId, int width, long plannedBytes)
	{
		lock (_lock)
		{
			_processId = processId;
			_width = width;
			_planned = plannedBytes;
		}
	}

	/// <summary>
	///     Records the capture job before its work starts.
	/// </summary>
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
	/// <returns>The running or registered job, or <see langword="null" /> while creation has not reached it.</returns>
	internal McpJob? BeginDelete()
	{
		lock (_lock)
		{
			_deleting = true;
			return _job;
		}
	}

	/// <summary>Records the capture's progress.</summary>
	/// <param name="attempted">The bytes requested so far.</param>
	/// <param name="bytesRead">The bytes read so far.</param>
	/// <param name="unreadableBytes">The requested bytes that could not be read so far.</param>
	/// <param name="pointers">The pointers captured so far.</param>
	internal void Report(long attempted, long bytesRead, long unreadableBytes, int pointers)
	{
		lock (_lock)
		{
			_attempted = attempted;
			_bytesRead = bytesRead;
			_unreadableBytes = unreadableBytes;
			_pointers = pointers;
		}
	}

	/// <summary>Stores the finished map and keeps only its pointers in the store's budget.</summary>
	/// <param name="map">The map.</param>
	internal void Complete(PointerMap map)
	{
		lock (_lock)
		{
			_map = map;
			_state = PointerJobState.Ready;
			_pointers = map.Entries.Length;
			_bytesRead = map.BytesRead;
			_unreadableBytes = map.UnreadableBytes;
			_incomplete = map.Incomplete;
			_attempted = _planned;
			Volatile.Write(ref _reservedPointers, map.Entries.Length);
		}
	}

	/// <summary>Publishes a fully parsed native map without creating a capture job.</summary>
	internal void LoadImportedMap(PointerMap map)
	{
		ArgumentNullException.ThrowIfNull(map);
		lock (_lock)
		{
			if (_deleting || _job is not null || _map is not null)
			{
				throw CheatEngineToolException.InvalidState("The pointer map slot cannot accept an import.",
					"Choose a new mapName and load the file again.");
			}

			_map = map;
			_state = PointerJobState.Ready;
			_processId = null;
			_width = map.Width;
			_pointers = map.Entries.Length;
			_bytesRead = 0;
			_unreadableBytes = 0;
			_incomplete = true;
			_planned = 0;
			_attempted = 0;
			Volatile.Write(ref _reservedPointers, map.Entries.Length);
		}
	}

	/// <summary>
	///     Ends a capture that was stopped before it finished: the pointers read so far stay usable and the map is built
	///     from them on first use, so a stop never waits for the build.
	/// </summary>
	/// <param name="state">Stopped or expired.</param>
	/// <param name="message">Why the capture ended early.</param>
	/// <param name="build">Builds the partial map.</param>
	/// <param name="pointers">The pointers captured.</param>
	internal void Interrupt(PointerJobState state, string message, Func<PointerMap> build, int pointers)
	{
		lock (_lock)
		{
			_state = state;
			_error = message;
			_build = build;
			_pointers = pointers;
			_incomplete = true;
			Volatile.Write(ref _reservedPointers, pointers);
		}
	}

	/// <summary>Ends a capture without a usable map.</summary>
	/// <param name="state">Failed, target changed or cancelled.</param>
	/// <param name="message">Why.</param>
	internal void Fail(PointerJobState state, string message)
	{
		lock (_lock)
		{
			_state = state;
			_error = message;
			_pointers = 0;
			_incomplete = true;
			Volatile.Write(ref _reservedPointers, 0);
		}
	}

	/// <summary>The map, once it is usable: ready, or stopped or expired with the pointers read so far.</summary>
	/// <returns>The map.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_state</c> while the capture runs or when it failed.</exception>
	internal PointerMap GetUsableMap()
	{
		lock (_lock)
		{
			if (_map is null && _build is not null)
			{
				_map = _build();
				_build = null;
			}

			if (_map is not null)
			{
				return _map;
			}

			throw _state is PointerJobState.Running
				? CheatEngineToolException.InvalidState($"The pointer map {Name} is still being captured.",
					"Poll pointer_list_maps until its state is ready.")
				: CheatEngineToolException.InvalidState($"The pointer map {Name} holds no pointers: {_error}",
					"Delete it with pointer_delete_map and capture a new map.");
		}
	}

	/// <summary>Describes the map and its capture.</summary>
	/// <returns>The description.</returns>
	internal PointerMapInfo Describe()
	{
		lock (_lock)
		{
			int progress = _state is PointerJobState.Ready || _planned <= 0
				? 100
				: (int) Math.Clamp(_attempted * 100 / _planned, 0, 100);
			return new PointerMapInfo(Name, _job?.Id ?? string.Empty, _state, _processId, _width, _pointers,
				_bytesRead, _unreadableBytes, _incomplete, progress, _error,
				_processId is null ? PointerCaptureCompleteness.Unknown : _incomplete
					? PointerCaptureCompleteness.Incomplete : PointerCaptureCompleteness.Complete);
		}
	}
}
