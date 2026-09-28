using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Structures;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>
///     The read-only Structure Dissect projections of one Cheat Engine instance: the structures and one structure's
///     elements. The <c>structure</c> variable completes from the global structure names.
/// </summary>
[McpServerResourceType]
public sealed class StructureLiveResources(StructureTools structures) : IMcpCompletionSource
{
	private const string StructuresPath = McpResourceUris.InstancePrefix + "structures";
	private const string StructureVariable = "structure";
	private const int DefaultStructures = 100;
	private const int MaximumStructures = 1000;
	private const int DefaultElements = 256;
	private const int MaximumElements = 1024;

	/// <summary>Reads a page of the global structures.</summary>
	/// <param name="offset">The index of the first structure; 0 when absent.</param>
	/// <param name="limit">The most structures to return; 100 when absent.</param>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>structure_list</c>.</returns>
	[McpServerResource(UriTemplate = StructuresPath + "{?offset,limit}", Name = "instance_structures",
		Title = "Structures", MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(StructureTools), CheatEngineToolNames.StructureList)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("A page of Cheat Engine's Structure Dissect definitions: 100 from offset 0 by default, or " +
				 "?offset=..&limit=.. in that order (limit 1 to 1000). Its JSON is the structured result of " +
				 CheatEngineToolNames.StructureList + "; use that tool to filter by name.")]
	public ReadResourceResult Structures(
		[Description("The index of the first structure, 0 by default.")]
		string? offset = null,
		[Description("The most structures to return, 1 to 1000, 100 by default.")]
		string? limit = null,
		CancellationToken cancellationToken = default)
	{
		int? first = McpResourceQuery.Number(offset, nameof(offset), 0, LiveResourceResults.MaximumOffset);
		int? count = McpResourceQuery.Number(limit, nameof(limit), 1, MaximumStructures);
		StructurePage page = structures.List(null, first ?? 0, count ?? DefaultStructures, cancellationToken);
		return LiveResourceResults.Json(
			McpResourceQuery.WithQuery(StructuresPath, ("offset", first), ("limit", count)),
			JsonSerializer.Serialize(page, StructuresJsonContext.Default.StructurePage));
	}

	/// <summary>Reads one structure and a page of its elements.</summary>
	/// <param name="structure">The structure's case-sensitive name.</param>
	/// <param name="offset">The index of the first element; 0 when absent.</param>
	/// <param name="limit">The most elements to return; 256 when absent.</param>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>structure_get</c>.</returns>
	[McpServerResource(UriTemplate = StructuresPath + "/{structure}{?offset,limit}", Name = "instance_structure",
		Title = "Structure definition", MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(StructureTools), CheatEngineToolNames.StructureGet)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("One structure by its case-sensitive, percent-encoded name, with a page of its elements ordered by " +
				 "offset: 256 from index 0 by default, or ?offset=..&limit=.. in that order (limit 1 to 1024), in " +
				 "the concise format. Its JSON is the structured result of " + CheatEngineToolNames.StructureGet +
				 "; use that tool for the detailed format.")]
	public ReadResourceResult Structure(
		[Description("The structure's case-sensitive name.")]
		[McpCompletion(McpCompletionCost.Dispatch)]
		string structure,
		[Description("The index of the first element, 0 by default.")]
		string? offset = null,
		[Description("The most elements to return, 1 to 1024, 256 by default.")]
		string? limit = null,
		CancellationToken cancellationToken = default)
	{
		string name = McpResourceQuery.Required(structure, nameof(structure));
		int? first = McpResourceQuery.Number(offset, nameof(offset), 0, LiveResourceResults.MaximumOffset);
		int? count = McpResourceQuery.Number(limit, nameof(limit), 1, MaximumElements);
		StructureDefinition definition = structures.Get(name, first ?? 0, count ?? DefaultElements,
			ResultFormat.Concise, cancellationToken);
		return LiveResourceResults.Json(
			McpResourceQuery.WithQuery(StructuresPath + "/" + McpResourceQuery.Segment(name), ("offset", first),
				("limit", count)),
			JsonSerializer.Serialize(definition, StructuresJsonContext.Default.StructureDefinition));
	}

	/// <summary>
	///     Lists the names of the first 1000 global structures for <c>structure</c>, in one short dispatch. Structures
	///     belong to Cheat Engine, not to the attached process, so the listing carries no selection epoch.
	/// </summary>
	/// <param name="variable">The completed variable: <c>structure</c>.</param>
	/// <param name="cancellationToken">The listing's cancellation.</param>
	/// <returns>The structure names in Cheat Engine's order.</returns>
	/// <exception cref="CheatEngineToolException">The dispatch failed.</exception>
	public McpCompletionValues ListCompletionValues(string variable, CancellationToken cancellationToken)
	{
		return string.Equals(variable, StructureVariable, StringComparison.Ordinal)
			? new McpCompletionValues(
			[
				.. structures.List(null, 0, MaximumStructures, cancellationToken).Structures
					.Select(static summary => summary.Name)
			])
			: throw new ArgumentOutOfRangeException(nameof(variable), variable, "Only structure is completed.");
	}
}
