using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Debugger;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     Exercises the public debugger adapters through the native fixed-Lua bridge. The Lua functions are inert CE API
///     doubles: they only capture arguments and return deterministic debugger state.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void DebuggerAdapter_AttachAndControlOperations_ForwardKnownArgumentsAndResults()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             attached = false
		             RAX, RIP, RSP = 1, 0x401000, 0x7000
		             co_run, co_stepinto, co_stepover = 10, 11, 12
		             bptExecute, bptAccess, bptWrite = 1, 2, 3
		             debug_isDebugging = function() return attached end
		             debug_canBreak = function() return true end
		             debug_isBroken = function() return true end
		             debug_isStepping = function() return false end
		             debug_getCurrentDebuggerInterface = function() return 1 end
		             debug_getContext = function(_) return attached end
		             debug_getCurrentContextTable = function(_) return {RAX = RAX, RIP = RIP, RSP = RSP} end
		             targetIs64Bit = function() return true end
		             targetIsX86 = function() return true end
		             getOpenedProcessID = function() return 77 end
		             getProcesslist = function() return {[77] = true} end
		             debugProcess = function(value) attached = true; requestedInterface = value end
		             debug_breakThread = function(value) breakThread = value end
		             continueModes = {}
		             debug_continueFromBreakpoint = function(value) continueModes[#continueModes + 1] = value end
		             debug_setContext = function(_) return true end
		             debug_updateGUI = function() guiUpdates = (guiUpdates or 0) + 1 end
		             debug_addThreadToNoBreakList = function(value) ignoredThread, ignored = value, true; return true end
		             debug_removeThreadFromNoBreakList = function(value) ignoredThread, ignored = value, false; return true end
		             readQword = function(address) return address == 0x7000 and 0x401005 or nil end
		             getPreviousOpcode = function(address) return address - 5 end
		             disassemble = function(address) return address == 0x401000 and 'call target' or 'nop' end
		             """);
		using DebuggerToolHarness harness = CreateDebuggerTools(withProcess: true);
		DebuggerTools tools = harness.Tools;

		DebuggerAttachment attached = tools.Attach(DebuggerInterface.Windows, Token);
		DebuggerStatus status = tools.GetStatus(Token);
		DebuggerBreakRequested broken = tools.BreakThread(19, Token);
		DebuggerExecutionContinued continued = tools.Continue(Token);
		DebuggerExecutionContinued stepped = tools.Step(DebuggerStepMode.Over, Token);
		DebuggerContext context = tools.GetContext(false, Token);
		DebuggerRegisterSet written = tools.SetRegister("eax", -1, Token);
		DebuggerThreadIgnoreChanged ignored = tools.SetThreadIgnored(19, true, Token);
		DebuggerStackTrace trace = tools.GetStackTrace(1, Token);

		Assert.Equal((true, false, 77, DebuggerInterface.Windows),
			(attached.Attached, attached.AlreadyAttached, attached.ProcessId, attached.ActiveInterface));
		Assert.Equal(1L, ReadGlobal("requestedInterface"));
		Assert.Equal((true, true, DebuggerInterface.Windows), (status.StateValid, status.Broken, status.ActiveInterface));
		Assert.Equal(new DebuggerBreakRequested(19, true), broken);
		Assert.Equal(("run", true), (continued.Mode, continued.Continued));
		Assert.Equal(("over", true), (stepped.Mode, stepped.Continued));
		object?[] continueModes = (object?[]) ReadGlobal("continueModes")!;
		Assert.Equal([10L, 12L], continueModes);
		Assert.Equal("1", context.Registers["RAX"]);
		Assert.Equal(new DebuggerRegisterSet("EAX", "RAX", "FFFFFFFF", true), written);
		Assert.Equal((4294967295L, 1L), (ReadGlobal("RAX"), ReadGlobal("guiUpdates")));
		Assert.Equal(new DebuggerThreadIgnoreChanged(19, true), ignored);
		Assert.Equal((19L, true), (ReadGlobal("ignoredThread"), ReadGlobal("ignored")));
		DebuggerStackFrame frame = Assert.Single(trace.Frames);
		Assert.Equal(("7000", "401005", "call target", true),
			(frame.StackAddress, frame.ReturnAddress, frame.CallInstruction, frame.IsHeuristic));
	}

	[Fact]
	public void DebuggerAdapter_BreakpointLifecycle_TracksThenReleasesTheKnownOwnedBreakpoint()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + """
		             bptExecute, bptAccess, bptWrite, bpmDebugRegister, bpmInt3, bpmException = 1, 2, 3, 4, 5, 6
		             debug_isDebugging = function() return true end
		             getOpenedProcessID = function() return 77 end
		             getAddressSafe = function(value) observedExpression = value; return 0x401000 end
		             breakpointAddresses = {}
		             debug_getBreakpointList = function() return breakpointAddresses end
		             debug_setBreakpoint = function(address, size, trigger, method, callback)
		             observedBreakpoint = {address, size, trigger, method, callback}; table.insert(breakpointAddresses, address)
		             return true, {ID = 88, PID = 77, DTID = 9}
		             end
		             debug_removeBreakpointByID = function(id)
		                 removalArgumentType = type(id)
		                 removedBreakpoint = type(id) == 'table' and id.ID or id
		                 table.remove(breakpointAddresses)
		                 return true
		             end
		             targetIs64Bit = function() return true end
		             """);
		using DebuggerToolHarness harness = CreateDebuggerTools();
		DebuggerTools tools = harness.Tools;

		DebuggerBreakpointSet set = tools.SetBreakpoint("game.exe+1000", DebuggerBreakpointTrigger.Execute, 1,
			DebuggerBreakpointMethod.Hardware, null, false, Token);
		DebuggerBreakpointDeleted deleted = tools.DeleteBreakpoint("game.exe+1000", Token);

		Assert.Equal(("401000", DebuggerBreakpointTrigger.Execute, 1, DebuggerBreakpointMethod.Hardware, false),
			(set.Address, set.Trigger, set.Size, set.Method, set.OneShot));
		Assert.Equal("game.exe+1000", ReadGlobal("observedExpression"));
		object?[] breakpointArguments = (object?[]) ReadGlobal("observedBreakpoint")!;
		Assert.Equal([0x401000L, 1L, 1L, 4L], breakpointArguments.Take(4));
		Assert.True(deleted.Released);
		Assert.Equal(set.ResourceId, deleted.ResourceId);
		Assert.Equal(88L, ReadGlobal("removedBreakpoint"));
		Assert.Equal("table", ReadGlobal("removalArgumentType"));
	}

	[Fact]
	public void DebuggerAdapter_PageExceptionBreakpoint_UsesCheatEnginesPageExceptionMethod()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + """
		             bptExecute, bptAccess, bptWrite, bpmDebugRegister, bpmInt3, bpmException = 1, 2, 3, 4, 5, 6
		             debug_isDebugging = function() return true end
		             getOpenedProcessID = function() return 77 end
		             getAddressSafe = function(_) return 0x401000 end
		             breakpointAddresses = {}
		             debug_getBreakpointList = function() return breakpointAddresses end
		             debug_setBreakpoint = function(address, size, trigger, method, callback)
		                 observedBreakpoint = {address, size, trigger, method, callback}
		                 table.insert(breakpointAddresses, address)
		                 return true, {ID = 88, PID = 77, DTID = 9}
		             end
		             debug_removeBreakpointByID = function(_) table.remove(breakpointAddresses); return true end
		             targetIs64Bit = function() return true end
		             """);
		using DebuggerToolHarness harness = CreateDebuggerTools();

		DebuggerBreakpointSet set = harness.Tools.SetBreakpoint("401000", DebuggerBreakpointTrigger.Execute, 1,
			DebuggerBreakpointMethod.PageException, cancellationToken: Token);

		object?[] breakpointArguments = (object?[]) ReadGlobal("observedBreakpoint")!;
		Assert.Equal([0x401000L, 1L, 1L, 6L], breakpointArguments.Take(4));
		Assert.Equal((DebuggerBreakpointTrigger.Execute, 1, DebuggerBreakpointMethod.PageException),
			(set.Trigger, set.Size, set.Method));
	}

	[Fact]
	public void DebuggerAdapter_BreakpointAcceptedWithoutId_RetainsLedgerEntryForManualRecovery()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + """
		             bptExecute, bptAccess, bptWrite = 1, 2, 3
		             debug_isDebugging = function() return true end
		             getOpenedProcessID = function() return 77 end
		             targetIs64Bit = function() return true end
		             getAddressSafe = function(_) return 0x401000 end
		             debug_setBreakpoint = function() return true, nil end
		             breakpointRemovalCalls = 0
		             debug_removeBreakpointByID = function(_)
		                 breakpointRemovalCalls = breakpointRemovalCalls + 1
		                 return true
		             end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		Assert.Throws<CheatEngineToolException>(() => dispatch.RunLua(CheatEngineToolNames.DebuggerSetBreakpoint,
			DebuggerLuaScripts.SetBreakpoint, DebuggerJsonContext.Default.DebuggerBreakpointSet, Token,
			OwnNamespace, "breakpoint-bbbb22-1", "401000", 1L, "execute", "default", null, false));
		LuaStateRelease cleanup = ReadKernel(LuaJobKernelScripts.StateReleaseUnmanaged,
			StateJsonContext.Default.LuaStateRelease, OwnNamespace, Array.Empty<string>(), false);

		Assert.NotNull(cleanup.Failed);
		Assert.Equal("breakpoint-bbbb22-1", cleanup.Failed.Id);
		Assert.Contains("without a usable ID", cleanup.Failed.CleanupError, StringComparison.Ordinal);
		Assert.Contains("breakpoint-bbbb22-1", Assert.Single(cleanup.Remaining).Id);
		Assert.Equal(0L, ReadGlobal("breakpointRemovalCalls"));
	}

	[Theory]
	[InlineData("true", true, true, false, 1)]
	[InlineData("false", true, false, false, 1)]
	[InlineData("{}", false, false, null, 1)]
	[InlineData("error('partial context failure')", false, false, null, 1)]
	[InlineData("false", true, false, false, 2)]
	[InlineData("false", true, false, false, 3)]
	public void DebuggerAdapter_Status_ContextRead_RestoresItsFixedRegisterGlobals(string contextResult, bool stateValid,
		bool broken, bool? reportedBroken, int interfaceId)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs($$"""
		             local names = {'THREADID', 'EFLAGS', 'EAX', 'EBX', 'ECX', 'EDX', 'ESI', 'EDI', 'EBP', 'ESP', 'EIP',
		                'RAX', 'RBX', 'RCX', 'RDX', 'RSI', 'RDI', 'RBP', 'RSP', 'R8', 'R9', 'R10', 'R11', 'R12', 'R13',
		                'R14', 'R15', 'RIP'}
		             originalRegisters = {}
		             for index, name in ipairs(names) do
		                rawset(_G, name, {name = name, index = index})
		                originalRegisters[name] = rawget(_G, name)
		             end
		             rawset(_G, 'THREADID', nil)
		             rawset(_G, 'EFLAGS', false)
		             originalRax = rawget(_G, 'RAX')
		             unrelated = {marker = 'before context'}
		             originalUnrelated = unrelated
		             debug_isDebugging = function() return true end
		             debug_canBreak = function() return true end
		             debug_isBroken = function() return false end
		             debug_isStepping = function() return false end
		             targetIsX86 = function() return true end
		             debug_getCurrentDebuggerInterface = function() return {{interfaceId}} end
		             debug_getContext = function(extraregs)
		                assert(extraregs == false)
		                for index, name in ipairs(names) do rawset(_G, name, index) end
		                unrelated.marker = 'written by context'
		                contextOnlyGlobal = {marker = 'written by context'}
		                return {{contextResult}}
		             end
		             """);
		using DebuggerToolHarness harness = CreateDebuggerTools();

		DebuggerStatus status = harness.Tools.GetStatus(Token);
		InstallStubs("""
		             registersRestored = rawget(_G, 'THREADID') == nil and rawget(_G, 'EFLAGS') == false
		                and rawget(_G, 'RAX') == originalRax and unrelated == originalUnrelated
		                and unrelated.marker == 'written by context' and contextOnlyGlobal.marker == 'written by context'
		             for name, value in pairs(originalRegisters) do
		                if name ~= 'THREADID' and name ~= 'EFLAGS' and rawget(_G, name) ~= value then
		                   registersRestored = false
		                end
		             end
		             """);

		Assert.Equal(stateValid, status.StateValid);
		Assert.Equal(broken, status.Broken);
		Assert.Equal(reportedBroken, status.ReportedBroken);
		Assert.Equal(stateValid ? (DebuggerInterface?) interfaceId : null, status.ActiveInterface);
		Assert.True((bool) ReadGlobal("registersRestored")!);
	}

	[Theory]
	[InlineData("targetIsX86 = function() return false end")]
	[InlineData("debug_getCurrentDebuggerInterface = function() return 4 end")]
	public void DebuggerAdapter_Status_UnsupportedContextPrerequisites_DoNotReadTheContext(string prerequisite)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             debug_isDebugging = function() return true end
		             debug_canBreak = function() return true end
		             debug_isBroken = function() return false end
		             debug_isStepping = function() return false end
		             targetIsX86 = function() return true end
		             debug_getCurrentDebuggerInterface = function() return 1 end
		             debug_getContext = function(_) contextReads = (contextReads or 0) + 1 return false end
		             """ + "\n" + prerequisite);
		using DebuggerToolHarness harness = CreateDebuggerTools();

		DebuggerStatus status = harness.Tools.GetStatus(Token);

		Assert.False(status.StateValid);
		Assert.Null(ReadGlobal("contextReads"));
	}

	[Theory]
	[InlineData("true", true)]
	[InlineData("false", false)]
	public void DebuggerAdapter_Status_BooleanReportedBroken_RemainsExact(string reportedResult, bool expected)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs($$"""
		             debug_isDebugging = function() return true end
		             debug_canBreak = function() return false end
		             debug_isBroken = function() return {{reportedResult}} end
		             debug_isStepping = function() return false end
		             targetIsX86 = function() return true end
		             debug_getCurrentDebuggerInterface = function() return 1 end
		             debug_getContext = function(extra) assert(extra == false); return false end
		             """);
		using DebuggerToolHarness harness = CreateDebuggerTools();

		DebuggerStatus status = harness.Tools.GetStatus(Token);

		Assert.True(status.StateValid);
		Assert.False(status.Broken);
		Assert.Equal((bool?) expected, status.ReportedBroken);
	}

	[Theory]
	[InlineData("debug_isBroken = function() return nil end")]
	[InlineData("debug_isBroken = function() return {} end")]
	[InlineData("debug_isBroken = function() error('opaque') end")]
	[InlineData("")]
	public void DebuggerAdapter_Status_OpaqueReportedBroken_DoesNotInvalidateRunningContext(string reportedStub)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             debug_isDebugging = function() return true end
		             debug_canBreak = function() return false end
		             debug_isStepping = function() return false end
		             targetIsX86 = function() return true end
		             debug_getCurrentDebuggerInterface = function() return 1 end
		             debug_getContext = function(extra) assert(extra == false); return false end
		             """ + "\n" + reportedStub);
		using DebuggerToolHarness harness = CreateDebuggerTools();

		DebuggerStatus status = harness.Tools.GetStatus(Token);

		Assert.True(status.StateValid);
		Assert.True(status.Attached);
		Assert.False(status.Broken);
		Assert.Null(status.ReportedBroken);
	}

	[Fact]
	public void DebuggerAdapter_Status_OpaqueReportedBroken_DoesNotInvalidateStoppedContext()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             debug_isDebugging = function() return true end
		             debug_canBreak = function() return false end
		             debug_isBroken = function() return {} end
		             debug_isStepping = function() return false end
		             targetIsX86 = function() return true end
		             debug_getCurrentDebuggerInterface = function() return 1 end
		             debug_getContext = function(extra) assert(extra == false); return true end
		             """);
		using DebuggerToolHarness harness = CreateDebuggerTools();

		DebuggerStatus status = harness.Tools.GetStatus(Token);

		Assert.True(status.StateValid);
		Assert.True(status.Broken);
		Assert.Null(status.ReportedBroken);
	}

	[Fact]
	public void DebuggerAdapter_Status_InvalidContext_RemainsInvalidWhenReportedBrokenIsTrue()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             debug_isDebugging = function() return true end
		             debug_canBreak = function() return false end
		             debug_isBroken = function() return true end
		             debug_isStepping = function() return false end
		             targetIsX86 = function() return true end
		             debug_getCurrentDebuggerInterface = function() return 1 end
		             debug_getContext = function(extra) assert(extra == false); return {} end
		             """);
		using DebuggerToolHarness harness = CreateDebuggerTools();

		DebuggerStatus status = harness.Tools.GetStatus(Token);

		Assert.False(status.StateValid);
		Assert.Null(status.ReportedBroken);
	}

	[Fact]
	public void DebuggerAdapter_CaptureTraceAndRunTo_CreateBoundedJobsWithKnownReceipts()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs + "\n" + """
		             co_run = 3
		             debug_continueFromBreakpoint = function(value) lastContinue = value end
		             """);
		using DebuggerToolHarness harness = CreateDebuggerTools();
		DebuggerTools tools = harness.Tools;

		DebuggerJobStarted capture = tools.StartCapture("401000", DebuggerBreakpointTrigger.Execute, 1,
			maximumHits: 4, lifetimeSeconds: 10, cancellationToken: Token);
		InstallStubs("captureCallback()");
		DebuggerJobStarted trace = tools.StartTrace("401000", DebuggerStepMode.Into, 3, "RAX=1", false, 10, Token);
		InstallStubs("RAX = 1; RIP = 0x401000; THREADID = 9; debugger_onBreakpoint()");
		InstallStubs("debug_getContext = function(_) return true end");
		DebuggerRunToStarted runTo = tools.RunTo("401000", 10, Token);
		DebuggerCapturePage capturePage = tools.PollCapture(capture.JobId, 0, 4, Token);
		DebuggerTracePage tracePage = tools.PollTrace(trace.JobId, 0, 3, Token);

		Assert.All(new[] { capture.JobId, trace.JobId, runTo.JobId }, static id => Assert.StartsWith("debug", id));
		Assert.Equal((77, "401000", 10), (capture.ProcessId, capture.Address, capture.LifetimeSeconds));
		Assert.Equal((77, "401000", 10), (trace.ProcessId, trace.Address, trace.LifetimeSeconds));
		Assert.Equal((77, "401000", 10), (runTo.ProcessId, runTo.Address, runTo.LifetimeSeconds));
		Assert.Equal("401000", Assert.Single(capturePage.Hits).Context.Ip);
		Assert.Equal("1", Assert.Single(tracePage.Steps).Registers["RAX"]);
		Assert.Equal(3L, ReadGlobal("lastContinue"));
	}

	[Fact]
	public void DebuggerAdapter_WriteCapture_UsesCheatEnginesWriteTriggerAndRequestedSize()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs);
		using DebuggerToolHarness harness = CreateDebuggerTools();

		DebuggerJobStarted capture = harness.Tools.StartCapture("401000", DebuggerBreakpointTrigger.Write, 4,
			maximumHits: 4, lifetimeSeconds: 10, cancellationToken: Token);

		Assert.Equal((77, "401000", 10), (capture.ProcessId, capture.Address, capture.LifetimeSeconds));
		Assert.Equal((1L, 0x401000L, 3L, 4L),
			(ReadGlobal("breakpointCalls"), ReadGlobal("breakpointAddress"), ReadGlobal("breakpointTrigger"), ReadGlobal("breakpointSize")));
	}

	[Fact]
	public void DebuggerAdapter_RunTo_StopAfterSelectedTargetChanges_RetainsJobWithoutRemovingFromTheNewTarget()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + """
		             selectedProcessId = 77
		             getOpenedProcessID = function() return selectedProcessId end
		             debug_isDebugging = function() return true end
		             debug_getContext = function(extra) assert(extra == false); return {} end
		             getAddressSafe = function(_) return 0x401000 end
		             bptExecute = 1
		             co_run = 0
		             debug_continueFromBreakpoint = function(_) end
		             breakpointAddresses = {}
		             debug_getBreakpointList = function() return breakpointAddresses end
		             debug_setBreakpoint = function(address, ...) table.insert(breakpointAddresses, address); return true, {ID = 88, PID = 77, DTID = 9} end
		             breakpointRemovalCalls = 0
		             debug_removeBreakpointByID = function(id)
		                 breakpointRemovalCalls = breakpointRemovalCalls + 1
		                 removedBreakpoint = id
		                 return true
		             end
		             """);

		DebuggerRunToStarted runTo = CreateNativeDispatch(new McpFeatureOptions()).RunLua(
			CheatEngineToolNames.DebuggerRunTo, DebuggerLuaScripts.StartRunTo,
			DebuggerJsonContext.Default.DebuggerRunToStarted, Token, OwnNamespace, "debugrunto-bbbb22-1", 1, 10_000L,
			"401000");
		InstallStubs("selectedProcessId = 78");
		LuaJobStop stopped = ReadKernel(LuaJobKernelScripts.Stop, StateJsonContext.Default.LuaJobStop,
			OwnNamespace, runTo.JobId);
		LuaJobPage retained = PollJob(runTo.JobId, 0, 1);

		Assert.Equal((77, "401000"), (runTo.ProcessId, runTo.Address));
		Assert.Equal((true, true, false, "stopped"), (stopped.Found, stopped.WasRunning, stopped.Released, stopped.State));
		Assert.Contains("selected target changed", stopped.CleanupError, StringComparison.OrdinalIgnoreCase);
		Assert.Equal((JobState.Stopped, true), (retained.Job.State, retained.Job.RequiresManualRecovery));
		Assert.Equal(0L, ReadGlobal("breakpointRemovalCalls"));
	}

	[Fact]
	public void DebuggerAdapter_RunTo_AcceptedBreakpointWithoutId_RetainsJobForManualRecovery()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + """
		             getOpenedProcessID = function() return 77 end
		             debug_isDebugging = function() return true end
		             debug_getContext = function(extra) assert(extra == false); return {} end
		             getAddressSafe = function(_) return 0x401000 end
		             bptExecute = 1
		             debug_setBreakpoint = function() return true, nil end
		             runToRemovalCalls = 0
		             debug_removeBreakpointByID = function(_)
		                 runToRemovalCalls = runToRemovalCalls + 1
		                 return true
		             end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		Assert.Throws<CheatEngineToolException>(() => dispatch.RunLua(CheatEngineToolNames.DebuggerRunTo,
			DebuggerLuaScripts.StartRunTo, DebuggerJsonContext.Default.DebuggerRunToStarted, Token,
			OwnNamespace, "debugrunto-bbbb22-1", 1, 10_000L, "401000"));
		LuaJobPage retained = PollJob("debugrunto-bbbb22-1", 0, 1);

		Assert.Equal(JobState.Failed, retained.Job.State);
		Assert.True(retained.Job.RequiresManualRecovery);
		Assert.Contains("without a usable ID", retained.Job.CleanupError, StringComparison.Ordinal);
		Assert.Equal(0L, ReadGlobal("runToRemovalCalls"));
	}

	private static DebuggerToolHarness CreateDebuggerTools(bool withProcess = false)
	{
		IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
		ICheatEngineClient client = withProcess ? CreateDebuggerClientWithProcess() : CreateJsonLuaClient();
		ToolDispatch dispatch = new(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), options,
			new DispatchStatistics(options), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
			new PluginFixedLuaExecutor(client));
		TargetResources resources = new(new McpStateLedger(dispatch, TimeProvider.System));
		JobRegistry jobs = new(dispatch, resources, options, TimeProvider.System);
		return new DebuggerToolHarness(new DebuggerTools(dispatch, jobs, resources), jobs, resources);
	}

	private sealed class DebuggerToolHarness(DebuggerTools tools, JobRegistry jobs, TargetResources resources) : IDisposable
	{
		public DebuggerTools Tools => tools;

		public void Dispose()
		{
			ReleaseAllResult released = resources.ReleaseAll(CancellationToken.None, includeOrphans: true);
			if (!released.IsComplete)
			{
				throw new InvalidOperationException("Debugger adapter test cleanup did not complete.");
			}

			jobs.Dispose();
		}
	}

	private static ICheatEngineClient CreateDebuggerClientWithProcess()
	{
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ILuaClient.Execute), method.Name);
			Type resultType = method.GetGenericArguments()[1];
			MethodInfo tryExecute = typeof(ILuaOperation<>).MakeGenericType(resultType)
				.GetMethod(nameof(ILuaOperation<>.TryExecute))!;
			object?[] call = [ActiveContext.Instance, null, null];
			if ((bool) tryExecute.Invoke(arguments![0], call)!)
			{
				return call[1];
			}

			((CheatEngineFailure) call[2]!).Throw();
			return null;
		});
		IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, _) => method.Name switch
		{
			nameof(IProcessClient.GetCurrentProcess) => new ProcessSnapshot(new TargetProcessId(77), "owned.exe", null,
				TargetBackend.LocalProcess, CheatEngineArchitecture.X64, PointerSize.Bit64, 8,
				new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero), 1),
			_ => throw new NotSupportedException(method.Name)
		});
		return ClientTestDouble.Client((nameof(ICheatEngineClient.Lua), lua),
			(nameof(ICheatEngineClient.Processes), processes));
	}
}
