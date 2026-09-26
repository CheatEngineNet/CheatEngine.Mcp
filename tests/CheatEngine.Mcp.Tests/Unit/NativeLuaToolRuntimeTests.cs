using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Tools;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.Mcp.Tests;

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
		LuaToolRuntime.LuaToolOperation operation = new("roundtrip",
			LuaToolRuntime.BuildSource("return table.pack(a[1], a[2], a[3], a[4], a[5])", ["\u2603\U0001F98A\"\\", null, true, false, ulong.MaxValue]));

		bool succeeded = operation.TryExecute(ActiveContext.Instance, out object? result, out CheatEngineFailure failure);

		Assert.True(succeeded, failure.Message);
		Assert.True(failure.IsDefault);
		object?[] values = Assert.IsAssignableFrom<object?[]>(result);
		Assert.Equal(new object?[] { "\u2603\U0001F98A\"\\", null, true, false, -1L }, values);
		Assert.True(initialTop == LuaApi.lua_gettop(s_state), "The operation must restore the Lua stack.");

		LuaToolRuntime.LuaToolOperation followUp = new("follow_up", "return 'still usable'");
		Assert.True(followUp.TryExecute(ActiveContext.Instance, out object? followUpResult, out CheatEngineFailure followUpFailure), followUpFailure.Message);
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
		AssertFailure("return { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = { child = 1 } } } } } } } } } } } } } } } } }", CheatEngineHostEffect.Completed);
		AssertFailure("return {" + string.Concat(Enumerable.Repeat("1,", LuaToolRuntime.MaximumItems + 1)) + "}", CheatEngineHostEffect.Completed);
	}

	[Fact]
	public void LuaToolOperation_MapAndArrayResults_AreCopiedWithoutLuaHandles()
	{
		using RuntimeScope scope = CreateScope();
		LuaToolRuntime.LuaToolOperation operation = new("map", "return { label = 'map', values = { 'x', 'y' } }");

		Assert.True(operation.TryExecute(ActiveContext.Instance, out object? result, out CheatEngineFailure failure), failure.Message);

		Dictionary<string, object?> map = Assert.IsAssignableFrom<Dictionary<string, object?>>(result);
		Assert.Equal("map", map["label"]);
		object?[] values = Assert.IsAssignableFrom<object?[]>(map["values"]);
		Assert.Equal(new object?[] { "x", "y" }, values);
	}

	[Fact]
	public void LuaDebuggerTool_StartSwitchDetachContinueAndBreakpoint_UseCeStateAndNumericEnums()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
			active=true; current=1; broken=true; debugCalls=0; detachCalls=0; continueCalls=0; unpauseCalls=0; breakpointTrigger=nil
			co_run=101; co_stepinto=202; co_stepover=303; bptExecute=11; bptAccess=22; bptWrite=33
			getOpenedProcessID=function() return 77 end; getProcesslist=function() return {[77]='target'} end
			debug_isDebugging=function() return active end; debug_getCurrentDebuggerInterface=function() return current end; debug_isBroken=function() return broken end
			debug_continueFromBreakpoint=function(mode) continueCalls=continueCalls+1; continuedMode=mode; broken=false end; unpause=function() unpauseCalls=unpauseCalls+1 end
			detachIfPossible=function() detachCalls=detachCalls+1; active=false end; debugProcess=function(requested) debugCalls=debugCalls+1; active=true; current=2 end
			openProcess=function() error('must not reopen target') end; getAddressSafe=function(_) return 4096 end
			debug_setBreakpoint=function(address,size,trigger) breakpointAddress=address; breakpointSize=size; breakpointTrigger=trigger; return true, 9 end
			""");
		LuaDebuggerTool tool = new(CreateDirectLuaClient());

		Dictionary<string, object?> repeated = ResultMap(tool.Start());
		Assert.Equal(true, repeated["alreadyAttached"]);
		Assert.Equal(0L, ReadGlobal("debugCalls"));

		Dictionary<string, object?> switched = ResultMap(tool.Start(3));
		Assert.Equal(true, switched["usedFallback"]);
		Assert.Equal(1L, ReadGlobal("debugCalls"));
		Assert.Equal(1L, ReadGlobal("detachCalls"));
		Assert.Equal(101L, ReadGlobal("continuedMode"));
		Dictionary<string, object?> repeatedFallback = ResultMap(tool.Start(3));
		Assert.Equal(true, repeatedFallback["alreadyAttached"]);
		Assert.Equal(true, repeatedFallback["usedFallback"]);
		Assert.True(ReadGlobal("debugCalls") is 1L, "A known interface fallback must not cause another attach.");
		Assert.Equal(1L, ReadGlobal("detachCalls"));

		ResultMap(tool.Detach());
		Assert.Equal(2L, ReadGlobal("detachCalls"));
		Assert.Equal(2L, ReadGlobal("unpauseCalls"));

		InstallStubs("broken=true");
		Dictionary<string, object?> continued = ResultMap(tool.Continue("stepInto"));
		Assert.Equal("stepInto", continued["mode"]);
		Assert.Equal(202L, ReadGlobal("continuedMode"));

		InstallStubs("active=true");
		Dictionary<string, object?> breakpoint = ResultMap(tool.AddBreakpoint("0x1000", 4, "access"));
		Assert.Equal(9L, breakpoint["id"]);
		Assert.Equal(22L, ReadGlobal("breakpointTrigger"));
		Assert.Equal(4L, ReadGlobal("breakpointSize"));
		InstallStubs("debug_setBreakpoint=function() return nil end");
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.AddBreakpoint("0x1000"), "success"));
	}

	[Fact]
	public void LuaCodeTool_ReferenceAndStringMaps_CopyBoundedEntriesAndEmptyArrays()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
			refs={[4096]='call',[8192]='jump'}; strings={[12288]='alpha',[16384]='beta'}
			getAddressSafe=function(_) return 20480 end
			getDissectCode=function() return { getReferences=function(_) return refs end, getReferencedStrings=function() return strings end } end
			""");
		LuaCodeTool tool = new(CreateDirectLuaClient());

		Dictionary<string, object?> references = ResultMap(tool.GetCodeReferences("target", 8));
		Assert.Equal(2L, references["count"]);
		object?[] referenceItems = Assert.IsAssignableFrom<object?[]>(references["references"]);
		Assert.Equal(2, referenceItems.Length);
		Assert.Contains(referenceItems.Cast<Dictionary<string, object?>>(), item => item["fromAddress"] is "0x1000" && item["type"] is "call");

		Dictionary<string, object?> strings = ResultMap(tool.GetReferencedStrings(8));
		Assert.Equal(2L, strings["count"]);
		Assert.Equal(2, Assert.IsAssignableFrom<object?[]>(strings["strings"]).Length);

		InstallStubs("refs={}; strings={}");
		Assert.Empty(Assert.IsAssignableFrom<object?[]>(ResultMap(tool.GetCodeReferences("target", 8))["references"]));
		Assert.Empty(Assert.IsAssignableFrom<object?[]>(ResultMap(tool.GetReferencedStrings(8))["strings"]));
	}

	[Fact]
	public void LuaDbvmTool_WatchUsesResolvedAddressAllowsIdZeroAndRejectsForbiddenFlags()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("getAddress=function(_) return 11259375 end; dbvm_watch_reads=function(address,size,options,count) watchedAddress=address; watchedSize=size; watchedOptions=options; watchedCount=count; return 0 end; sleep=function(duration) waited=duration end; dbvm_watch_retrievelog=function() return {} end; stops=0; dbvm_watch_disable=function(id) stoppedId=id; stops=stops+1 end");
		int dispatches = 0;
		LuaDbvmTool tool = new(CreateDirectLuaClient(() => dispatches++));

		Dictionary<string, object?> watch = ResultMap(tool.Watch("read", "physical", 4096, 15, 4096));
		Assert.Equal(0L, watch["id"]);
		Assert.Equal(11259375L, ReadGlobal("watchedAddress"));
		Assert.Equal(4096L, ReadGlobal("watchedSize"));
		Assert.Equal(15L, ReadGlobal("watchedOptions"));
		Assert.Equal(4096L, ReadGlobal("watchedCount"));
		Assert.Equal(true, watch["stopped"]);
		Assert.Equal(0L, ReadGlobal("stoppedId"));
		Assert.Equal(1L, ReadGlobal("stops"));
		Assert.Equal(100L, ReadGlobal("waited"));

		object invalid = tool.Watch("read", "physical", 1, 16, 1);
		ToolResultAssert.IsFailure(invalid, "address is required, byteSize must be between 1 and 4096, internalEntryCount must be between 1 and 4096, and options may use only bits 0 through 3.");
		Assert.Equal(1, dispatches);

		InstallStubs("dbvm_watch_retrievelog=function() error('read failed') end");
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.Watch("read", "physical"), "success"));
		Assert.True(ReadGlobal("stops") is 2L, "A failed log read must still stop its watch.");
		InstallStubs("dbvm_watch_disable=function() return false end");
		object failedCleanup = tool.Watch("read", "physical");
		Assert.False(ToolResultAssert.GetProperty<bool>(failedCleanup, "success"));
		Assert.Contains("watch 0; manual recovery required", ToolResultAssert.GetProperty<string>(failedCleanup, "error"));
	}

	private static void AssertFailure(string source, CheatEngineHostEffect expectedEffect)
	{
		LuaToolRuntime.LuaToolOperation operation = new("failure", source);

		bool succeeded = operation.TryExecute(ActiveContext.Instance, out object? result, out CheatEngineFailure failure);

		Assert.False(succeeded);
		Assert.Null(result);
		Assert.Equal(expectedEffect, failure.HostEffect);
		Assert.False(failure.IsDefault);
	}

	private static ICheatEngineClient CreateDirectLuaClient(Action? executed = null)
	{
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ILuaClient.Execute), method.Name);
			executed?.Invoke();
			ILuaOperation<object?> operation = Assert.IsAssignableFrom<ILuaOperation<object?>>(arguments![0]);
			if (!operation.TryExecute(ActiveContext.Instance, out object? result, out CheatEngineFailure failure))
			{
				failure.Throw();
			}

			return result;
		});
		return ClientTestDouble.Client((nameof(ICheatEngineClient.Lua), lua));
	}

	private static Dictionary<string, object?> ResultMap(object response)
	{
		ToolResultAssert.IsSuccess(response);
		return Assert.IsAssignableFrom<Dictionary<string, object?>>(ToolResultAssert.GetProperty<object>(response, "result"));
	}

	private static void InstallStubs(string source)
	{
		LuaToolRuntime.LuaToolOperation operation = new("stubs", source);
		Assert.True(operation.TryExecute(ActiveContext.Instance, out _, out CheatEngineFailure failure), failure.Message);
	}

	private static object? ReadGlobal(string name)
	{
		LuaToolRuntime.LuaToolOperation operation = new("read_global", $"return _G['{name}']");
		Assert.True(operation.TryExecute(ActiveContext.Instance, out object? result, out CheatEngineFailure failure), failure.Message);
		return result;
	}

	private static RuntimeScope CreateScope()
	{
		string? path = Environment.GetEnvironmentVariable(LuaPathVariable);
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new InvalidOperationException($"Set {LuaPathVariable} to an explicit Lua 5.3 DLL path to run NativeLua tests.");
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

	private sealed class ActiveContext : ILuaExecutionContext
	{
		public static ActiveContext Instance { get; } = new();

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
}
