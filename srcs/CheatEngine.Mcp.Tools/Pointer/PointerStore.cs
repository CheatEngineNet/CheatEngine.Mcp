using System.Globalization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>
///     The activation's pointer maps and path scans. They live in managed memory, hold no Cheat Engine object and
///     survive a target switch, so paths found before a restart can be rescanned after it; the activation's end
///     discards them. Maps and scans are not target resources, but their running capture and search jobs are.
/// </summary>
/// <remarks>
///     The store is bounded: at most <see cref="MaximumMaps" /> maps holding <see cref="MaximumTotalPointers" /> pointers
///     in total (about 160 MiB), and <see cref="MaximumScans" /> scans holding <see cref="MaximumTotalPaths" /> paths.
///     A capture or search reserves its limit when it starts and keeps only what it found once it ends. The lock guards
///     the lists only; it is never held during a dispatch or a job's work.
/// </remarks>
public sealed class PointerStore : IDisposable
{
	/// <summary>The most maps retained at once.</summary>
	internal const int MaximumMaps = 4;

	/// <summary>The most pointers retained across maps, reserved by each capture's pointer limit.</summary>
	internal const int MaximumTotalPointers = 8 * 1024 * 1024;

	/// <summary>The most scans retained at once.</summary>
	internal const int MaximumScans = 16;

	/// <summary>The most paths retained across scans, reserved by each search's result limit.</summary>
	internal const int MaximumTotalPaths = 1_000_000;

	/// <summary>The longest map or scan name.</summary>
	internal const int MaximumNameLength = 128;

	private readonly Lock _lock = new();
	private readonly List<PointerMapSlot> _maps = [];
	private readonly List<PointerScanSlot> _scans = [];
	private bool _disposed;

	/// <summary>Drops every map and scan as the activation ends; running jobs end through the job registry.</summary>
	public void Dispose()
	{
		lock (_lock)
		{
			_disposed = true;
			_maps.Clear();
			_scans.Clear();
		}
	}

	/// <summary>Reserves a new map name and its pointer limit.</summary>
	/// <param name="name">The map name.</param>
	/// <param name="maximumPointers">The capture's pointer limit.</param>
	/// <returns>The new map, running.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_state</c> for a taken name, <c>limit_exceeded</c> when full.</exception>
	internal PointerMapSlot ReserveMap(string name, int maximumPointers)
	{
		lock (_lock)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_maps.Exists(slot => string.Equals(slot.Name, name, StringComparison.Ordinal)))
			{
				throw CheatEngineToolException.InvalidState($"A pointer map named {name} already exists.",
					"Delete it with pointer_delete_map, or choose another mapName.");
			}

			if (_maps.Count >= MaximumMaps)
			{
				throw CheatEngineToolException.LimitExceeded("mapName", string.Create(CultureInfo.InvariantCulture,
					$"at most {MaximumMaps} pointer maps are retained; delete one with pointer_delete_map first."));
			}

			long reserved = _maps.Sum(static slot => (long) slot.ReservedPointers);
			if (reserved + maximumPointers > MaximumTotalPointers)
			{
				throw CheatEngineToolException.LimitExceeded("maxPointers", string.Create(CultureInfo.InvariantCulture,
					$"only {MaximumTotalPointers - reserved} of {MaximumTotalPointers} pointers remain for all maps; delete a map or lower maxPointers."));
			}

			PointerMapSlot map = new(name, maximumPointers);
			_maps.Add(map);
			return map;
		}
	}

	/// <summary>Finds a map by name.</summary>
	/// <param name="name">The map name.</param>
	/// <returns>The map.</returns>
	/// <exception cref="CheatEngineToolException"><c>not_found</c>.</exception>
	internal PointerMapSlot GetMap(string name)
	{
		lock (_lock)
		{
			return _maps.Find(slot => string.Equals(slot.Name, name, StringComparison.Ordinal)) ??
				   throw CheatEngineToolException.NotFound($"No pointer map is named {name}.",
					   "List the maps with pointer_list_maps.");
		}
	}

	/// <summary>The maps, oldest first.</summary>
	/// <returns>A copy of the list.</returns>
	internal PointerMapSlot[] ListMaps()
	{
		lock (_lock)
		{
			return [.. _maps];
		}
	}

	/// <summary>Drops a map and releases its reservation.</summary>
	/// <param name="map">The map.</param>
	/// <returns><see langword="true" /> when it was stored.</returns>
	internal bool RemoveMap(PointerMapSlot map)
	{
		lock (_lock)
		{
			return _maps.Remove(map);
		}
	}

	/// <summary>Reserves a new scan name and its path limit.</summary>
	/// <param name="name">The scan name.</param>
	/// <param name="mapName">The searched map.</param>
	/// <param name="target">The target address.</param>
	/// <param name="width">The map's pointer width.</param>
	/// <param name="maximumResults">The search's result limit.</param>
	/// <returns>The new scan, running.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_state</c> for a taken name, <c>limit_exceeded</c> when full.</exception>
	internal PointerScanSlot ReserveScan(string name, string mapName, ulong target, int width, int maximumResults)
	{
		lock (_lock)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_scans.Exists(slot => string.Equals(slot.Name, name, StringComparison.Ordinal)))
			{
				throw CheatEngineToolException.InvalidState($"A pointer scan named {name} already exists.",
					"Delete it with pointer_delete_scan, or choose another scanName.");
			}

			if (_scans.Count >= MaximumScans)
			{
				throw CheatEngineToolException.LimitExceeded("scanName", string.Create(CultureInfo.InvariantCulture,
					$"at most {MaximumScans} pointer scans are retained; delete one with pointer_delete_scan first."));
			}

			long reserved = _scans.Sum(static slot => (long) slot.ReservedPaths);
			if (reserved + maximumResults > MaximumTotalPaths)
			{
				throw CheatEngineToolException.LimitExceeded("maxResults", string.Create(CultureInfo.InvariantCulture,
					$"only {MaximumTotalPaths - reserved} of {MaximumTotalPaths} paths remain for all scans; delete a scan or lower maxResults."));
			}

			PointerScanSlot scan = new(name, mapName, target, width, maximumResults);
			_scans.Add(scan);
			return scan;
		}
	}

	/// <summary>Finds a scan by name.</summary>
	/// <param name="name">The scan name.</param>
	/// <returns>The scan.</returns>
	/// <exception cref="CheatEngineToolException"><c>not_found</c>.</exception>
	internal PointerScanSlot GetScan(string name)
	{
		lock (_lock)
		{
			return _scans.Find(slot => string.Equals(slot.Name, name, StringComparison.Ordinal)) ??
				   throw CheatEngineToolException.NotFound($"No pointer scan is named {name}.",
					   "List the scans with pointer_list_scans.");
		}
	}

	/// <summary>The scans, oldest first.</summary>
	/// <returns>A copy of the list.</returns>
	internal PointerScanSlot[] ListScans()
	{
		lock (_lock)
		{
			return [.. _scans];
		}
	}

	/// <summary>Drops a scan and releases its reservation.</summary>
	/// <param name="scan">The scan.</param>
	/// <returns><see langword="true" /> when it was stored.</returns>
	internal bool RemoveScan(PointerScanSlot scan)
	{
		lock (_lock)
		{
			return _scans.Remove(scan);
		}
	}
}
