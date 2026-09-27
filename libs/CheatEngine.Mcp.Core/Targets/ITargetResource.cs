namespace CheatEngine.Mcp.Core.Targets;

/// <summary>
///     One MCP-owned resource that <see cref="TargetResources" /> tracks and releases before an explicit target change:
///     a Client lease, a job or an effect recorded in Cheat Engine's Lua state.
/// </summary>
/// <remarks>
///     Core implements it for Client leases, jobs and recorded Lua state; primitives create those through
///     <see cref="TargetResources" /> and <see cref="Jobs.JobRegistry" /> instead of implementing it.
/// </remarks>
public interface ITargetResource
{
	/// <summary>The current, bounded description of the resource.</summary>
	public TargetResourceDescriptor Descriptor
	{
		get;
	}

	/// <summary>Whether the handle ended, by a release or outside a request; an ended handle can be forgotten.</summary>
	public bool IsEnded
	{
		get;
	}

	/// <summary>
	///     Whether the resource holds, or may hold, state in Cheat Engine or the target: an active lease, a running job, or
	///     an ended one whose cleanup requires manual recovery. Only such resources block a target change.
	/// </summary>
	public bool HoldsHostState
	{
		get;
	}

	/// <summary>Releases the resource. Call it inside a dispatch; it never throws for a cleanup that failed.</summary>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>What the attempt did.</returns>
	public ResourceReleaseOutcome Release(CancellationToken cancellationToken);
}
