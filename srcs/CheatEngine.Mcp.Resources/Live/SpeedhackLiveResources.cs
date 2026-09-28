using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Speedhack;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>The read-only speedhack projection of one Cheat Engine instance: the configured speed.</summary>
[McpServerResourceType]
public sealed class SpeedhackLiveResources(SpeedhackTools speedhack)
{
	private const string SpeedhackPath = McpResourceUris.InstancePrefix + "speedhack";

	/// <summary>Reads the speedhack state.</summary>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>speedhack_get_state</c>.</returns>
	[McpServerResource(UriTemplate = SpeedhackPath, Name = "instance_speedhack", Title = "Speedhack state",
		MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(SpeedhackTools), CheatEngineToolNames.SpeedhackGetState)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("Cheat Engine's last configured speedhack multiplier and whether its hook symbol exists; a speed " +
				 "other than 1 is a lasting host effect. Its JSON is the structured result of " +
				 CheatEngineToolNames.SpeedhackGetState + ".")]
	public ReadResourceResult Speedhack(CancellationToken cancellationToken = default)
	{
		return LiveResourceResults.Json(SpeedhackPath, JsonSerializer.Serialize(speedhack.GetState(cancellationToken),
			SpeedhackJsonContext.Default.SpeedhackState));
	}
}
