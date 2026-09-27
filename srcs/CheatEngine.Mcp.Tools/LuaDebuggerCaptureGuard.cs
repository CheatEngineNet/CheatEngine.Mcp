using CheatEngine.Client;
using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools;

/// <summary>Checks actual CE callback ownership before a target or debugger transition.</summary>
public sealed class LuaDebuggerCaptureGuard(ICheatEngineClient client) : ITargetTransitionGuard
{
	internal const string ActiveMessage =
		"Debugger capture or step trace is still active; stop it before changing debugger state or target.";

	/// <summary>Refuses a target change while a debugger capture or step trace owns CE callbacks. Inside a dispatch.</summary>
	/// <param name="transition">The selected and the requested process.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	public void EnsureCanChangeTarget(TargetTransition transition, CancellationToken cancellationToken)
	{
		if (!transition.IsReselection && PrepareForTransition() is not null)
		{
			throw CheatEngineToolException.Busy(ActiveMessage,
				"Stop the capture or trace with its stop tool, or wait for its lifetime to end.");
		}
	}

	internal object? PrepareForTransition()
	{
		return LuaToolRuntime.Execute(client, "debugger_capture_guard", LuaDebuggerScripts.Guard) is true
			? ToolExecution.Error(ActiveMessage)
			: null;
	}
}
