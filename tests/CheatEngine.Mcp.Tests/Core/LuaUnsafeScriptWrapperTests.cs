using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class LuaUnsafeScriptWrapperTests
{
	[Theory]
	[InlineData("return 1", 0)]
	[InlineData("return ']]'", 1)]
	[InlineData("return ']]', ']=]'", 2)]
	[InlineData("return ']=]', ']]', ']==]'", 3)]
	[InlineData("return ']==]'", 0)]
	[InlineData("local t = {1}; return t[1]", 1)]
	[InlineData("return 1 --]=", 0)]
	[InlineData("return 1 --]]]=", 2)]
	[InlineData("]", 1)]
	[InlineData("", 0)]
	public void SelectLevel_ClosingBracketsInSource_PicksTheSmallestLevelTheSourceCannotClose(string source,
		int expected)
	{
		Assert.Equal(expected, LuaUnsafeScriptWrapper.SelectLevel(source));
	}

	[Fact]
	public void Build_HostileSourceAndChunkName_KeepsThemAsLiteralData()
	{
		const string source = "return ']]', ']=]' -- \"quoted\" \\ \n next line";
		const string chunkName = "=name \" with ' quotes \\ and\nnewline ]]";

		LuaUnsafeScriptWrapper.WrappedScript script = LuaUnsafeScriptWrapper.Build(source, chunkName);

		Assert.Contains("__load([==[\n" + source + "]==], ", script.Source, StringComparison.Ordinal);
		Assert.Contains("\"=name \\034 with ' quotes \\092 and\\010newline ]]\", 't')", script.Source,
			StringComparison.Ordinal);
		Assert.DoesNotContain(chunkName, script.Source, StringComparison.Ordinal);
		Assert.Matches("^[0-9a-f]{32}$", script.Token);
		Assert.Equal(2, script.Source.Split(script.Token).Length - 1);
		Assert.NotEqual(script.Token, LuaUnsafeScriptWrapper.Build(source, chunkName).Token);
		Assert.Contains("\"=lua_execute\", 't')", LuaUnsafeScriptWrapper.Build("return 1", null).Source,
			StringComparison.Ordinal);
		Assert.Contains("__load([[\r\rreturn 1]], ", LuaUnsafeScriptWrapper.Build("\rreturn 1", null).Source,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Build_InvalidTextOrOversizedInput_ThrowsBeforeDispatch()
	{
		Assert.Throws<ArgumentException>(() => LuaUnsafeScriptWrapper.Build("return '\uD800'", null));
		Assert.Throws<ArgumentException>(() => LuaUnsafeScriptWrapper.Build("return 1", "\uDC00"));
		Assert.Throws<ArgumentException>(() => LuaUnsafeScriptWrapper.Build("return 1", string.Empty));
		Assert.Throws<ArgumentException>(() => LuaUnsafeScriptWrapper.Build("return 1",
			new string('n', LuaUnsafeScriptWrapper.MaximumChunkNameBytes + 1)));
		Assert.Throws<ArgumentException>(() =>
			LuaUnsafeScriptWrapper.Build(new string('x', LuaUnsafeScriptWrapper.MaximumSourceBytes + 1), null));
		Assert.Throws<ArgumentNullException>(() => LuaUnsafeScriptWrapper.Build(null!, null));
	}

	[Fact]
	public void ReadResult_FixedBody_ClearsTheGlobalFirstAndNeverLoadsCode()
	{
		string[] lines = LuaUnsafeScriptWrapper.ReadResult.Split('\n');

		Assert.StartsWith("local result = rawget(_ENV, '" + LuaUnsafeScriptWrapper.ResultGlobal + "')", lines[0],
			StringComparison.Ordinal);
		Assert.StartsWith("rawset(_ENV, '" + LuaUnsafeScriptWrapper.ResultGlobal + "', nil)", lines[1],
			StringComparison.Ordinal);
		LuaFixedScriptAssert.NeverLoadsCode(LuaUnsafeScriptWrapper.ReadResult);
	}
}
