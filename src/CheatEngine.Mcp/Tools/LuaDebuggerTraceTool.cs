using System.ComponentModel;

using CheatEngine.Client;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Exposes a finite trace of one thread beginning at a requested execute breakpoint.</summary>
[McpServerToolType]
public sealed class LuaDebuggerTraceTool(ICheatEngineClient client)
{
	[McpServerTool(Name = "debugger_start_step_trace"), Description("Break at an address and single-step one thread for a bounded number of contexts, then leave it stopped. Requires no existing breakpoints or global breakpoint hook. Results expire 30 seconds after completion or timeout.")]
	public object Start([Description("Entry instruction address expression.")] string address,
		[Description("Maximum contexts including the entry instruction, 1-256.")] int maximumSteps = 32,
		[Description("Maximum trace lifetime in seconds, 1-60.")] int lifetimeSeconds = 30)
	{
		if (string.IsNullOrWhiteSpace(address) || maximumSteps is < 1 or > 256 || lifetimeSeconds is < 1 or > 60)
		{
			return ToolExecution.Error("address is required, maximumSteps must be between 1 and 256, and lifetimeSeconds must be between 1 and 60.");
		}
		return LuaDebuggerScripts.Invoke(client, "debugger_start_step_trace", LuaDebuggerScripts.Start,
			Guid.NewGuid().ToString("N"), address, "trace", "execute", 1, maximumSteps, lifetimeSeconds);
	}

	[McpServerTool(Name = "debugger_poll_step_trace"), Description("Copy collected trace contexts without waiting or consuming them.")]
	public object Poll([Description("Identifier returned by debugger_start_step_trace.")] string traceId) =>
		LuaDebuggerScripts.Invoke(client, "debugger_poll_step_trace", LuaDebuggerScripts.Poll, traceId, "trace", 256, false);

	[McpServerTool(Name = "debugger_stop_step_trace"), Description("Remove the owned trace breakpoint, hook, timer and results. Does not resume a stopped thread; cleanup failures retain the ID for retry.")]
	public object Stop([Description("Identifier returned by debugger_start_step_trace.")] string traceId) =>
		LuaDebuggerScripts.Invoke(client, "debugger_stop_step_trace", LuaDebuggerScripts.Stop, traceId, "trace");
}
