using System.Globalization;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Debugger;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     Native Lua coverage for debugger_start_trace: the entry breakpoint, then one step per
///     <c>debugger_onBreakpoint</c> call until maximumSteps contexts or stopCondition, with deferred terminal cleanup.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private const string TraceJobId = "debugtrace-bbbb22-1";

	// The stepping API the trace body calls; each continue arms the next single step.
	private const string TraceHostStubs = """
	                                      co_stepinto = 1
	                                      co_stepover = 2
	                                      stepping = false
	                                      continues = 0
	                                      debug_isStepping = function() return stepping end
	                                      debug_continueFromBreakpoint = function(option)
	                                          continues = continues + 1
	                                          continueOption = option
	                                          stepping = true
	                                          if option == co_run and traceBreakpointRemovalPending then
	                                              traceBreakpointListed = false
	                                              traceBreakpointRemovalPending = false
	                                          end
	                                      end
	                                      traceTimers = {}
	                                      createTimer = function(delay, callback)
	                                          local timer = { callback = callback }
	                                          timer.destroy = function() timer.destroyed = true end
	                                          if delay ~= nil then
	                                              assert(delay == 1 and type(callback) == 'function')
	                                              table.insert(traceTimers, timer)
	                                          end
	                                          return timer
	                                      end
	                                      runNextTraceTimer = function()
	                                          local timer = table.remove(traceTimers, 1)
	                                          if timer == nil or timer.destroyed then return false end
	                                          timer.callback()
	                                          return true
	                                      end
	                                      """;

	private const string TraceBreakpointStub = """
	                                           breakpointAccepted = true
	                                           breakpointRemovalAccepted = true
	                                           breakpointRemovalCalls = 0
	                                           traceBreakpointDeferredRemoval = false
	                                           traceBreakpointListed = false
	                                           traceBreakpointRemovalPending = false
	                                           debug_getBreakpointList = function()
	                                               return traceBreakpointListed and {0x401000} or {}
	                                           end
	                                           debug_removeBreakpointByID = function(id)
	                                               breakpointRemovalCalls = breakpointRemovalCalls + 1
	                                               removedBreakpoint = id
	                                               if not breakpointRemovalAccepted then return false end
	                                               if traceBreakpointDeferredRemoval then
	                                                   traceBreakpointRemovalPending = true
	                                               else
	                                                   traceBreakpointListed = false
	                                               end
	                                               return true
	                                           end
	                                           debug_setBreakpoint = function(...)
	                                               local arguments = table.pack(...)
	                                           breakpointCalls = breakpointCalls + 1
	                                               breakpointArgumentCount = arguments.n
	                                               breakpointAddress = arguments[1]
	                                               breakpointTrigger = arguments[3]
	                                               breakpointCallback = arguments[4]
	                                               traceBreakpointListed = true
	                                               return breakpointAccepted, breakpointAccepted and 88 or nil
	                                           end
	                                           """;

	// Hits an unrelated breakpoint, then the entry breakpoint with RAX = 1 at 401000, then steps through the global
	// hook; step n reports RAX = n and RIP = 401000 + n - 1.
	private const string TraceRun = """
	                                THREADID = 12
	                                stepping = false
	                                RIP = 0x401100
	                                unrelatedBreakpointResult = debugger_onBreakpoint()
	                                hookRetainedAfterUnrelatedBreakpoint = debugger_onBreakpoint ~= nil
	                                RAX = 1
	                                RIP = 0x401000
	                                entryCallbackResult = debugger_onBreakpoint()
	                                if entryCallbackResult ~= 0 then
	                                 for n = 2, 20 do
	                                  if debugger_onBreakpoint == nil then break end
	                                  RAX = n
	                                  RIP = 0x401000 + n - 1
	                                  stepCallbackResult = debugger_onBreakpoint()
	                                  if stepCallbackResult == 0 then break end
	                                 end
	                                end
	                                """;

	[Theory]
	[InlineData(null, null, 5L, 5L)]
	[InlineData(null, null, 1L, 1L)]
	[InlineData("RAX", "3", 5L, 3L)]
	[InlineData("RAX", "1", 5L, 1L)]
	[InlineData("IP", "401001", 5L, 2L)]
	[InlineData("RAX", "63", 4L, 4L)]
	public void DebuggerV2_Trace_RunsMaximumStepsUnlessTheStopConditionMatchesFirst(string? register, string? value,
		long maximumSteps, long expectedContexts)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs + "\n" + TraceBreakpointStub);

		DebuggerJobStarted started = StartTrace(maximumSteps, register, value);
		InstallStubs(TraceRun);
		LuaJobPage pending = PollJob(TraceJobId, 0, 100);
		Assert.Equal((JobState.Running, 0L), (pending.Job.State, ReadGlobal("breakpointRemovalCalls")));
		InstallStubs("assert(runNextTraceTimer())");

		LuaJobPage page = PollJob(TraceJobId, 0, 100);
		Assert.Equal((TraceJobId, 77, "401000"), (started.JobId, started.ProcessId, started.Address));
		Assert.Equal(JobState.Completed, page.Job.State);
		DebuggerCaptureContext[] contexts =
		[
			.. page.Items.Select(static item => item.Deserialize(DebuggerJsonContext.Default.DebuggerCaptureContext)!)
		];
		Assert.Equal(expectedContexts, contexts.Length);
		string[] steps =
		[
			.. Enumerable.Range(1, contexts.Length).Select(static n => n.ToString("X", CultureInfo.InvariantCulture))
		];
		Assert.Equal(steps, contexts.Select(static context => context.Registers["RAX"]));
		// The last context stays stopped: every earlier one was continued with one step over, and the hook is gone.
		Assert.Equal(expectedContexts - 1, ReadGlobal("continues"));
		Assert.Equal(expectedContexts > 1 ? 2L : null, ReadGlobal("continueOption"));
		Assert.Equal(expectedContexts > 1 ? 1L : 0L, ReadGlobal("entryCallbackResult"));
		Assert.Equal(0L, ReadGlobal("unrelatedBreakpointResult"));
		Assert.Equal(true, ReadGlobal("hookRetainedAfterUnrelatedBreakpoint"));
		Assert.Equal(88L, ReadGlobal("removedBreakpoint"));
		Assert.Equal(1L, ReadGlobal("breakpointRemovalCalls"));
		Assert.Equal(3L, ReadGlobal("breakpointArgumentCount"));
		Assert.Null(ReadGlobal("breakpointCallback"));
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));
		Assert.Equal((1L, 1L), (ReadGlobal("breakpointCalls"), ReadGlobal("breakpointTrigger")));
		LuaFixedScriptAssert.NeverLoadsCode(DebuggerLuaScripts.StartTrace);
	}

	[Fact]
	public void DebuggerV2_Trace_DefersTerminalBreakpointCleanupUntilTheCallbackReturns()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs + "\n" + TraceBreakpointStub);
		StartTrace(2L, null, null);
		InstallStubs("THREADID = 12; RIP = 0x401000; RAX = 1; entryCallbackResult = debugger_onBreakpoint()");
		InstallStubs("THREADID = 12; RIP = 0x401007; RAX = 2; stepping = true; stepCallbackResult = debugger_onBreakpoint()");

		LuaJobPage pending = PollJob(TraceJobId, 0, 10);

		Assert.Equal((JobState.Running, 0L, 0L),
			(pending.Job.State, ReadGlobal("breakpointRemovalCalls"), ReadGlobal("stepCallbackResult")));
		Assert.Equal("function", EvaluateLua("type(debugger_onBreakpoint)"));
		InstallStubs("assert(runNextTraceTimer())");

		LuaJobPage completed = PollJob(TraceJobId, 0, 10);

		Assert.Equal((JobState.Completed, 2L, 1L),
			(completed.Job.State, completed.Job.Total, ReadGlobal("breakpointRemovalCalls")));
		Assert.Equal(88L, ReadGlobal("removedBreakpoint"));
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));
	}

	[Fact]
	public void DebuggerV2_Trace_CheatEngineDefersBreakpointListRemovalUntilContinue()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs + "\n" + TraceBreakpointStub);
		InstallStubs("traceBreakpointDeferredRemoval = true; co_run = 0; debug_isDebugging = function() return true end; debug_getContext = function() return false end");
		StartTrace(2L, null, null);
		InstallStubs("THREADID = 12; RIP = 0x401000; RAX = 1; entryCallbackResult = debugger_onBreakpoint()");
		InstallStubs("THREADID = 12; RIP = 0x401007; RAX = 2; stepping = true; stepCallbackResult = debugger_onBreakpoint()");
		InstallStubs("assert(runNextTraceTimer())");

		LuaJobPage completed = PollJob(TraceJobId, 0, 10);

		Assert.Equal((JobState.Completed, false, true, 1L, 1L),
			(completed.Job.State, completed.Job.RequiresManualRecovery, ReadGlobal("traceBreakpointListed"),
				ReadGlobal("breakpointRemovalCalls"), EvaluateLua("#debug_getBreakpointList()")));
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));

		InstallStubs("debug_getContext = function() return true end");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		DebuggerExecutionContinued continued = dispatch.RunLua(CheatEngineToolNames.DebuggerContinue,
			DebuggerLuaScripts.Continue, DebuggerJsonContext.Default.DebuggerExecutionContinued, Token);

		Assert.True(continued.Continued);
		Assert.Equal((false, false, 0L),
			(ReadGlobal("traceBreakpointListed"), ReadGlobal("traceBreakpointRemovalPending"), EvaluateLua("#debug_getBreakpointList()")));
	}

	[Theory]
	[InlineData(true, "EAX")]
	[InlineData(false, "RAX")]
	[InlineData(false, "R8")]
	public void DebuggerV2_Trace_StopConditionRegisterOfTheOtherWidth_IsInvalidArgumentBeforeAnyBreakpoint(
		bool wide, string register)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs + "\n" + TraceBreakpointStub);
		InstallStubs(wide ? "wide = true" : "wide = false");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			StartTrace(5L, register, "1"));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains(register, exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0L, ReadGlobal("breakpointCalls"));
	}

	[Theory]
	[InlineData(true, "IP")]
	[InlineData(false, "IP")]
	[InlineData(false, "EFLAGS")]
	[InlineData(false, "EAX")]
	public void DebuggerV2_Trace_StopConditionRegisterOfEitherOrTheTargetWidth_ArmsTheEntryBreakpoint(bool wide,
		string register)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs + "\n" + TraceBreakpointStub);
		InstallStubs(wide ? "wide = true" : "wide = false");

		StartTrace(5L, register, "1");

		Assert.Equal(1L, ReadGlobal("breakpointCalls"));
	}

	[Fact]
	public void DebuggerV2_Trace_SetupRejectionClearsTheInstalledHookAndReleasesTheJob()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs + "\n" + TraceBreakpointStub);
		InstallStubs("breakpointAccepted = false");

		CheatEngineToolException rejected = Assert.Throws<CheatEngineToolException>(() => StartTrace(5L, null, null));

		Assert.Equal(3L, ReadGlobal("breakpointArgumentCount"));
		Assert.Null(ReadGlobal("breakpointCallback"));
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));
		Assert.Equal("not_found", PollError(TraceJobId, 0, 1).Kind);

		InstallStubs("breakpointAccepted = true");
		StartTrace(5L, null, null);
		LuaJobStop stopped = ReadKernel(LuaJobKernelScripts.Stop, StateJsonContext.Default.LuaJobStop,
			OwnNamespace, TraceJobId);
		Assert.True(stopped.Released);
		Assert.Equal(88L, ReadGlobal("removedBreakpoint"));
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));
		Assert.Contains("rejected", rejected.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void DebuggerV2_Trace_RetainsEntryBreakpointUntilTerminalCleanup()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs + "\n" + TraceBreakpointStub);
		StartTrace(5L, null, null);
		InstallStubs("THREADID = 12; RIP = 0x401000; RAX = 1; entryCallbackResult = debugger_onBreakpoint()");

		LuaJobPage running = PollJob(TraceJobId, 0, 10);

		Assert.Equal(JobState.Running, running.Job.State);
		Assert.Equal(1L, ReadGlobal("entryCallbackResult"));
		Assert.Equal(0L, ReadGlobal("breakpointRemovalCalls"));
		InstallStubs("traceHookType = type(debugger_onBreakpoint)");
		Assert.Equal("function", ReadGlobal("traceHookType"));

		LuaJobStop stopped = ReadKernel(LuaJobKernelScripts.Stop, StateJsonContext.Default.LuaJobStop,
			OwnNamespace, TraceJobId);

		Assert.Equal((true, "stopped", true), (stopped.Found, stopped.State, stopped.Released));
		Assert.Equal(88L, ReadGlobal("removedBreakpoint"));
		Assert.Equal(1L, ReadGlobal("breakpointRemovalCalls"));
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));
	}

	[Fact]
	public void DebuggerV2_Trace_TargetChangesBeforeCleanup_RetainsJobForManualRecovery()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs + "\n" + TraceBreakpointStub);
		InstallStubs("selectedProcessId = 77; getOpenedProcessID = function() return selectedProcessId end");
		StartTrace(5L, null, null);
		InstallStubs("selectedProcessId = 78");

		LuaJobStop stopped = ReadKernel(LuaJobKernelScripts.Stop, StateJsonContext.Default.LuaJobStop,
			OwnNamespace, TraceJobId);
		LuaJobPage retained = PollJob(TraceJobId, 0, 10);

		Assert.Equal((true, true, false, "stopped"), (stopped.Found, stopped.WasRunning, stopped.Released, stopped.State));
		Assert.Contains("selected target changed", stopped.CleanupError, StringComparison.OrdinalIgnoreCase);
		Assert.Equal((JobState.Stopped, true), (retained.Job.State, retained.Job.RequiresManualRecovery));
		Assert.Equal(0L, ReadGlobal("breakpointRemovalCalls"));
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));
	}

	[Fact]
	public void DebuggerV2_Trace_StopBeforeEntryRemovesItsExactBreakpointAndHook()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs + "\n" + TraceBreakpointStub);
		StartTrace(5L, null, null);
		InstallStubs("savedTraceHook = debugger_onBreakpoint");

		LuaJobStop stopped = ReadKernel(LuaJobKernelScripts.Stop, StateJsonContext.Default.LuaJobStop,
			OwnNamespace, TraceJobId);

		Assert.Equal((true, true, true, "stopped"),
			(stopped.Found, stopped.WasRunning, stopped.Released, stopped.State));
		Assert.Equal(88L, ReadGlobal("removedBreakpoint"));
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));
		Assert.Equal(0L, EvaluateLua("savedTraceHook()"));
		Assert.Equal(1L, ReadGlobal("breakpointRemovalCalls"));
	}

	[Fact]
	public void DebuggerV2_Trace_BreakpointRemovalFailureReportsManualRecoveryAndClearsOwnedHook()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs + "\n" + TraceBreakpointStub);
		InstallStubs("breakpointRemovalAccepted = false");
		StartTrace(2L, null, null);
		InstallStubs("breakpointRemovalAccepted = false");
		InstallStubs(TraceRun);
		Assert.Equal(0L, ReadGlobal("breakpointRemovalCalls"));
		InstallStubs("assert(runNextTraceTimer())");

		LuaJobPage page = PollJob(TraceJobId, 0, 10);

		Assert.Equal((JobState.Completed, 2L), (page.Job.State, page.Job.Total));
		Assert.True(page.Job.RequiresManualRecovery);
		Assert.Contains("rejected removal", page.Job.CleanupError, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(88L, ReadGlobal("removedBreakpoint"));
		Assert.Equal(1L, ReadGlobal("breakpointRemovalCalls"));
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));
	}

	[Fact]
	public void DebuggerV2_Trace_ExpiryBeforeEntryRemovesItsExactBreakpointAndHook()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs + "\n" + TraceBreakpointStub);
		StartTrace(5L, null, null, lifetimeMilliseconds: 10_000L);
		InstallStubs("clock = 10000");

		LuaStateSweep swept = ReadKernel(LuaJobKernelScripts.Sweep, StateJsonContext.Default.LuaStateSweep,
			OwnNamespace);

		Assert.Equal((1, 1, 0), (swept.Expired, swept.Removed, swept.AwaitingRecovery));
		Assert.Equal(88L, ReadGlobal("removedBreakpoint"));
		Assert.Equal(1L, ReadGlobal("breakpointRemovalCalls"));
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));
		Assert.Equal("not_found", PollError(TraceJobId, 0, 1).Kind);
	}

	[Fact]
	public void DebuggerV2_Trace_UnexpectedPostEntryBreakpointFailsAndReleasesHook()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs + "\n" + TraceBreakpointStub);
		StartTrace(5L, null, null);
		InstallStubs("THREADID = 12; RIP = 0x401000; RAX = 1; debugger_onBreakpoint(); stepping = false; debugger_onBreakpoint()");
		InstallStubs("assert(runNextTraceTimer())");

		LuaJobPage page = PollJob(TraceJobId, 0, 10);

		Assert.Equal((JobState.Failed, "The trace was interrupted before its next single-step"),
			(page.Job.State, page.Job.Error));
		Assert.False(page.Job.RequiresManualRecovery);
		Assert.Equal(88L, ReadGlobal("removedBreakpoint"));
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));
	}

	private static DebuggerJobStarted StartTrace(long maximumSteps, string? register, string? value,
		long lifetimeMilliseconds = 60_000L)
	{
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		return dispatch.RunLua(CheatEngineToolNames.DebuggerStartTrace, DebuggerLuaScripts.StartTrace,
			DebuggerJsonContext.Default.DebuggerJobStarted, Token, OwnNamespace, TraceJobId, 256L, lifetimeMilliseconds,
			"401000", "over", maximumSteps, register, value, false);
	}
}
