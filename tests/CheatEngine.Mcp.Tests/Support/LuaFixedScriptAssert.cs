namespace CheatEngine.Mcp.Tests.Support;

/// <summary>Checks that implementation-owned Lua never compiles code or reads a caller-chosen global.</summary>
internal static class LuaFixedScriptAssert
{
	private static readonly string[] ForbiddenFragments =
	[
		"load(", "loadstring(", "dofile(", "loadfile(", "require(", "_G[a", "rawget(_G,a", "rawget(_G, a",
		"_ENV[a", "rawget(_ENV, a", "debug.", "os."
	];

	internal static void NeverLoadsCode(string script)
	{
		foreach (string fragment in ForbiddenFragments)
		{
			Assert.DoesNotContain(fragment, script, StringComparison.Ordinal);
		}
	}
}
