using System.ComponentModel;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>One dereference of a pointer chain.</summary>
/// <param name="ReadAt">The address the pointer was read from.</param>
/// <param name="PointerValue">The pointer value read there, published as <c>pointer</c>.</param>
/// <param name="Offset">The signed offset added to the pointer.</param>
/// <param name="Next">The pointer plus the offset: the next address to read, or the final address.</param>
public sealed record PointerHop(
	[property: Description("The address the pointer was read from, uppercase hexadecimal.")]
	string ReadAt,
	[property: Description("The pointer value read there, uppercase hexadecimal.")]
	[property: JsonPropertyName("pointer")]
	string PointerValue,
	[property: Description("The signed hexadecimal offset added to the pointer.")]
	string Offset,
	[property: Description("The pointer plus the offset: the next address read, or the final address.")]
	string Next);

/// <summary>A pointer chain followed hop by hop.</summary>
/// <param name="Expression">The chain as a Cheat Engine address expression.</param>
/// <param name="Base">The resolved base address.</param>
/// <param name="Hops">Every dereference, in order.</param>
/// <param name="Address">The final address.</param>
/// <param name="Value">The value at the final address, when a value type was requested.</param>
public sealed record PointerChainResult(
	[property: Description("The chain as a Cheat Engine address expression, such as [[game.exe+1A2B30]+10]+4C8.")]
	string Expression,
	[property: Description("The resolved base address, uppercase hexadecimal.")]
	string Base,
	[property: Description("Every dereference, in order.")]
	IReadOnlyList<PointerHop> Hops,
	[property: Description("The final address, uppercase hexadecimal.")]
	string Address,
	[property: Description("The value at the final address as text, when valueType was given.")]
	string? Value = null);

/// <summary>The details of an unreadable hop.</summary>
/// <param name="HopIndex">The zero-based hop that failed; the offset count means the final value read.</param>
/// <param name="ReadAt">The address that could not be read.</param>
public sealed record PointerHopFailure(
	[property: Description("The zero-based hop that failed; the offset count means the final value read.")]
	int HopIndex,
	[property: Description("The address that could not be read, uppercase hexadecimal.")]
	string ReadAt);

/// <summary>Where a reference search looked.</summary>
[JsonConverter(typeof(ContractEnumConverter<PointerReferenceSource>))]
public enum PointerReferenceSource
{
	/// <summary>A stored pointer map.</summary>
	Map,

	/// <summary>The target's live memory.</summary>
	Live
}

/// <summary>One address that holds a pointer at or below the target.</summary>
/// <param name="Address">The address that holds the pointer.</param>
/// <param name="Value">The pointer value.</param>
/// <param name="Offset">The target minus the pointer value.</param>
/// <param name="Symbol">The holder as module plus offset, when a module holds it.</param>
public sealed record PointerReference(
	[property: Description("The address that holds the pointer, uppercase hexadecimal.")]
	string Address,
	[property: Description("The pointer value, uppercase hexadecimal.")]
	string Value,
	[property: Description("The target minus the pointer value, signed hexadecimal: the offset to add after reading.")]
	string Offset,
	[property: Description("The holder as module+offset, such as game.exe+1A2B30, when a module image holds it.")]
	string? Symbol = null);

/// <summary>The pointers found at or below a target.</summary>
/// <param name="Target">The target address.</param>
/// <param name="Source">Where the search looked.</param>
/// <param name="Count">How many holders match, beyond the limit too.</param>
/// <param name="Truncated">Whether more holders match than were returned.</param>
/// <param name="References">The holders returned.</param>
public sealed record PointerReferenceResult(
	[property: Description("The target address, uppercase hexadecimal.")]
	string Target,
	[property: Description("Where the search looked: map or live.")]
	PointerReferenceSource Source,
	[property: Description("How many holders match, including those beyond the limit.")]
	int Count,
	[property: Description("Whether more holders match than were returned.")]
	bool Truncated,
	[property: Description("The holders returned; a map search lists the nearest first.")]
	IReadOnlyList<PointerReference> References);

/// <summary>Where a pointer map capture or path search stands.</summary>
[JsonConverter(typeof(ContractEnumConverter<PointerJobState>))]
public enum PointerJobState
{
	/// <summary>The job is still working.</summary>
	Running,

	/// <summary>The job finished; the data is usable.</summary>
	Ready,

	/// <summary>A caller stopped the job; the data found so far is usable and incomplete.</summary>
	Stopped,

