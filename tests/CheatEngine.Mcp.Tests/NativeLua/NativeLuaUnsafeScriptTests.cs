using System.Text;

using CheatEngine.Client.Results;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void LuaUnsafeScript_SourcesWithClosingBrackets_RunUnchanged()
	{
		using RuntimeScope scope = CreateScope();

		Assert.Equal(["]]", "]=]", "]==]", "]===]", "a]]b]=]c"],
			ReturnedStrings(ExecuteCallerLua("return ']]', ']=]', ']==]', ']===]', [==[a]]b]=]c]==]")));
		Assert.Equal("[7]", ReturnedJson(ExecuteCallerLua("local t = {7}; return t[1]")));
		Assert.Equal("[1]", ReturnedJson(ExecuteCallerLua("return 1 --]==")));
		Assert.Equal("[2]", ReturnedJson(ExecuteCallerLua("return 2 --]=")));
		Assert.Equal("[3]", ReturnedJson(ExecuteCallerLua("return 3 --]]")));
		Assert.Equal("[\"]\"]", ReturnedJson(ExecuteCallerLua("return (']')")));
		Assert.Null(ReadGlobal(LuaUnsafeScriptWrapper.ResultGlobal));
	}

	[Fact]
	public void LuaUnsafeScript_SyntaxAndRuntimeErrors_AreReportedAndClearTheGlobal()
	{
		using RuntimeScope scope = CreateScope();

		LuaExecuteOutcome syntax = ExecuteCallerLua("return +", "=user").Value!;
		Assert.False(syntax.Ok);
		Assert.Equal("compile", syntax.Phase);
		Assert.StartsWith("user:1:", syntax.Error, StringComparison.Ordinal);
		Assert.Null(ReadGlobal(LuaUnsafeScriptWrapper.ResultGlobal));

		LuaExecuteOutcome runtime = ExecuteCallerLua("local x = 1\nerror('boom')", "=user").Value!;
		Assert.False(runtime.Ok);
		Assert.Equal("runtime", runtime.Phase);
		Assert.Equal("user:2: boom", runtime.Error);
		Assert.Null(ReadGlobal(LuaUnsafeScriptWrapper.ResultGlobal));

		LuaExecuteOutcome tableError = ExecuteCallerLua("error({code = 1})").Value!;
		Assert.Equal("Lua raised a table error value.", tableError.Error);
		Assert.Null(ReadGlobal(LuaUnsafeScriptWrapper.ResultGlobal));

		LuaExecuteOutcome binary = ExecuteCallerLua("\u001bLuaS\0").Value!;
		Assert.Equal("compile", binary.Phase);
		Assert.Contains("binary chunk", binary.Error, StringComparison.Ordinal);
	}

	[Fact]
	public void LuaUnsafeScript_MultipleReturnsNilHolesAndOpaqueValues_AreCopiedAsBoundedValues()
	{
		using RuntimeScope scope = CreateScope();

		LuaJsonResult<LuaExecuteOutcome> mixed =
			ExecuteCallerLua("return 1, nil, 'x', print, {a = print, b = {1, 2}}, nil");
		Assert.True(mixed.Value!.Ok);
		Assert.Equal("""[1,null,"x",null,{"b":[1,2]},null]""", ReturnedJson(mixed));
		Assert.Equal(2, mixed.DroppedOpaqueCount);
		Assert.Equal("[]", ReturnedJson(ExecuteCallerLua("local unused = 1")));
		Assert.Equal("[null]", ReturnedJson(ExecuteCallerLua("return nil")));
		LuaJsonResult<LuaExecuteOutcome> function = ExecuteCallerLua("return function() end");
		Assert.Equal("[null]", ReturnedJson(function));
		Assert.Equal(1, function.DroppedOpaqueCount);
		Assert.Equal("[9223372036854775807,-1,\"FFFFFFFFFFFFFFFF\"]",
			ReturnedJson(ExecuteCallerLua("return math.maxinteger, -1, string.format('%X', -1)")));
	}

	[Fact]
	public void LuaUnsafeScript_ChunkNameWithQuotesAndNewlines_StaysALiteral()
	{
		using RuntimeScope scope = CreateScope();
		const string verbatim = "=quoted \" ' \\ ]] ]==] \n canary = true --";
		const string quoted = "x\"); canary = true; --";

		Assert.Equal([verbatim], ReturnedStrings(ExecuteCallerLua("return debug.getinfo(1, 'S').source", verbatim)));
		Assert.Equal("quoted \" ' \\ ]] ]==] \n canary = true --:1: boom",
			ExecuteCallerLua("error('boom')", verbatim).Value!.Error);
		Assert.Equal("[string \"x\"); canary = true; --\"]:1: boom",
			ExecuteCallerLua("error('boom')", quoted).Value!.Error);
		Assert.Null(ReadGlobal("canary"));
		Assert.Equal(["=lua_execute"], ReturnedStrings(ExecuteCallerLua("return debug.getinfo(1, 'S').source")));
	}

	[Fact]
	public void LuaUnsafeScript_LoadedChunk_SeesGlobalsAndKeepsItsLineNumbers()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("getTickCount = function() return 1234 end");

		Assert.Equal("[1234]", ReturnedJson(ExecuteCallerLua("return getTickCount()")));
		ExecuteCallerLua("shared = 41");
		Assert.Equal("[42]", ReturnedJson(ExecuteCallerLua("return shared + 1")));
		const string line = "return debug.getinfo(1, 'l').currentline";
		Assert.Equal("[1]", ReturnedJson(ExecuteCallerLua(line)));
		Assert.Equal("[3]", ReturnedJson(ExecuteCallerLua("\n\n" + line)));
		Assert.Equal("[3]", ReturnedJson(ExecuteCallerLua("\r\n\r\n" + line)));
		Assert.Equal("[3]", ReturnedJson(ExecuteCallerLua("\r\r" + line)));
		Assert.Equal("[2]", ReturnedJson(ExecuteCallerLua("\r" + line)));
		Assert.Equal("[2]", ReturnedJson(ExecuteCallerLua("\n\r" + line)));
		Assert.Equal("[\"a\\nb\"]", ReturnedJson(ExecuteCallerLua("return [[a\r\nb]]")));
	}

	[Fact]
	public void LuaUnsafeScript_StaleOrMissingResult_IsNeverReturned()
	{
		using RuntimeScope scope = CreateScope();

		Assert.Equal("internal", ReadCallerResult("never-issued").Error!.Kind);
		LuaUnsafeScriptWrapper.WrappedScript first = LuaUnsafeScriptWrapper.Build("return 'first'", null);
		RunStageA(first.Source);
		LuaJsonResult<LuaExecuteOutcome> mismatched = ReadCallerResult("another-token");
		Assert.True(mismatched.IsError);
		Assert.Equal("unknown", mismatched.Error.HostEffect);
		Assert.Null(ReadGlobal(LuaUnsafeScriptWrapper.ResultGlobal));
		Assert.True(ReadCallerResult(first.Token).IsError);

		LuaUnsafeScriptWrapper.WrappedScript second = LuaUnsafeScriptWrapper.Build("return 'second'", null);
		RunStageA(second.Source);
		Assert.Equal(["second"], ReturnedStrings(ReadCallerResult(second.Token)));
	}

	[Fact]
	public void LuaUnsafeScript_CopyFailure_StillClearsTheGlobal()
	{
		using RuntimeScope scope = CreateScope();

		(string Source, LuaJsonViolation Violation)[] failures =
		[
			("return string.rep('x', 5 * 1024 * 1024)", LuaJsonViolation.Limit),
			("return 0/0", LuaJsonViolation.Contract),
			("return {1, x = 2}", LuaJsonViolation.Contract)
		];
		foreach ((string source, LuaJsonViolation violation) in failures)
		{
			LuaJsonException exception = Assert.Throws<LuaJsonException>(() => ExecuteCallerLua(source));
			Assert.Equal(violation, exception.Violation);
			Assert.Null(ReadGlobal(LuaUnsafeScriptWrapper.ResultGlobal));
		}
	}

	[Fact]
	public void LuaUnsafeScript_StrictGlobalsAndReplacedBuiltins_DoNotBreakTheResultChannel()
	{
		using RuntimeScope scope = CreateScope();
		object? environmentField = EvaluateLua("rawget(_G, '_ENV') ~= nil");
		object? tickCountType = EvaluateLua("type(rawget(_G, 'getTickCount'))");

		Assert.Equal(["strict"], ReturnedStrings(ExecuteCallerLua("""
		                                                          setmetatable(_G, {
		                                                          	__newindex = function(_, k) error('assignment to undeclared global ' .. k, 2) end,
		                                                          	__index = function(_, k) error('undeclared global ' .. k, 2) end})
		                                                          return 'strict'
		                                                          """)));
		// Stage A captured the Stage-B primitives before the caller's code replaced them.
		Assert.Equal(["replaced"], ReturnedStrings(ExecuteCallerLua("""
		                                                            local put = rawset
		                                                            put(_G, 'load', nil); put(_G, 'pcall', nil); put(_G, 'table', nil)
		                                                            put(_G, 'rawget', function() error('replaced rawget') end)
		                                                            put(_G, 'rawset', function() error('replaced rawset') end)
		                                                            put(_G, 'type', function() error('replaced type') end)
		                                                            put(_G, 'getTickCount', function() error('replaced getTickCount') end)
		                                                            put(_G, '_ENV', {})
		                                                            return 'replaced'
		                                                            """)));
		Assert.Equal(true, EvaluateLua("rawget(_G, '__cheatengine_mcp_lua_result') == nil"));
		Assert.Equal(environmentField, EvaluateLua("rawget(_G, '_ENV') ~= nil"));
		Assert.Equal(tickCountType, EvaluateLua("type(rawget(_G, 'getTickCount'))"));
	}

	private static LuaJsonResult<LuaExecuteOutcome> ExecuteCallerLua(string source, string? chunkName = null)
	{
		LuaUnsafeScriptWrapper.WrappedScript script = LuaUnsafeScriptWrapper.Build(source, chunkName);
		RunStageA(script.Source);
		return ReadCallerResult(script.Token);
	}

	/// <summary>Stands in for <c>IUnsafeLuaClient.Execute</c>: a protected call of text that returns nothing.</summary>
	private static void RunStageA(string source)
	{
		using LuaRuntimeOperation operation = AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		LuaStatus status = state.TryExecute(Encoding.UTF8.GetBytes(source), 0, "=CheatEngine.Mcp/lua_execute"u8);
		if (!status.IsOk)
		{
			Assert.Fail(LuaError.FromStack(state, status).Message);
		}
	}

	private static LuaJsonResult<LuaExecuteOutcome> ReadCallerResult(string token)
	{
		return ReadJson(LuaToolRuntime.BuildSource(LuaUnsafeScriptWrapper.ReadResult, [token]),
			TestJsonContext.Default.LuaExecuteOutcome, LuaOpaqueValueHandling.Drop);
	}

	private static string ReturnedJson(LuaJsonResult<LuaExecuteOutcome> result)
	{
		Assert.False(result.IsError, result.Error?.Message);
		Assert.True(result.Value.Ok, result.Value.Error);
		return "[" + string.Join(',', result.Value.ReturnValues!.Select(value => value.GetRawText())) + "]";
	}

	private static string?[] ReturnedStrings(LuaJsonResult<LuaExecuteOutcome> result)
	{
		Assert.False(result.IsError, result.Error?.Message);
		Assert.True(result.Value.Ok, result.Value.Error);
		return [.. result.Value.ReturnValues!.Select(value => value.GetString())];
	}

	private static object? EvaluateLua(string expression)
	{
		PluginLuaToolRuntime.LuaToolOperation operation = new("evaluate", "return " + expression);
		Assert.True(operation.TryExecute(ActiveContext.Instance, out object? result, out CheatEngineFailure failure),
			failure.Message);
		return result;
	}
}
