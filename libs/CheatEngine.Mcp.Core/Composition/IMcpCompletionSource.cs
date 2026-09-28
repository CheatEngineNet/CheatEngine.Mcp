namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     A live resource container that lists the current values of its template variables marked with
///     <see cref="McpCompletionAttribute" />. The backend's <c>completion/complete</c> handler filters the values by
///     the typed prefix and never lets a failure reach the client.
/// </summary>
/// <remarks>
///     A <see cref="McpCompletionCost.Dispatch" /> variable is listed off the request thread, at most once at a time
///     across the server, and the request waits only briefly for it. A <see cref="McpCompletionCost.Memory" /> variable
///     is listed on the request thread for every keystroke, so its listing must be cheap, bounded, thread-safe and
///     never block: it must not call Cheat Engine, wait on a lock held across a dispatch, or read state that only
///     Cheat Engine's main thread may touch.
/// </remarks>
public interface IMcpCompletionSource
{
	/// <summary>Lists every current value of one marked variable, unfiltered, in the host's order.</summary>
	/// <param name="variable">The variable's name, such as <c>module</c>.</param>
	/// <param name="cancellationToken">The listing's token; a dispatch also observes the activation's stopping.</param>
	/// <returns>The values and, for a target-bound listing, the selection epoch they were read in.</returns>
	/// <exception cref="Contract.CheatEngineToolException">Nothing could be listed, so nothing is offered.</exception>
	public McpCompletionValues ListCompletionValues(string variable, CancellationToken cancellationToken);
}
