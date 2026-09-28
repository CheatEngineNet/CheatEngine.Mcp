using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.Mcp.Tests.NativeLua;

[Trait("Category", "NativeLua")]
[Collection(nameof(SerialTestGroup))]
public sealed unsafe partial class NativeLuaToolRuntimeTests
{
	private const string LuaPathVariable = "CHEATENGINE_MCP_LUA53_PATH";
	private static lua_State* s_state;

	[Fact]
	public void LuaToolOperation_ValuesRoundTripAndStackRestores()
	{
		using RuntimeScope scope = CreateScope();
		int initialTop = LuaApi.lua_gettop(s_state);
		PluginLuaToolRuntime.LuaToolOperation operation = new("roundtrip",
			LuaToolRuntime.BuildSource("return table.pack(a[1], a[2], a[3], a[4], a[5])",
				["\u2603\U0001F98A\"\\", null, true, false, ulong.MaxValue]));

		bool succeeded =
			operation.TryExecute(ActiveContext.Instance, out object? result, out CheatEngineFailure failure);

		Assert.True(succeeded, failure.Message);
		Assert.True(failure.IsDefault);
		object?[] values = Assert.IsAssignableFrom<object?[]>(result);
		Assert.Equal(new object?[] { "\u2603\U0001F98A\"\\", null, true, false, -1L }, values);
		Assert.True(initialTop == LuaApi.lua_gettop(s_state), "The operation must restore the Lua stack.");

		PluginLuaToolRuntime.LuaToolOperation followUp = new("follow_up", "return 'still usable'");
		Assert.True(
			followUp.TryExecute(ActiveContext.Instance, out object? followUpResult,
				out CheatEngineFailure followUpFailure), followUpFailure.Message);
		Assert.Equal("still usable", followUpResult);
		Assert.Equal(initialTop, LuaApi.lua_gettop(s_state));
	}

	[Fact]
	public void LuaToolOperation_CompileRuntimeAndCopyFailures_ReportCorrectHostEffect()
	{
		using RuntimeScope scope = CreateScope();

		AssertFailure("[", CheatEngineHostEffect.NotStarted);
		AssertFailure("error('runtime failure')", CheatEngineHostEffect.Started);
		AssertFailure("local t = {}; t.self = t; return t", CheatEngineHostEffect.Completed);
		AssertFailure(
			"return { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = 1 } } } } } } } } } } } } } } } } }",
			CheatEngineHostEffect.Completed);
		AssertFailure("return {" + string.Concat(Enumerable.Repeat("1,", LuaToolRuntime.MaximumItems + 1)) + "}",
			CheatEngineHostEffect.Completed);
	}

	[Fact]
	public void LuaToolOperation_MapAndArrayResults_AreCopiedWithoutLuaHandles()
	{
		using RuntimeScope scope = CreateScope();
		PluginLuaToolRuntime.LuaToolOperation operation = new("map", "return { label = 'map', values = { 'x', 'y' } }");

		Assert.True(operation.TryExecute(ActiveContext.Instance, out object? result, out CheatEngineFailure failure),
			failure.Message);

		Dictionary<string, object?> map = Assert.IsAssignableFrom<Dictionary<string, object?>>(result);
		Assert.Equal("map", map["label"]);
		object?[] values = Assert.IsAssignableFrom<object?[]>(map["values"]);
		Assert.Equal(new object?[] { "x", "y" }, values);
	}

	private static void AssertFailure(string source, CheatEngineHostEffect expectedEffect)
	{
		PluginLuaToolRuntime.LuaToolOperation operation = new("failure", source);

		bool succeeded =
			operation.TryExecute(ActiveContext.Instance, out object? result, out CheatEngineFailure failure);

		Assert.False(succeeded);
		Assert.Null(result);
		Assert.Equal(expectedEffect, failure.HostEffect);
		Assert.False(failure.IsDefault);
	}

	private static void InstallStubs(string source)
	{
		PluginLuaToolRuntime.LuaToolOperation operation = new("stubs", source);
		Assert.True(operation.TryExecute(ActiveContext.Instance, out _, out CheatEngineFailure failure),
			failure.Message);
	}

	private static object? ReadGlobal(string name)
	{
		PluginLuaToolRuntime.LuaToolOperation operation = new("read_global", $"return _G['{name}']");
		Assert.True(operation.TryExecute(ActiveContext.Instance, out object? result, out CheatEngineFailure failure),
			failure.Message);
		return result;
	}

	private static RuntimeScope CreateScope()
	{
		string? path = Environment.GetEnvironmentVariable(LuaPathVariable);
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new InvalidOperationException(
				$"Set {LuaPathVariable} to an explicit Lua 5.3 DLL path to run NativeLua tests.");
		}

		string fullPath = Path.GetFullPath(path);
		if (!File.Exists(fullPath))
		{
			throw new InvalidOperationException($"{LuaPathVariable} does not exist: {fullPath}");
		}

		if (!NativeLibrary.TryLoad(fullPath, out nint module))
		{
			throw new InvalidOperationException($"Could not load Lua 5.3 from {fullPath}.");
		}

		if (!LuaApi.TryInitialize(module, out string? failure))
		{
			throw new InvalidOperationException($"Could not bind Lua 5.3 from {fullPath}: {failure}");
		}

		lua_State* state = LuaApi.luaL_newstate();
		if (state is null)
		{
			throw new InvalidOperationException("luaL_newstate returned null.");
		}

		LuaApi.luaL_openlibs(state);
		return new RuntimeScope(state);
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static void* ProvideState()
	{
		try
		{
			return s_state;
		}
		catch
		{
			return null;
		}
	}

	private sealed class ActiveContext : ILuaExecutionContext
	{
		public static ActiveContext Instance
		{
			get;
		} = new();

		public long Epoch => 1;
		public bool IsActive => true;

		public void ThrowIfExpired()
		{
		}
	}

	private sealed class RuntimeScope : IDisposable
	{
		private lua_State* _state;

		public RuntimeScope(lua_State* state)
		{
			_state = state;
			s_state = state;
			delegate* unmanaged[Stdcall]<void*> provider = &ProvideState;
			LuaHostBinding binding = new((nint) provider, 0, Environment.CurrentManagedThreadId);
			LuaRuntime.Attach(in binding);
		}

		public void Dispose()
		{
			LuaRuntime.Detach();
			if (_state is null)
			{
				return;
			}

			LuaApi.lua_close(_state);
			_state = null;
			s_state = null;
		}
	}
}
