using CheatEngine.Client;

namespace CheatEngine.Mcp.Tools;

/// <summary>Checks actual CE callback ownership before a target or debugger transition.</summary>
public sealed class LuaDebuggerCaptureGuard(ICheatEngineClient client)
{
	internal object? PrepareForTransition() =>
		LuaToolRuntime.Execute(client, "debugger_capture_guard", LuaDebuggerScripts.Guard) is true
			? ToolExecution.Error("Debugger capture or step trace is still active; stop it before changing debugger state or target.")
			: null;
}
