using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Modules;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>The read-only loaded-module snapshot of one Cheat Engine instance.</summary>
[McpServerResourceType]
public sealed class ModuleLiveResources(ModuleTools modules)
{
	private const string Uri = McpResourceUris.InstancePrefix + "modules";

	/// <summary>Reads the v2 module list with its default arguments.</summary>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>module_list</c>.</returns>
	[McpServerResource(UriTemplate = Uri, Name = "instance_modules", Title = "Loaded modules",
		MimeType = McpResourceUris.JsonMimeType)]
	[Description("The first page of modules loaded in this Cheat Engine target. Its JSON is the structured result of " +
				 CheatEngineToolNames.ModuleList +
				 " with default arguments; use that tool to filter, page or request details.")]
	public ReadResourceResult Modules(CancellationToken cancellationToken = default)
	{
		return LiveResourceResults.Json(Uri, JsonSerializer.Serialize(
			modules.List(cancellationToken: cancellationToken),
			ModuleJsonContext.Default.ModuleList));
	}
}
