using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Runtime;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>The read-only runtime snapshot of one Cheat Engine instance.</summary>
[McpServerResourceType]
public sealed class RuntimeLiveResources(RuntimeTools runtime)
{
	private const string Uri = McpResourceUris.InstancePrefix + "runtime";

	/// <summary>Reads the v2 runtime overview with its default arguments.</summary>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>runtime_get_overview</c>.</returns>
	[McpServerResource(UriTemplate = Uri, Name = "instance_runtime", Title = "Runtime overview",
		MimeType = McpResourceUris.JsonMimeType)]
	[Description("The runtime overview of this Cheat Engine instance. Its JSON is the structured result of " +
				 CheatEngineToolNames.RuntimeGetOverview + ".")]
	public ReadResourceResult Runtime(CancellationToken cancellationToken = default)
	{
		return LiveResourceResults.Json(Uri, JsonSerializer.Serialize(runtime.GetOverview(cancellationToken),
			RuntimeJsonContext.Default.RuntimeOverviewResult));
	}
}
