namespace CheatEngine.Mcp.Tests.Core;

public sealed class LuaToolRuntimeTests
{
	[Fact]
	public void BuildSource_HostileTextAndNilArguments_EncodesDataAndPreservesPositions()
	{
		const string hostile = "quote\" slash\\ newline\nUnicode: \u2603 \U0001F98A";

		string source = LuaToolRuntime.BuildSource("return a", [hostile, null, "tail"]);

		Assert.StartsWith("local a = { n = 3, [1] = \"", source);
		Assert.Contains("[2] = nil", source, StringComparison.Ordinal);
		Assert.Contains("[3] = \"tail\"", source, StringComparison.Ordinal);
		Assert.Contains("\\034", source, StringComparison.Ordinal);
		Assert.Contains("\\092", source, StringComparison.Ordinal);
		Assert.Contains("\\010", source, StringComparison.Ordinal);
		Assert.DoesNotContain(hostile, source, StringComparison.Ordinal);
		Assert.DoesNotContain("\u2603", source, StringComparison.Ordinal);
		Assert.EndsWith(" };\nreturn a", source, StringComparison.Ordinal);
	}

	[Fact]
	public void BuildSource_UnsignedPointerAndNestedValues_UsesLuaLiteralForms()
	{
		string source = LuaToolRuntime.BuildSource("return a", [ulong.MaxValue, new object?[] { true, 7L, null }]);

		Assert.Contains("[1] = 0xFFFFFFFFFFFFFFFF", source, StringComparison.Ordinal);
		Assert.Contains("[2] = {true,7,nil,}", source, StringComparison.Ordinal);
	}

	[Fact]
	public void BuildSource_UnsupportedOrOversizedValue_ThrowsBeforeLuaDispatch()
	{
		Assert.Throws<ArgumentException>(() => LuaToolRuntime.BuildSource("return a", [double.PositiveInfinity]));
		Assert.Throws<ArgumentException>(() => LuaToolRuntime.BuildSource("return a", [new object()]));
		Assert.Throws<ArgumentException>(() =>
			LuaToolRuntime.BuildSource("return a", [new string('x', (LuaToolRuntime.MaximumStringBytes / 4) + 1)]));
	}
}
