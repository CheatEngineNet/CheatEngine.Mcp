using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Structures;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>The read-only Structure Dissect snapshot of one Cheat Engine instance.</summary>
[McpServerResourceType]
public sealed class StructureLiveResources(StructureTools structures)
{
	private const string Uri = McpResourceUris.InstancePrefix + "structures";

	/// <summary>Reads the v2 structure list with its default arguments.</summary>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>structure_list</c>.</returns>
	[McpServerResource(UriTemplate = Uri, Name = "instance_structures", Title = "Structures",
		MimeType = McpResourceUris.JsonMimeType)]
	[Description(
		"The first page of Structure Dissect definitions in Cheat Engine. Its JSON is the structured result of " +
		CheatEngineToolNames.StructureList + " with default arguments; use that tool to filter or page.")]
	public ReadResourceResult Structures(CancellationToken cancellationToken = default)
	{
		return LiveResourceResults.Json(Uri, JsonSerializer.Serialize(
			structures.List(cancellationToken: cancellationToken),
			StructuresJsonContext.Default.StructurePage));
	}
}
