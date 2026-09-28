using CheatEngine.Client;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>A Client lease tracked by <see cref="TargetResources" />; the lease itself says whether it ended and how.</summary>
/// <param name="lease">The Client lease.</param>
/// <param name="descriptor">Its description while it is active.</param>
internal sealed class ClientLeaseResource(ICheatEngineLease lease, TargetResourceDescriptor descriptor)
	: ITargetResource
{
	/// <summary>The Client lease.</summary>
	internal ICheatEngineLease Lease => lease;

	/// <summary>The resource id, read without any call to the lease.</summary>
	internal string Id => descriptor.Id;

	/// <inheritdoc />
	public TargetResourceDescriptor Descriptor
	{
		get
		{
			if (!lease.IsReleased)
			{
				return descriptor;
			}

			bool manual = lease.RequiresManualRecovery;
			return descriptor with
			{
				State = manual ? TargetResourceState.CleanupFailed : TargetResourceState.Ended,
				RequiresManualRecovery = manual
			};
		}
	}

	/// <inheritdoc />
	public bool IsEnded => lease.IsReleased;

	/// <inheritdoc />
	public bool HoldsHostState => !lease.IsReleased || lease.RequiresManualRecovery;

	/// <inheritdoc />
	public ResourceReleaseOutcome Release(CancellationToken cancellationToken)
	{
		// A Client release never throws for a failed cleanup, and an ended lease returns the outcome that ended it.
		return ResourceReleaseOutcome.From(lease.Release());
	}
}
