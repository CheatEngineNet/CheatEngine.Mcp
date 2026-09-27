using System.ComponentModel;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>A bounded description of one MCP-owned resource, safe to report and enough to recover it manually.</summary>
/// <param name="Id">The id, <c>kind-namespace-number</c>, where the namespace names the plugin activation.</param>
/// <param name="Kind">What the resource is.</param>
/// <param name="Category">How it is held.</param>
/// <param name="State">Where it stands.</param>
/// <param name="CreatedUtc">When it was created.</param>
/// <param name="Name">The owner-chosen name, when there is one.</param>
/// <param name="Address">The address as uppercase hexadecimal without <c>0x</c>, when there is one.</param>
/// <param name="Size">The size in bytes, when there is one.</param>
/// <param name="Detail">A short owner-supplied detail, such as a speed.</param>
/// <param name="ProcessId">The process it was created in, when known.</param>
/// <param name="Orphaned">Whether an earlier plugin activation created it.</param>
/// <param name="CleanupError">Why its cleanup failed.</param>
/// <param name="RequiresManualRecovery">Whether something may remain that only manual recovery can remove.</param>
public sealed record TargetResourceDescriptor(
	[property: Description(
		"The resource id, kind-namespace-number; the namespace names the plugin activation that created it.")]
	string Id,
	[property: Description(
		"What the resource is, such as allocation, patch, scan, symbol, speedhack, pause, breakpoint or a job kind.")]
	string Kind,
	[property: Description("How it is held: client_lease, job or lua_state.")]
	TargetResourceCategory Category,
	[property: Description("Where it stands: active, ended, stop_pending or cleanup_failed.")]
	TargetResourceState State,
	[property: Description("When it was created, in UTC.")]
	DateTimeOffset CreatedUtc,
	[property: Description("The owner-chosen name, when there is one.")]
	string? Name = null,
	[property: Description("Its address as uppercase hexadecimal without 0x, when there is one.")]
	string? Address = null,
	[property: Description("Its size in bytes, when there is one.")]
	long? Size = null,
	[property: Description("A short owner-supplied detail, such as a speed.")]
	string? Detail = null,
	[property: Description("The process it was created in, when known.")]
	int? ProcessId = null,
	[property: Description(
		"Whether an earlier plugin activation created it; release it or, after manual recovery, acknowledge it.")]
	bool Orphaned = false,
	[property: Description("Why its cleanup failed; it then needs manual recovery and an acknowledgement.")]
	string? CleanupError = null,
	[property: Description("Whether something may remain that only manual recovery can remove.")]
	bool RequiresManualRecovery = false)
{
	/// <summary>Whether the resource holds, or may hold, state in Cheat Engine or the target.</summary>
	internal bool HoldsHostState => State is not TargetResourceState.Ended;
}
