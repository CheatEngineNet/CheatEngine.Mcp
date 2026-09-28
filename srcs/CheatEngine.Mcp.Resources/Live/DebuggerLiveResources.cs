using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Debugger;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>The read-only debugger projections of one Cheat Engine instance: its state and its breakpoints.</summary>
[McpServerResourceType]
public sealed class DebuggerLiveResources(DebuggerTools debugger)
{
	private const string DebuggerPath = McpResourceUris.InstancePrefix + "debugger";
	private const int DefaultBreakpoints = 256;
	private const int MaximumBreakpoints = 1024;

	/// <summary>Reads the debugger state.</summary>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>debugger_get_status</c>.</returns>
	[McpServerResource(UriTemplate = DebuggerPath, Name = "instance_debugger", Title = "Debugger status",
		MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(DebuggerTools), CheatEngineToolNames.DebuggerGetStatus)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("Cheat Engine's debugger state: whether a debugger is attached and whether the target is broken, " +
				 "proven by its context. Its JSON is the structured result of " +
				 CheatEngineToolNames.DebuggerGetStatus + ".")]
	public ReadResourceResult Debugger(CancellationToken cancellationToken = default)
	{
		return LiveResourceResults.Json(DebuggerPath, JsonSerializer.Serialize(debugger.GetStatus(cancellationToken),
			DebuggerJsonContext.Default.DebuggerStatus));
	}

	/// <summary>Reads a bounded copy of the breakpoints.</summary>
	/// <param name="limit">The most breakpoints to copy; 256 when absent.</param>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>debugger_list_breakpoints</c>.</returns>
	[McpServerResource(UriTemplate = DebuggerPath + "/breakpoints{?limit}", Name = "instance_breakpoints",
		Title = "Debugger breakpoints", MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(DebuggerTools), CheatEngineToolNames.DebuggerListBreakpoints)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("The breakpoint addresses Cheat Engine reports, marking those this activation owns: 256 by default " +
				 "or ?limit=.. from 1 to 1024. Its JSON is the structured result of " +
				 CheatEngineToolNames.DebuggerListBreakpoints + ".")]
	public ReadResourceResult Breakpoints(
		[Description("The most breakpoint addresses to copy, 1 to 1024, 256 by default.")]
		string? limit = null,
		CancellationToken cancellationToken = default)
	{
		int? count = McpResourceQuery.Number(limit, nameof(limit), 1, MaximumBreakpoints);
		DebuggerBreakpointPage page = debugger.ListBreakpoints(count ?? DefaultBreakpoints, cancellationToken);
		return LiveResourceResults.Json(McpResourceQuery.WithQuery(DebuggerPath + "/breakpoints", ("limit", count)),
			JsonSerializer.Serialize(page, DebuggerJsonContext.Default.DebuggerBreakpointPage));
	}
}
