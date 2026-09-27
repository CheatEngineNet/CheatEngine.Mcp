using System.ComponentModel;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>The value or values one <c>memory_read</c> call copied.</summary>
/// <param name="Address">The resolved address.</param>
/// <param name="ValueType">The value type that was read.</param>
/// <param name="Value">The value, when one was read.</param>
/// <param name="Values">The values in address order, when <c>count</c> was above one.</param>
public sealed record MemoryReadResult(
	[property: Description("The resolved address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The value type that was read.")]
	McpValueType ValueType,
	[property: Description(
		"The value as text when count is 1: decimal integers, round-trippable floats, a hexadecimal pointer, the text of a string or spaced hexadecimal bytes.")]
	string? Value = null,
	[property: Description("The consecutive values in address order when count is above 1.")]
	string[]? Values = null);

/// <summary>One address of a <c>memory_read_batch</c> request.</summary>
/// <param name="Address">The address expression.</param>
/// <param name="ValueType">The value type to read.</param>
/// <param name="Size">The byte count of a bytes read.</param>
/// <param name="Length">The maximum length of a string read.</param>
public sealed record MemoryReadItem(
	[property: Description("An address or Cheat Engine address expression, such as game.exe+1C or 7FF6A1B2C3D0.")]
	string Address,
	[property: Description("The value type to read.")]
	McpValueType ValueType,
	[property: Description("The byte count, 1 to 16384; required for bytes and refused for other types.")]
	int? Size = null,
	[property: Description("The maximum string length, 1 to 4096 (default 256); only for string and wstring.")]
	int? Length = null);

/// <summary>What one <c>memory_read_batch</c> call copied, one entry per requested address.</summary>
/// <param name="Failed">How many entries carry an error instead of a value.</param>
/// <param name="Items">One entry per request item, in request order.</param>
public sealed record MemoryReadBatchResult(
	[property: Description("How many items carry an error instead of a value; check every item.")]
	int Failed,
	[property: Description("One entry per request item, in request order.")]
	MemoryReadBatchEntry[] Items);

/// <summary>One entry of a <c>memory_read_batch</c> result.</summary>
/// <param name="Address">The resolved address, or the request text when it did not resolve.</param>
/// <param name="Value">The value, when it was read.</param>
/// <param name="Error">Why the item was not read.</param>
public sealed record MemoryReadBatchEntry(
	[property: Description(
		"The resolved address in uppercase hexadecimal, or the request text when it did not resolve.")]
	string Address,
	[property: Description("The value as text, when it was read.")]
	string? Value = null,
	[property: Description("Why this item was not read; the other items are unaffected.")]
	MemoryItemError? Error = null);

/// <summary>The failure of one item of a batch; the other items are unaffected.</summary>
/// <param name="Kind">The failure class.</param>
/// <param name="Message">A caller-facing explanation.</param>
public sealed record MemoryItemError(
	[property: Description("The failure class, such as memory_read_failed or not_found.")]
	ToolErrorKind Kind,
	[property: Description("A caller-facing explanation.")]
	string Message);