	/// <summary>The job's TTL ended; the data found so far is usable and incomplete.</summary>
	Expired,

	/// <summary>The job failed; there is no data.</summary>
	Failed,

	/// <summary>The selected process changed during the capture; the data was discarded.</summary>
	TargetChanged,

	/// <summary>The plugin activation ended the job.</summary>
	Cancelled
}

/// <summary>A pointer map and its capture.</summary>
/// <param name="MapName">The map name.</param>
/// <param name="JobId">The capture job's id.</param>
/// <param name="State">Where the capture stands.</param>
/// <param name="ProcessId">The captured process.</param>
/// <param name="PointerSize">The pointer width, 4 or 8.</param>
/// <param name="Pointers">How many pointers are captured.</param>
/// <param name="BytesRead">How many bytes were read.</param>
/// <param name="UnreadableBytes">How many requested bytes could not be read.</param>
/// <param name="Incomplete">Whether the capture skipped memory or stopped early.</param>
/// <param name="ProgressPercent">How much of the planned memory was read.</param>
/// <param name="Error">Why the capture failed or ended early.</param>
public sealed record PointerMapInfo(
	[property: Description("The map name.")]
	string MapName,
	[property: Description("The capture job's id, for runtime_list_jobs and runtime_stop_job.")]
	string JobId,
	[property: Description("Where the capture stands; the map is usable when ready, stopped or expired.")]
	PointerJobState State,
	[property: Description("The process the map was captured from.")]
	int ProcessId,
	[property: Description("The target pointer width in bytes, 4 or 8.")]
	int PointerSize,
	[property: Description("How many pointers are captured.")]
	int Pointers,
	[property: Description("How many bytes were read.")]
	long BytesRead,
	[property: Description("How many requested bytes could not be read.")]
	long UnreadableBytes,
	[property: Description("Whether the capture skipped memory or stopped early; absence in the map is then no proof.")]
	bool Incomplete,
	[property: Description("How much of the planned memory was read, 0 to 100.")]
	int ProgressPercent,
	[property: Description("Why the capture failed or ended early.")]
	string? Error = null);

/// <summary>The stored pointer maps.</summary>
/// <param name="Maps">The maps, oldest first.</param>
public sealed record PointerMapList(
	[property: Description("The maps, oldest first.")]
	IReadOnlyList<PointerMapInfo> Maps);

/// <summary>What deleting a map or scan did.</summary>
/// <param name="Name">The deleted map or scan.</param>
/// <param name="CancelledJob">Whether a running job was stopped.</param>
public sealed record PointerDeleteResult(
	[property: Description("The deleted map or scan name.")]
	string Name,
	[property: Description("Whether its job was still running and was stopped.")]
	bool CancelledJob);

/// <summary>A pointer path search and its results.</summary>
/// <param name="ScanName">The scan name.</param>
/// <param name="JobId">The search job's id.</param>
/// <param name="State">Where the search stands.</param>
/// <param name="MapName">The searched map.</param>
/// <param name="Target">The target address.</param>
/// <param name="Count">How many paths are stored.</param>
/// <param name="Incomplete">Whether the paths may miss some.</param>
/// <param name="TraversalLimited">Whether the node or result limit stopped the search.</param>
/// <param name="VisitedNodes">How many candidate pointers were visited.</param>
/// <param name="ElapsedMs">How long the search ran.</param>
/// <param name="Error">Why the search failed or ended early.</param>
public sealed record PointerScanInfo(
	[property: Description("The scan name.")]
	string ScanName,
	[property: Description("The search job's id, for runtime_list_jobs and runtime_stop_job.")]
	string JobId,
	[property: Description("Where the search stands; the paths are usable when ready, stopped or expired.")]
	PointerJobState State,
	[property: Description("The searched map.")]
	string MapName,
	[property: Description("The target address, uppercase hexadecimal.")]
	string Target,
	[property: Description("How many paths are stored.")]
	int Count,
	[property: Description("Whether the paths may miss some: an incomplete map, a limit, a stop or unresolved paths.")]
	bool Incomplete,
	[property: Description("Whether maxNodes or maxResults stopped the search.")]
	bool TraversalLimited,
	[property: Description("How many candidate pointers were visited.")]
	int VisitedNodes,
	[property: Description("How long the search ran, in milliseconds.")]
	long ElapsedMs,
	[property: Description("Why the search failed or ended early.")]
	string? Error = null);

