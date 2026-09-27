namespace CheatEngine.Mcp.Core.Targets;

/// <summary>One job or recorded resource of the Lua state root, as the fixed state scripts describe it.</summary>
/// <param name="Id">The id, <c>kind-namespace-number</c>.</param>
/// <param name="Kind">The job or resource kind.</param>
/// <param name="Family"><c>job</c> or <c>resource</c>.</param>
/// <param name="Namespace">The activation namespace that created it.</param>
/// <param name="State">A job state, or <c>active</c>, <c>released</c> or <c>cleanup_failed</c> for a resource.</param>
/// <param name="AgeMs">Milliseconds since it was created, by Cheat Engine's tick count.</param>
/// <param name="HoldsHostState">Whether it holds, or may hold, state in Cheat Engine or the target.</param>
/// <param name="ExpiresInMs">Milliseconds until its TTL ends, when it has one.</param>
/// <param name="Name">The owner-chosen name.</param>
/// <param name="Detail">A short owner-supplied detail.</param>
/// <param name="Address">The address as uppercase hexadecimal.</param>
/// <param name="Size">The size in bytes.</param>
/// <param name="ProcessId">The process selected when it was created.</param>
/// <param name="CleanupError">Why its cleanup failed.</param>
internal sealed record LuaStateEntry(
	string Id,
	string Kind,
	string Family,
	string Namespace,
	string State,
	long AgeMs,
	bool HoldsHostState,
	long? ExpiresInMs = null,
	string? Name = null,
	string? Detail = null,
	string? Address = null,
	long? Size = null,
	int? ProcessId = null,
	string? CleanupError = null)
{
	/// <summary>Whether the entry is a job.</summary>
	internal bool IsJob => Family == "job";
}

/// <summary>Every entry of the Lua state root, newest first and bounded.</summary>
/// <param name="Entries">The entries, at most <see cref="Jobs.LuaJobKernelScripts.MaximumSnapshotEntries" />.</param>
/// <param name="Total">How many entries exist.</param>
/// <param name="Truncated">Whether <paramref name="Entries" /> omits some.</param>
internal sealed record LuaStateSnapshot(LuaStateEntry[] Entries, int Total, bool Truncated);

/// <summary>What releasing the entries no managed handle tracks did, newest first.</summary>
/// <param name="Released">The entries released.</param>
/// <param name="Failed">The first entry whose cleanup failed; the release stopped there.</param>
/// <param name="Remaining">The entries that still hold host state.</param>
internal sealed record LuaStateRelease(LuaStateEntry[] Released, LuaStateEntry? Failed, LuaStateEntry[] Remaining);

/// <summary>The entries an acknowledgement removed.</summary>
/// <param name="Acknowledged">The removed entries, as they were described before removal.</param>
internal sealed record LuaStateAcknowledgement(LuaStateEntry[] Acknowledged);

/// <summary>What releasing one recorded resource of the activation did.</summary>
/// <param name="Found">Whether the resource was still recorded.</param>
/// <param name="Released">Whether its release function succeeded and the entry was removed.</param>
/// <param name="CleanupError">Why its release failed; the entry is then retained for manual recovery.</param>
internal sealed record LuaResourceRelease(bool Found, bool Released, string? CleanupError = null);
