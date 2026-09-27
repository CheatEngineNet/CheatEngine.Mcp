namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>What one search of a pointer map found.</summary>
/// <param name="Paths">The paths found, in traversal order.</param>
/// <param name="Truncated">Whether the node or result limit stopped the traversal.</param>
/// <param name="VisitedNodes">How many candidate pointers were visited.</param>
/// <param name="Cancelled">Whether the search was cancelled; <paramref name="Paths" /> then holds what it found so far.</param>
internal sealed record PointerSearchResult(PointerPath[] Paths, bool Truncated, int VisitedNodes, bool Cancelled);

/// <summary>The limits of one search of a pointer map.</summary>
/// <param name="Target">The address the paths must reach.</param>
/// <param name="MaximumDepth">The most dereferences in a path, 1 to 8.</param>
/// <param name="MaximumOffset">The largest offset magnitude after a dereference.</param>
/// <param name="AllowNegativeOffsets">Whether a pointer may point past the address it leads to.</param>
/// <param name="StaticRootsOnly">Whether only roots inside a module are kept.</param>
/// <param name="MaximumResults">The most paths kept.</param>
/// <param name="MaximumNodes">The most candidate pointers visited.</param>
internal readonly record struct PointerSearchOptions(
	ulong Target,
	int MaximumDepth,
	int MaximumOffset,
	bool AllowNegativeOffsets,
	bool StaticRootsOnly,
	int MaximumResults,
	int MaximumNodes);
