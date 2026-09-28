using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Processes;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>The read-only process projections of one Cheat Engine instance: the selected process and its threads.</summary>
[McpServerResourceType]
public sealed class ProcessLiveResources(ProcessTools processes)
{
	private const string ProcessPath = McpResourceUris.InstancePrefix + "process";
	private const string ThreadsPath = McpResourceUris.InstancePrefix + "threads";

	/// <summary>Reads the v2 current-process result.</summary>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>process_get_current</c>.</returns>
	[McpServerResource(UriTemplate = ProcessPath, Name = "instance_process", Title = "Current process",
		MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(ProcessTools), CheatEngineToolNames.ProcessGetCurrent)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("The process currently selected in this Cheat Engine instance; isOpen=false is a normal unattached " +
				 "state. Its JSON is the structured result of " + CheatEngineToolNames.ProcessGetCurrent + ".")]
	public ReadResourceResult Process(CancellationToken cancellationToken = default)
	{
		return LiveResourceResults.Json(ProcessPath, JsonSerializer.Serialize(processes.GetCurrent(cancellationToken),
			ProcessJsonContext.Default.ProcessCurrentResult));
	}

	/// <summary>Reads the target's thread ids.</summary>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>process_list_threads</c>.</returns>
	[McpServerResource(UriTemplate = ThreadsPath, Name = "instance_threads", Title = "Target threads",
		MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(ProcessTools), CheatEngineToolNames.ProcessListThreads)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("Up to 4096 thread ids of the attached target, marked truncated when Cheat Engine reports more. Its " +
				 "JSON is the structured result of " + CheatEngineToolNames.ProcessListThreads + ".")]
	public ReadResourceResult Threads(CancellationToken cancellationToken = default)
	{
		return LiveResourceResults.Json(ThreadsPath, JsonSerializer.Serialize(processes.ListThreads(cancellationToken),
			ProcessJsonContext.Default.ProcessThreadListResult));
	}
}
