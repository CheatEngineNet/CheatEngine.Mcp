namespace CheatEngine.Mcp.Tools;

internal sealed record PointerModule(string Name, ulong BaseAddress, ulong Size)
{
	internal bool Contains(ulong address)
	{
		return address >= BaseAddress && address - BaseAddress < Size;
	}
}