/// <summary>The stored pointer scans.</summary>
/// <param name="Scans">The scans, oldest first.</param>
public sealed record PointerScanList(
	[property: Description("The scans, oldest first.")]
	IReadOnlyList<PointerScanInfo> Scans);

/// <summary>What a rescan kept.</summary>
/// <param name="ScanName">The scan name.</param>
/// <param name="Count">How many paths remain.</param>
/// <param name="Verified">How many remaining paths reached the target.</param>
/// <param name="Removed">How many paths were removed.</param>
/// <param name="Unresolved">How many paths could not be followed.</param>
/// <param name="Incomplete">Whether the remaining paths are not all verified.</param>
public sealed record PointerRescanResult(
	[property: Description("The scan name.")]
	string ScanName,
	[property: Description("How many paths remain.")]
	int Count,
	[property: Description("How many remaining paths reached the target.")]
	int Verified,
	[property: Description("How many paths were removed: they reach another address, or were unresolved and dropped.")]
	int Removed,
	[property: Description("How many paths could not be followed; they are kept unless dropUnresolved was set.")]
	int Unresolved,
	[property: Description("Whether the scan may miss paths or keeps unverified ones.")]
	bool Incomplete);

/// <summary>How stored paths are ordered.</summary>
[JsonConverter(typeof(ContractEnumConverter<PointerPathSort>))]
public enum PointerPathSort
{
	/// <summary>Fewest dereferences first, then smallest offsets.</summary>
	Depth,

	/// <summary>Smallest sum of offset magnitudes first, then fewest dereferences.</summary>
	OffsetSum,

	/// <summary>By root module name, then module offset; absolute roots last.</summary>
	Module
}

/// <summary>How a stored path was last verified.</summary>
[JsonConverter(typeof(ContractEnumConverter<PointerVerification>))]
public enum PointerVerification
{
	/// <summary>The path reached the target in a pointer map.</summary>
	SnapshotMatch,

	/// <summary>The path reached the target in live memory.</summary>
	LiveMatch,

	/// <summary>A hop could not be followed at the last rescan; the path is not verified.</summary>
	Unresolved
}

/// <summary>One stored pointer path.</summary>
/// <param name="Expression">The path as a Cheat Engine address expression.</param>
/// <param name="Base">The root address when the path was found.</param>
/// <param name="Offsets">The offsets in dereference order.</param>
/// <param name="Verification">How the path was last verified.</param>
/// <param name="Module">The root's module, when a module holds the root.</param>
/// <param name="ModuleOffset">The root's offset in its module.</param>
public sealed record PointerPathItem(
	[property: Description("The path as a Cheat Engine address expression, such as [[\"game.exe\"+1A2B30]+10]+4C8.")]
	string Expression,
	[property: Description("The root address when the path was found, uppercase hexadecimal.")]
	string Base,
	[property: Description("The offsets in dereference order, signed hexadecimal.")]
	IReadOnlyList<string> Offsets,
	[property: Description("How the path was last verified: snapshot_match, live_match or unresolved.")]
	PointerVerification Verification,
	[property: Description("The root's module, when a module holds the root; rescans rebase it by name.")]
	string? Module = null,
	[property: Description("The root's offset in its module, uppercase hexadecimal, when a module holds the root.")]
	string? ModuleOffset = null);

/// <summary>One page of stored pointer paths.</summary>
/// <param name="ScanName">The scan name.</param>
/// <param name="Total">How many paths match the filter.</param>
/// <param name="Incomplete">Whether the scan may miss paths or keeps unverified ones.</param>
/// <param name="Paths">The page.</param>
/// <param name="NextOffset">The offset of the next page; omitted on the last page.</param>
public sealed record PointerPathPage(
	[property: Description("The scan name.")]
	string ScanName,
	[property: Description("How many paths match the filter.")]
	int Total,
	[property: Description("Whether the scan may miss paths or keeps unverified ones.")]
	bool Incomplete,
	[property: Description("The page of paths.")]
	IReadOnlyList<PointerPathItem> Paths,
	[property: Description("The offset of the next page; omitted on the last page.")]
	int? NextOffset = null);

/// <summary>The details of a reference search whose temporary scan could not be released.</summary>
/// <param name="ResourceId">The tracked resource that still holds the scan.</param>
/// <param name="Retryable">Whether releasing it again may succeed.</param>
public sealed record PointerCleanupDetails(
	[property:
		Description("The resource that still holds the temporary scan; release it with runtime_release_resources.")]
	string ResourceId,
	[property: Description("Whether releasing it again may succeed.")]
	bool Retryable);
