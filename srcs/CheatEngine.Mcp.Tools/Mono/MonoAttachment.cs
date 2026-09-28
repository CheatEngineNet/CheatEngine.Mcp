using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Mono;

/// <summary>Tracks the activation-owned Mono collector attach so target changes and runtime cleanup detach it first.</summary>
/// <remarks>
///     The release closes the collector only while the Lua record that <see cref="MonoLuaScripts.Attach" /> left under
///     this resource's id still describes Cheat Engine's current attachment; otherwise that attachment already ended
///     outside MCP, the current one is left alone, and the outcome is
///     <see cref="ResourceReleaseOutcome.ExternallyRemoved" />.
/// </remarks>
internal sealed class MonoAttachment : ITargetResource
{
	private readonly TargetResourceDescriptor _active;
	private readonly ToolDispatch _dispatch;
	private string? _cleanupError;
	private int _ended;

	internal MonoAttachment(string id, ToolDispatch dispatch)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
		_active = new TargetResourceDescriptor(id, "mono", TargetResourceCategory.LuaState,
			TargetResourceState.Active, DateTimeOffset.UtcNow, Detail: "collector attachment");
	}

	/// <inheritdoc />
	public TargetResourceDescriptor Descriptor => Volatile.Read(ref _ended) != 0
		? _active with
		{
			State = TargetResourceState.Ended
		}
		: _cleanupError is { } error
			? _active with
			{
				State = TargetResourceState.CleanupFailed,
				CleanupError = error,
				RequiresManualRecovery = true
			}
			: _active;

	/// <inheritdoc />
	public bool IsEnded => Volatile.Read(ref _ended) != 0;

	/// <inheritdoc />
	public bool HoldsHostState => !IsEnded;

	/// <inheritdoc />
	public ResourceReleaseOutcome Release(CancellationToken cancellationToken)
	{
		if (IsEnded)
		{
			return ResourceReleaseOutcome.AlreadyReleased();
		}

		try
		{
			MonoDetachResult result = _dispatch.ExecuteLua(CheatEngineToolNames.MonoDetach, MonoLuaScripts.Detach,
				MonoJsonContext.Default.MonoDetachResult, cancellationToken, _active.Id);
			if (!result.Detached)
			{
				_cleanupError = "Cheat Engine did not confirm that the Mono collector detached.";
				return ResourceReleaseOutcome.CleanupFailed();
			}

			Interlocked.Exchange(ref _ended, 1);
			return result.AlreadyEnded ? ResourceReleaseOutcome.ExternallyRemoved() : ResourceReleaseOutcome.Released();
		}
		catch (Exception exception)
		{
			_cleanupError = exception is CheatEngineToolException tool
				? tool.Error.Message
				: "Cheat Engine did not confirm that the Mono collector detached.";
			return ResourceReleaseOutcome.CleanupFailed();
		}
	}
}