/// <summary>What one <c>memory_write</c> call wrote.</summary>
/// <param name="Address">The resolved address.</param>
/// <param name="BytesWritten">How many bytes were written.</param>
/// <param name="Previous">The bytes the range held before the write, at most 64.</param>
/// <param name="Verified">Whether a read-back matched every written byte.</param>
public sealed record MemoryWriteResult(
	[property: Description("The resolved address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("How many bytes were written.")]
	int BytesWritten,
	[property: Description(
		"The first bytes the range held before the write, at most 64, as spaced hexadecimal; write them back with valueType bytes to undo.")]
	string Previous,
	[property: Description(
		"Whether a read-back matched every written byte; false when verify was off or another writer changed the range.")]
	bool Verified);

/// <summary>One write of a <c>memory_write_batch</c> request.</summary>
/// <param name="Address">The address expression.</param>
/// <param name="ValueType">The value type to write.</param>
/// <param name="Value">The value as text.</param>
public sealed record MemoryWriteItem(
	[property: Description("An address or Cheat Engine address expression, such as game.exe+1C or 7FF6A1B2C3D0.")]
	string Address,
	[property: Description("The value type to write.")]
	McpValueType ValueType,
	[property: Description(
		"The value as text: decimal or 0x hexadecimal integers, invariant floats, a hexadecimal pointer, the text of a string or spaced hexadecimal bytes.")]
	string Value);

/// <summary>What one <c>memory_write_batch</c> call wrote.</summary>
/// <param name="Written">How many items were written.</param>
/// <param name="Verified">Whether a read-back matched every item.</param>
/// <param name="Mismatched">The items whose read-back differs.</param>
public sealed record MemoryWriteBatchResult(
	[property: Description("How many items were written, all of them on success.")]
	int Written,
	[property: Description("Whether a read-back matched every item; false when verify was off or an item differs.")]
	bool Verified,
	[property: Description("The indices of the items whose read-back differs from the written bytes.")]
	int[]? Mismatched = null);

/// <summary>The <c>details</c> of a <c>memory_write_batch</c> failure: how far the ordered writes got.</summary>
/// <param name="Completed">How many items, in request order, were written before the failure.</param>
/// <param name="FailedIndex">The index of the item that failed, when known.</param>
/// <param name="EffectState">What the failed batch did to the target.</param>
public sealed record MemoryWriteBatchFailure(
	[property: Description("How many items, in request order, were written before the failure.")]
	int Completed,
	[property: Description("The index of the item that failed, when known.")]
	int? FailedIndex,
	[property: Description("What the batch did to the target: not_started, partial or unknown.")]
	BatchWriteEffect EffectState);

/// <summary>What a failed write batch did to the target; the wire value is the <c>snake_case</c> member name.</summary>
[JsonConverter(typeof(ContractEnumConverter<BatchWriteEffect>))]
public enum BatchWriteEffect
{
	/// <summary>No item was written.</summary>
	NotStarted,

	/// <summary>A prefix of the items was written; the rest was not.</summary>
	Partial,

	/// <summary>Whether an item was written is unknown; read the addresses before anything else.</summary>
	Unknown
}

/// <summary>What <c>memory_get_address_info</c> found for each address.</summary>
/// <param name="Items">One entry per requested address, in request order.</param>
public sealed record AddressInfoResult(
	[property: Description("One entry per requested address, in request order; check each error.")]
	AddressInfo[] Items);

/// <summary>The description of one address.</summary>
/// <param name="Address">The resolved address, or the request text when it did not resolve.</param>
/// <param name="Symbol">Cheat Engine's name for the address.</param>
/// <param name="Module">The loaded module that contains it.</param>
/// <param name="Section">The module section that contains it.</param>
/// <param name="IsSystemModule">Whether Cheat Engine counts it as inside a system module.</param>
/// <param name="Region">The memory region that contains it.</param>
/// <param name="PointerValue">The pointer-sized value stored at it.</param>
/// <param name="RttiClass">The RTTI class name of the object at it.</param>
/// <param name="Error">Why the address was not described.</param>
public sealed record AddressInfo(
	[property: Description(
		"The resolved address in uppercase hexadecimal, or the request text when it did not resolve.")]
	string Address,
	[property: Description("Cheat Engine's name for the address, such as game.exe+1C0 or a symbol.")]
	string? Symbol = null,
	[property: Description("The loaded module that contains the address, when there is one.")]
	string? Module = null,
	[property: Description("The module section that contains the address, such as .text, when known.")]
	string? Section = null,
	[property: Description("Whether Cheat Engine counts the address as inside a system module.")]
	bool IsSystemModule = false,
	[property: Description("The memory region that contains the address, when Cheat Engine reports one.")]
	MemoryRegionDetail? Region = null,
	[property: Description("The pointer-sized value stored at the address, when includePointerValue was set and it is readable.")]
	string? PointerValue = null,
	[property: Description(
		"The RTTI class name of the object at the address, when includeRtti was set and Cheat Engine finds one.")]
	string? RttiClass = null,
	[property: Description("Why this address was not described; the other items are unaffected.")]
	MemoryItemError? Error = null);

/// <summary>The memory region that contains an address.</summary>
/// <param name="Base">The region's base address.</param>
/// <param name="Size">The region's size in bytes.</param>
/// <param name="State">The region's state.</param>
/// <param name="Protection">The region's current access.</param>
/// <param name="Type">The region's backing.</param>
/// <param name="AllocationBase">The base of the allocation the region belongs to.</param>
public sealed record MemoryRegionDetail(
	[property: Description("The region's base address, uppercase hexadecimal.")]
	string Base,
	[property: Description("The region's size in bytes.")]
	long Size,
	[property: Description("The region's state: committed, reserved, free or unknown.")]
	RegionState State,
	[property: Description("The region's current access.")]
	MemoryAccess Protection,
	[property: Description("The region's backing: image, mapped, private, none or unknown.")]
	RegionType Type,
	[property: Description("The base of the allocation the region belongs to, uppercase hexadecimal.")]
	string AllocationBase);

/// <summary>The access that a page protection grants.</summary>
/// <param name="Read">Whether the pages can be read.</param>
/// <param name="Write">Whether the pages can be written, copy-on-write included.</param>
/// <param name="Execute">Whether the pages can be executed.</param>
/// <param name="CopyOnWrite">Whether a write gives the process a private copy of the page.</param>
public sealed record MemoryAccess(
	[property: Description("Whether the pages can be read.")]
	bool Read,
	[property: Description("Whether the pages can be written, copy-on-write included.")]
	bool Write,
	[property: Description("Whether the pages can be executed.")]
	bool Execute,
	[property: Description("Whether a write gives the process a private copy of the page.")]
	bool CopyOnWrite);

/// <summary>A memory region's state; the wire value is the <c>snake_case</c> member name.</summary>
[JsonConverter(typeof(ContractEnumConverter<RegionState>))]
public enum RegionState
{
	/// <summary>Committed pages.</summary>
	Committed,

	/// <summary>Reserved, uncommitted pages.</summary>
	Reserved,

	/// <summary>A free range.</summary>
	Free,

	/// <summary>A state Cheat Engine reported that this version does not name.</summary>
	Unknown
}

/// <summary>A memory region's backing; the wire value is the <c>snake_case</c> member name.</summary>
[JsonConverter(typeof(ContractEnumConverter<RegionType>))]
public enum RegionType
{
	/// <summary>An executable image, such as a module.</summary>
	Image,

	/// <summary>A mapped file or section.</summary>
	Mapped,

	/// <summary>Private memory, such as the heap or a stack.</summary>
	Private,

	/// <summary>No backing, as for a free range.</summary>
	None,

	/// <summary>A backing Cheat Engine reported that this version does not name.</summary>
	Unknown
}

/// <summary>Which region states <c>memory_list_regions</c> keeps; the wire value is the <c>snake_case</c> member name.</summary>
[JsonConverter(typeof(ContractEnumConverter<RegionStateFilter>))]
public enum RegionStateFilter
{
	/// <summary>Committed regions only.</summary>
	Committed,

	/// <summary>Reserved regions only.</summary>
	Reserved,

	/// <summary>Free ranges only.</summary>
	Free,

	/// <summary>Every region.</summary>
	Any
}

/// <summary>Which region backings <c>memory_list_regions</c> keeps; the wire value is the <c>snake_case</c> member name.</summary>
[JsonConverter(typeof(ContractEnumConverter<RegionTypeFilter>))]
public enum RegionTypeFilter
{
	/// <summary>Image regions only.</summary>
	Image,

	/// <summary>Mapped regions only.</summary>
	Mapped,

	/// <summary>Private regions only.</summary>
	Private,

	/// <summary>Every backing.</summary>
	Any
}

/// <summary>
///     What a filter requires of one protection flag (writable, executable or copy-on-write); the wire value is the
///     <c>snake_case</c> member name.
/// </summary>
[JsonConverter(typeof(ContractEnumConverter<ProtectionRequirement>))]
public enum ProtectionRequirement
{
	/// <summary>The flag must be set.</summary>
	Required,

	/// <summary>The flag must not be set.</summary>
	Excluded,

	/// <summary>The flag may be set or not.</summary>
	Any
}

/// <summary>One page of the target's memory regions.</summary>
/// <param name="Total">How many regions match the filters.</param>
/// <param name="NextOffset">The offset of the next page.</param>
/// <param name="Truncated">Whether a host-side cap shortened the region map.</param>
/// <param name="Regions">The regions of this page, in address order.</param>
public sealed record RegionList(
	[property: Description("How many regions match the filters.")]
	int Total,
	[property: Description("The offset of the next page; omitted when this page ends the listing.")]
	int? NextOffset,
	[property: Description("Whether a host-side cap shortened the region map itself.")]
	bool Truncated,
	[property: Description("The regions of this page, in address order.")]
	MemoryRegionEntry[] Regions);

/// <summary>One region of <c>memory_list_regions</c>.</summary>
/// <param name="Base">The region's base address.</param>
/// <param name="Size">The region's size in bytes.</param>
/// <param name="State">The region's state.</param>
/// <param name="Protection">The region's current access.</param>
/// <param name="Type">The region's backing.</param>
/// <param name="Module">The loaded module that contains the region.</param>
/// <param name="AllocationBase">The base of the allocation the region belongs to (detailed format).</param>
/// <param name="AllocationProtection">The access the allocation was created with (detailed format).</param>
/// <param name="MappedFile">Cheat Engine's description of a mapped file (detailed format).</param>
public sealed record MemoryRegionEntry(
	[property: Description("The region's base address, uppercase hexadecimal.")]
	string Base,
	[property: Description("The region's size in bytes.")]
	long Size,
	[property: Description("The region's state: committed, reserved, free or unknown.")]
	RegionState State,
	[property: Description("The region's current access.")]
	MemoryAccess Protection,
	[property: Description("The region's backing: image, mapped, private, none or unknown.")]
	RegionType Type,
	[property: Description("The loaded module whose image contains the region, when there is one.")]
	string? Module = null,
	[property: Description("The base of the allocation the region belongs to; detailed format only.")]
	string? AllocationBase = null,
	[property: Description("The access the allocation was created with; detailed format only.")]
	MemoryAccess? AllocationProtection = null,
	[property: Description("Cheat Engine's description of the mapped file, when it reports one; detailed format only.")]
	string? MappedFile = null);

/// <summary>What <c>memory_set_protection</c> changed.</summary>
/// <param name="Address">The resolved address.</param>
/// <param name="Size">The size of the changed range.</param>
/// <param name="Previous">The access of every page in the changed range before the change.</param>
/// <param name="Current">The access of the first page after the change.</param>
public sealed record ProtectionChange(
	[property: Description("The resolved address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The size of the changed range in bytes.")]
	long Size,
	[property: Description("The access of every page before the change; restore the whole range with this tool using it.")]
	ProtectionFlags Previous,
	[property: Description(
		"The access of the first page after the change; compare it with the request, as some systems refuse write and execute together.")]
	ProtectionFlags Current);

/// <summary>The read, write and execute access Cheat Engine reports for a page.</summary>
/// <param name="Read">Whether the page can be read.</param>
/// <param name="Write">Whether the page can be written.</param>
/// <param name="Execute">Whether the page can be executed.</param>
public sealed record ProtectionFlags(
	[property: Description("Whether the page can be read.")]
	bool Read,
	[property: Description("Whether the page can be written.")]
	bool Write,
	[property: Description("Whether the page can be executed.")]
	bool Execute);

/// <summary>A named allocation that this activation owns.</summary>
/// <param name="Name">The caller's name for the allocation.</param>
/// <param name="Address">The allocated address.</param>
/// <param name="Size">The requested size.</param>
/// <param name="Executable">Whether the memory is executable.</param>
/// <param name="ResourceId">The id under which runtime_list_resources lists it.</param>
public sealed record AllocationInfo(
	[property: Description("The caller's name for the allocation; pass it to memory_free.")]
	string Name,
	[property: Description("The allocated address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The requested size in bytes; Cheat Engine may round the allocation up to a page.")]
	long Size,
	[property: Description("Whether the memory is executable (read, write and execute) rather than read and write.")]
	bool Executable,
	[property: Description("The id under which runtime_list_resources lists the allocation.")]
	string ResourceId);

/// <summary>What <c>memory_free</c> did to a named allocation.</summary>
/// <param name="Name">The allocation's name.</param>
/// <param name="Address">The allocation's address.</param>
/// <param name="Size">The allocation's size.</param>
/// <param name="Release">What the release did.</param>
public sealed record ReleaseResult(
	[property: Description("The allocation's name.")]
	string Name,
	[property: Description("The allocation's address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The allocation's size in bytes.")]
	long Size,
	[property: Description("What the release did; only a complete release frees the name.")]
	ResourceReleaseOutcome Release);

/// <summary>What <c>memory_copy</c> copied.</summary>
/// <param name="Source">The resolved source address.</param>
/// <param name="Destination">The resolved destination address.</param>
/// <param name="Size">The number of bytes copied.</param>
public sealed record MemoryCopyResult(
	[property: Description("The resolved source address, uppercase hexadecimal without 0x.")]
	string Source,
	[property: Description("The resolved destination address, uppercase hexadecimal without 0x.")]
	string Destination,
	[property: Description("The number of bytes copied.")]
	int Size);

/// <summary>How two target ranges differ.</summary>
/// <param name="AddressA">The resolved first address.</param>
/// <param name="AddressB">The resolved second address.</param>
/// <param name="Size">The number of bytes compared.</param>
/// <param name="Equal">Whether the ranges hold the same bytes.</param>
/// <param name="Truncated">Whether more differences exist than listed.</param>
/// <param name="Differences">The differing runs, in offset order.</param>
public sealed record MemoryCompareResult(
	[property: Description("The resolved first address, uppercase hexadecimal without 0x.")]
	string AddressA,
	[property: Description("The resolved second address, uppercase hexadecimal without 0x.")]
	string AddressB,
	[property: Description("The number of bytes compared.")]
	int Size,
	[property: Description("Whether the two ranges hold the same bytes.")]
	bool Equal,
	[property: Description("Whether more differences exist than maxDifferences listed; comparing stopped there.")]
	bool Truncated,
	[property: Description("The differing runs, in offset order; a run longer than 64 bytes continues in the next entry.")]
	MemoryDifference[] Differences);

/// <summary>One run of differing bytes.</summary>
/// <param name="Offset">The run's offset from both addresses.</param>
/// <param name="Length">The run's length in bytes.</param>
/// <param name="A">The bytes at the first address.</param>
/// <param name="B">The bytes at the second address.</param>
public sealed record MemoryDifference(
	[property: Description("The run's offset from both addresses, hexadecimal.")]
	string Offset,
	[property: Description("The run's length in bytes, at most 64.")]
	int Length,
	[property: Description("The bytes of the run at addressA, spaced hexadecimal.")]
	string A,
	[property: Description("The bytes of the run at addressB, spaced hexadecimal.")]
	string B);

/// <summary>A hash of a target range.</summary>
/// <param name="Address">The resolved address.</param>
/// <param name="Size">The number of bytes hashed.</param>
/// <param name="Algorithm">The hash algorithm.</param>
/// <param name="Hash">The digest.</param>
public sealed record MemoryHashResult(
	[property: Description("The resolved address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The number of bytes hashed.")]
	int Size,
	[property: Description("The hash algorithm.")]
	MemoryHashAlgorithm Algorithm,
	[property: Description("The digest as lowercase hexadecimal without separators.")]
	string Hash);

/// <summary>A hash algorithm of <c>memory_hash</c>; the wire value is the lowercase name.</summary>
[JsonConverter(typeof(ContractEnumConverter<MemoryHashAlgorithm>))]
public enum MemoryHashAlgorithm
{
	/// <summary>MD5, the digest of Cheat Engine's md5memory.</summary>
	[JsonStringEnumMemberName("md5")] Md5,

	/// <summary>SHA-1.</summary>
	[JsonStringEnumMemberName("sha1")] Sha1,

	/// <summary>SHA-256.</summary>
	[JsonStringEnumMemberName("sha256")] Sha256
}

/// <summary>What <c>memory_dump_to_file</c> does with unreadable memory; the wire value is the <c>snake_case</c> member name.</summary>
[JsonConverter(typeof(ContractEnumConverter<UnreadableMemory>))]
public enum UnreadableMemory
{
	/// <summary>Fail the dump; no file is written.</summary>
	Fail,

	/// <summary>Write zeros for the unreadable bytes and list them.</summary>
	Zero
}

/// <summary>The file one <c>memory_dump_to_file</c> call wrote.</summary>
/// <param name="Path">The normalized path of the written file.</param>
/// <param name="Address">The resolved address.</param>
/// <param name="BytesWritten">The file's length.</param>
/// <param name="ZeroFilledBytes">How many bytes were unreadable and written as zeros.</param>
/// <param name="ZeroFilled">The zero-filled ranges.</param>
/// <param name="Truncated">Whether more zero-filled ranges exist than listed.</param>
public sealed record FileDumpResult(
	[property: Description("The normalized full path of the written file.")]
	string Path,
	[property: Description("The resolved address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The file's length in bytes: the requested size.")]
	int BytesWritten,
	[property: Description("How many bytes were unreadable and written as zeros.")]
	int ZeroFilledBytes,
	[property: Description("The zero-filled ranges, in offset order, at most 256.")]
	ZeroFilledRange[] ZeroFilled,
	[property: Description("Whether more zero-filled ranges exist than listed.")]
	bool Truncated);

/// <summary>A range of the dump written as zeros because the target memory was unreadable.</summary>
/// <param name="Offset">The range's offset in the file.</param>
/// <param name="Length">The range's length in bytes.</param>
public sealed record ZeroFilledRange(
	[property: Description("The range's offset in the file and from the address, hexadecimal.")]
	string Offset,
	[property: Description("The range's length in bytes.")]
	int Length);

/// <summary>What the fixed <c>memory_get_address_info</c> script reports for each resolved address.</summary>
/// <param name="System">Whether each address is inside a system module.</param>
/// <param name="Rtti">The RTTI class name of each address, when found.</param>
public sealed record AddressExtras(bool[] System, string?[] Rtti);

/// <summary>What the fixed <c>memory_set_protection</c> script reports.</summary>
/// <param name="Previous">The first page's access before the change.</param>
/// <param name="Current">The first page's access after the change.</param>
public sealed record ProtectionProbe(ProtectionFlags Previous, ProtectionFlags Current);
