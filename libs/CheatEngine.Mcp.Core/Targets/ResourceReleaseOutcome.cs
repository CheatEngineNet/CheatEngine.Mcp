using System.ComponentModel;

using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>What one attempt to release an MCP-owned resource did. Exactly one of the three flags is true.</summary>
/// <param name="Kind">What the attempt did.</param>
/// <param name="HostEffect">How far the Cheat Engine release work got.</param>
/// <param name="IsComplete">The resource is gone: nothing remains to release.</param>
/// <param name="IsRetryable">Nothing was done yet; the resource stays tracked and a later release tries again.</param>
/// <param name="RequiresManualRecovery">Something may remain that only manual recovery can remove; it is never retried.</param>
public sealed record ResourceReleaseOutcome(
	[property: Description("What the release attempt did.")]
	ResourceReleaseKind Kind,
	[property: Description("How far the Cheat Engine release work got.")]
	ToolHostEffect HostEffect,
	[property: Description("Whether the resource is gone.")]
	bool IsComplete,
	[property: Description("Whether nothing was done yet, so a later release tries again.")]
	bool IsRetryable,
	[property: Description("Whether something may remain that only manual recovery can remove.")]
	bool RequiresManualRecovery)
{
	/// <summary>A confirmed release.</summary>
	/// <param name="effect">The host effect of the release work.</param>
	/// <returns>A complete outcome.</returns>
	public static ResourceReleaseOutcome Released(ToolHostEffect effect = ToolHostEffect.Completed)
	{
		return new ResourceReleaseOutcome(ResourceReleaseKind.Released, effect, true, false, false);
	}

	/// <summary>The resource was already gone, or its job had already ended; nothing was done.</summary>
	/// <returns>A complete outcome.</returns>
	public static ResourceReleaseOutcome AlreadyReleased()
	{
		return new ResourceReleaseOutcome(ResourceReleaseKind.AlreadyReleased, ToolHostEffect.NotStarted, true, false,
			false);
	}

	/// <summary>Something outside the handle already removed the resource; nothing was done.</summary>
	/// <returns>A complete outcome.</returns>
	public static ResourceReleaseOutcome ExternallyRemoved()
	{
		return new ResourceReleaseOutcome(ResourceReleaseKind.ExternallyRemoved, ToolHostEffect.NotStarted, true,
			false, false);
	}

	/// <summary>A stop was requested but the job's work has not ended yet.</summary>
	/// <returns>A retryable outcome.</returns>
	public static ResourceReleaseOutcome StopPending()
	{
		return new ResourceReleaseOutcome(ResourceReleaseKind.StopPending, ToolHostEffect.Started, false, true, false);
	}

	/// <summary>A Lua cleanup raised an error; the entry is retained in Cheat Engine for manual recovery.</summary>
	/// <returns>An outcome that requires manual recovery.</returns>
	public static ResourceReleaseOutcome CleanupFailed()
	{
		return new ResourceReleaseOutcome(ResourceReleaseKind.CleanupFailed, ToolHostEffect.Started, false, false,
			true);
	}

	/// <summary>Copies a Client lease outcome, keeping the Client's own completeness, retry and recovery flags.</summary>
	/// <param name="outcome">The Client outcome.</param>
	/// <returns>The contract outcome; a kind this version does not know reads as <see cref="ResourceReleaseKind.Unknown" />.</returns>
	public static ResourceReleaseOutcome From(LeaseReleaseOutcome outcome)
	{
		ResourceReleaseKind kind = outcome.Kind switch
		{
			LeaseReleaseKind.Released => ResourceReleaseKind.Released,
			LeaseReleaseKind.AlreadyReleased => ResourceReleaseKind.AlreadyReleased,
			LeaseReleaseKind.PartiallyReleased => ResourceReleaseKind.PartiallyReleased,
			LeaseReleaseKind.Replaced => ResourceReleaseKind.Replaced,
			LeaseReleaseKind.Superseded => ResourceReleaseKind.Superseded,
			LeaseReleaseKind.ExternallyRemoved => ResourceReleaseKind.ExternallyRemoved,
			LeaseReleaseKind.RefusedTargetNotAttached => ResourceReleaseKind.RefusedTargetNotAttached,
			LeaseReleaseKind.RefusedTargetChanged => ResourceReleaseKind.RefusedTargetChanged,
			LeaseReleaseKind.RefusedTargetIdentityUnavailable => ResourceReleaseKind.RefusedTargetIdentityUnavailable,
			LeaseReleaseKind.RefusedRuntimeChanged => ResourceReleaseKind.RefusedRuntimeChanged,
			LeaseReleaseKind.CleanupUnconfirmed => ResourceReleaseKind.CleanupUnconfirmed,
			LeaseReleaseKind.CleanupUnavailable => ResourceReleaseKind.CleanupUnavailable,
			_ => ResourceReleaseKind.Unknown
		};
		return new ResourceReleaseOutcome(kind, ToolFailureMapping.MapHostEffect(outcome.HostEffect),
			outcome.IsComplete, outcome.IsRetryable, outcome.RequiresManualRecovery);
	}
}
