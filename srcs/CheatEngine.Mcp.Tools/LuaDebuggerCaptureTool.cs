using System.ComponentModel;

using CheatEngine.Client;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Exposes bounded, CE-owned write/access breakpoint collectors.</summary>
[McpServerToolType]
public sealed class LuaDebuggerCaptureTool(ICheatEngineClient client)
{
	[McpServerTool(Name = "debugger_start_capture")]
	[Description(
		"Find instructions accessing or writing an address using a pollable breakpoint capture. Hits contain trap IP, thread ID and registers. Automatically expires; poll results within 30 seconds of expiry.")]
	public object StartCapture([Description("Target address expression or number.")] string address,
		[Description("execute, access, or write.")]
		string trigger = "write",
		[Description("Aligned data-watch size: 1, 2, 4, or 8.")]
		int size = 4,
		[Description("Maximum buffered hits, 1-1024; excess hits are counted as dropped.")]
		int maximumHits = 256,
		[Description("Capture lifetime in seconds, 1-300.")]
		int lifetimeSeconds = 60)
	{
		if (string.IsNullOrWhiteSpace(address) || size is not 1 and not 2 and not 4 and not 8 ||
		    maximumHits is < 1 or > 1024 || lifetimeSeconds is < 1 or > 300)
		{
			return ToolExecution.Error(
				"address is required, size must be 1, 2, 4, or 8, maximumHits must be between 1 and 1024, and lifetimeSeconds must be between 1 and 300.");
		}

		if (trigger is not ("execute" or "access" or "write"))
		{
			return ToolExecution.Error("trigger must be execute, access, or write.");
		}

		return LuaDebuggerScripts.Invoke(client, "debugger_start_capture", LuaDebuggerScripts.Start,
			Guid.NewGuid().ToString("N"), address, "capture", trigger, size, maximumHits, lifetimeSeconds);
	}

	[McpServerTool(Name = "debugger_poll_capture")]
	[Description("Read buffered breakpoint hits in FIFO order without waiting; clear removes only the returned hits.")]
	public object PollCapture([Description("Capture identifier returned by debugger_start_capture.")] string captureId,
		[Description("Maximum copied hits, 1-1024.")]
		int maximumHits = 256,
		[Description("Remove the returned hits from the buffer.")]
		bool clear = true)
	{
		return maximumHits is < 1 or > 1024
			? ToolExecution.Error("maximumHits must be between 1 and 1024.")
			: LuaDebuggerScripts.Invoke(client, "debugger_poll_capture", LuaDebuggerScripts.Poll, captureId, "capture",
				maximumHits, clear);
	}

	[McpServerTool(Name = "debugger_stop_capture")]
	[Description("Remove the owned capture breakpoint, timer, and results. Cleanup failures retain the ID for retry.")]
	public object StopCapture([Description("Capture identifier returned by debugger_start_capture.")] string captureId)
	{
		return LuaDebuggerScripts.Invoke(client, "debugger_stop_capture", LuaDebuggerScripts.Stop, captureId,
			"capture");
	}
}
