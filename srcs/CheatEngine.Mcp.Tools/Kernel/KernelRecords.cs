using System.ComponentModel;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Kernel;

/// <summary>The available DBK/DBVM state and control-register values, without initializing either component.</summary>
public sealed record KernelStatus(
	bool DbkInitialized,
	bool DbvmInitialized,
	string? Cr0,
	string? Cr3,
	string? Cr4,
	string? DbvmCr4);

/// <summary>Confirms that Cheat Engine reports DBVM initialized after an initialization request.</summary>
public sealed record KernelDbvmInitialization(
	[property: Description("Always true: Cheat Engine confirmed that DBVM runs; any other outcome is an error.")]
	bool DbvmInitialized,
	[property: Description("The requested offloadOperatingSystem value.")]
	bool OffloadOperatingSystem,
	[property: Description("The caller's reason as passed; omitted when none was given.")]
	string? Reason);

/// <summary>The physical address currently mapped for one selected-target virtual address.</summary>
public sealed record KernelAddressTranslation(string VirtualAddress, string PhysicalAddress);

/// <summary>A bounded physical-memory read copied from DBVM.</summary>
public sealed record KernelPhysicalRead(string PhysicalAddress, string Bytes);

/// <summary>Confirms the byte count accepted for a DBVM physical-memory write.</summary>
public sealed record KernelPhysicalWrite(string PhysicalAddress, int BytesWritten);

/// <summary>The event class a DBVM watch records.</summary>
[JsonConverter(typeof(ContractEnumConverter<KernelWatchAccess>))]
public enum KernelWatchAccess
{
	/// <summary>Reads and writes, as Cheat Engine's DBVM read-watch API reports them.</summary>
	Read,

	/// <summary>Writes.</summary>
	Write,

	/// <summary>Instruction execution.</summary>
	Execute
}

/// <summary>The newly armed DBVM watch job.</summary>
public sealed record KernelWatchStart(
	string JobId,
	string PhysicalAddress,
	KernelWatchAccess Access,
	int ByteSize,
	int InternalEntryCount);

/// <summary>A bounded register-only projection of one DBVM watch event.</summary>
public sealed record KernelWatchEvent(
	[property: Description(
		"The 1-based position of the event among all events DBVM returned for this watch; it equals its job sequence.")]
	long SourceIndex,
	[property: Description("RIP at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? Rip,
	[property: Description("RSP at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? Rsp,
	[property: Description("RAX at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? Rax,
	[property: Description("RBX at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? Rbx,
	[property: Description("RCX at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? Rcx,
	[property: Description("RDX at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? Rdx,
	[property: Description("RSI at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? Rsi,
	[property: Description("RDI at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? Rdi,
	[property: Description("RBP at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? Rbp,
	[property: Description("R8 at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? R8,
	[property: Description("R9 at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? R9,
	[property: Description("R10 at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? R10,
	[property: Description("R11 at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? R11,
	[property: Description("R12 at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? R12,
	[property: Description("R13 at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? R13,
	[property: Description("R14 at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? R14,
	[property: Description("R15 at the event, uppercase hexadecimal without 0x; omitted when DBVM gave none.")]
	string? R15,
	[property: Description("CR3 of the accessing address space, uppercase hexadecimal without 0x; omitted if absent.")]
	string? Cr3);

/// <summary>A non-consuming page of events retained by a DBVM watch job.</summary>
public sealed record KernelWatchPoll(
	[property: Description("The watch job status, refreshed from Cheat Engine by this poll.")]
	JobStatus Job,
	[property: Description("Events after afterSequence, oldest first.")]
	KernelWatchEvent[] Events,
	[property: Description("The oldest retained sequence; a gap after the cursor means evicted events.")]
	long FirstSequence,
	[property: Description("The afterSequence to pass to the next poll.")]
	long NextAfterSequence,
	[property: Description("Whether retained events remain after nextAfterSequence.")]
	bool More,
	[property: Description("How many of the oldest events the bounded job ring evicted.")]
	long Dropped);

/// <summary>The confirmation returned immediately after a DBVM watch is armed.</summary>
public sealed record LuaKernelWatchArmed(long WatchId);

/// <summary>What moving newly logged DBVM events into a watch job's ring did, before the job poll.</summary>
/// <param name="Found">Whether the watch job still exists in Cheat Engine's Lua state.</param>
/// <param name="Retrieved">How many events DBVM returned and were pushed into the ring by this call.</param>
public sealed record LuaKernelWatchDrain(bool Found, long Retrieved);
