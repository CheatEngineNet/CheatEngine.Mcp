using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Scan;

/// <summary>Refuses a target change while Cheat Engine's visible main scan is running or repeating.</summary>
/// <param name="dispatch">The activation's bounded Client dispatch.</param>
internal sealed class MainScannerTransitionGuard(ToolDispatch dispatch) : ITargetTransitionGuard
{
	/// <summary>Checks the main scanner inside the dispatch that changes the target.</summary>
	/// <param name="transition">The selected and the requested process.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	public void EnsureCanChangeTarget(TargetTransition transition, CancellationToken cancellationToken)
	{
		if (!transition.IsReselection && ScanTools.IsMainScannerBusy(dispatch, cancellationToken))
		{
			throw CheatEngineToolException.Busy(ScanTools.MainScannerBusyMessage,
				"Poll the main scan status until it is idle, or cancel it in Cheat Engine.");
		}
	}
}
