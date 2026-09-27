using CheatEngine.Client.Results;

namespace CheatEngine.Mcp.Core.Contract;

/// <summary>Maps Client failure classifications to the MCP error contract.</summary>
public static class ToolFailureMapping
{
	internal const string AttachHint = "Attach a process with process_attach.";
	internal const string TargetChangedHint = "Re-attach, then repeat reads; never repeat a mutation blindly.";
	internal const string AmbiguousHint = "Narrow the pattern, module or name.";
	internal const string LimitHint = "Lower limit or size, or page with offset.";
	internal const string ActivationEndedHint = "The plugin activation ended; reconnect or call instance_list again.";
	internal const string RuntimeChangedHint = "Disable and re-enable the plugin.";

	internal const string MemoryReadHint =
		"Check that the address is mapped and readable with memory_get_address_info.";

	internal const string MemoryWriteHint =
		"Check that the address is mapped and writable with memory_get_address_info.";

	internal const string IndeterminateHint = "The result is indeterminate; never treat it as absence.";

	/// <summary>Maps a classified Client failure, keeping its operation and host effect.</summary>
	/// <param name="failure">The Client failure.</param>
	/// <param name="activationStopping">
	///     Whether the activation's stopping token was set: a cancellation or invalid state then reports
	///     <see cref="ToolErrorKind.Stopping" />.
	/// </param>
	/// <returns>The contract error.</returns>
	public static ToolError Map(in CheatEngineFailure failure, bool activationStopping)
	{
		(ToolErrorKind kind, string? hint) = Classify(failure.Kind, activationStopping);
		ToolHostEffect effect = MapHostEffect(failure.HostEffect);
		string message = string.IsNullOrWhiteSpace(failure.Message)
			? "Cheat Engine reported an unclassified failure."
			: failure.Message;
		string? operation = string.IsNullOrWhiteSpace(failure.Operation) ? null : failure.Operation;
		return new ToolError(kind, message, operation, effect, IsRetryable(kind, effect), hint);
	}

	/// <summary>Maps a Client host effect one for one.</summary>
	/// <param name="effect">The Client host effect.</param>
	/// <returns>The contract host effect; an undefined value maps to <see cref="ToolHostEffect.Unknown" />.</returns>
	public static ToolHostEffect MapHostEffect(CheatEngineHostEffect effect)
	{
		return effect switch
		{
			CheatEngineHostEffect.NotStarted => ToolHostEffect.NotStarted,
			CheatEngineHostEffect.NotApplied => ToolHostEffect.NotApplied,
			CheatEngineHostEffect.Started => ToolHostEffect.Started,
			CheatEngineHostEffect.Completed => ToolHostEffect.Completed,
			CheatEngineHostEffect.CleanupUnconfirmed => ToolHostEffect.CleanupUnconfirmed,
			_ => ToolHostEffect.Unknown
		};
	}

	/// <summary>
	///     Whether repeating the identical call later is safe and may succeed: a busy or cancelled call whose host effect
	///     is <see cref="ToolHostEffect.NotStarted" /> or <see cref="ToolHostEffect.NotApplied" />.
	/// </summary>
	/// <param name="kind">The failure class.</param>
	/// <param name="effect">What the call did before it failed.</param>
	/// <returns><see langword="true" /> when the call may be repeated as is.</returns>
	public static bool IsRetryable(ToolErrorKind kind, ToolHostEffect effect)
	{
		return kind is ToolErrorKind.Busy or ToolErrorKind.Cancelled
			   && effect is ToolHostEffect.NotStarted or ToolHostEffect.NotApplied;
	}

	private static (ToolErrorKind Kind, string? Hint) Classify(CheatEngineFailureKind kind, bool activationStopping)
	{
		return kind switch
		{
			CheatEngineFailureKind.TargetNotAttached => (ToolErrorKind.NotAttached, AttachHint),
			CheatEngineFailureKind.TargetChanged or CheatEngineFailureKind.TargetIdentityUnavailable =>
				(ToolErrorKind.TargetChanged, TargetChangedHint),
			CheatEngineFailureKind.NotFound => (ToolErrorKind.NotFound, null),
			CheatEngineFailureKind.AmbiguousMatch => (ToolErrorKind.InvalidArgument, AmbiguousHint),
			CheatEngineFailureKind.ResultLimitExceeded => (ToolErrorKind.LimitExceeded, LimitHint),
			CheatEngineFailureKind.Cancelled => activationStopping
				? (ToolErrorKind.Stopping, ActivationEndedHint)
				: (ToolErrorKind.Cancelled, null),
			CheatEngineFailureKind.ActivationExpired => (ToolErrorKind.Stopping, ActivationEndedHint),
			CheatEngineFailureKind.InvalidState => activationStopping
				? (ToolErrorKind.Stopping, ActivationEndedHint)
				: (ToolErrorKind.InvalidState, null),
			CheatEngineFailureKind.RuntimeChanged => (ToolErrorKind.InvalidState, RuntimeChangedHint),
			CheatEngineFailureKind.CapabilityUnavailable or CheatEngineFailureKind.Unsupported =>
				(ToolErrorKind.Unsupported, null),
			CheatEngineFailureKind.OperationRejected or CheatEngineFailureKind.LuaError =>
				(ToolErrorKind.HostRefused, null),
			CheatEngineFailureKind.MemoryReadFailed => (ToolErrorKind.MemoryReadFailed, MemoryReadHint),
			CheatEngineFailureKind.MemoryWriteFailed => (ToolErrorKind.MemoryWriteFailed, MemoryWriteHint),
			CheatEngineFailureKind.IndeterminateHostResult => (ToolErrorKind.Internal, IndeterminateHint),
			_ => (ToolErrorKind.Internal, null)
		};
	}
}
