using System.ComponentModel;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Debugger;

/// <summary>The debugger interface requested from Cheat Engine.</summary>
[JsonConverter(typeof(ContractEnumConverter<DebuggerInterface>))]
public enum DebuggerInterface
{
	/// <summary>Cheat Engine's configured default interface.</summary>
	Default,

	/// <summary>The Windows debugging API.</summary>
	Windows,

	/// <summary>Cheat Engine's vectored-exception-handler debugger.</summary>
	Veh,

	/// <summary>Cheat Engine's DBK kernel debugger.</summary>
	Kernel
}

/// <summary>The event that causes a breakpoint to fire.</summary>
[JsonConverter(typeof(ContractEnumConverter<DebuggerBreakpointTrigger>))]
public enum DebuggerBreakpointTrigger
{
	/// <summary>Break before an instruction executes.</summary>
	Execute,

	/// <summary>Break after an instruction reads or writes the watched bytes.</summary>
	Access,

	/// <summary>Break after an instruction writes the watched bytes.</summary>
	Write
}

/// <summary>The implementation Cheat Engine uses for a breakpoint.</summary>
[JsonConverter(typeof(ContractEnumConverter<DebuggerBreakpointMethod>))]
public enum DebuggerBreakpointMethod
{
	/// <summary>Let Cheat Engine choose its default method.</summary>
	Default,

	/// <summary>Use an x86 debug-register slot.</summary>
	Hardware,

	/// <summary>Use an INT3 code breakpoint.</summary>
	Int3,

	/// <summary>Use a page-exception breakpoint.</summary>
	PageException
}

/// <summary>The single-step operation.</summary>
[JsonConverter(typeof(ContractEnumConverter<DebuggerStepMode>))]
public enum DebuggerStepMode
{
	/// <summary>Step into a call.</summary>
	Into,

	/// <summary>Step over a call.</summary>
	Over
}

/// <summary>What debugger_attach established.</summary>
public sealed record DebuggerAttachment(
	[property: Description("Whether Cheat Engine reports a debugger attached.")]
	bool Attached,
	[property: Description("Whether the requested interface was already attached.")]
	bool AlreadyAttached,
	[property: Description("The selected target process identifier.")]
	int ProcessId,
	[property: Description("The interface requested by the caller.")]
	DebuggerInterface RequestedInterface,
	[property: Description("The interface Cheat Engine actually activated.")]
	DebuggerInterface ActiveInterface,
	[property: Description("Whether Cheat Engine selected a different interface than the explicit request.")]
	bool UsedFallback);

/// <summary>What debugger_detach did to the current debugger state.</summary>
public sealed record DebuggerDetached(
	[property: Description("The selected target process identifier when detachment ran.")]
	int ProcessId,
	[property: Description("Whether Cheat Engine reported an attached debugger before the call.")]
	bool WasAttached,
	[property: Description("Whether a broken debugger context was resumed before detaching.")]
	bool Continued,
	[property: Description("Whether Cheat Engine reports the debugger detached after the call.")]
	bool Detached);

/// <summary>A copied debugger status snapshot.</summary>
public sealed record DebuggerStatus(
	[property: Description("Whether Cheat Engine returned a complete debugger-state snapshot.")]
	bool StateValid,
	[property: Description("Whether a debugger is attached.")]
	bool Attached,
	[property: Description("Whether Cheat Engine currently permits a break request.")]
	bool CanBreak,
	[property: Description("Whether debug_getContext proved that a broken context is available.")]
	bool Broken,
	[property: Description("The separate state reported by debug_isBroken.")]
	bool ReportedBroken,
	[property: Description("Whether Cheat Engine reports that it is single stepping.")]
	bool Stepping,
	[property: Description("The active debugger interface when attached.")]
	DebuggerInterface? ActiveInterface = null,
	[property: Description("A bounded host message when the state could not be read.")]
	string? Error = null);

/// <summary>The asynchronous break request sent to one target thread.</summary>
public sealed record DebuggerBreakRequested(
	[property: Description("The target thread identifier supplied by the caller.")]
	long ThreadId,
	[property: Description("Whether Cheat Engine accepted the break request; poll debugger_get_status for the stop.")]
	bool Requested);

