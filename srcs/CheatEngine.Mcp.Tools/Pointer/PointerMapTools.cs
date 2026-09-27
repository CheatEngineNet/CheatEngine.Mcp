using System.ComponentModel;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;

using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>Captures, lists and deletes pointer maps.</summary>
[McpServerToolType]
public sealed class PointerMapTools
{
	/// <summary>The largest byte limit of one capture, 512 MiB.</summary>
	internal const int MaximumBytes = 512 * 1024 * 1024;

	/// <summary>The default byte limit of one capture, 64 MiB.</summary>
	internal const int DefaultBytes = 64 * 1024 * 1024;

	/// <summary>The largest pointer limit of one capture.</summary>
	internal const int MaximumPointers = 4 * 1024 * 1024;

	/// <summary>The default pointer limit of one capture.</summary>
	internal const int DefaultPointers = 1024 * 1024;

	private readonly TimeSpan _slice;
	private readonly ToolDispatch _dispatch;
	private readonly JobRegistry _jobs;
	private readonly PointerStore _store;
	private readonly TimeProvider _time;

	/// <summary>Creates the tool; nothing touches Cheat Engine until a call.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="jobs">The activation's jobs.</param>
	/// <param name="store">The activation's pointer maps.</param>
	/// <param name="options">The execution limits: half the dispatch budget bounds one capture read.</param>
	/// <param name="time">The clock of job TTLs.</param>
	public PointerMapTools(ToolDispatch dispatch, JobRegistry jobs, PointerStore store,
		IOptions<McpExecutionOptions> options, TimeProvider time)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(jobs);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(time);
		_dispatch = dispatch;
		_jobs = jobs;
		_store = store;
		_time = time;
		_slice = TimeSpan.FromMilliseconds(Math.Max(1, options.Value.DispatchBudgetMilliseconds / 2));
	}

	/// <summary>Starts a job that captures a pointer map.</summary>
	/// <param name="mapName">The new map's name.</param>
	/// <param name="startAddress">The first address to capture.</param>
	/// <param name="endAddress">The last address to capture.</param>
	/// <param name="writableOnly">Whether only writable memory is captured.</param>
	/// <param name="alignment">The slot alignment.</param>
	/// <param name="maxBytes">The byte limit.</param>
	/// <param name="maxPointers">The pointer limit.</param>
	/// <param name="lifetimeSeconds">The job's TTL.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <returns>The new map, running.</returns>
	[McpServerTool(Name = CheatEngineToolNames.PointerCreateMap, Title = "Create a pointer map", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Starts a job that captures a pointer map: every aligned pointer-sized value that points into committed " +
		"memory, read in short dispatches that recheck the selected process. Poll pointer_list_maps until state is " +
		"ready; a changed process discards the map (target_changed). Stop it with pointer_delete_map or " +
		"runtime_stop_job(jobId): a stopped map keeps what it read. The job ends at its TTL or a plugin disable; " +
		"the map lives in this activation's memory until deleted, at most 4 maps and 8388608 pointers.")]
	public PointerMapInfo CreateMap(
		[Description("The new map's name, 1 to 128 characters.")]
		string mapName,
		[Description("The first address to capture, an address or expression; requires endAddress.")]
		string? startAddress = null,
		[Description("The last address to capture, inclusive; requires startAddress.")]
		string? endAddress = null,
		[Description("Capture only writable memory, where static pointers live.")]
		bool writableOnly = true,
		[Description("The alignment of captured pointers: 1, 2, 4 or 8. 4 also finds 4-aligned pointers on x64.")]
		int alignment = 4,
		[Description("The most bytes read, 1 to 536870912 (512 MiB).")]
		int maxBytes = DefaultBytes,
		[Description("The most pointers kept, 1 to 4194304.")]
		int maxPointers = DefaultPointers,
		[Description("The job's TTL in seconds, 1 to 300; 120 by default. The map outlives its job.")]
		int? lifetimeSeconds = null,
		CancellationToken cancellationToken = default)
	{
		string name = PointerSupport.Name(mapName, "mapName");
		if ((startAddress is null) != (endAddress is null))
		{
			throw CheatEngineToolException.InvalidArgument(startAddress is null ? "startAddress" : "endAddress",
				"startAddress and endAddress must be given together.");
		}

		string? first = startAddress is null ? null : PointerSupport.Expression(startAddress, "startAddress");
		string? last = endAddress is null ? null : PointerSupport.Expression(endAddress, "endAddress");
		if (alignment is not (1 or 2 or 4 or 8))
		{
			throw CheatEngineToolException.InvalidArgument("alignment", "must be 1, 2, 4 or 8.");
		}

		PointerSupport.Range(maxBytes, 1, MaximumBytes, "maxBytes");
		PointerSupport.Range(maxPointers, 1, MaximumPointers, "maxPointers");
		TimeSpan lifetime = _jobs.ResolveTimeToLive(lifetimeSeconds);
		PointerJobs.EnsureCapacity(_jobs, PointerJobs.MapKind);
		PointerMapSlot map = _store.ReserveMap(name, maxPointers);
		try
		{
			PointerCapturePlan plan = _dispatch.Run(CheatEngineToolNames.PointerCreateMap,
				token => PointerCaptureJob.Plan(_dispatch.Client, first, last, writableOnly, alignment, maxBytes,
					maxPointers, token), cancellationToken);
			map.Prepare(plan.Process.Id.Value, plan.Width, plan.PlannedBytes);
			ThrowIfDeleting(map);
			DateTimeOffset expires = _time.GetUtcNow() + lifetime;
			void Attach(ManagedJob<int> job)
			{
				if (!map.TryAttach(job))
				{
					throw Deleting(map);
				}
			}

			ManagedJob<int> job = _jobs.StartManaged<int>(PointerJobs.MapKind, lifetime, PointerJobs.BufferLimit,
				(writer, token) => PointerCaptureJob.RunAsync(_dispatch, plan, map, writer, _slice, expires, _time,
					token), Attach);
			return map.Describe();
		}
		catch
		{
			_store.RemoveMap(map);
			throw;
		}
	}

	/// <summary>Lists the pointer maps and their captures.</summary>
	/// <returns>The maps.</returns>
	[McpServerTool(Name = CheatEngineToolNames.PointerListMaps, Title = "List pointer maps", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Lists the pointer maps with their capture progress, oldest first, without any Cheat Engine call. Poll it " +
		"after pointer_create_map: a map is usable when state is ready, or stopped or expired with what it read. " +
		"Check incomplete and unreadableBytes before trusting an absence.")]
	public PointerMapList ListMaps()
	{
		return new PointerMapList([.. _store.ListMaps().Select(static map => map.Describe())]);
	}

	/// <summary>Deletes a pointer map and stops its capture.</summary>
	/// <param name="mapName">The map name.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <returns>What the deletion did.</returns>
	[McpServerTool(Name = CheatEngineToolNames.PointerDeleteMap, Title = "Delete a pointer map", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Deletes a pointer map and frees its memory, stopping its capture job first when it still runs. Scans " +
		"already found from the map keep their paths. An unknown name fails with not_found.")]
	public PointerDeleteResult DeleteMap(
		[Description("The map to delete.")] string mapName,
		CancellationToken cancellationToken = default)
	{
		PointerMapSlot map = _store.GetMap(PointerSupport.Name(mapName, "mapName"));
		bool cancelled = PointerJobs.StopIfKnown(_jobs, map.BeginDelete(), cancellationToken);
		_store.RemoveMap(map);
		return new PointerDeleteResult(map.Name, cancelled);
	}

	private static void ThrowIfDeleting(PointerMapSlot map)
	{
		if (map.IsDeleting)
		{
			throw Deleting(map);
		}
	}

	private static CheatEngineToolException Deleting(PointerMapSlot map)
	{
		return CheatEngineToolException.InvalidState($"The pointer map {map.Name} was deleted while it was starting.",
			"Choose another mapName and create the map again.");
	}
}
