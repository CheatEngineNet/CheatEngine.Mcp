namespace CheatEngine.Mcp.Tools;

internal sealed record PointerSearchResult(PointerPath[] Paths, bool Truncated, int VisitedNodes);
