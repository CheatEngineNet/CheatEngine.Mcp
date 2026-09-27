using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Record;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>The read-only address-record snapshot of one Cheat Engine instance.</summary>
[McpServerResourceType]
public sealed class RecordLiveResources(RecordReadTools records)
{
	private const string Uri = McpResourceUris.InstancePrefix + "records";

	/// <summary>Reads the v2 record list with its default arguments.</summary>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>record_list</c>.</returns>
	[McpServerResource(UriTemplate = Uri, Name = "instance_records", Title = "Address records",
		MimeType = McpResourceUris.JsonMimeType)]
	[Description("The first page of address-list records in this Cheat Engine instance. Its JSON is the structured " +
				 "result of " + CheatEngineToolNames.RecordList + "; use that tool to page through the list.")]
	public ReadResourceResult Records(CancellationToken cancellationToken = default)
	{
		return LiveResourceResults.Json(Uri, JsonSerializer.Serialize(
			records.List(cancellationToken: cancellationToken),
			RecordJsonContext.Default.RecordPage));
	}
}
