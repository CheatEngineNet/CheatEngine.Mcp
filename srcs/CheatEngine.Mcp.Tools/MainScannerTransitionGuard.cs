using CheatEngine.Client;
using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools;

/// <summary>Refuses a target change while CE's visible main scan is running or repeating; the main scan owns no lease.</summary>
/// <param name="client">The activation's Client.</param>
public sealed class MainScannerTransitionGuard(ICheatEngineClient client) : ITargetTransitionGuard
{
	/// <summary>Checks the main scanner inside the dispatch that changes the target.</summary>
	/// <param name="transition">The selected and the requested process.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	public void EnsureCanChangeTarget(TargetTransition transition, CancellationToken cancellationToken)
	{
		if (!transition.IsReselection && MainScanner.IsBusy(client))
		{
			throw CheatEngineToolException.Busy(MainScanner.BusyTargetMessage,
				"Poll the main scan status until it is idle, or cancel it in Cheat Engine.");
		}
	}
}
