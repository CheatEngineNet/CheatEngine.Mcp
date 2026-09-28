using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>
///     What one attempt to release an MCP-owned resource did; the wire value is the <c>snake_case</c> member name. The
///     Client lease kinds map one for one, and <see cref="CleanupFailed" /> and <see cref="StopPending" /> cover jobs and
///     state recorded in Cheat Engine's Lua.
/// </summary>
[JsonConverter(typeof(ContractEnumConverter<ResourceReleaseKind>))]
public enum ResourceReleaseKind
{
	/// <summary>No outcome could be established; the resource stays tracked and a later release tries again.</summary>
	Unknown,

	/// <summary>The resource was released and Cheat Engine confirmed it.</summary>
	Released,

	/// <summary>The resource was already released, or its job had already ended; nothing was done.</summary>
	AlreadyReleased,

	/// <summary>Part of the resource was released and at least one part failed; the failed parts may remain.</summary>
	PartiallyReleased,

	/// <summary>A third party replaced the resource, so it was left in place and nothing was released.</summary>
	Replaced,

	/// <summary>A newer registration replaced the resource, so nothing was left to release.</summary>
	Superseded,

	/// <summary>Something outside MCP already removed the resource.</summary>
	ExternallyRemoved,

	/// <summary>Cleanup was refused because no target is selected; the resource may remain in its target.</summary>
	RefusedTargetNotAttached,

	/// <summary>Cleanup was refused because another process is selected; the resource may remain in its process.</summary>
	RefusedTargetChanged,

	/// <summary>Cleanup was refused because the selected target's identity is unknown; the resource may remain.</summary>
	RefusedTargetIdentityUnavailable,

	/// <summary>Cleanup was refused because the Lua runtime that created the resource is gone; it may remain.</summary>
	RefusedRuntimeChanged,

	/// <summary>A release call began but was not confirmed; it is never retried because it may have had an effect.</summary>
	CleanupUnconfirmed,

	/// <summary>No release call could begin; nothing happened and a later release tries again.</summary>
	CleanupUnavailable,

	/// <summary>The Lua cleanup of a job or recorded state raised an error; it needs manual recovery and an acknowledgement.</summary>
	CleanupFailed,

	/// <summary>A job was asked to stop but its work has not ended yet; a later release completes it.</summary>
	StopPending
}
