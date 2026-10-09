using System.ComponentModel;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Core.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Kernel;

/// <summary>Explicitly gated DBK and DBVM tools, including DBVM watches with Lua-job cleanup.</summary>
[McpServerToolType]
public sealed class KernelTools
{
	private const string WatchJobKind = "kernelwatch";
	private readonly ToolDispatch _dispatch;
	private readonly JobRegistry _jobs;

	/// <summary>Creates the tool container without initializing DBK, DBVM or a watch.</summary>
	public KernelTools(ToolDispatch dispatch, JobRegistry jobs)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(jobs);
		_dispatch = dispatch;
		_jobs = jobs;
	}

	/// <summary>Reads DBK/DBVM state and available control registers without initializing either component.</summary>
	[McpServerTool(Name = CheatEngineToolNames.KernelGetStatus, Title = "Get DBK and DBVM status", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[RequiresFeature(McpFeature.KernelAccess)]
	[Description(
		"Read whether DBK and DBVM are already initialized, plus any CR0, CR3, CR4 and DBVM real-CR4 values that Cheat Engine can read. It does not load a driver or initialize DBVM. Null status and register values are unavailable, not false or zero; call this before any kernel operation.")]
	public KernelStatus GetStatus(CancellationToken cancellationToken = default)
	{
		RequireKernel(CheatEngineToolNames.KernelGetStatus);
		return _dispatch.RunLua(CheatEngineToolNames.KernelGetStatus, KernelScripts.GetStatus,
			KernelJsonContext.Default.KernelStatus, cancellationToken);
	}

	/// <summary>
	///     Confirms that DBVM runs, or, with <paramref name="offloadOperatingSystem" />, asks Cheat Engine to load DBK
	///     and then DBVM after a human confirms.
	/// </summary>
	[McpServerTool(Name = CheatEngineToolNames.KernelInitializeDbvm, Title = "Initialize DBVM", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.MayPrompt)]
	[RequiresFeature(McpFeature.KernelAccess)]
	[Description(
		"Initialize DBVM. With offloadOperatingSystem=false (the default) Cheat Engine loads nothing: the call only " +
		"confirms that DBVM already runs and otherwise fails with invalid_state and hostEffect not_started. With " +
		"offloadOperatingSystem=true, when DBVM is not yet running and the CPU can run it, Cheat Engine first loads " +
		"its DBK kernel driver without any prompt, then shows a confirmation dialog that a human must answer: Cheat " +
		"Engine's own crash warning followed by reason. Loading DBVM can freeze or blue-screen the host. Never retry " +
		"an unknown outcome: call kernel_get_status after the host responds.")]
	public KernelDbvmInitialization InitializeDbvm(
		[Description(
			"false only confirms that DBVM already runs and loads nothing; true loads DBK, then DBVM after a human " +
			"confirms, which can crash the host.")]
		bool offloadOperatingSystem = false,
		[Description(
			"Short operator-facing reason, at most 256 characters. With offloadOperatingSystem=true the dialog shows " +
			"it after Cheat Engine's own crash warning.")]
		string? reason = null,
		CancellationToken cancellationToken = default)
	{
		RequireKernel(CheatEngineToolNames.KernelInitializeDbvm);
		if (reason is { Length: > 256 })
		{
			throw CheatEngineToolException.InvalidArgument("reason", "must not exceed 256 characters.");
		}

		return _dispatch.RunLua(CheatEngineToolNames.KernelInitializeDbvm, KernelScripts.InitializeDbvm,
			KernelJsonContext.Default.KernelDbvmInitialization, cancellationToken, offloadOperatingSystem,
			KernelScripts.DbvmCrashWarning, reason);
	}

	/// <summary>Translates one selected-target virtual address to its current physical address through DBK.</summary>
	[McpServerTool(Name = CheatEngineToolNames.KernelTranslateAddress, Title = "Translate virtual address",
		ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[RequiresFeature(McpFeature.KernelAccess)]
	[Description(
		"Translate one virtual address in the selected target to its current physical address through DBK. The virtual expression is resolved with Client before the fixed DBK call. Translate one page at a time immediately before access: physical mappings can change or cease to be resident.")]
	public KernelAddressTranslation TranslateAddress(
		[Description("Virtual target address or Cheat Engine expression.")]
		string address,
		CancellationToken cancellationToken = default)
	{
		RequireKernel(CheatEngineToolNames.KernelTranslateAddress);
		string expression = KernelSupport.Expression(address, "address");
		return _dispatch.Run(CheatEngineToolNames.KernelTranslateAddress, token =>
		{
			ulong virtualAddress = KernelSupport.Resolve(_dispatch.Client, expression, "address", token).ToUInt64();
			return _dispatch.ExecuteLua(CheatEngineToolNames.KernelTranslateAddress, KernelScripts.TranslateAddress,
				KernelJsonContext.Default.KernelAddressTranslation, token, virtualAddress);
		}, cancellationToken);
	}

	/// <summary>Reads a small DBVM physical-memory range.</summary>
	[McpServerTool(Name = CheatEngineToolNames.KernelReadPhysical, Title = "Read physical memory", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[RequiresFeature(McpFeature.KernelAccess)]
	[Description(
		"Read 1 to 4096 bytes from a literal physical address through DBVM. The whole range must stay inside the unsigned 64-bit physical-address space. Physical memory can belong to Windows, a driver or another process, and a translation can go stale; prefer memory_read for normal target memory and call kernel_translate_address again just before this operation.")]
	public KernelPhysicalRead ReadPhysical(
		[Description("Physical hexadecimal address, such as 1A2B3000.")]
		string physicalAddress,
		[Description("Number of bytes to read, 1 to 4096.")]
		int size,
		CancellationToken cancellationToken = default)
	{
		RequireKernel(CheatEngineToolNames.KernelReadPhysical);
		ulong address = KernelSupport.PhysicalAddress(physicalAddress, "physicalAddress");
		KernelSupport.Range(size, 1, KernelSupport.MaximumPhysicalBytes, "size");
		KernelSupport.PhysicalRange(address, size, "physicalAddress");
		return _dispatch.RunLua(CheatEngineToolNames.KernelReadPhysical, KernelScripts.ReadPhysical,
			KernelJsonContext.Default.KernelPhysicalRead, cancellationToken, address, size);
	}

	/// <summary>Writes a small DBVM physical-memory range.</summary>
	[McpServerTool(Name = CheatEngineToolNames.KernelWritePhysical, Title = "Write physical memory", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[RequiresFeature(McpFeature.KernelAccess)]
	[Description(
		"Write 1 to 4096 hexadecimal bytes to a literal physical address through DBVM, bypassing page protection. The whole range must stay inside the unsigned 64-bit physical-address space. The range may back Windows, a driver or another process. A failed call after admission has hostEffect unknown; read the physical range before attempting another write or a restore.")]
	public KernelPhysicalWrite WritePhysical(
		[Description("Physical hexadecimal address, such as 1A2B3000.")]
		string physicalAddress,
		[Description("Hexadecimal bytes, for example 48 8B 05; at most 4096 bytes.")]
		string bytes,
		CancellationToken cancellationToken = default)
	{
		RequireKernel(CheatEngineToolNames.KernelWritePhysical);
		ulong address = KernelSupport.PhysicalAddress(physicalAddress, "physicalAddress");
		byte[] values = KernelSupport.PhysicalBytes(bytes, "bytes");
		KernelSupport.PhysicalRange(address, values.Length, "physicalAddress");
		return _dispatch.RunLua(CheatEngineToolNames.KernelWritePhysical, KernelScripts.WritePhysical,
			KernelJsonContext.Default.KernelPhysicalWrite, cancellationToken, address, values);
	}

	/// <summary>Starts a DBVM page watch as a tracked Lua job.</summary>
	[McpServerTool(Name = CheatEngineToolNames.KernelStartWatch, Title = "Start DBVM watch", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.KernelAccess)]
	[Description(
		"Start a DBVM physical-page watch as a bounded job. The watched range must stay inside one 4 KiB physical " +
		"page: (physicalAddress & 0xFFF) + byteSize may not exceed 4096, because DBVM silently shortens a range " +
		"that crosses a page boundary. The watch observes every process and kernel access to the physical page. " +
		"Poll it with kernel_poll_watch; runtime_stop_job(jobId), TTL expiry and plugin shutdown run its Lua cleanup " +
		"hook to disable the DBVM watch. options may use only bits 0 to 3, because options 5 and above can create " +
		"unbounded DBVM logs or change execution.")]
	public KernelWatchStart StartWatch(
		[Description("read, write or execute. A read watch also reports writes, as Cheat Engine documents.")]
		KernelWatchAccess access,
		[Description("Literal physical hexadecimal address to watch.")]
		string physicalAddress,
		[Description("Watched byte size, 1 to 4096, without crossing a 4 KiB physical page boundary.")]
		int byteSize = 1,
		[Description("DBVM watch option bits 0 to 3 only.")]
		int options = 0,
		[Description("Maximum entries DBVM retains internally, 1 to 4096.")]
		int internalEntryCount = 1024,
		[Description("Watch job lifetime in seconds, 1 to 300; default from Mcp:Execution.")]
		int? lifetimeSeconds = null,
		[Description(
			"Events retained in the MCP job buffer, 1 to Mcp:Execution:JobBufferLimit, which is also the default; " +
			"older events are evicted and counted in dropped.")]
		int? bufferLimit = null,
		CancellationToken cancellationToken = default)
	{
		RequireKernel(CheatEngineToolNames.KernelStartWatch);
		if (!Enum.IsDefined(access))
		{
			throw CheatEngineToolException.InvalidArgument("access", "must be read, write or execute.");
		}

		ulong address = KernelSupport.PhysicalAddress(physicalAddress, "physicalAddress");
		KernelSupport.Range(byteSize, 1, KernelSupport.MaximumPhysicalBytes, "byteSize");
		KernelSupport.PhysicalRange(address, byteSize, "physicalAddress");
		KernelSupport.WithinPhysicalPage(address, byteSize, "byteSize");
		KernelSupport.Range(internalEntryCount, 1, KernelSupport.MaximumWatchEntries, "internalEntryCount");
		if ((options & ~0x0F) != 0)
		{
			throw CheatEngineToolException.InvalidArgument("options", "may use only DBVM bits 0 through 3.");
		}

		TimeSpan lifetime = _jobs.ResolveTimeToLive(lifetimeSeconds);
		int retained = _jobs.ResolveBufferLimit(bufferLimit, "bufferLimit");
		LuaJob<KernelWatchEvent> job = _jobs.StartLua(WatchJobKind, lifetime, retained,
			KernelJsonContext.Default.KernelWatchEvent, start =>
			{
				_ = _dispatch.RunLua(CheatEngineToolNames.KernelStartWatch, KernelScripts.StartWatch,
					KernelJsonContext.Default.LuaKernelWatchArmed, cancellationToken, start.Namespace, start.Id,
					start.BufferLimit, (int) start.TimeToLive.TotalMilliseconds, (int) access, address, byteSize,
					options,
					internalEntryCount);
			});
		return new KernelWatchStart(job.Id, HexFormat.Address(address), access, byteSize, internalEntryCount);
	}

	/// <summary>
	///     Drains newly available DBVM events into the job ring, then returns a non-consuming page through the job
	///     poll, which refreshes the managed job state.
	/// </summary>
	[McpServerTool(Name = CheatEngineToolNames.KernelPollWatch, Title = "Poll DBVM watch", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.KernelAccess)]
	[Description(
		"Poll one DBVM watch job. The first call uses afterSequence=0, then returns nextAfterSequence for the next " +
		"page. Each call first moves every event DBVM logged since the previous call into the job's bounded ring " +
		"(DBVM empties its log on each retrieval), then returns a non-consuming page with the refreshed job status; " +
		"dropped counts events evicted from that ring. Events DBVM discarded because internalEntryCount filled " +
		"between polls are not reported, so poll often; events logged after the last poll are lost when the job " +
		"ends. Events include only a bounded register projection, never FPU or stack snapshots. Stop with " +
		"runtime_stop_job(jobId), which disables the watch.")]
	public KernelWatchPoll PollWatch(
		[Description("Job id returned by kernel_start_watch.")]
		string jobId,
		[Description("0 first, then nextAfterSequence from the previous response.")]
		long afterSequence = 0,
		[Description("Events in this page, 1 to 1000.")]
		int limit = 256,
		CancellationToken cancellationToken = default)
	{
		RequireKernel(CheatEngineToolNames.KernelPollWatch);
		JobRegistry.ValidatePoll(afterSequence, limit);
		LuaJob<KernelWatchEvent> job = _jobs.Get<LuaJob<KernelWatchEvent>>(jobId, WatchJobKind);
		// A job that no longer exists in Lua is reported, and its managed handle retired, by the job poll below.
		_ = _dispatch.RunLua(CheatEngineToolNames.KernelPollWatch, KernelScripts.DrainWatch,
			KernelJsonContext.Default.LuaKernelWatchDrain, cancellationToken, _jobs.Namespace, job.Id);
		JobPoll<KernelWatchEvent> page = job.Poll(afterSequence, limit, cancellationToken,
			CheatEngineToolNames.KernelPollWatch);
		return new KernelWatchPoll(page.Job, [.. page.Items], page.FirstSequence, page.NextAfterSequence, page.More,
			page.Dropped);
	}

	private void RequireKernel(string toolName)
	{
		_dispatch.Features.Require(McpFeature.KernelAccess, toolName);
	}
}
