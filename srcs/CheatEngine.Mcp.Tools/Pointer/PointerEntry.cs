namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>One captured pointer: the address that holds it and the value it holds.</summary>
/// <param name="Address">The target address of the pointer.</param>
/// <param name="Value">The pointer value read there.</param>
internal readonly record struct PointerEntry(ulong Address, ulong Value);
