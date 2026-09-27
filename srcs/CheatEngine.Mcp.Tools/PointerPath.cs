namespace CheatEngine.Mcp.Tools;

internal sealed record PointerPath(ulong BaseAddress, string? Module, ulong ModuleOffset, long[] Offsets)
{
	internal string Verification
	{
		get;
		init;
	} = "snapshotMatch";
}
