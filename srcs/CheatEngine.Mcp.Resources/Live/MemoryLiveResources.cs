using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Memory;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>The read-only memory-map snapshot of one Cheat Engine instance.</summary>
[McpServerResourceType]
public sealed class MemoryLiveResources(MemoryInfoTools memory)
{
	private const string Uri = McpResourceUris.InstancePrefix + "memory/regions";

	/// <summary>Reads the v2 memory-region list with its default arguments.</summary>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>memory_list_regions</c>.</returns>
	[McpServerResource(UriTemplate = Uri, Name = "instance_memory_regions", Title = "Memory regions",
		MimeType = McpResourceUris.JsonMimeType)]
	[Description("The first page of committed memory regions in this Cheat Engine target. Its JSON is the structured " +
				 "result of " + CheatEngineToolNames.MemoryListRegions +
				 "; use that tool to select another state, range, module or page.")]
	public ReadResourceResult Regions(CancellationToken cancellationToken = default)
	{
		RegionList regions = memory.ListRegions(cancellationToken: cancellationToken);
		return LiveResourceResults.Json(Uri, JsonSerializer.Serialize(regions,
			MemoryJsonContext.Default.RegionList));
	}
}
