namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>A CE pointer-map root. Minus one names a static absolute address.</summary>
internal readonly record struct PointerStaticRoot(int ModuleIndex, ulong Offset);

/// <summary>The optional static-base address range stored in a CE pointer map.</summary>
internal readonly record struct PointerStaticRange(ulong Start, ulong End);
