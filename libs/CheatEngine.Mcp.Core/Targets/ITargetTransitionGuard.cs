namespace CheatEngine.Mcp.Core.Targets;

/// <summary>
///     Refuses an explicit target change while something it owns depends on the selected process, such as retained
///     resources, a running main scan or an active debugger job. Register every guard as an activation-scoped
///     <see cref="ITargetTransitionGuard" /> service; <see cref="TargetTransitionGuards" /> consults all of them.
/// </summary>
public interface ITargetTransitionGuard
{
	/// <summary>
	///     Checks a target change. Call it inside the dispatch that changes the target, before the change; a reselection of
	///     the selected process (<see cref="TargetTransition.IsReselection" />) is not a change and is always allowed.
	/// </summary>
	/// <param name="transition">The selected and the requested process.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <exception cref="Contract.CheatEngineToolException">
	///     The change is refused, as <c>busy</c> or <c>invalid_state</c> with <c>not_started</c>.
	/// </exception>
	public void EnsureCanChangeTarget(TargetTransition transition, CancellationToken cancellationToken);
}
