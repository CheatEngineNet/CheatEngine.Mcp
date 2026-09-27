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
public sealed record KernelDbvmInitialization(bool DbvmInitialized, bool OffloadOperatingSystem, string? Reason);

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
	long SourceIndex,
	string? Rip,
	string? Rsp,
	string? Rax,
	string? Rbx,
	string? Rcx,
	string? Rdx,
	string? Rsi,
	string? Rdi,
	string? Rbp,
	string? R8,
	string? R9,
	string? R10,
	string? R11,
	string? R12,
	string? R13,
	string? R14,
	string? R15,
	string? Cr3);

/// <summary>A non-consuming page of events retained by a DBVM watch job.</summary>
public sealed record KernelWatchPoll(
	JobStatus Job,
	KernelWatchEvent[] Events,
	long FirstSequence,
	long NextAfterSequence,
	bool More,
	long Dropped);

/// <summary>The confirmation returned immediately after a DBVM watch is armed.</summary>
public sealed record LuaKernelWatchArmed(long WatchId);
