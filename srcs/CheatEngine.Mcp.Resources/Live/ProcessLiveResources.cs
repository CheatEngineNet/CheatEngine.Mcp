using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Processes;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>The read-only selected-process snapshot of one Cheat Engine instance.</summary>
[McpServerResourceType]
public sealed class ProcessLiveResources(ProcessTools processes)
{
	private const string Uri = McpResourceUris.InstancePrefix + "process";

	/// <summary>Reads the v2 current-process result with its default arguments.</summary>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>process_get_current</c>.</returns>
	[McpServerResource(UriTemplate = Uri, Name = "instance_process", Title = "Current process",
		MimeType = McpResourceUris.JsonMimeType)]
	[Description("The process currently selected in this Cheat Engine instance. Its JSON is the structured result of " +
				 CheatEngineToolNames.ProcessGetCurrent + ".")]
	public ReadResourceResult Process(CancellationToken cancellationToken = default)
	{
		return LiveResourceResults.Json(Uri, JsonSerializer.Serialize(processes.GetCurrent(cancellationToken),
			ProcessJsonContext.Default.ProcessCurrentResult));
	}
}
