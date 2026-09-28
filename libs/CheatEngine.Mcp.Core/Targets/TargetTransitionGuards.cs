using System.Runtime.ExceptionServices;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>
///     Consults every registered <see cref="ITargetTransitionGuard" /> before an explicit target change: retained
///     resources and orphans (<see cref="TargetResources" />), a running main scan, active debugger jobs and any later
///     guard. Process tools call it inside the dispatch that changes the target, before the change.
/// </summary>
public sealed class TargetTransitionGuards
{
	private readonly ITargetTransitionGuard[] _guards;

	/// <summary>Creates the activation's guard set, in registration order.</summary>
	/// <param name="guards">The registered guards.</param>
	public TargetTransitionGuards(IEnumerable<ITargetTransitionGuard> guards)
	{
		ArgumentNullException.ThrowIfNull(guards);
		_guards = [.. guards];
		foreach (ITargetTransitionGuard guard in _guards)
		{
			ArgumentNullException.ThrowIfNull(guard, nameof(guards));
		}
	}

	/// <summary>The guards, in the order they are consulted.</summary>
	public IReadOnlyList<ITargetTransitionGuard> Guards => _guards;

	/// <summary>
	///     Checks a target change against every guard. A reselection of the selected process is not a change and consults
	///     none. Every guard is consulted even after one refused as <c>busy</c>, so the caller learns every blocker at once:
	///     a single refusal is rethrown unchanged and several become one <c>busy</c> error whose details list them. Any
	///     other failure, such as a guard's Lua error, stops the check at once and propagates, so nothing is changed.
	/// </summary>
	/// <param name="transition">The selected and the requested process.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <exception cref="CheatEngineToolException">The change is refused, or a guard failed.</exception>
	public void EnsureCanChangeTarget(TargetTransition transition, CancellationToken cancellationToken)
	{
		if (transition.IsReselection)
		{
			return;
		}

		List<CheatEngineToolException>? refusals = null;
		foreach (ITargetTransitionGuard guard in _guards)
		{
			try
			{
				guard.EnsureCanChangeTarget(transition, cancellationToken);
			}
			catch (CheatEngineToolException refusal) when (IsRefusal(refusal.Error))
			{
				(refusals ??= []).Add(refusal);
			}
		}

		if (refusals is null)
		{
			return;
		}

		if (refusals.Count == 1)
		{
			ExceptionDispatchInfo.Throw(refusals[0]);
		}

		TargetTransitionBlocker[] blockers =
		[
			.. refusals.Select(static refusal =>
				new TargetTransitionBlocker(refusal.Error.Message, refusal.Error.Hint, refusal.Error.Details))
		];
		JsonElement details = JsonSerializer.SerializeToElement(new TargetTransitionRefusals(blockers),
			StateJsonContext.Default.TargetTransitionRefusals);
		throw new CheatEngineToolException(new ToolError(ToolErrorKind.Busy,
			$"{blockers.Length} blockers refuse the target change: "
			+ string.Join(" ", blockers.Select(static blocker => blocker.Message)), null, ToolHostEffect.NotStarted,
			ToolFailureMapping.IsRetryable(ToolErrorKind.Busy, ToolHostEffect.NotStarted),
			"Resolve every listed blocker, then repeat the call.", details));
	}

	private static bool IsRefusal(ToolError error)
	{
		return error.Kind is ToolErrorKind.Busy or ToolErrorKind.InvalidState
			   && error.HostEffect is ToolHostEffect.NotStarted;
	}
}
