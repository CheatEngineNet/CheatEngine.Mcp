using CheatEngine.Client;
using CheatEngine.Mcp.Tools;

namespace CheatEngine.Mcp.Tests;

public sealed unsafe partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void LuaDebuggerCaptureTool_CallbackGlobalsPollFifoExpiryAndRetryableStop_WorkThroughLua()
	{
		using RuntimeScope scope = CreateScope();
		InstallDebuggerStubs();
		ICheatEngineClient client = CreateDirectLuaClient();
		LuaDebuggerCaptureTool tool = new(client);

		Dictionary<string, object?> started = ResultMap(tool.StartCapture("0x1000", "write", 4, 2, 1));
		string id = Assert.IsAssignableFrom<string>(started["captureId"]);
		Assert.Equal(99L, Assert.IsAssignableFrom<Dictionary<string, object?>>(started["breakpointId"])["PID"]);
		InstallStubs("THREADID=7; RIP=4096; RAX=17; firstReturn=callbacks[1](); THREADID=8; RIP=4096; RAX=18; callbacks[1](); THREADID=9; RIP=4096; RAX=19; callbacks[1]()");

		Assert.Equal(0L, ReadGlobal("firstReturn"));
		Assert.Equal(0L, ReadGlobal("continueCount"));
		Dictionary<string, object?> first = ResultMap(tool.PollCapture(id, 1, true));
		Assert.Equal(2L, first["count"]);
		Assert.Equal(1L, first["returned"]);
		Assert.Equal(1L, first["pending"]);
		Assert.Equal(1L, first["dropped"]);
		Dictionary<string, object?> firstHit = Assert.IsAssignableFrom<object?[]>(first["hits"])[0] as Dictionary<string, object?> ?? throw new Xunit.Sdk.XunitException("The first captured hit was missing.");
		Assert.Equal(7L, firstHit["threadId"]);
		Assert.Equal("0x1000", firstHit["ip"]);
		Assert.Equal("0x11", Assert.IsAssignableFrom<Dictionary<string, object?>>(firstHit["registers"])["RAX"]);

		Dictionary<string, object?> second = ResultMap(tool.PollCapture(id, 2, true));
		Assert.Equal(1L, second["count"]);
		Assert.Equal(0L, second["pending"]);
		Dictionary<string, object?> secondHit = Assert.IsAssignableFrom<object?[]>(second["hits"])[0] as Dictionary<string, object?> ?? throw new Xunit.Sdk.XunitException("The second captured hit was missing.");
		Assert.Equal(8L, secondHit["threadId"]);

		InstallStubs("tick=1000; timers[1].OnTimer()");
		Dictionary<string, object?> expired = ResultMap(tool.PollCapture(id, 1, false));
		Assert.Equal(true, expired["expired"]);
		Assert.Equal(1L, ReadGlobal("removedId"));

		Dictionary<string, object?> startedForRetry = ResultMap(tool.StartCapture("0x2000", "access", 4, 1, 5));
		string retryId = Assert.IsAssignableFrom<string>(startedForRetry["captureId"]);
		InstallStubs("removeFails=true");
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.StopCapture(retryId), "success"));
		InstallStubs("removeFails=false");
		Assert.Equal(true, ResultMap(tool.StopCapture(retryId))["released"]);
	}

	[Fact]
	public void LuaDebuggerCaptureTool_FailedStartCompensatesWithoutLeakingBreakpointOrHook()
	{
		using RuntimeScope scope = CreateScope();
		InstallDebuggerStubs();
		InstallStubs("startFails=true");
		LuaDebuggerCaptureTool tool = new(CreateDirectLuaClient());

		object failed = tool.StartCapture("0x3000");

		Assert.False(ToolResultAssert.GetProperty<bool>(failed, "success"));
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));
		Assert.Null(ReadGlobal("lastAcceptedId"));
		InstallStubs("startFails=false");
		Assert.True(ToolResultAssert.GetProperty<bool>(tool.StartCapture("0x3000"), "success"));
	}

	[Fact]
	public void LuaDebuggerCaptureTool_TimerEnableFailureAndMissingBreakpointId_CompensateOrRetainManualRecovery()
	{
		using RuntimeScope scope = CreateScope();
		InstallDebuggerStubs();
		ICheatEngineClient client = CreateDirectLuaClient();
		LuaDebuggerCaptureTool tool = new(client);
		InstallStubs("""
			destroyed=0
			createTimer=function() local timer={}; setmetatable(timer,{__newindex=function(t,key,value) if key=='Enabled' and value then error('timer enable failed') end rawset(t,key,value) end}); timer.destroy=function() destroyed=destroyed+1 end; timers[#timers+1]=timer; return timer end
			""");

		Assert.False(ToolResultAssert.GetProperty<bool>(tool.StartCapture("0x5000"), "success"));
		Assert.Equal(1L, ReadGlobal("removedId"));
		Assert.Equal(1L, ReadGlobal("destroyed"));
		InstallDebuggerStubs();
		Assert.True(ToolResultAssert.GetProperty<bool>(tool.StartCapture("0x5000"), "success"));

		InstallDebuggerStubs();
		InstallStubs("debug_setBreakpoint=function() return true,nil end");
		Dictionary<string, object?> missingId = Assert.IsAssignableFrom<Dictionary<string, object?>>(ToolResultAssert.GetProperty<object>(tool.StartCapture("0x6000"), "result"));
		Assert.Equal(true, missingId["requiresManualRecovery"]);
		Assert.NotNull(new LuaDebuggerCaptureGuard(client).PrepareForTransition());
	}

	[Fact]
	public void LuaDebuggerCaptureTool_ExpiryPurgesRetainedBufferAndGuardTracksActiveFailedAndCompletedJobs()
	{
		using RuntimeScope scope = CreateScope();
		InstallDebuggerStubs();
		ICheatEngineClient client = CreateDirectLuaClient();
		LuaDebuggerCaptureTool tool = new(client);
		LuaDebuggerCaptureGuard guard = new(client);
		Dictionary<string, object?> started = ResultMap(tool.StartCapture("0x7000", "write", 4, 1, 1));
		string id = Assert.IsAssignableFrom<string>(started["captureId"]);

		Assert.NotNull(guard.PrepareForTransition());
		InstallStubs("tick=1000; timers[1].OnTimer()");
		Assert.Null(guard.PrepareForTransition());
		InstallStubs("tick=31000; timers[1].OnTimer()");
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.PollCapture(id), "success"));

		InstallStubs("nextId=2; debug_setBreakpoint=function() return true,nil end");
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.StartCapture("0x7000"), "success"));
		Assert.NotNull(guard.PrepareForTransition());
	}

	[Fact]
	public void LuaDebuggerTraceTool_OnlyStepsOwningThreadAndRejectsHookOrBreakpointConflicts()
	{
		using RuntimeScope scope = CreateScope();
		InstallDebuggerStubs();
		LuaDebuggerTraceTool tool = new(CreateDirectLuaClient());

		InstallStubs("debugger_onBreakpoint=function() return 0 end");
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.Start("0x4000"), "success"));
		InstallStubs("debugger_onBreakpoint=nil; existing={123}");
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.Start("0x4000"), "success"));
		InstallStubs("existing={}");

		Dictionary<string, object?> started = ResultMap(tool.Start("0x4000", 3, 5));
		string id = Assert.IsAssignableFrom<string>(started["traceId"]);
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));
		InstallStubs("THREADID=41; RIP=16384; RAX=1; entryReturn=callbacks[1](); THREADID=42; RIP=16385; RAX=2; stepping=true; foreignResult=debugger_onBreakpoint(); THREADID=41; RIP=16385; RAX=3; ownFirst=debugger_onBreakpoint(); THREADID=41; RIP=16386; RAX=4; ownSecond=debugger_onBreakpoint()");

		Assert.Equal(0L, ReadGlobal("entryReturn"));
		Assert.Equal(0L, ReadGlobal("foreignResult"));
		Assert.Equal(1L, ReadGlobal("ownFirst"));
		Assert.Equal(0L, ReadGlobal("ownSecond"));
		Assert.Equal(2L, ReadGlobal("continueCount"));
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));
		Dictionary<string, object?> polled = ResultMap(tool.Poll(id));
		Assert.Equal(3L, polled["count"]);
		Assert.Equal(true, polled["completed"]);
		object?[] steps = Assert.IsAssignableFrom<object?[]>(polled["steps"]);
		Assert.Equal(3, steps.Length);
		Assert.True(steps.Cast<Dictionary<string, object?>>().All(step => step["threadId"] is 41L));
	}

	[Fact]
	public void LuaDebuggerCaptureTool_RejectedBreakpointWithError_DoesNotRemoveUnownedId()
	{
		using RuntimeScope scope = CreateScope();
		InstallDebuggerStubs();
		InstallStubs("originalSet=debug_setBreakpoint; debug_setBreakpoint=function() return false,'hardware slots full' end; removeCalls=0; originalRemove=debug_removeBreakpointByID; debug_removeBreakpointByID=function(id) removeCalls=removeCalls+1; return originalRemove(id) end");
		ICheatEngineClient client = CreateDirectLuaClient();
		LuaDebuggerCaptureTool tool = new(client);

		object response = tool.StartCapture("0x3000");

		Assert.False(ToolResultAssert.GetProperty<bool>(response, "success"));
		Dictionary<string, object?> result = Assert.IsAssignableFrom<Dictionary<string, object?>>(ToolResultAssert.GetProperty<object>(response, "result"));
		Assert.Equal(false, result["requiresManualRecovery"]);
		Assert.Equal(0L, ReadGlobal("removeCalls"));
		Assert.Null(new LuaDebuggerCaptureGuard(client).PrepareForTransition());
		InstallStubs("debug_setBreakpoint=originalSet");
		Assert.True(ToolResultAssert.GetProperty<bool>(tool.StartCapture("0x3000"), "success"));
	}

	[Fact]
	public void LuaDebuggerTool_BreakThreadWithoutDebugger_DoesNotImplicitlyAttach()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("active=false; breaks=0; debug_isDebugging=function() return active end; debug_breakThread=function(id) breaks=breaks+1; brokenThread=id end");
		LuaDebuggerTool tool = new(CreateDirectLuaClient());

		Assert.False(ToolResultAssert.GetProperty<bool>(tool.BreakThread(123), "success"));
		Assert.Equal(0L, ReadGlobal("breaks"));
		InstallStubs("active=true");
		Assert.True(ToolResultAssert.GetProperty<bool>(tool.BreakThread(123), "success"));
		Assert.Equal(1L, ReadGlobal("breaks"));
		Assert.Equal(123L, ReadGlobal("brokenThread"));
	}

	[Fact]
	public void LuaDebuggerTool_NoTarget_DoesNotInvokeNativeDebugger()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("getOpenedProcessID=function() return 0 end; attachCalls=0; debugProcess=function() attachCalls=attachCalls+1 end");

		object response = new LuaDebuggerTool(CreateDirectLuaClient()).Start(2);

		Assert.False(ToolResultAssert.GetProperty<bool>(response, "success"));
		Assert.Equal(0L, ReadGlobal("attachCalls"));
	}

	[Fact]
	public void LuaDebuggerTraceTool_OneStepEntry_StaysStoppedWithoutContinuing()
	{
		using RuntimeScope scope = CreateScope();
		InstallDebuggerStubs();
		LuaDebuggerTraceTool tool = new(CreateDirectLuaClient());
		string id = Assert.IsAssignableFrom<string>(ResultMap(tool.Start("0x4000", 1, 5))["traceId"]);

		InstallStubs("THREADID=41; RIP=16384; RAX=1; entryResult=callbacks[1]()");

		Assert.Equal(1L, ReadGlobal("entryResult"));
		Assert.Equal(0L, ReadGlobal("continueCount"));
		Assert.Equal(1L, ReadGlobal("removedId"));
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));
		Dictionary<string, object?> result = ResultMap(tool.Poll(id));
		Assert.Equal(true, result["completed"]);
		Assert.Equal(1L, result["count"]);
	}

	[Theory]
	[InlineData("debug_isDebugging")]
	[InlineData("debug_canBreak")]
	[InlineData("debug_isBroken")]
	[InlineData("debug_isStepping")]
	[InlineData("debug_getCurrentDebuggerInterface")]
	[InlineData("debug_getContext")]
	public void LuaDebuggerTool_StatusOpaqueNativeValue_ReportsInvalidState(string function)
	{
		using RuntimeScope scope = CreateScope();
		InstallDebuggerStubs();
		InstallStubs("debug_canBreak=function() return false end; debug_getCurrentDebuggerInterface=function() return 2 end");
		InstallStubs(function + "=function() return function() end end");

		object response = new LuaDebuggerTool(CreateDirectLuaClient()).Status();

		Assert.False(ToolResultAssert.GetProperty<bool>(response, "success"));
		Dictionary<string, object?> result = Assert.IsAssignableFrom<Dictionary<string, object?>>(ToolResultAssert.GetProperty<object>(response, "result"));
		Assert.Equal(false, result["stateValid"]);
		Assert.DoesNotContain("opaque object", ToolResultAssert.GetProperty<string>(response, "error"));
		Assert.Equal(0L, ReadGlobal("continueCount"));
	}

	[Fact]
	public void LuaDebuggerTool_StaleBrokenFlag_ReportsRunningAndRefusesContextMutations()
	{
		using RuntimeScope scope = CreateScope();
		InstallDebuggerStubs();
		InstallStubs("""
			broken=true; RAX=42; contextCopies=0; writes=0
			debug_getContext=function() return false end
			debug_getCurrentContextTable=function() contextCopies=contextCopies+1; return {} end
			debug_setContext=function() writes=writes+1; return true end
			debug_canBreak=function() return true end; debug_getCurrentDebuggerInterface=function() return 2 end
			""");
		ICheatEngineClient client = CreateDirectLuaClient();
		LuaDebuggerTool tool = new(client);

		Dictionary<string, object?> status = ResultMap(tool.Status());
		Assert.Equal(true, status["stateValid"]);
		Assert.Equal(false, status["broken"]);
		Assert.Equal(true, status["reportedBroken"]);
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.Context(), "success"));
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.Continue(), "success"));
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.SetRegister("RAX", 99), "success"));
		Assert.Equal(0L, ReadGlobal("contextCopies"));
		Assert.Equal(0L, ReadGlobal("writes"));
		Assert.Equal(0L, ReadGlobal("continueCount"));
		Assert.Equal(42L, ReadGlobal("RAX"));
		Assert.True(ToolResultAssert.GetProperty<bool>(new LuaDebuggerCaptureTool(client).StartCapture("0x1000"), "success"));
	}

	[Fact]
	public void LuaDebuggerTool_FailedVehAttach_QuarantinesProcessLifetimeWithoutBlockingPidReuse()
	{
		using RuntimeScope scope = CreateScope();
		InstallDebuggerStubs();
		InstallStubs("""
			active=false; attachCalls=0
			getProcesslist=function() return {[99]='target'} end
			debug_getCurrentDebuggerInterface=function() return 2 end
			debugProcess=function() attachCalls=attachCalls+1; error('partial native attach') end
			""");
		ICheatEngineClient client = CreateDirectLuaClient();
		object Attach(string identity) => LuaToolRuntime.Invoke(client, "attach_probe", LuaDebuggerScripts.Attach, 2, 99, identity, null, null, null);

		Assert.False(ToolResultAssert.GetProperty<bool>(Attach("99:100"), "success"));
		Assert.Equal(1L, ReadGlobal("attachCalls"));
		InstallStubs("debugProcess=function() attachCalls=attachCalls+1; active=true end");
		object rejected = Attach("99:100");
		Assert.False(ToolResultAssert.GetProperty<bool>(rejected, "success"));
		Assert.Contains("restart the target", ToolResultAssert.GetProperty<string>(rejected, "error"));
		Assert.Equal(1L, ReadGlobal("attachCalls"));
		Assert.Equal(true, ResultMap(Attach("99:200"))["attached"]);
		Assert.Equal(2L, ReadGlobal("attachCalls"));
	}

	[Fact]
	public void LuaDebuggerTool_SetRegister_ReadsBackAliasesAndRejectsWrongWidth()
	{
		using RuntimeScope scope = CreateScope();
		InstallDebuggerStubs();
		InstallStubs("broken=true; RAX=1; contextReads=0; debug_getContext=function() contextReads=contextReads+1; return true end; debug_setContext=function() return true end; debug_updateGUI=function() end");
		LuaDebuggerTool tool = new(CreateDirectLuaClient());

		Dictionary<string, object?> written = ResultMap(tool.SetRegister("EAX", -1));
		Assert.Equal("RAX", written["contextRegister"]);
		Assert.Equal("0xFFFFFFFF", written["value"]);
		Assert.Equal(true, written["verified"]);
		Assert.Equal(4294967295L, ReadGlobal("RAX"));
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.SetRegister("EAX", 4294967296), "success"));

		InstallStubs("contextReads=0; debug_getContext=function() contextReads=contextReads+1; if contextReads>1 then RAX=0 end return true end");
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.SetRegister("RAX", 9), "success"));
	}

	[Fact]
	public void LuaDebuggerTools_X86AllowsEightByteExecuteButRejectsEightByteDataAndRaxEdits()
	{
		using RuntimeScope scope = CreateScope();
		InstallDebuggerStubs();
		InstallStubs("wide=false; EAX=1; debug_getContext=function() return broken end; debug_setContext=function() return true end; debug_updateGUI=function() end");
		ICheatEngineClient client = CreateDirectLuaClient();
		LuaDebuggerCaptureTool captures = new(client);
		LuaDebuggerTool debugger = new(client);

		Assert.True(ToolResultAssert.GetProperty<bool>(captures.StartCapture("0x8000", "execute", 8), "success"));
		Assert.False(ToolResultAssert.GetProperty<bool>(captures.StartCapture("0x8000", "write", 8), "success"));
		InstallStubs("broken=true");
		Assert.False(ToolResultAssert.GetProperty<bool>(debugger.SetRegister("RAX", 1), "success"));
	}

	private static void InstallDebuggerStubs() => InstallStubs("""
		active=true; broken=false; stepping=true; wide=true; pid=99; tick=0; nextId=1; callbacks={}; ids={}; timers={}; existing={}; removedId=nil; lastAcceptedId=nil; removeFails=false; startFails=false
		bptExecute=1; bptAccess=2; bptWrite=3; co_run=10; co_stepinto=11
		debug_isDebugging=function() return active end; debug_isBroken=function() return broken end; debug_isStepping=function() return stepping end; debug_getContext=function() return broken end
		targetIsX86=function() return true end; targetIs64Bit=function() return wide end; getOpenedProcessID=function() return pid end; getTickCount=function() return tick end
		getAddressSafe=function(text) return tonumber(string.gsub(text,'0x',''),16) or 4096 end; debug_getBreakpointList=function() return existing end
		debug_setBreakpoint=function(address,size,trigger,callback) if startFails then return false,nil end local id=nextId; nextId=nextId+1; callbacks[id]=callback; lastAcceptedId=id; ids[id]={PID=pid,DTID=123,ID=id,opaque=function() end}; return true,ids[id] end
		debug_removeBreakpointByID=function(id) assert(type(id)=='table' and id==ids[id.ID],'Must retain the original CE ID'); removedId=id.ID; if removeFails then return false end; callbacks[id.ID]=nil; return true end
		continueCount=0; debug_continueFromBreakpoint=function(mode) continued=mode; continueCount=continueCount+1 end
		createTimer=function(_,_) local timer={Enabled=false,Interval=0}; timer.destroy=function() timer.destroyed=true end; timers[#timers+1]=timer; return timer end
		""");
}
