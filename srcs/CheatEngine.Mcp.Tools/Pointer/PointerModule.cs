namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>A module of the captured process, the static root of module-relative paths.</summary>
/// <param name="Name">The module name, such as <c>game.exe</c>.</param>
/// <param name="BaseAddress">The module base address.</param>
/// <param name="Size">The module image size in bytes.</param>
internal sealed record PointerModule(string Name, ulong BaseAddress, ulong Size)
{
	/// <summary>Whether an address lies inside the module image.</summary>
	/// <param name="address">The address.</param>
	/// <returns><see langword="true" /> inside <c>[BaseAddress, BaseAddress + Size)</c>.</returns>
	internal bool Contains(ulong address)
	{
		return address >= BaseAddress && address - BaseAddress < Size;
	}
}
