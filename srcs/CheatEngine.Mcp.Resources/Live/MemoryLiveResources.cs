using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Memory;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>
///     The read-only memory projections of one Cheat Engine instance: the memory map and the bytes at one address.
/// </summary>
/// <remarks>
///     <c>memory/{address}</c> matches every <c>memory/…</c> path, so no other resource may use a literal segment under
///     <c>memory/</c>; the region map is <c>regions</c> for that reason.
/// </remarks>
[McpServerResourceType]
public sealed class MemoryLiveResources(MemoryInfoTools memory, MemoryReadTools reads)
{
	private const string RegionsPath = McpResourceUris.InstancePrefix + "regions";
	private const string MemoryPath = McpResourceUris.InstancePrefix + "memory";
	private const int DefaultRegions = 100;
	private const int MaximumRegions = 2000;
	private const int DefaultBytes = 256;
	private const int MaximumBytes = 16384;

	/// <summary>Reads a page of the committed memory regions.</summary>
	/// <param name="offset">The index of the first region; 0 when absent.</param>
	/// <param name="limit">The most regions to return; 100 when absent.</param>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>memory_list_regions</c>.</returns>
	[McpServerResource(UriTemplate = RegionsPath + "{?offset,limit}", Name = "instance_regions",
		Title = "Memory regions", MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(MemoryInfoTools), CheatEngineToolNames.MemoryListRegions, PreparedProjection = nameof(MemoryInfoTools.ListPreparedRegions))]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("A page of the attached target's committed memory regions in address order: 100 from offset 0 by " +
				 "default (fewer than the tool's 500), or ?offset=..&limit=.. in that order (limit 1 to 2000). First " +
				 "run " + CheatEngineToolNames.MemoryListRegions + " explicitly; this resource reads its prepared " +
				 "snapshot and must be read within 5 seconds. Use that tool to filter by range, module, state, backing " +
				 "or protection.")]
	public ReadResourceResult Regions(
		[Description("The index of the first region, 0 by default.")]
		string? offset = null,
		[Description("The most regions to return, 1 to 2000, 100 by default.")]
		string? limit = null,
		CancellationToken cancellationToken = default)
	{
		int? first = McpResourceQuery.Number(offset, nameof(offset), 0, LiveResourceResults.MaximumOffset);
		int? count = McpResourceQuery.Number(limit, nameof(limit), 1, MaximumRegions);
		RegionList regions = memory.ListPreparedRegions(offset: first ?? 0, limit: count ?? DefaultRegions,
			cancellationToken: cancellationToken);
		return LiveResourceResults.Json(McpResourceQuery.WithQuery(RegionsPath, ("offset", first), ("limit", count)),
			JsonSerializer.Serialize(regions, MemoryJsonContext.Default.RegionList));
	}

	/// <summary>Reads raw bytes at one address.</summary>
	/// <param name="address">The address or address expression.</param>
	/// <param name="size">The byte count; 256 when absent.</param>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>memory_read</c> with valueType bytes.</returns>
	[McpServerResource(UriTemplate = MemoryPath + "/{address}{?size}", Name = "instance_memory",
		Title = "Memory bytes", MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(MemoryReadTools), CheatEngineToolNames.MemoryRead)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("The bytes at one address of the attached target, as spaced hexadecimal: valueType bytes, 256 bytes " +
				 "by default or ?size=.. from 1 to 16384. The address is an address or Cheat Engine expression such " +
				 "as game.exe+1C, percent-encoded where it contains /, ?, # or &. Its JSON is the structured result " +
				 "of " + CheatEngineToolNames.MemoryRead + "; use that tool for typed values and strings.")]
	public ReadResourceResult Memory(
		[Description("An address or Cheat Engine address expression, such as game.exe+1C or 7FF6A1B2C3D0.")]
		string address,
		[Description("The byte count, 1 to 16384, 256 by default.")]
		string? size = null,
		CancellationToken cancellationToken = default)
	{
		string expression = McpResourceQuery.Required(address, nameof(address));
		int? bytes = McpResourceQuery.Number(size, nameof(size), 1, MaximumBytes);
		MemoryReadResult read = reads.Read(expression, McpValueType.Bytes, size: bytes ?? DefaultBytes,
			cancellationToken: cancellationToken);
		return LiveResourceResults.Json(
			McpResourceQuery.WithQuery(MemoryPath + "/" + McpResourceQuery.Segment(expression), ("size", bytes)),
			JsonSerializer.Serialize(read, MemoryJsonContext.Default.MemoryReadResult));
	}
}
