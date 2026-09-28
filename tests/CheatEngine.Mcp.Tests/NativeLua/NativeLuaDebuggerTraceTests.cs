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
///     <c>debugger_onBreakpoint</c> call until maximumSteps contexts or the first context that matches stopCondition.
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
	                                      end
	                                      """;

	// Hits the entry breakpoint with RAX = 1 at 401000, then steps while the trace hook is installed; step n reports
	// RAX = n and RIP = 401000 + n - 1.
	private const string TraceRun = """
	                                RAX = 1
	                                RIP = 0x401000
	                                captureCallback()
	                                for n = 2, 20 do
	                                	if debugger_onBreakpoint == nil then break end
	                                	RAX = n
	                                	RIP = 0x401000 + n - 1
	                                	debugger_onBreakpoint()
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
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs);

		DebuggerJobStarted started = StartTrace(maximumSteps, register, value);
		InstallStubs(TraceRun);

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
		Assert.Null(ReadGlobal("debugger_onBreakpoint"));
		Assert.Equal((1L, 1L), (ReadGlobal("breakpointCalls"), ReadGlobal("breakpointTrigger")));
		LuaFixedScriptAssert.NeverLoadsCode(DebuggerLuaScripts.StartTrace);
	}

	[Theory]
	[InlineData(true, "EAX")]
	[InlineData(false, "RAX")]
	[InlineData(false, "R8")]
	public void DebuggerV2_Trace_StopConditionRegisterOfTheOtherWidth_IsInvalidArgumentBeforeAnyBreakpoint(
		bool wide, string register)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs);
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
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs + "\n" + TraceHostStubs);
		InstallStubs(wide ? "wide = true" : "wide = false");

		StartTrace(5L, register, "1");

		Assert.Equal(1L, ReadGlobal("breakpointCalls"));
	}

	private static DebuggerJobStarted StartTrace(long maximumSteps, string? register, string? value)
	{
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		return dispatch.RunLua(CheatEngineToolNames.DebuggerStartTrace, DebuggerLuaScripts.StartTrace,
			DebuggerJsonContext.Default.DebuggerJobStarted, Token, OwnNamespace, TraceJobId, 256L, 60_000L,
			"401000", "over", maximumSteps, register, value, false);
	}
}
