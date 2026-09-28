using System.ComponentModel;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>A saved or loaded CE native pointer map.</summary>
/// <param name="FilePath">The normalized local path.</param>
/// <param name="Format">The native interchange format.</param>
/// <param name="Map">The map retained by this activation.</param>
public sealed record PointerMapFileResult(
	[property: Description("The normalized local file path.")] string FilePath,
	[property: Description("CE.scandata.v1.")] string Format,
	[property: Description("The map and its capture provenance.")] PointerMapInfo Map);

/// <summary>A saved or loaded MCP pointer-path scan.</summary>
/// <param name="FilePath">The normalized local path.</param>
/// <param name="Format">The MCP pointer-path file format.</param>
/// <param name="Scan">The scan retained by this activation.</param>
public sealed record PointerScanFileResult(
	[property: Description("The normalized local file path.")] string FilePath,
	[property: Description("The MCP pointer-scan JSON format.")] string Format,
	[property: Description("The scan and its retained paths.")] PointerScanInfo Scan);