/// <summary>An MCP-owned breakpoint that Cheat Engine accepted.</summary>
public sealed record DebuggerBreakpointSet(
	[property: Description("The resource identifier used by runtime_list_resources and debugger_delete_breakpoint.")]
	string ResourceId,
	[property: Description("The resolved breakpoint address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The selected trigger.")]
	DebuggerBreakpointTrigger Trigger,
	[property: Description("The watched byte count. Execute breakpoints ignore it.")]
	int Size,
	[property: Description("The requested breakpoint method.")]
	DebuggerBreakpointMethod Method,
	[property: Description("Whether the breakpoint is restricted to one target thread.")]
	long? ThreadId,
	[property: Description("Whether the breakpoint removes itself when it fires.")]
	bool OneShot);

/// <summary>The outcome of releasing one MCP-owned breakpoint.</summary>
public sealed record DebuggerBreakpointDeleted(
	[property: Description("The released MCP resource identifier.")]
	string ResourceId,
	[property: Description("The breakpoint address supplied by the caller.")]
	string Address,
	[property: Description("Whether Cheat Engine confirmed removal of the owned breakpoint.")]
	bool Released,
	[property: Description("A bounded cleanup error when removal could not be confirmed.")]
	string? CleanupError = null);

/// <summary>One breakpoint visible in Cheat Engine.</summary>
public sealed record DebuggerBreakpoint(
	[property: Description("The breakpoint address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("Whether this activation owns a removable resource at the address.")]
	bool Owned,
	[property: Description("The MCP resource id when owned is true.")]
	string? ResourceId = null);

/// <summary>A bounded breakpoint list.</summary>
public sealed record DebuggerBreakpointPage(
	[property: Description("The breakpoint addresses copied from Cheat Engine.")]
	DebuggerBreakpoint[] Breakpoints,
	[property: Description("The number Cheat Engine reported before the response limit.")]
	int Total,
	[property: Description("Whether the response omitted remaining breakpoints.")]
	bool Truncated);

/// <summary>The effect of continuing or single stepping a stopped context.</summary>
public sealed record DebuggerExecutionContinued(
	[property: Description("run, into or over.")]
	string Mode,
	[property: Description("Whether Cheat Engine accepted the continuation request.")]
	bool Continued);

/// <summary>A copied, normalised debugger context.</summary>
public sealed record DebuggerContext(
	[property: Description("Whether the target is 64-bit according to Cheat Engine.")]
	bool Is64Bit,
	[property: Description("Uppercase register names and their hexadecimal or bounded textual values.")]
	Dictionary<string, string> Registers,
	[property: Description("Whether FPU and XMM values were requested.")]
	bool IncludesExtraRegisters);

/// <summary>The verified result of changing a general-purpose register.</summary>
public sealed record DebuggerRegisterSet(
	[property: Description("The requested register name, uppercase.")]
	string Register,
	[property: Description("The concrete context register Cheat Engine wrote.")]
	string ContextRegister,
	[property: Description("The verified register value, uppercase hexadecimal without 0x.")]
	string Value,
	[property: Description("Whether a fresh context read matched the requested value.")]
	bool Verified);

/// <summary>The result of adding or removing a thread from Cheat Engine's no-break list.</summary>
public sealed record DebuggerThreadIgnoreChanged(
	[property: Description("The target thread identifier.")]
	long ThreadId,
	[property: Description("Whether the thread is now ignored by breakpoints.")]
	bool Ignored);

/// <summary>A candidate return address found by the bounded stack heuristic.</summary>
public sealed record DebuggerStackFrame(
	[property: Description("The address of the stack slot containing the candidate, uppercase hexadecimal without 0x.")]
	string StackAddress,
	[property: Description("The candidate return address, uppercase hexadecimal without 0x.")]
	string ReturnAddress,
	[property: Description("The instruction immediately before returnAddress when Cheat Engine could disassemble one.")]
	string? CallInstruction,
	[property: Description("Always true: stale stack values and non-call code pointers can produce false frames.")]
	bool IsHeuristic = true);

/// <summary>A bounded heuristic stack scan of the current broken context.</summary>
public sealed record DebuggerStackTrace(
	[property: Description("The stack pointer read from the broken context, uppercase hexadecimal without 0x.")]
	string StackPointer,
	[property: Description("The pointer width used to scan stack slots.")]
	int PointerSize,
	[property: Description("Candidate call-return frames in stack order.")]
	DebuggerStackFrame[] Frames,
	[property: Description("The number of stack slots examined.")]
	int ScannedSlots);

/// <summary>The common start receipt of an MCP debugger job.</summary>
public sealed record DebuggerJobStarted(
	[property: Description("The job identifier; poll it or stop it with runtime_stop_job.")]
	string JobId,
	[property: Description("The target process identifier captured at start.")]
	int ProcessId,
	[property: Description("The resolved breakpoint address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("How long the job and its results remain available, in seconds.")]
	int LifetimeSeconds);

/// <summary>One stopped-context snapshot captured by a breakpoint or a step trace.</summary>
public sealed record DebuggerCaptureContext(
	[property: Description("The reported instruction pointer, uppercase hexadecimal without 0x.")]
	string Ip,
	[property: Description("The target thread identifier.")]
	long ThreadId,
	[property:
		Description("The candidate executed instruction address for a data trap, uppercase hexadecimal without 0x.")]
	string? InstructionAddress,
	[property: Description("Whether instructionAddress was derived by reverse disassembly and is only a heuristic.")]
	bool IsHeuristic,
	[property: Description("A bounded disassembly string when Cheat Engine could produce one.")]
	string? Disassembly,
	[property: Description("Uppercase general-purpose registers captured at the event.")]
	Dictionary<string, string> Registers,
	[property: Description("The stack pointer at the event when the trace requested it.")]
	string? StackPointer = null);

/// <summary>One capture result, optionally aggregated by candidate instruction.</summary>
public sealed record DebuggerCaptureItem(
	[property: Description("The latest context for this item.")]
	DebuggerCaptureContext Context,
	[property: Description("The number of events represented by this item; one when aggregation is disabled.")]
	long HitCount,
	[property: Description("The first context in an aggregated group; omitted for individual hits.")]
	DebuggerCaptureContext? FirstContext = null,
	[property: Description("The latest context in an aggregated group; omitted for individual hits.")]
	DebuggerCaptureContext? LastContext = null);

/// <summary>A page of non-consuming capture results.</summary>
public sealed record DebuggerCapturePage(
	[property: Description("The runtime job status and bounded buffer counters.")]
	JobStatus Job,
	[property: Description("Capture items after afterSequence, oldest first.")]
	IReadOnlyList<DebuggerCaptureItem> Hits,
	[property: Description("The oldest retained sequence.")]
	long FirstSequence,
	[property: Description("Pass this cursor to the next poll.")]
	long NextAfterSequence,
	[property: Description("Whether further retained items follow this page.")]
	bool More,
	[property: Description("How many oldest items the bounded job buffer evicted.")]
	long Dropped);

/// <summary>A page of non-consuming step-trace contexts.</summary>
public sealed record DebuggerTracePage(
	[property: Description("The runtime job status and bounded buffer counters.")]
	JobStatus Job,
	[property: Description("Trace steps after afterSequence, oldest first.")]
	IReadOnlyList<DebuggerCaptureContext> Steps,
	[property: Description("The oldest retained sequence.")]
	long FirstSequence,
	[property: Description("Pass this cursor to the next poll.")]
	long NextAfterSequence,
	[property: Description("Whether further retained steps follow this page.")]
	bool More,
	[property: Description("How many oldest steps the bounded job buffer evicted.")]
	long Dropped);

/// <summary>The tracked one-shot breakpoint that debugger_run_to armed.</summary>
public sealed record DebuggerRunToStarted(
	[property: Description("The job identifier; runtime_list_jobs reports whether the target reached it.")]
	string JobId,
	[property: Description("The target process identifier captured at start.")]
	int ProcessId,
	[property: Description("The destination instruction address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("How long the one-shot breakpoint remains armed, in seconds.")]
	int LifetimeSeconds);
