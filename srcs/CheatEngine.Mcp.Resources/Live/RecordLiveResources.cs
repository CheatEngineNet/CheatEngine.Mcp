using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Record;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>The read-only address-list projections of one Cheat Engine instance: the records and one record.</summary>
[McpServerResourceType]
public sealed class RecordLiveResources(RecordReadTools records)
{
	private const string RecordsPath = McpResourceUris.InstancePrefix + "records";
	private const int DefaultRecords = 100;
	private const int MaximumRecords = 1000;

	/// <summary>Reads a page of the top-level address-list records.</summary>
	/// <param name="offset">The index of the first record; 0 when absent.</param>
	/// <param name="limit">The most records to return; 100 when absent.</param>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>record_list</c>.</returns>
	[McpServerResource(UriTemplate = RecordsPath + "{?offset,limit}", Name = "instance_records",
		Title = "Address records", MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(RecordReadTools), CheatEngineToolNames.RecordList)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("A page of the top-level address-list records in this Cheat Engine instance: 100 from offset 0 by " +
				 "default, or ?offset=..&limit=.. in that order (limit 1 to 1000). Its JSON is the structured result " +
				 "of " + CheatEngineToolNames.RecordList + ".")]
	public ReadResourceResult Records(
		[Description("The index of the first top-level record, 0 by default.")]
		string? offset = null,
		[Description("The most records to return, 1 to 1000, 100 by default.")]
		string? limit = null,
		CancellationToken cancellationToken = default)
	{
		int? first = McpResourceQuery.Number(offset, nameof(offset), 0, LiveResourceResults.MaximumOffset);
		int? count = McpResourceQuery.Number(limit, nameof(limit), 1, MaximumRecords);
		RecordPage page = records.List(first ?? 0, count ?? DefaultRecords, cancellationToken: cancellationToken);
		return LiveResourceResults.Json(McpResourceQuery.WithQuery(RecordsPath, ("offset", first), ("limit", count)),
			JsonSerializer.Serialize(page, RecordJsonContext.Default.RecordPage));
	}

	/// <summary>Reads one record by its current id.</summary>
	/// <param name="recordId">The record id.</param>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>record_get</c> for that one id.</returns>
	[McpServerResource(UriTemplate = RecordsPath + "/{recordId}", Name = "instance_record", Title = "Address record",
		MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(RecordReadTools), CheatEngineToolNames.RecordGet)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("One address-list record by its current id, a non-negative decimal integer from records or a record " +
				 "tool. Ids expire when a table loads. Its JSON is the structured result of " +
				 CheatEngineToolNames.RecordGet + " for that one id.")]
	public ReadResourceResult Record(
		[Description("The record's current id, such as 12.")]
		string recordId,
		CancellationToken cancellationToken = default)
	{
		int id = McpResourceQuery.RequiredNumber(recordId, nameof(recordId), 0, int.MaxValue);
		return LiveResourceResults.Json(RecordsPath + "/" + id.ToString(CultureInfo.InvariantCulture),
			JsonSerializer.Serialize(records.Get([id], cancellationToken), RecordJsonContext.Default.RecordGetResult));
	}
}
