using System.ComponentModel;
using System.Globalization;

using CheatEngine.Client.Processes;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Debugger;

/// <summary>
///     The <c>debugger_*</c> tools. CheatEngine.Client intentionally exposes no debugger API yet, so each host operation
///     uses a fixed Lua body through <see cref="ToolDispatch" /> and every lasting breakpoint is recorded as a resource or
///     Lua job.
/// </summary>
[McpServerToolType]
public sealed class DebuggerTools
{
	private const int MaximumAddressLength = 512;
	private const int MaximumBreakpointList = 1024;
	private const int MaximumCaptureHits = 1024;
	private const int MaximumTraceSteps = 256;
	private const int MaximumStackDepth = 128;
	private const string CleanupFailedMessage = "The recorded cleanup did not complete.";

	private readonly ToolDispatch _dispatch;
	private readonly JobRegistry _jobs;
	private readonly TargetResources _resources;

	/// <summary>Creates the activation-scoped debugger tools.</summary>
	public DebuggerTools(ToolDispatch dispatch, JobRegistry jobs, TargetResources resources)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(jobs);
		ArgumentNullException.ThrowIfNull(resources);
		_dispatch = dispatch;
		_jobs = jobs;
		_resources = resources;
	}

	/// <summary>Attaches the selected Cheat Engine debugger interface.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerAttach, Title = "Attach debugger", ReadOnly = false,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.MayPrompt)]
	[Description(
		"Attaches Cheat Engine's debugger to the selected local process. Windows is the compatible interface; VEH " +
		"injects a helper and requires target_code_execution, and kernel requires kernel_access. default attaches " +
		"the interface selected in Cheat Engine's settings: MCP reads that setting first and refuses with " +
		"unsupported when it selects the DBVM debugger or cannot be identified, and with capability_disabled when " +
		"the selected interface needs a switch that is off (VEH needs target_code_execution, kernel needs " +
		"kernel_access); an already attached debugger is only reported. While Cheat Engine is connected to a " +
		"ceserver, an attach is refused with unsupported before Cheat Engine attaches its network debugger, and an " +
		"attached interface MCP cannot drive, such as DBVM, a GDB server or ceserver, is reported as unsupported. " +
		"A target that used VEH must restart before another attach. Read activeInterface because Cheat Engine can " +
		"fall back.")]
	public DebuggerAttachment Attach(
		[Description(
			"default, windows, veh or kernel. default follows the debugger interface selected in Cheat Engine's " +
			"settings, needs the same exposure switch as that interface and is refused for the DBVM debugger.")]
		DebuggerInterface @interface = DebuggerInterface.Default,
		CancellationToken cancellationToken = default)
	{
		if (@interface is DebuggerInterface.Veh)
		{
			_dispatch.Features.Require(McpFeature.TargetCodeExecution, CheatEngineToolNames.DebuggerAttach);
		}
		else if (@interface is DebuggerInterface.Kernel)
		{
			_dispatch.Features.Require(McpFeature.KernelAccess, CheatEngineToolNames.DebuggerAttach);
		}

		return _dispatch.Run(CheatEngineToolNames.DebuggerAttach, token =>
		{
			if (@interface is DebuggerInterface.Default)
			{
				RequireConfiguredInterface(token);
			}

			ProcessSnapshot process = _dispatch.Client.Processes.GetCurrentProcess(token);
			if (process.StartTimeUtc is not { } startTime)
			{
				throw CheatEngineToolException.Unsupported(
					"Cheat Engine did not provide the selected process start time, so VEH attach cannot be guarded safely.",
					CheatEngineToolNames.DebuggerAttach);
			}

			string identity = string.Create(CultureInfo.InvariantCulture,
				$"{process.Id.Value}:{startTime.UtcDateTime.Ticks}");
			return _dispatch.ExecuteLua(CheatEngineToolNames.DebuggerAttach, DebuggerLuaScripts.Attach,
				DebuggerJsonContext.Default.DebuggerAttachment, token, (int) @interface, process.Id.Value, identity);
		}, cancellationToken);
	}

	/// <summary>Resumes a stopped thread if necessary and detaches the debugger.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerDetach, Title = "Detach debugger", ReadOnly = false,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Resumes a stopped debugger context, unpauses the selected target and detaches Cheat Engine's debugger. " +
		"Refuses with busy while any resource this MCP activation tracks still holds target state, whatever its " +
		"kind (breakpoint, debugger or other job, pause, allocation, patch, scan, speedhack and so on); release it " +
		"first. Cheat Engine's Lua state is re-read before deciding, so jobs and breakpoints that already ended " +
		"there, for example at their TTL, no longer block.")]
	public DebuggerDetached Detach(CancellationToken cancellationToken = default)
	{
		return _dispatch.Run(CheatEngineToolNames.DebuggerDetach, token =>
		{
			EnsureNoActiveResources(token);
			return _dispatch.ExecuteLua(CheatEngineToolNames.DebuggerDetach, DebuggerLuaScripts.Detach,
				DebuggerJsonContext.Default.DebuggerDetached, token);
		}, cancellationToken);
	}

	/// <summary>Gets a copied debugger-state snapshot.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerGetStatus, Title = "Get debugger status", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Reads Cheat Engine's debugger state. broken is proven by obtaining the context and is more reliable than " +
		"reportedBroken alone. Poll after debugger_break_thread because a break request is asynchronous.")]
	public DebuggerStatus GetStatus(CancellationToken cancellationToken = default)
	{
		return _dispatch.RunLua(CheatEngineToolNames.DebuggerGetStatus, DebuggerLuaScripts.Status,
			DebuggerJsonContext.Default.DebuggerStatus, cancellationToken);
	}

	/// <summary>Requests that a target thread stop at the next safe debugger opportunity.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerBreakThread, Title = "Break a thread", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Requests that one target thread break. The call only submits the request; poll debugger_get_status for the stopped context.")]
	public DebuggerBreakRequested BreakThread(
		[Description("The positive target thread identifier from process_list_threads.")]
		long threadId,
		CancellationToken cancellationToken = default)
	{
		Positive(threadId, "threadId");
		return _dispatch.RunLua(CheatEngineToolNames.DebuggerBreakThread, DebuggerLuaScripts.BreakThread,
			DebuggerJsonContext.Default.DebuggerBreakRequested, cancellationToken, threadId);
	}

	/// <summary>Creates an MCP-owned breakpoint that stays armed until explicitly released.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerSetBreakpoint, Title = "Set debugger breakpoint",
		ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Creates a tracked execute, access or write breakpoint. Hardware slots are scarce; data watches require an " +
		"aligned size of 1, 2, 4 or 8 bytes, and eight-byte watches require x64. MCP records only its own breakpoint " +
		"so debugger_delete_breakpoint never removes one created in Cheat Engine's UI.")]
	public DebuggerBreakpointSet SetBreakpoint(
		[Description("A bounded Cheat Engine address expression, such as game.exe+1C0.")]
		string address,
		[Description("execute, access or write.")]
		DebuggerBreakpointTrigger trigger = DebuggerBreakpointTrigger.Execute,
		[Description("The watched byte count: 1, 2, 4 or 8. Execute ignores it.")]
		int size = 1,
		[Description("default, hardware, int3 or page_exception.")]
		DebuggerBreakpointMethod method = DebuggerBreakpointMethod.Default,
		[Description("Restrict the breakpoint to this target thread; omit for all threads.")]
		long? threadId = null,
		[Description("Remove the breakpoint after its first hit while leaving the thread stopped.")]
		bool oneShot = false,
		CancellationToken cancellationToken = default)
	{
		string expression = Address(address, "address");
		BreakpointSize(size, trigger);
		if (method is DebuggerBreakpointMethod.Int3 && trigger is not DebuggerBreakpointTrigger.Execute)
		{
			throw CheatEngineToolException.InvalidArgument("method", "int3 supports execute breakpoints only.");
		}

		if (threadId is { } value)
		{
			Positive(value, "threadId");
		}

		string resourceId = _resources.NextId("breakpoint");
		DebuggerBreakpointSet set = _dispatch.RunLua(CheatEngineToolNames.DebuggerSetBreakpoint,
			DebuggerLuaScripts.SetBreakpoint, DebuggerJsonContext.Default.DebuggerBreakpointSet, cancellationToken,
			_resources.Namespace, resourceId, expression, size, Trigger(trigger), Method(method), threadId, oneShot);
		_resources.TrackState(resourceId, "breakpoint", address: set.Address, size: size, detail: Trigger(trigger));
		return set;
	}

	/// <summary>Releases the MCP-owned breakpoint at the requested address.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerDeleteBreakpoint, Title = "Delete debugger breakpoint",
		ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Releases an MCP-owned breakpoint at address. It refuses when the address belongs only to a breakpoint created " +
		"by Cheat Engine or another owner, preserving user breakpoints. Cleanup failure is reported as partial_effect.")]
	public DebuggerBreakpointDeleted DeleteBreakpoint(
		[Description("The address expression of an MCP-owned breakpoint.")]
		string address,
		CancellationToken cancellationToken = default)
	{
		string expression = Address(address, "address");
		DebuggerBreakpointDeleted deleted = _dispatch.RunLua(CheatEngineToolNames.DebuggerDeleteBreakpoint,
			DebuggerLuaScripts.DeleteBreakpoint, DebuggerJsonContext.Default.DebuggerBreakpointDeleted,
			cancellationToken, _resources.Namespace, expression);
		if (deleted.Released)
		{
			_resources.Forget(deleted.ResourceId);
			return deleted;
		}

		deleted = deleted with
		{
			CleanupError = CleanupFailedMessage
		};
		throw CheatEngineToolException.PartialEffect(
			"Cheat Engine did not confirm removal of the MCP-owned breakpoint.", ToolHostEffect.CleanupUnconfirmed,
			deleted, DebuggerJsonContext.Default.DebuggerBreakpointDeleted, false,
			"Inspect debugger_list_breakpoints and use runtime_release_resources after manual recovery.");
	}

	/// <summary>Lists a bounded copy of all currently visible breakpoints.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerListBreakpoints, Title = "List debugger breakpoints",
		ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Copies up to 1024 breakpoint addresses from Cheat Engine and identifies entries this MCP activation owns.")]
	public DebuggerBreakpointPage ListBreakpoints(
		[Description("The maximum addresses to copy, from 1 through 1024.")]
		int limit = 256,
		CancellationToken cancellationToken = default)
	{
		Range(limit, "limit", 1, MaximumBreakpointList);
		return _dispatch.RunLua(CheatEngineToolNames.DebuggerListBreakpoints, DebuggerLuaScripts.ListBreakpoints,
			DebuggerJsonContext.Default.DebuggerBreakpointPage, cancellationToken, _resources.Namespace, limit);
	}

	/// <summary>Continues the current stopped debugger context.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerContinue, Title = "Continue debugger", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Continues the current stopped debugger context. The target remains stopped when no broken context exists.")]
	public DebuggerExecutionContinued Continue(CancellationToken cancellationToken = default)
	{
		return _dispatch.RunLua(CheatEngineToolNames.DebuggerContinue, DebuggerLuaScripts.Continue,
			DebuggerJsonContext.Default.DebuggerExecutionContinued, cancellationToken);
	}

	/// <summary>Single-steps the current stopped debugger context.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerStep, Title = "Step debugger", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Steps into or over one instruction from the current stopped context.")]
	public DebuggerExecutionContinued Step(
		[Description("into follows a call; over runs a call to its return.")]
		DebuggerStepMode mode = DebuggerStepMode.Into,
		CancellationToken cancellationToken = default)
	{
		return _dispatch.RunLua(CheatEngineToolNames.DebuggerStep, DebuggerLuaScripts.Step,
			DebuggerJsonContext.Default.DebuggerExecutionContinued, cancellationToken, Step(mode));
	}

	/// <summary>Copies the current stopped register context.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerGetContext, Title = "Get debugger context", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Copies normalised registers from the current stopped context; integer registers are uppercase hexadecimal " +
		"without 0x. Include extra registers only when FPU or XMM values matter: Cheat Engine then supplies FP0-FP7 " +
		"(10 bytes each) and XMM0-XMM15 (16 bytes each; XMM0-XMM7 on 32-bit targets) as byte arrays, returned as " +
		"space-separated hexadecimal bytes in memory order, least significant byte first.")]
	public DebuggerContext GetContext(
		[Description(
			"Include the FPU (FP0-FP7) and XMM registers where Cheat Engine exposes them, as space-separated " +
			"hexadecimal bytes.")]
		bool includeExtraRegisters = false,
		CancellationToken cancellationToken = default)
	{
		return _dispatch.RunLua(CheatEngineToolNames.DebuggerGetContext, DebuggerLuaScripts.GetContext,
			DebuggerJsonContext.Default.DebuggerContext, cancellationToken, includeExtraRegisters);
	}

	/// <summary>Writes and verifies one general-purpose register in the stopped context.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerSetRegister, Title = "Set debugger register", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Writes a general-purpose register or EFLAGS in the current stopped context, then reads it back. Changing RIP " +
		"redirects execution, so use a verified instruction boundary.")]
	public DebuggerRegisterSet SetRegister(
		[Description("A supported general-purpose register or EFLAGS.")]
		string register,
		[Description("The signed integer value to write. Use -1 for an all-ones unsigned register value.")]
		long value,
		CancellationToken cancellationToken = default)
	{
		string name = Register(register);
		return _dispatch.RunLua(CheatEngineToolNames.DebuggerSetRegister, DebuggerLuaScripts.SetRegister,
			DebuggerJsonContext.Default.DebuggerRegisterSet, cancellationToken, name, value);
	}

	/// <summary>Adds or removes a target thread from Cheat Engine's no-break list.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerSetThreadIgnored, Title = "Set ignored debugger thread",
		ReadOnly = false,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Adds or removes one target thread from Cheat Engine's breakpoint no-break list.")]
	public DebuggerThreadIgnoreChanged SetThreadIgnored(
		[Description("The positive target thread identifier.")]
		long threadId,
		[Description("True ignores future breakpoints on this thread; false removes the exception.")]
		bool ignored = true,
		CancellationToken cancellationToken = default)
	{
		Positive(threadId, "threadId");
		return _dispatch.RunLua(CheatEngineToolNames.DebuggerSetThreadIgnored, DebuggerLuaScripts.SetThreadIgnored,
			DebuggerJsonContext.Default.DebuggerThreadIgnoreChanged, cancellationToken, threadId, ignored);
	}

	/// <summary>Finds candidate return addresses in a bounded stack scan.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerGetStackTrace, Title = "Get heuristic stack trace",
		ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Scans at most 128 stack slots of the current stopped context for values after a call instruction. Every frame " +
		"is heuristic: verify candidates with code_disassemble before treating them as callers.")]
	public DebuggerStackTrace GetStackTrace(
		[Description("The stack slots to inspect, from 1 through 128.")]
		int depth = 32,
		CancellationToken cancellationToken = default)
	{
		Range(depth, "depth", 1, MaximumStackDepth);
		return _dispatch.RunLua(CheatEngineToolNames.DebuggerGetStackTrace, DebuggerLuaScripts.GetStackTrace,
			DebuggerJsonContext.Default.DebuggerStackTrace, cancellationToken, depth);
	}

	/// <summary>Starts a bounded breakpoint capture that automatically continues the target.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerStartCapture, Title = "Start debugger capture", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Starts a TTL-bounded access, write or execute capture. The callback records bounded contexts and automatically " +
		"continues the target. With groupByEffectiveAddress, an execute capture groups hits by the memory address " +
		"that the instruction's operand accesses, like Cheat Engine's find out what addresses this instruction " +
		"accesses. Poll with debugger_poll_capture and stop or release the returned job with runtime_stop_job.")]
	public DebuggerJobStarted StartCapture(
		[Description("The address expression to capture.")]
		string address,
		[Description("execute, access or write.")]
		DebuggerBreakpointTrigger trigger = DebuggerBreakpointTrigger.Write,
		[Description("The watched byte count: 1, 2, 4 or 8. Execute ignores it.")]
		int size = 4,
		[Description("Group repeated hits by instruction address and retain first and last contexts.")]
		bool aggregateByInstruction = false,
		[Description(
			"Group repeated hits by the effective address of the instruction's one [...] memory operand, computed " +
			"from the registers before it runs, and report effectiveAddress and operandSize per group with first and " +
			"last contexts. Needs trigger execute on that instruction and excludes aggregateByInstruction. lea and " +
			"nop, instructions without exactly one memory operand, fs: or gs: operands, vector-indexed operands and " +
			"16-bit addressing are refused before any breakpoint is set.")]
		bool groupByEffectiveAddress = false,
		[Description(
			"The maximum retained hits, instruction groups or effective-address groups, from 1 through 1024 and the " +
			"configured job buffer limit.")]
		int? maximumHits = null,
		[Description("The job lifetime in seconds. Defaults to the configured job TTL and is at most 300 seconds.")]
		int? lifetimeSeconds = null,
		CancellationToken cancellationToken = default)
	{
		string expression = Address(address, "address");
		BreakpointSize(size, trigger);
		EffectiveAddressGrouping(groupByEffectiveAddress, trigger, aggregateByInstruction);
		int requested = maximumHits ?? Math.Min(256, MaximumCaptureHits);
		Range(requested, "maximumHits", 1, MaximumCaptureHits);
		TimeSpan ttl = _jobs.ResolveTimeToLive(lifetimeSeconds);
		int buffer = _jobs.ResolveBufferLimit(requested, "maximumHits");
		DebuggerJobStarted? receipt = null;
		LuaJob<DebuggerCaptureItem> job = _jobs.StartLua("debugcapture", ttl, buffer,
			DebuggerJsonContext.Default.DebuggerCaptureItem, start =>
			{
				receipt = _dispatch.RunLua(CheatEngineToolNames.DebuggerStartCapture, DebuggerLuaScripts.StartCapture,
					DebuggerJsonContext.Default.DebuggerJobStarted, cancellationToken, start.Namespace, start.Id,
					start.BufferLimit, start.TimeToLiveMilliseconds, expression, Trigger(trigger), size,
					aggregateByInstruction, groupByEffectiveAddress);
			});
		return receipt is { } started
			? started with
			{
				JobId = job.Id
			}
			: throw CheatEngineToolException.Internal("The capture job started without a receipt.");
	}

	/// <summary>Polls a debugger capture without consuming its retained results.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerPollCapture, Title = "Poll debugger capture", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Reads one idempotent page of captured hits. Reuse nextAfterSequence for the next page; dropped reports evicted old hits.")]
	public DebuggerCapturePage PollCapture(
		[Description("The debugcapture job identifier returned by debugger_start_capture.")]
		string jobId,
		[Description("Zero for the first page, then nextAfterSequence from the previous page.")]
		long afterSequence = 0,
		[Description("The page size, from 1 through 1000.")]
		int limit = 256,
		CancellationToken cancellationToken = default)
	{
		LuaJob<DebuggerCaptureItem> job = _jobs.Get<LuaJob<DebuggerCaptureItem>>(jobId, "debugcapture");
		JobPoll<DebuggerCaptureItem> page = job.Poll(afterSequence, limit, cancellationToken,
			CheatEngineToolNames.DebuggerPollCapture);
		return new DebuggerCapturePage(page.Job, page.Items, page.FirstSequence, page.NextAfterSequence, page.More,
			page.Dropped);
	}

	/// <summary>Starts one bounded trace from an execute breakpoint.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerStartTrace, Title = "Start debugger trace", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Arms an execute breakpoint and then traces only the hitting thread for at most 256 steps. Only one trace " +
		"can run at a time, because it installs Cheat Engine's global debugger_onBreakpoint hook while active: the " +
		"call refuses (host_refused) while any debugger_onBreakpoint hook is installed, such as another trace or " +
		"one set with lua_execute, and the job fails if one appears before its entry breakpoint is hit. The trace " +
		"completes after maximumSteps contexts, the entry context included, or earlier after the first context that " +
		"matches stopCondition. Completion leaves the thread stopped; runtime_stop_job releases the job and its hook.")]
	public DebuggerJobStarted StartTrace(
		[Description("The instruction address expression where the trace starts.")]
		string address,
		[Description("into follows calls; over steps across them.")]
		DebuggerStepMode mode = DebuggerStepMode.Over,
		[Description(
			"The contexts to retain and execute, from 1 through 256, the entry context included; without " +
			"stopCondition the trace runs all of them.")]
		int maximumSteps = 32,
		[Description(
			"An optional equality condition such as RAX=1A or IP=7FF612341000, compared as a hexadecimal number " +
			"(leading zeros and 0x ignored). It stops the trace after the first matching context, the entry context " +
			"included. Name a register of the target's width: RAX-R15 and RIP on x64, EAX-EIP on x86, EFLAGS or IP.")]
		string? stopCondition = null,
		[Description("Include the stack pointer in each retained context.")]
		bool includeStack = false,
		[Description("The job lifetime in seconds. Defaults to the configured job TTL and is at most 300 seconds.")]
		int? lifetimeSeconds = null,
		CancellationToken cancellationToken = default)
	{
		string expression = Address(address, "address");
		Range(maximumSteps, "maximumSteps", 1, MaximumTraceSteps);
		(string? register, string? value) = TraceCondition(stopCondition);
		TimeSpan ttl = _jobs.ResolveTimeToLive(lifetimeSeconds);
		int buffer = _jobs.ResolveBufferLimit(maximumSteps, "maximumSteps");
		DebuggerJobStarted? receipt = null;
		LuaJob<DebuggerCaptureContext> job = _jobs.StartLua("debugtrace", ttl, buffer,
			DebuggerJsonContext.Default.DebuggerCaptureContext, start =>
			{
				receipt = _dispatch.RunLua(CheatEngineToolNames.DebuggerStartTrace, DebuggerLuaScripts.StartTrace,
					DebuggerJsonContext.Default.DebuggerJobStarted, cancellationToken, start.Namespace, start.Id,
					start.BufferLimit, start.TimeToLiveMilliseconds, expression, Step(mode), maximumSteps, register,
					value, includeStack);
			});
		return receipt is { } started
			? started with
			{
				JobId = job.Id
			}
			: throw CheatEngineToolException.Internal("The trace job started without a receipt.");
	}

	/// <summary>Polls a step trace without consuming its retained contexts.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerPollTrace, Title = "Poll debugger trace", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Reads one idempotent page of trace contexts. Completion leaves the traced thread stopped until debugger_continue.")]
	public DebuggerTracePage PollTrace(
		[Description("The debugtrace job identifier returned by debugger_start_trace.")]
		string jobId,
		[Description("Zero for the first page, then nextAfterSequence from the previous page.")]
		long afterSequence = 0,
		[Description("The page size, from 1 through 1000.")]
		int limit = 128,
		CancellationToken cancellationToken = default)
	{
		LuaJob<DebuggerCaptureContext> job = _jobs.Get<LuaJob<DebuggerCaptureContext>>(jobId, "debugtrace");
		JobPoll<DebuggerCaptureContext> page = job.Poll(afterSequence, limit, cancellationToken,
			CheatEngineToolNames.DebuggerPollTrace);
		return new DebuggerTracePage(page.Job, page.Items, page.FirstSequence, page.NextAfterSequence, page.More,
			page.Dropped);
	}

	/// <summary>Arms a tracked one-shot execute breakpoint and continues the stopped thread to it.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DebuggerRunTo, Title = "Run debugger to address", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Arms a TTL-bounded one-shot execute breakpoint and continues the current stopped thread. On hit the breakpoint " +
		"removes itself and the thread remains stopped. Runtime jobs track and release the still-armed breakpoint.")]
	public DebuggerRunToStarted RunTo(
		[Description("The destination instruction address expression.")]
		string address,
		[Description("The job lifetime in seconds. Defaults to the configured job TTL and is at most 300 seconds.")]
		int? lifetimeSeconds = null,
		CancellationToken cancellationToken = default)
	{
		string expression = Address(address, "address");
		TimeSpan ttl = _jobs.ResolveTimeToLive(lifetimeSeconds);
		DebuggerRunToStarted? receipt = null;
		LuaJob<DebuggerCaptureContext> job = _jobs.StartLua("debugrunto", ttl, 1,
			DebuggerJsonContext.Default.DebuggerCaptureContext, start =>
			{
				receipt = _dispatch.RunLua(CheatEngineToolNames.DebuggerRunTo, DebuggerLuaScripts.StartRunTo,
					DebuggerJsonContext.Default.DebuggerRunToStarted, cancellationToken, start.Namespace, start.Id,
					start.BufferLimit, start.TimeToLiveMilliseconds, expression);
			});
		return receipt is { } started
			? started with
			{
				JobId = job.Id
			}
			: throw CheatEngineToolException.Internal("The run-to job started without a receipt.");
	}

	/// <summary>
	///     Reads the interface that Cheat Engine's settings select for <c>debugProcess(0)</c>, inside the attach
	///     dispatch and before Cheat Engine attaches anything. It refuses a ceserver connection, the DBVM debugger and
	///     an unidentified setting, which the attach could not drive, and applies the exposure switch of the selected
	///     interface.
	/// </summary>
	private void RequireConfiguredInterface(CancellationToken cancellationToken)
	{
		LuaDebuggerDefaultInterface configured = _dispatch.ExecuteLua(CheatEngineToolNames.DebuggerAttach,
			DebuggerLuaScripts.DefaultInterface, DebuggerLuaJsonContext.Default.LuaDebuggerDefaultInterface,
			cancellationToken);
		if (configured.Attached)
		{
			// The attach only reports the debugger that is already attached; Cheat Engine attaches nothing.
			return;
		}

		(McpFeature? required, string? name) = configured.Configured switch
		{
			"windows" => ((McpFeature?) null, "Windows"),
			"veh" => (McpFeature.TargetCodeExecution, "VEH"),
			"kernel" => (McpFeature.KernelAccess, "kernel"),
			_ => ((McpFeature?) null, null)
		};
		if (name is null)
		{
			const string selectHint =
				"Select the Windows, VEH or kernel debugger in Cheat Engine's settings, or pass interface windows.";
			(string refused, string hint) = configured.Configured switch
			{
				"ceserver" => (
					"debugger_attach with interface default would attach the network debugger of the ceserver " +
					"that Cheat Engine is connected to, which MCP cannot drive.",
					"Disconnect Cheat Engine from the ceserver and open a local process, then attach again or " +
					"pass interface windows."),
				"dbvm" => (
					"debugger_attach with interface default would attach the DBVM debugger selected in Cheat " +
					"Engine's settings, which MCP cannot drive.", selectHint),
				_ => (
					"debugger_attach could not identify a Windows, VEH or kernel debugger in Cheat Engine's " +
					"settings, so interface default is refused.", selectHint)
			};
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.Unsupported, refused, null,
				ToolHostEffect.NotStarted, false, hint));
		}

		McpFeatureGate features = _dispatch.Features;
		if (required is not { } feature || features.IsEnabled(feature))
		{
			return;
		}

		string setting = McpFeatureGate.SettingName(feature);
		throw new CheatEngineToolException(new ToolError(ToolErrorKind.CapabilityDisabled,
			$"debugger_attach with interface default would attach the {name} debugger selected in Cheat Engine's " +
			$"settings, which the Mcp:{setting} setting disables.", null, ToolHostEffect.NotStarted, false,
			$"Pass interface windows, or set Mcp:{setting} to true in appsettings.json, then disable and re-enable " +
			"the plugin."));
	}

	/// <summary>
	///     Refuses while a resource of this activation holds target state; inside the detach dispatch. The Lua ledger
	///     is re-read first, so a job or breakpoint that already ended in Lua, at its TTL say, no longer blocks.
	/// </summary>
	private void EnsureNoActiveResources(CancellationToken cancellationToken)
	{
		_ = _resources.ListAll(cancellationToken);
		TargetResourceDescriptor? resource = _resources.List().FirstOrDefault(static item =>
			item.State is TargetResourceState.Active or TargetResourceState.StopPending
				or TargetResourceState.CleanupFailed);
		if (resource is not null)
		{
			throw CheatEngineToolException.Busy(
				$"The debugger cannot transition while MCP resource {resource.Id} still holds target state.",
				"Release it with runtime_stop_job or runtime_release_resources first.");
		}
	}

	private static string Address(string value, string parameter)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"must be a non-empty Cheat Engine address expression.");
		}

		string expression = value.Trim();
		if (expression.Length > MaximumAddressLength)
		{
			throw CheatEngineToolException.LimitExceeded(parameter,
				$"must be at most {MaximumAddressLength} characters.");
		}

		return expression;
	}

	private static void BreakpointSize(int size, DebuggerBreakpointTrigger trigger)
	{
		if (size is not 1 and not 2 and not 4 and not 8)
		{
			throw CheatEngineToolException.InvalidArgument("size", "must be 1, 2, 4 or 8.");
		}

		_ = trigger;
	}

	/// <summary>
	///     Effective-address grouping reads the registers an instruction uses for its memory operand before it runs,
	///     which only an execute breakpoint on that instruction provides, and replaces the instruction grouping.
	/// </summary>
	private static void EffectiveAddressGrouping(bool enabled, DebuggerBreakpointTrigger trigger,
		bool aggregateByInstruction)
	{
		if (!enabled)
		{
			return;
		}

		if (trigger is not DebuggerBreakpointTrigger.Execute)
		{
			throw CheatEngineToolException.InvalidArgument("groupByEffectiveAddress",
				"requires trigger execute on the instruction whose memory operand is grouped.",
				"Find the accessing instruction with a write or access capture first, then capture its " +
				"instructionAddress with trigger execute.");
		}

		if (aggregateByInstruction)
		{
			throw CheatEngineToolException.InvalidArgument("groupByEffectiveAddress",
				"cannot be combined with aggregateByInstruction; an execute capture has one instruction.",
				"Pass only one grouping option.");
		}
	}

	private static void Positive(long value, string parameter)
	{
		if (value <= 0)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must be positive.");
		}
	}

	private static void Range(int value, string parameter, int minimum, int maximum)
	{
		if (value < minimum || value > maximum)
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				string.Create(CultureInfo.InvariantCulture, $"must be between {minimum} and {maximum}."));
		}
	}

	private static string Trigger(DebuggerBreakpointTrigger trigger)
	{
		return trigger switch
		{
			DebuggerBreakpointTrigger.Execute => "execute",
			DebuggerBreakpointTrigger.Access => "access",
			DebuggerBreakpointTrigger.Write => "write",
			_ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "Unknown debugger trigger.")
		};
	}

	private static string Method(DebuggerBreakpointMethod method)
	{
		return method switch
		{
			DebuggerBreakpointMethod.Default => "default",
			DebuggerBreakpointMethod.Hardware => "hardware",
			DebuggerBreakpointMethod.Int3 => "int3",
			DebuggerBreakpointMethod.PageException => "pageException",
			_ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown debugger breakpoint method.")
		};
	}

	private static string Step(DebuggerStepMode mode)
	{
		return mode switch
		{
			DebuggerStepMode.Into => "into",
			DebuggerStepMode.Over => "over",
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown debugger step mode.")
		};
	}

	private static string Register(string? register)
	{
		string value = register?.Trim().ToUpperInvariant() ?? string.Empty;
		return value is "EAX" or "EBX" or "ECX" or "EDX" or "ESI" or "EDI" or "EBP" or "ESP" or "EIP" or
			"RAX" or "RBX" or "RCX" or "RDX" or "RSI" or "RDI" or "RBP" or "RSP" or "RIP" or "R8" or
			"R9" or "R10" or "R11" or "R12" or "R13" or "R14" or "R15" or "EFLAGS"
			? value
			: throw CheatEngineToolException.InvalidArgument("register",
				"must name a supported general-purpose register or EFLAGS.");
	}

	private static (string? Register, string? Value) TraceCondition(string? condition)
	{
		if (string.IsNullOrWhiteSpace(condition))
		{
			return (null, null);
		}

		string value = condition.Trim();
		if (value.Length > 64)
		{
			throw CheatEngineToolException.LimitExceeded("stopCondition", "must be at most 64 characters.");
		}

		int separator = value.IndexOf('=');
		if (separator is <= 0 || separator != value.LastIndexOf('='))
		{
			throw CheatEngineToolException.InvalidArgument("stopCondition",
				"must read REGISTER=HEX, for example RAX=1A.");
		}

		string register = value[..separator].Trim().ToUpperInvariant();
		if (register != "IP")
		{
			register = Register(register);
		}

		string hexadecimal = value[(separator + 1)..].Trim();
		if (hexadecimal.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			hexadecimal = hexadecimal[2..];
		}

		if (hexadecimal.Length is < 1 or > 16 || !hexadecimal.All(char.IsAsciiHexDigit))
		{
			throw CheatEngineToolException.InvalidArgument("stopCondition",
				"must compare the register with 1 to 16 hexadecimal digits.");
		}

		// The trace formats every register without leading zeros, so compare the number rather than its spelling.
		string significant = hexadecimal.TrimStart('0');
		return (register, significant.Length == 0 ? "0" : significant.ToUpperInvariant());
	}
}
