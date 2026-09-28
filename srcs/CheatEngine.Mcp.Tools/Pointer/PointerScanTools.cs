using System.Collections.Immutable;
using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>Searches pointer maps for paths to a target, then rescans, pages and deletes the stored paths.</summary>
[McpServerToolType]
public sealed class PointerScanTools
{
	/// <summary>The deepest path a search follows.</summary>
	internal const int MaximumDepth = 8;

	/// <summary>The largest offset a search accepts, 1 MiB.</summary>
	internal const int MaximumOffset = 1024 * 1024;

	/// <summary>The most paths one search keeps.</summary>
	internal const int MaximumResults = 100_000;

	/// <summary>The most candidate pointers one search visits.</summary>
	internal const int MaximumNodes = 10_000_000;

	/// <summary>The most paths one page returns.</summary>
	internal const int MaximumPage = 500;

	/// <summary>The paths one live rescan dispatch follows.</summary>
	internal const int RescanChunk = 256;

	private readonly ToolDispatch _dispatch;
	private readonly JobRegistry _jobs;
	private readonly PointerStore _store;
	private readonly TimeProvider _time;

	/// <summary>Creates the tool; nothing touches Cheat Engine until a call.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="jobs">The activation's jobs.</param>
	/// <param name="store">The activation's pointer maps and scans.</param>
	/// <param name="time">The clock of job TTLs.</param>
	public PointerScanTools(ToolDispatch dispatch, JobRegistry jobs, PointerStore store, TimeProvider time)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(jobs);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(time);
		_dispatch = dispatch;
		_jobs = jobs;
		_store = store;
		_time = time;
	}

	/// <summary>Starts a job that searches a pointer map for paths to a target.</summary>
	/// <param name="scanName">The new scan's name.</param>
	/// <param name="mapName">The map to search.</param>
	/// <param name="target">The address the paths must reach.</param>
	/// <param name="maxDepth">The most dereferences in a path.</param>
	/// <param name="maxOffset">The largest offset after a dereference.</param>
	/// <param name="allowNegativeOffsets">Whether offsets may be negative.</param>
	/// <param name="staticRootsOnly">Whether only module roots are kept.</param>
	/// <param name="maxResults">The most paths kept.</param>
	/// <param name="maxNodes">The most candidate pointers visited.</param>
	/// <param name="lifetimeSeconds">The job's TTL.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <returns>The new scan, running.</returns>
	[McpServerTool(Name = CheatEngineToolNames.PointerFindPaths, Title = "Find pointer paths", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Starts a job that searches a stored pointer map for paths from static roots to target, off Cheat Engine's " +
		"main thread; a symbolic target costs one dispatch to resolve. Poll pointer_list_scans until state is " +
		"ready, then page the paths with pointer_list_paths. Stop it with pointer_delete_scan or " +
		"runtime_stop_job(jobId): a stopped search keeps the paths found so far. The job ends at its TTL or a " +
		"plugin disable. Negative offsets and larger limits multiply the work; incomplete or traversalLimited " +
		"with no path is no proof that none exists.")]
	public PointerScanInfo FindPaths(
		[Description("The new scan's name, 1 to 128 characters.")]
		string scanName,
		[Description("The pointer map to search; see pointer_list_maps.")]
		string mapName,
		[Description("The address the paths must reach: hexadecimal, or a symbol resolved in the selected process.")]
		string target,
		[Description("The most dereferences in a path, 1 to 8.")]
		int maxDepth = 5,
		[Description("The largest offset after a dereference, 0 to 1048576 bytes.")]
		int maxOffset = 4096,
		[Description("Also follow pointers that point past the address they lead to (negative offsets).")]
		bool allowNegativeOffsets = false,
		[Description("Keep only paths whose root lies in a module image, so they survive a restart.")]
		bool staticRootsOnly = true,
		[Description("The most paths kept, 1 to 100000.")]
		int maxResults = 1000,
		[Description("The most candidate pointers visited, 1 to 10000000.")]
		int maxNodes = 1_000_000,
		[Description("The job's TTL in seconds, 1 to 300; 120 by default. The paths outlive the job.")]
		int? lifetimeSeconds = null,
		CancellationToken cancellationToken = default)
	{
		string name = PointerSupport.Name(scanName, "scanName");
		string source = PointerSupport.Name(mapName, "mapName");
		string targetText = PointerSupport.Expression(target, "target");
		PointerSupport.Range(maxDepth, 1, MaximumDepth, "maxDepth");
		PointerSupport.Range(maxOffset, 0, MaximumOffset, "maxOffset");
		PointerSupport.Range(maxResults, 1, MaximumResults, "maxResults");
		PointerSupport.Range(maxNodes, 1, MaximumNodes, "maxNodes");
		TimeSpan lifetime = _jobs.ResolveTimeToLive(lifetimeSeconds);
		PointerMap map = _store.GetMap(source).GetUsableMap();
		PointerJobs.EnsureCapacity(_jobs, PointerJobs.ScanKind);
		ulong address = PointerTargets.Resolve(_dispatch, CheatEngineToolNames.PointerFindPaths, targetText,
			map.Width, cancellationToken);
		PointerSearchOptions options = new(address, maxDepth, maxOffset, allowNegativeOffsets, staticRootsOnly,
			maxResults, maxNodes);
		PointerScanSlot scan = _store.ReserveScan(name, source, address, map.Width, maxResults);
		try
		{
			DateTimeOffset expires = _time.GetUtcNow() + lifetime;
			ICheatEngineClient client = _dispatch.Client;

			void Attach(ManagedJob<int> job)
			{
				if (!scan.TryAttach(job))
				{
					throw CheatEngineToolException.InvalidState(
						$"The pointer scan {scan.Name} was deleted while it was starting.",
						"Choose another scanName and search again.");
				}
			}

			ManagedJob<int> job = _jobs.StartManaged<int>(PointerJobs.ScanKind, lifetime, PointerJobs.BufferLimit,
				(writer, token) => PointerSearchJob.Run(client, map, options, scan, writer, expires, _time, token),
				Attach);
			return scan.Describe();
		}
		catch
		{
			_store.RemoveScan(scan);
			throw;
		}
	}

	/// <summary>Filters stored paths against a new map or live memory.</summary>
	/// <param name="scanName">The scan.</param>
	/// <param name="target">The target's new address.</param>
	/// <param name="mapName">A map to check against instead of live memory.</param>
	/// <param name="dropUnresolved">Whether unresolved paths are removed too.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <returns>What the rescan kept.</returns>
	[McpServerTool(Name = CheatEngineToolNames.PointerRescanPaths, Title = "Rescan pointer paths", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Keeps only the stored paths that still reach target, typically after a restart. With mapName it follows " +
		"them through that map without scanning; otherwise it reads them live, 256 paths per short dispatch, and " +
		"a changed process selection stops it with target_changed. Module roots rebase by module name; absolute " +
		"roots stay absolute. A path that cannot be followed is kept as unresolved unless dropUnresolved is set. " +
		"Any failure keeps the original paths.")]
	public PointerRescanResult RescanPaths(
		[Description("The scan whose paths are filtered.")]
		string scanName,
		[Description("The target's current address: hexadecimal, or a symbol resolved in the selected process.")]
		string target,
		[Description("A pointer map to check against instead of live memory.")]
		string? mapName = null,
		[Description("Also remove the paths that could not be followed.")]
		bool dropUnresolved = false,
		CancellationToken cancellationToken = default)
	{
		PointerScanSlot scan = _store.GetScan(PointerSupport.Name(scanName, "scanName"));
		string targetText = PointerSupport.Expression(target, "target");
		PointerMap? map = mapName is null
			? null
			: _store.GetMap(PointerSupport.Name(mapName, "mapName")).GetUsableMap();
		if (map is not null && map.Width != scan.Width)
		{
			throw CheatEngineToolException.InvalidState(
				$"The map holds {map.Width}-byte pointers, but the scan was found with {scan.Width}-byte pointers.",
				"Rescan against a map of the same process architecture.");
		}

		if (!scan.TryBeginRescan(out PointerPath[] paths, out bool incomplete))
		{
			throw CheatEngineToolException.Busy($"Another rescan of {scan.Name} is running.",
				"Repeat the call once it ends.");
		}

		try
		{
			PointerVerification match = map is null ? PointerVerification.LiveMatch : PointerVerification.SnapshotMatch;
			ulong address;
			ulong?[] destinations = map is null
				? ResolveLive(paths, scan.Width, targetText, cancellationToken, out address)
				: ResolveInMap(map, paths, targetText, cancellationToken, out address);
			List<PointerPath> kept = [];
			int verified = 0;
			int unresolved = 0;
			for (int index = 0; index < paths.Length; index++)
			{
				if (destinations[index] is not { } destination)
				{
					unresolved++;
					if (!dropUnresolved)
					{
						// A missing snapshot entry or an unreadable live hop is no proof that the path is wrong.
						kept.Add(paths[index] with { Verification = PointerVerification.Unresolved });
					}
				}
				else if (destination == address)
				{
					verified++;
					kept.Add(paths[index] with { Verification = match });
				}
			}

			bool coverageIncomplete = incomplete || map?.Incomplete == true || dropUnresolved && unresolved != 0;
			bool nowIncomplete = coverageIncomplete || unresolved != 0;
			scan.Replace([.. kept], coverageIncomplete);
			return new PointerRescanResult(scan.Name, kept.Count, verified, paths.Length - kept.Count, unresolved,
				nowIncomplete);
		}
		finally
		{
			scan.EndRescan();
		}
	}

	/// <summary>Pages the stored paths of a scan.</summary>
	/// <param name="scanName">The scan.</param>
	/// <param name="offset">The first path of the page.</param>
	/// <param name="limit">The page size.</param>
	/// <param name="sortBy">The order of the paths.</param>
	/// <param name="moduleContains">Keeps only roots whose module name contains this text.</param>
	/// <returns>The page.</returns>
	[McpServerTool(Name = CheatEngineToolNames.PointerListPaths, Title = "List pointer paths", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Pages the stored paths of a pointer scan without any Cheat Engine call. Offsets are signed hexadecimal in " +
		"dereference order (Cheat Engine's address list shows them reversed); expression is ready for other " +
		"tools. Prefer roots in the main module, fewer levels and small offsets.")]
	public PointerPathPage ListPaths(
		[Description("The scan whose paths are listed.")]
		string scanName,
		[Description("The zero-based first path of the page.")]
		int offset = 0,
		[Description("The most paths returned, 1 to 500.")]
		int limit = 100,
		[Description("The order: depth (fewest levels), offset_sum (smallest offsets) or module (by root module).")]
		PointerPathSort sortBy = PointerPathSort.Depth,
		[Description("Keep only paths whose root module name contains this text, ignoring case.")]
		string? moduleContains = null)
	{
		PointerScanSlot scan = _store.GetScan(PointerSupport.Name(scanName, "scanName"));
		string? filter = moduleContains is null ? null : PointerSupport.Name(moduleContains, "moduleContains");
		if (offset < 0)
		{
			throw CheatEngineToolException.InvalidArgument("offset", "must be zero or greater.");
		}

		PointerSupport.Range(limit, 1, MaximumPage, "limit");
		PointerPath[] paths = scan.GetUsablePaths(out bool incomplete);
		IEnumerable<PointerPath> selected = filter is null
			? paths
			: paths.Where(path => path.Module?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);
		PointerPath[] sorted = [.. Sort(selected, sortBy)];
		PageSlice<PointerPath> page = Paging.Slice(sorted, offset, limit, MaximumPage);
		return new PointerPathPage(scan.Name, page.Total, incomplete, [.. page.Items.Select(Describe)],
			page.NextOffset);
	}

	/// <summary>Lists the pointer scans and their searches.</summary>
	/// <returns>The scans.</returns>
	[McpServerTool(Name = CheatEngineToolNames.PointerListScans, Title = "List pointer scans", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Lists the pointer scans with their search progress and counts, oldest first, without any Cheat Engine " +
		"call. Poll it after pointer_find_paths: the paths are usable when state is ready, or stopped or expired " +
		"with what was found.")]
	public PointerScanList ListScans()
	{
		return new PointerScanList([.. _store.ListScans().Select(static scan => scan.Describe())]);
	}

	/// <summary>Deletes a pointer scan and stops its search.</summary>
	/// <param name="scanName">The scan.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <returns>What the deletion did.</returns>
	[McpServerTool(Name = CheatEngineToolNames.PointerDeleteScan, Title = "Delete a pointer scan", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Deletes a pointer scan and frees its paths, stopping its search job first when it still runs. An unknown " +
		"name fails with not_found.")]
	public PointerDeleteResult DeleteScan(
		[Description("The scan to delete.")] string scanName,
		CancellationToken cancellationToken = default)
	{
		PointerScanSlot scan = _store.GetScan(PointerSupport.Name(scanName, "scanName"));
		bool cancelled = PointerJobs.StopIfKnown(_jobs, scan.BeginDelete(), cancellationToken);
		_store.RemoveScan(scan);
		return new PointerDeleteResult(scan.Name, cancelled);
	}

	private ulong?[] ResolveInMap(PointerMap map, PointerPath[] paths, string target,
		CancellationToken cancellationToken, out ulong address)
	{
		address = PointerTargets.Resolve(_dispatch, CheatEngineToolNames.PointerRescanPaths, target, map.Width,
			cancellationToken);
		ulong?[] destinations = new ulong?[paths.Length];
		for (int index = 0; index < paths.Length; index++)
		{
			destinations[index] = map.TryResolve(paths[index], out ulong destination) ? destination : null;
		}

		return destinations;
	}

	private ulong?[] ResolveLive(PointerPath[] paths, int width, string target,
		CancellationToken cancellationToken, out ulong address)
	{
		(ProcessSnapshot process, PointerModule[] modules, ulong resolved) = _dispatch.Run(
			CheatEngineToolNames.PointerRescanPaths, token =>
			{
				ICheatEngineClient client = _dispatch.Client;
				ProcessSnapshot current = client.Processes.GetCurrentProcess(token);
				if (PointerSupport.Width(current) != width)
				{
					throw CheatEngineToolException.InvalidState(
						$"The selected process has {PointerSupport.Width(current)}-byte pointers, but the scan was found with {width}-byte pointers.",
						"Attach a process of the same architecture.");
				}

				ImmutableArray<MemoryRegionInfo> regions = client.Inspection.GetMemoryRegions(
					new InspectionCollectionRequest(PointerSupport.RegionLimit), token);
				return (current, PointerSupport.Modules(client, regions, token),
					PointerTargets.Check(PointerSupport.Resolve(client, target, token), width, "target"));
			}, cancellationToken);
		address = resolved;
		ulong?[] destinations = new ulong?[paths.Length];
		for (int first = 0; first < paths.Length; first += RescanChunk)
		{
			int start = first;
			int end = Math.Min(paths.Length, first + RescanChunk);
			_dispatch.Run(CheatEngineToolNames.PointerRescanPaths, token =>
			{
				ICheatEngineClient client = _dispatch.Client;
				if (client.Processes.GetCurrentProcess(token) != process)
				{
					throw PointerSupport.TargetChanged(
						"The selected process changed during the rescan; the original paths were kept.");
				}

				for (int index = start; index < end; index++)
				{
					destinations[index] = ResolveOne(client, paths[index], modules, token);
				}

				return true;
			}, cancellationToken);
		}

		return destinations;
	}

	private static ulong? ResolveOne(ICheatEngineClient client, PointerPath path, PointerModule[] modules,
		CancellationToken cancellationToken)
	{
		if (!PointerMap.TryRebase(path, modules, out ulong root))
		{
			return null;
		}

		if (client.Memory.TryResolvePointerChain(new PointerChainRequest(new Address(root), path.Offsets),
			    out Address destination, out CheatEngineFailure failure, cancellationToken))
		{
			return destination.ToUInt64();
		}

		// An unreadable hop or a missing module is no proof; any other failure stops the rescan.
		return failure.Kind is CheatEngineFailureKind.MemoryReadFailed or CheatEngineFailureKind.NotFound
			? null
			: throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
	}

	private static IEnumerable<PointerPath> Sort(IEnumerable<PointerPath> paths, PointerPathSort sortBy)
	{
		return sortBy switch
		{
			PointerPathSort.OffsetSum => paths.OrderBy(static path => path.OffsetSum)
				.ThenBy(static path => path.Offsets.Length)
				.ThenBy(static path => path.Module ?? "￿", StringComparer.OrdinalIgnoreCase)
				.ThenBy(static path => path.Module is null ? path.BaseAddress : path.ModuleOffset),
			PointerPathSort.Module => paths.OrderBy(static path => path.Module is null)
				.ThenBy(static path => path.Module, StringComparer.OrdinalIgnoreCase)
				.ThenBy(static path => path.Module is null ? path.BaseAddress : path.ModuleOffset)
				.ThenBy(static path => path.Offsets.Length)
				.ThenBy(static path => path.OffsetSum),
			_ => paths.OrderBy(static path => path.Offsets.Length)
				.ThenBy(static path => path.OffsetSum)
				.ThenBy(static path => path.Module ?? "￿", StringComparer.OrdinalIgnoreCase)
				.ThenBy(static path => path.Module is null ? path.BaseAddress : path.ModuleOffset)
		};
	}

	private static PointerPathItem Describe(PointerPath path)
	{
		return new PointerPathItem(PointerSupport.ChainExpression(PointerSupport.Root(path), path.Offsets),
			HexFormat.Address(path.BaseAddress), [.. path.Offsets.Select(HexFormat.Offset)], path.Verification,
			path.Module, path.Module is null ? null : HexFormat.Address(path.ModuleOffset));
	}
}
