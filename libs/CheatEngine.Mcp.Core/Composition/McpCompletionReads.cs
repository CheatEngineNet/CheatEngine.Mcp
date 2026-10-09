using CheatEngine.Client;
using CheatEngine.Client.Processes;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Execution;
using CheatEngine.Mcp.Core.Inspection;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     The Cheat Engine reads behind the completions of live resource templates, for containers that cannot call the
///     Client themselves: each is one short dispatch reported as <see cref="Operation" />, never a tool call. It takes
///     one of the activation's dispatch slots like a tool call does, so the completion handler runs at most one such
///     read at a time.
/// </summary>
public static class McpCompletionReads
{
	/// <summary>The operation name completion dispatches report in dispatch statistics and logs.</summary>
	public const string Operation = "completion/complete";

	/// <summary>The most module names one read copies; a larger module list offers no module names.</summary>
	public const int MaximumModules = 4096;

	/// <summary>
	///     Reads the attached process's module names and the target-selection epoch they belong to, in one dispatch.
	/// </summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="prepared">The activation's explicitly prepared module snapshots.</param>
	/// <param name="cancellationToken">The token of the listing.</param>
	/// <returns>The distinct module names in Cheat Engine's order, ignoring case, and the selection epoch.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>not_attached</c> without a process, <c>busy</c> when the dispatch limit is reached, or the Client's
	///     failure.
	/// </exception>
	public static McpCompletionValues ModuleNames(ToolDispatch dispatch, PreparedInspectionStore prepared,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(prepared);
		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run(Operation, token =>
		{
			ProcessSnapshot target = client.Processes.GetCurrentProcess(token);
			if (!prepared.TryGetModules(target, out PreparedModuleSnapshot snapshot))
			{
				throw CheatEngineToolException.InvalidState("No current prepared module snapshot is available.",
					"Run module_list without processId first.");
			}
			if (snapshot.Modules.Length > MaximumModules)
			{
				throw CheatEngineToolException.LimitExceeded("modules",
					$"Prepared module snapshots for completion may contain at most {MaximumModules} modules.");
			}

			string[] names =
			[
			.. snapshot.Modules.Select(static module => module.Name).Where(static name => !string.IsNullOrEmpty(name))
					.Distinct(StringComparer.OrdinalIgnoreCase)
			];
			return new McpCompletionValues(names, target.SelectionEpoch);
		}, cancellationToken);
	}
}
