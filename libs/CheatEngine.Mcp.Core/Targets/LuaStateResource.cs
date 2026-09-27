namespace CheatEngine.Mcp.Core.Targets;

/// <summary>
///     An effect that a fixed script recorded in the Lua state root with <c>resourceRecord</c>, tracked by
///     <see cref="TargetResources" /> so that it is released in creation order with the Client leases. Releasing it runs
///     the release function the script recorded, once.
/// </summary>
/// <param name="ledger">The activation's state ledger.</param>
/// <param name="descriptor">Its description while it is active.</param>
internal sealed class LuaStateResource(McpStateLedger ledger, TargetResourceDescriptor descriptor)
	: ITargetResource, ILuaStateBacked
{
	private const int Active = 0;
	private const int Released = 1;
	private const int Failed = 2;
	private string? _cleanupError;
	private int _state;

	/// <inheritdoc />
	public string StateId => descriptor.Id;

	/// <inheritdoc />
	public void Observe(LuaStateEntry? entry)
	{
		if (entry is null)
		{
			Volatile.Write(ref _state, Released);
		}
		else if (entry.CleanupError is not null)
		{
			_cleanupError = entry.CleanupError;
			Volatile.Write(ref _state, Failed);
		}
	}

	/// <inheritdoc />
	public TargetResourceDescriptor Descriptor => Volatile.Read(ref _state) switch
	{
		Active => descriptor,
		Failed => descriptor with
		{
			State = TargetResourceState.CleanupFailed, CleanupError = _cleanupError, RequiresManualRecovery = true
		},
		_ => descriptor with { State = TargetResourceState.Ended }
	};

	/// <inheritdoc />
	public bool IsEnded => Volatile.Read(ref _state) != Active;

	/// <inheritdoc />
	public bool HoldsHostState => Volatile.Read(ref _state) != Released;

	/// <inheritdoc />
	public ResourceReleaseOutcome Release(CancellationToken cancellationToken)
	{
		switch (Volatile.Read(ref _state))
		{
			case Released:
				return ResourceReleaseOutcome.AlreadyReleased();
			case Failed:
				return ResourceReleaseOutcome.CleanupFailed();
		}

		LuaResourceRelease result = ledger.ReleaseResource(descriptor.Id, cancellationToken);
		if (!result.Found)
		{
			Volatile.Write(ref _state, Released);
			return ResourceReleaseOutcome.ExternallyRemoved();
		}

		if (result.Released)
		{
			Volatile.Write(ref _state, Released);
			return ResourceReleaseOutcome.Released();
		}

		_cleanupError = result.CleanupError;
		Volatile.Write(ref _state, Failed);
		return ResourceReleaseOutcome.CleanupFailed();
	}
}
