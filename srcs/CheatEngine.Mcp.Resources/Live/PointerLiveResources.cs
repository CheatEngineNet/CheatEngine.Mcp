using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Pointer;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>
///     The read-only pointer projections of one Cheat Engine instance: the pointer maps, the pointer scans and one scan's
///     paths. None of them calls Cheat Engine, and neither does the completion of <c>scanName</c>.
/// </summary>
[McpServerResourceType]
public sealed class PointerLiveResources(PointerMapTools maps, PointerScanTools scans) : IMcpCompletionSource
{
	private const string MapsPath = McpResourceUris.InstancePrefix + "pointer-maps";
	private const string ScansPath = McpResourceUris.InstancePrefix + "pointer-scans";
	private const string ScanNameVariable = "scanName";
	private const int DefaultPaths = 100;
	private const int MaximumPaths = 500;

	/// <summary>Reads the pointer maps and their capture progress.</summary>
	/// <returns>The structured result of <c>pointer_list_maps</c>.</returns>
	[McpServerResource(UriTemplate = MapsPath, Name = "instance_pointer_maps", Title = "Pointer maps",
		MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(PointerMapTools), CheatEngineToolNames.PointerListMaps)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("The pointer maps of this activation with their capture progress, oldest first, read without any " +
				 "Cheat Engine call. Its JSON is the structured result of " + CheatEngineToolNames.PointerListMaps +
				 ".")]
	public ReadResourceResult Maps()
	{
		return LiveResourceResults.Json(MapsPath,
			JsonSerializer.Serialize(maps.ListMaps(), PointerJsonContext.Default.PointerMapList));
	}

	/// <summary>Reads the pointer scans and their search progress.</summary>
	/// <returns>The structured result of <c>pointer_list_scans</c>.</returns>
	[McpServerResource(UriTemplate = ScansPath, Name = "instance_pointer_scans", Title = "Pointer scans",
		MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(PointerScanTools), CheatEngineToolNames.PointerListScans)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("The pointer scans of this activation with their search progress and counts, oldest first, read " +
				 "without any Cheat Engine call. Its JSON is the structured result of " +
				 CheatEngineToolNames.PointerListScans + ".")]
	public ReadResourceResult Scans()
	{
		return LiveResourceResults.Json(ScansPath,
			JsonSerializer.Serialize(scans.ListScans(), PointerJsonContext.Default.PointerScanList));
	}

	/// <summary>Reads a page of one pointer scan's paths.</summary>
	/// <param name="scanName">The scan name.</param>
	/// <param name="offset">The index of the first path; 0 when absent.</param>
	/// <param name="limit">The most paths to return; 100 when absent.</param>
	/// <returns>The structured result of <c>pointer_list_paths</c>.</returns>
	[McpServerResource(UriTemplate = ScansPath + "/{scanName}/paths{?offset,limit}", Name = "instance_pointer_paths",
		Title = "Pointer paths", MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(PointerScanTools), CheatEngineToolNames.PointerListPaths)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("A page of the stored paths of one pointer scan, named in the percent-encoded path, fewest levels " +
				 "first: 100 from offset 0 by default, or ?offset=..&limit=.. in that order (limit 1 to 500); read " +
				 "without any Cheat Engine call. Its JSON is the structured result of " +
				 CheatEngineToolNames.PointerListPaths + "; use that tool for another order or a module filter.")]
	public ReadResourceResult Paths(
		[Description("The pointer scan whose paths are listed.")]
		[McpCompletion(McpCompletionCost.Memory)]
		string scanName,
		[Description("The index of the first path, 0 by default.")]
		string? offset = null,
		[Description("The most paths to return, 1 to 500, 100 by default.")]
		string? limit = null)
	{
		string name = McpResourceQuery.Required(scanName, nameof(scanName));
		int? first = McpResourceQuery.Number(offset, nameof(offset), 0, LiveResourceResults.MaximumOffset);
		int? count = McpResourceQuery.Number(limit, nameof(limit), 1, MaximumPaths);
		PointerPathPage page = scans.ListPaths(name, first ?? 0, count ?? DefaultPaths);
		return LiveResourceResults.Json(
			McpResourceQuery.WithQuery(ScansPath + "/" + McpResourceQuery.Segment(name) + "/paths", ("offset", first),
				("limit", count)),
			JsonSerializer.Serialize(page, PointerJsonContext.Default.PointerPathPage));
	}

	/// <summary>Lists this activation's pointer scan names for <c>scanName</c>, oldest first, from memory.</summary>
	/// <param name="variable">The completed variable: <c>scanName</c>.</param>
	/// <param name="cancellationToken">Unused: the scans are read from memory.</param>
	/// <returns>The scan names.</returns>
	public McpCompletionValues ListCompletionValues(string variable, CancellationToken cancellationToken)
	{
		return string.Equals(variable, ScanNameVariable, StringComparison.Ordinal)
			? new McpCompletionValues([.. scans.ListScans().Scans.Select(static scan => scan.ScanName)])
			: throw new ArgumentOutOfRangeException(nameof(variable), variable, "Only scanName is completed.");
	}
}
