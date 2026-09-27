using System.Reflection;
using System.Text;
using System.Text.Json;

using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Lua;
using CheatEngine.Mcp.Core.Targets;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Asm;
using CheatEngine.Mcp.Tools.Debugger;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>Native Lua coverage for the v2 assembler and debugger fixed script bodies.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void AsmV2_GenerateApiHook_ExecutesTheFixedBodyAndPreservesEveryArgument()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
			generateAPIHookScript = function(address, destination, newCall, extension, targetSelf)
				observedAddress = address
				observedDestination = destination
				observedNewCall = newCall
				observedExtension = extension
				observedTargetSelf = targetSelf
				return '[ENABLE]\nnop\n[DISABLE]\nnop'
			end
			""");
		AsmTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		AsmGeneratedScript generated = tools.GenerateApiHook("401000", "401005", "newCall", "x64", true, Token);

		Assert.Equal("[ENABLE]\nnop\n[DISABLE]\nnop", generated.Script);
		Assert.Equal("401000", ReadGlobal("observedAddress"));
		Assert.Equal("401005", ReadGlobal("observedDestination"));
		Assert.Equal("newCall", ReadGlobal("observedNewCall"));
		Assert.Equal("x64", ReadGlobal("observedExtension"));
		Assert.Equal(true, ReadGlobal("observedTargetSelf"));
		LuaFixedScriptAssert.NeverLoadsCode(AsmScripts.GenerateApiHook);
	}

	[Fact]
	public void DebuggerV2_FixedLuaBodies_CompileAndNeverLoadCallerCode()
	{
		using RuntimeScope scope = CreateScope();
		KeyValuePair<string, string>[] scripts = [.. typeof(DebuggerLuaScripts)
			.GetFields(BindingFlags.NonPublic | BindingFlags.Static)
			.Where(static field => field.FieldType == typeof(string))
			.Select(static field => new KeyValuePair<string, string>(field.Name, (string)field.GetRawConstantValue()!))];

		Assert.NotEmpty(scripts);
		foreach ((string name, string body) in scripts)
		{
			Assert.DoesNotContain("load(", body, StringComparison.Ordinal);
			Assert.DoesNotContain("loadstring(", body, StringComparison.Ordinal);
			Assert.DoesNotContain("dofile(", body, StringComparison.Ordinal);
			Assert.DoesNotContain("loadfile(", body, StringComparison.Ordinal);
			Assert.DoesNotContain("require(", body, StringComparison.Ordinal);
			AssertDebuggerScriptCompiles(name, LuaToolRuntime.BuildSource(body, 100,
				["mcp", "resource", 8L, 1_000L, "401000", "execute", 1L, false, null, 0L, false]));
		}
	}

	[Fact]
	public void DebuggerV2_StatusContextAndRegisterWrite_ExecuteWithTypedResults()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
			RAX = 1
			guiUpdates = 0
			debug_isDebugging = function() return true end
			debug_canBreak = function() return true end
			debug_isBroken = function() return true end
			debug_isStepping = function() return false end
			debug_getContext = function(_) return true end
			debug_getCurrentDebuggerInterface = function() return 1 end
			targetIs64Bit = function() return true end
			targetIsX86 = function() return true end
			debug_getCurrentContextTable = function(_) return {RAX = RAX, RIP = 4096} end
			debug_setContext = function(_) return true end
			debug_updateGUI = function() guiUpdates = guiUpdates + 1 end
			""");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		DebuggerStatus status = dispatch.RunLua("debugger_get_status", DebuggerLuaScripts.Status,
			DebuggerJsonContext.Default.DebuggerStatus, Token);
		DebuggerContext context = dispatch.RunLua("debugger_get_context", DebuggerLuaScripts.GetContext,
			DebuggerJsonContext.Default.DebuggerContext, Token, false);
		DebuggerRegisterSet written = dispatch.RunLua("debugger_set_register", DebuggerLuaScripts.SetRegister,
			DebuggerJsonContext.Default.DebuggerRegisterSet, Token, "EAX", -1L);

		Assert.True(status.StateValid);
		Assert.True(status.Attached);
		Assert.True(status.CanBreak);
		Assert.True(status.Broken);
		Assert.True(status.ReportedBroken);
		Assert.False(status.Stepping);
		Assert.Equal(DebuggerInterface.Windows, status.ActiveInterface);
		Assert.True(context.Is64Bit);
		Assert.False(context.IncludesExtraRegisters);
		Assert.Equal("1", context.Registers["RAX"]);
		Assert.Equal("1000", context.Registers["RIP"]);
		Assert.Equal(new DebuggerRegisterSet("EAX", "RAX", "FFFFFFFF", true), written);
		Assert.Equal((4294967295L, 1L), (ReadGlobal("RAX"), ReadGlobal("guiUpdates")));
	}

	[Fact]
	public void DebuggerV2_Attach_EmitsContractInterfaceNames()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
			debugAttached = false
			activeInterface = 0
			debugProcessCalls = 0
			debug_isDebugging = function() return debugAttached end
			debug_getCurrentDebuggerInterface = function() return activeInterface end
			getOpenedProcessID = function() return 77 end
			getProcesslist = function() return {[77] = true} end
			debugProcess = function(requested)
				debugProcessCalls = debugProcessCalls + 1
				requestedInterface = requested
				activeInterface = requested == 0 and 1 or requested
				debugAttached = true
			end
			""");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		DebuggerAttachment veh = dispatch.RunLua("debugger_attach", DebuggerLuaScripts.Attach,
			DebuggerJsonContext.Default.DebuggerAttachment, Token, 2L, 77L, "77:1");
		DebuggerAttachment @default = dispatch.RunLua("debugger_attach", DebuggerLuaScripts.Attach,
			DebuggerJsonContext.Default.DebuggerAttachment, Token, 0L, 77L, "77:1");

		Assert.Equal((DebuggerInterface.Veh, DebuggerInterface.Veh, false, false),
			(veh.RequestedInterface, veh.ActiveInterface, veh.AlreadyAttached, veh.UsedFallback));
		Assert.Equal((DebuggerInterface.Default, DebuggerInterface.Veh, true, false),
			(@default.RequestedInterface, @default.ActiveInterface, @default.AlreadyAttached, @default.UsedFallback));
		Assert.Equal((1L, 2L), (ReadGlobal("debugProcessCalls"), ReadGlobal("requestedInterface")));
	}

	[Fact]
	public void DebuggerV2_AggregatedCapture_EvictsItsGroupIndexWithTheBoundedRing()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + """
			bptExecute = 1
			bptAccess = 2
			bptWrite = 3
			debug_isDebugging = function() return true end
			debug_getContext = function(_) return false end
			targetIsX86 = function() return true end
			targetIs64Bit = function() return true end
			getOpenedProcessID = function() return 77 end
			getAddressSafe = function(_) return 0x401000 end
			debug_removeBreakpointByID = function(_) return true end
			debug_setBreakpoint = function(_, _, _, callback) captureCallback = callback; return true, 88 end
			""");

		LuaJsonResult<DebuggerJobStarted> result = ReadJson(LuaToolRuntime.BuildSource(DebuggerLuaScripts.StartCapture,
			[OwnNamespace, "debugcapture-bbbb22-1", 2L, 60_000L, "401000", "execute", 4L, true]),
			DebuggerJsonContext.Default.DebuggerJobStarted);
		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal("debugcapture-bbbb22-1", result.Value.JobId);

		InstallStubs("""
			for index = 1, 1000 do
				RIP = index
				THREADID = index
				captureCallback()
			end
			""");

		JsonElement capture = Parse(RunKernel("""
			local job = jobFind(a[1], a[2])
			local groups, groupKeys = 0, 0
			for _ in pairs(job.groups) do groups = groups + 1 end
			for _ in pairs(job.groupKeys) do groupKeys = groupKeys + 1 end
			return {groups = groups, groupKeys = groupKeys, total = job.total, dropped = job.dropped, last = job.last, first = job.first}
			""", OwnNamespace, "debugcapture-bbbb22-1"));
		Assert.Equal((2L, 2L), (capture.GetProperty("groups").GetInt64(), capture.GetProperty("groupKeys").GetInt64()));
		Assert.Equal((1000L, 998L, 1000L, 2L),
			(capture.GetProperty("total").GetInt64(), capture.GetProperty("dropped").GetInt64(),
				capture.GetProperty("last").GetInt64(),
				capture.GetProperty("last").GetInt64() - capture.GetProperty("first").GetInt64() + 1));
	}

	private static void AssertDebuggerScriptCompiles(string name, string source)
	{
		LuaAdmissionStatus admission = LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation acquired);
		Assert.Equal(LuaAdmissionStatus.Admitted, admission);
		using LuaRuntimeOperation operation = acquired;
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		LuaStatus status = state.TryLoad(Encoding.UTF8.GetBytes(source),
			Encoding.UTF8.GetBytes("=CheatEngine.Mcp/" + name));
		Assert.True(status.IsOk, status.IsOk ? null : LuaError.FromStack(state, status).Message);
	}
}
