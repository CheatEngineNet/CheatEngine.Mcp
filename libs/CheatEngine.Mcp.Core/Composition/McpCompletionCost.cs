namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     What listing the values of a completed live template variable costs, which decides how the backend's
///     <c>completion/complete</c> handler asks for them (<see cref="McpCompletionAttribute" />).
/// </summary>
public enum McpCompletionCost
{
	/// <summary>
	///     Read from the activation's own memory without any Cheat Engine call, such as a scanner or pointer scan name:
	///     listed again for every request.
	/// </summary>
	Memory,

	/// <summary>
	///     Read with one short Cheat Engine dispatch, such as a module or structure name: listed off the request
	///     thread, cached for a few seconds per target-selection epoch, refreshed at most once per second, never waited
	///     on for long, and never listed while another dispatched listing of the server runs.
	/// </summary>
	Dispatch
}
