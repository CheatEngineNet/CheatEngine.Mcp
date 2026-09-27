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
		"Read whether DBK and DBVM are already initialized, plus any CR0, CR3, CR4 and DBVM real-CR4 values that Cheat Engine can read. It does not load a driver or initialize DBVM. Null register values are unavailable, not zero; call this before any kernel operation.")]
	public KernelStatus GetStatus(CancellationToken cancellationToken = default)
	{
		RequireKernel(CheatEngineToolNames.KernelGetStatus);
		return _dispatch.RunLua(CheatEngineToolNames.KernelGetStatus, KernelScripts.GetStatus,
			KernelJsonContext.Default.KernelStatus, cancellationToken);
	}

	/// <summary>Asks Cheat Engine to initialize DBVM.</summary>
	[McpServerTool(Name = CheatEngineToolNames.KernelInitializeDbvm, Title = "Initialize DBVM", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.MayPrompt)]
	[RequiresFeature(McpFeature.KernelAccess)]
	[Description(
		"Initialize DBVM. Cheat Engine may show a confirmation dialog that a human must answer; offloadOperatingSystem=true can destabilize or blue-screen the host. reason is a short operator-facing rationale passed to Cheat Engine. Never retry an unknown outcome: call kernel_get_status after the host responds.")]
	public KernelDbvmInitialization InitializeDbvm(
		[Description("Ask DBVM to offload the operating system when possible; this is the dangerous path.")]
		bool offloadOperatingSystem = false,
		[Description("Short reason shown to or recorded by Cheat Engine, at most 256 characters.")]
		string? reason = null,
		CancellationToken cancellationToken = default)
	{
		RequireKernel(CheatEngineToolNames.KernelInitializeDbvm);
		if (reason is { Length: > 256 })
		{
			throw CheatEngineToolException.InvalidArgument("reason", "must not exceed 256 characters.");
		}

		return _dispatch.RunLua(CheatEngineToolNames.KernelInitializeDbvm, KernelScripts.InitializeDbvm,
			KernelJsonContext.Default.KernelDbvmInitialization, cancellationToken, offloadOperatingSystem, reason);
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
		"Start a DBVM physical-page watch as a bounded job. The whole watched range must stay inside the unsigned 64-bit physical-address space. The watch observes every process and kernel access to the physical page. Poll it with kernel_poll_watch; runtime_stop_job(jobId), TTL expiry and plugin shutdown run its Lua cleanup hook to disable the DBVM watch. options may use only bits 0 to 3, because options 5 and above can create unbounded DBVM logs or change execution.")]
	public KernelWatchStart StartWatch(
		[Description("read, write or execute. A read watch also reports writes, as Cheat Engine documents.")]
		KernelWatchAccess access,
		[Description("Literal physical hexadecimal address to watch.")]
		string physicalAddress,
		[Description("Watched byte size, 1 to 4096.")]
		int byteSize = 1,
		[Description("DBVM watch option bits 0 to 3 only.")]
		int options = 0,
		[Description("Maximum entries DBVM retains internally, 1 to 4096.")]
		int internalEntryCount = 1024,
		[Description("Watch job lifetime in seconds, 1 to 300; default from Mcp:Execution.")]
		int? lifetimeSeconds = null,
		[Description(
			"Events retained in the MCP job buffer; default from Mcp:Execution, subject to its configured limit.")]
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
		KernelSupport.Range(internalEntryCount, 1, KernelSupport.MaximumWatchEntries, "internalEntryCount");
		if ((options & ~0x0F) != 0)
		{
			throw CheatEngineToolException.InvalidArgument("options", "may use only DBVM bits 0 through 3.");
		}

		TimeSpan lifetime = _jobs.ResolveTimeToLive(lifetimeSeconds);
		int retained = _jobs.ResolveBufferLimit(bufferLimit);
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

	/// <summary>Drains newly available DBVM events into the job ring and returns a non-consuming page.</summary>
	[McpServerTool(Name = CheatEngineToolNames.KernelPollWatch, Title = "Poll DBVM watch", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.KernelAccess)]
	[Description(
		"Poll one DBVM watch job. The first call uses afterSequence=0, then returns nextAfterSequence for the next page. Newly logged DBVM events are copied into the job's bounded ring before the page is returned; dropped reports ring eviction. Events include only a bounded register projection, never FPU or stack snapshots. Stop with runtime_stop_job(jobId), which disables the watch.")]
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
		_ = _jobs.Get<LuaJob<KernelWatchEvent>>(jobId, WatchJobKind);
		return _dispatch.RunLua(CheatEngineToolNames.KernelPollWatch, KernelScripts.PollWatch,
			KernelJsonContext.Default.KernelWatchPoll, cancellationToken, _jobs.Namespace, jobId, afterSequence, limit);
	}

	private void RequireKernel(string toolName)
	{
		_dispatch.Features.Require(McpFeature.KernelAccess, toolName);
	}
}
