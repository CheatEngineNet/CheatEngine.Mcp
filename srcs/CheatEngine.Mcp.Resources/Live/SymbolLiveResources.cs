using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Symbol;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>The read-only symbol projection of one Cheat Engine instance: the registered symbols.</summary>
[McpServerResourceType]
public sealed class SymbolLiveResources(SymbolRegistrationTools symbols)
{
	private const string SymbolsPath = McpResourceUris.InstancePrefix + "symbols";
	private const int DefaultSymbols = 200;
	private const int MaximumSymbols = 1000;

	/// <summary>Reads a page of the registered symbols.</summary>
	/// <param name="offset">The index of the first symbol; 0 when absent.</param>
	/// <param name="limit">The most symbols to return; 200 when absent.</param>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>symbol_list_registered</c>.</returns>
	[McpServerResource(UriTemplate = SymbolsPath + "{?offset,limit}", Name = "instance_symbols",
		Title = "Registered symbols", MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(SymbolRegistrationTools), CheatEngineToolNames.SymbolListRegistered, PreparedProjection = nameof(SymbolRegistrationTools.ListPreparedRegistered))]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("A page of the latest global registered-symbol copy explicitly prepared by symbol_list_registered, with " +
				 "ownedByMcp on those this activation can unregister: 200 from offset 0 by default, or ?offset=..&limit=.. " +
				 "in that order (limit 1 to 1000). The copy expires after five seconds and contains at most 8192 symbols; " +
				 "that copy cap does not bound Cheat Engine's native enumeration. Run symbol_list_registered before reading this " +
				 "resource; use the tool to filter by name.")]
	public ReadResourceResult Symbols(
		[Description("The index of the first symbol, 0 by default.")]
		string? offset = null,
		[Description("The most symbols to return, 1 to 1000, 200 by default.")]
		string? limit = null,
		CancellationToken cancellationToken = default)
	{
		int? first = McpResourceQuery.Number(offset, nameof(offset), 0, LiveResourceResults.MaximumOffset);
		int? count = McpResourceQuery.Number(limit, nameof(limit), 1, MaximumSymbols);
		RegisteredSymbolList page = symbols.ListPreparedRegistered(first ?? 0, count ?? DefaultSymbols,
			cancellationToken);
		return LiveResourceResults.Json(McpResourceQuery.WithQuery(SymbolsPath, ("offset", first), ("limit", count)),
			JsonSerializer.Serialize(page, SymbolJsonContext.Default.RegisteredSymbolList));
	}
}
