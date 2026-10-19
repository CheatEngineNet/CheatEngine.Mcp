using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Processes;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     Native Lua coverage for process_set_paused against the real state kernel: a pause that MCP makes is recorded
///     as a <c>pause</c> effect that blocks a target change, and its release resumes only the process it paused.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	// Cheat Engine's Pause button: pause and unpause act on the opened process and do nothing when the button is
	// already in the requested state; isPaused is also true while the debugger is stopped at a breakpoint.
	private const string PauseHostStubs = """
	                                      pid = 77
	                                      paused = false
	                                      debugging = false
	                                      broken = false
	                                      pauseCalls = 0
	                                      unpauseCalls = 0
	                                      getOpenedProcessID = function() return pid end
	                                      pause = function() pauseCalls = pauseCalls + 1; paused = true end
	                                      unpause = function() unpauseCalls = unpauseCalls + 1; paused = false end
	                                      isPaused = function() return paused or broken end
	                                      debug_isBroken = function() return broken end
	                                      debug_isDebugging = function() return debugging end
	                                      debug_getContext = function(_) return broken end
	                                      """;

	[Fact]
	public void ProcessV2_Pause_IsRecordedBlocksATargetChangeAndIsResumedByTheRelease()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + PauseHostStubs);
		(ProcessTools tools, TargetResources resources) = PauseHarness();

		ProcessPausedResult paused = tools.SetPaused(true, Token);
		ProcessPausedResult again = tools.SetPaused(true, Token);

		string id = $"pause-{resources.Namespace}-1";
		Assert.Equal((new ProcessPausedResult(true, id), new ProcessPausedResult(true, id)), (paused, again));
		TargetResourceDescriptor listed = Assert.Single(resources.ListAll(Token));
		Assert.Equal((id, "pause", TargetResourceState.Active, false),
			(listed.Id, listed.Kind, listed.State, listed.Orphaned));
		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			resources.EnsureCanChangeTarget(new TargetTransition(77, 88), Token));
		Assert.Equal((ToolErrorKind.Busy, ToolHostEffect.NotStarted), (refused.Error.Kind, refused.Error.HostEffect));

		ReleaseAllResult release = resources.ReleaseAll(Token);

		Assert.True(release.IsComplete);
		Assert.Equal((1L, 1L, false), (ReadGlobal("pauseCalls"), ReadGlobal("unpauseCalls"), ReadGlobal("paused")));
		Assert.Empty(resources.ListAll(Token));
		resources.EnsureCanChangeTarget(new TargetTransition(77, 88), Token);
		LuaFixedScriptAssert.NeverLoadsCode(ProcessPauseScripts.Pause);
		LuaFixedScriptAssert.NeverLoadsCode(ProcessPauseScripts.Resume);
	}

	[Fact]
	public void ProcessV2_Pause_TargetAlreadyPaused_RecordsNothing()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + PauseHostStubs + "\npaused = true");
		(ProcessTools tools, TargetResources resources) = PauseHarness();

		ProcessPausedResult paused = tools.SetPaused(true, Token);

		Assert.Equal(new ProcessPausedResult(true), paused);
		Assert.Equal(0L, ReadGlobal("pauseCalls"));
		Assert.Empty(resources.ListAll(Token));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ProcessV2_SetPaused_RunningTarget_PreservesRegistersAndSkipsUnattachedBreakpointQuery(bool attached)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + PauseHostStubs + "\n" + """
			brokenReads = 0
			contextReads = 0
			RAX = 12345
			THREADID = 567
			debug_isBroken = function()
				brokenReads = brokenReads + 1
				if debugging then return broken end
				return debug_isBroken
			end
			debug_getContext = function(_)
				contextReads = contextReads + 1
				RAX = 0
				THREADID = 0
				error('Context reads overwrite prepared registers')
			end
			""");
		InstallStubs(attached ? "debugging = true" : "debugging = false");
		(ProcessTools tools, TargetResources resources) = PauseHarness();

		string first = tools.SetPaused(true, Token).ResourceId!;
		Assert.Equal(first, Assert.Single(resources.ListAll(Token)).Id);
		Assert.Equal(new ProcessPausedResult(false), tools.SetPaused(false, Token));
		Assert.Empty(resources.ListAll(Token));
		string second = tools.SetPaused(true, Token).ResourceId!;
		ReleaseAllResult released = resources.ReleaseAll(Token);

		Assert.True(released.IsComplete);
		Assert.Equal(second, Assert.Single(released.Released).Resource.Id);
		Assert.Equal((2L, 3L, false, 0L),
			(ReadGlobal("pauseCalls"), ReadGlobal("unpauseCalls"), ReadGlobal("paused"), ReadGlobal("contextReads")));
		Assert.Equal((12345L, 567L), (ReadGlobal("RAX"), ReadGlobal("THREADID")));
		Assert.Equal(attached, Assert.IsType<long>(ReadGlobal("brokenReads")) > 0);
		Assert.Empty(resources.ListAll(Token));
	}

	[Theory]
	[InlineData("debug_isDebugging = nil")]
	[InlineData("debug_isDebugging = function() error('state read failed') end")]
	[InlineData("debug_isDebugging = function() return debug_isDebugging end")]
	[InlineData("debugging = true; debug_isBroken = nil")]
	[InlineData("debugging = true; debug_isBroken = function() error('breakpoint read failed') end")]
	[InlineData("debugging = true; debug_isBroken = function() return debug_isBroken end")]
	public void ProcessV2_SetPaused_UnknownDebuggerState_RefusesBothDirectionsBeforeAnyEffect(string stubs)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + PauseHostStubs + "\n" + stubs);
		(ProcessTools tools, TargetResources resources) = PauseHarness();

		foreach (bool pause in new[] { true, false })
		{
			CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() => tools.SetPaused(pause, Token));
			Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.NotStarted),
				(refused.Error.Kind, refused.Error.HostEffect));
		}

		Assert.Equal((0L, 0L, false), (ReadGlobal("pauseCalls"), ReadGlobal("unpauseCalls"), ReadGlobal("paused")));
		Assert.Empty(resources.ListAll(Token));
	}

	[Theory]
	[InlineData("debug_isBroken = function() return debug_isBroken end")]
	[InlineData("debugging = true; debug_isBroken = function() return debug_isBroken end")]
	public void ProcessV2_Release_IgnoredResumeWithUnprovenBreakpoint_RetainsTheFailedPause(string stubs)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + PauseHostStubs);
		(ProcessTools tools, TargetResources resources) = PauseHarness();
		string id = tools.SetPaused(true, Token).ResourceId!;
		InstallStubs(stubs + "\nunpause = function() unpauseCalls = unpauseCalls + 1 end");

		ReleaseAllResult released = resources.ReleaseAll(Token);

		Assert.False(released.IsComplete);
		Assert.Empty(released.Released);
		Assert.Equal((id, ResourceReleaseKind.CleanupFailed),
			(released.Failed?.Resource.Id, released.Failed?.Release.Kind));
		TargetResourceDescriptor retained = Assert.Single(resources.ListAll(Token));
		Assert.Equal((id, TargetResourceState.CleanupFailed, true),
			(retained.Id, retained.State, retained.RequiresManualRecovery));
		Assert.Equal((1L, true), (ReadGlobal("unpauseCalls"), ReadGlobal("paused")));
	}

	[Fact]
	public void ProcessV2_Release_ConfirmedBreakpointRemains_RemovesOnlyMcpSuspension()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + PauseHostStubs);
		(ProcessTools tools, TargetResources resources) = PauseHarness();
		string id = tools.SetPaused(true, Token).ResourceId!;
		InstallStubs("debugging = true; broken = true");

		ReleaseAllResult released = resources.ReleaseAll(Token);

		Assert.True(released.IsComplete);
		Assert.Equal(id, Assert.Single(released.Released).Resource.Id);
		Assert.Equal((1L, false, true, true),
			(ReadGlobal("unpauseCalls"), ReadGlobal("paused"), ReadGlobal("broken"), ReadGlobal("debugging")));
		Assert.Empty(resources.ListAll(Token));
	}

	[Fact]
	public void ProcessV2_SetPaused_StoppedDebugger_PreservesPreparedRegistersOnRefusal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + PauseHostStubs + "\n" + """
			debugging = true
			broken = true
			RAX = 12345
			THREADID = 567
			debug_getContext = function(_) RAX = 0; THREADID = 0; return true end
			""");
		(ProcessTools tools, TargetResources resources) = PauseHarness();

		foreach (bool pause in new[] { true, false })
		{
			CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() => tools.SetPaused(pause, Token));
			Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
				(refused.Error.Kind, refused.Error.HostEffect));
		}

		Assert.Equal((12345L, 567L, 0L, 0L),
			(ReadGlobal("RAX"), ReadGlobal("THREADID"), ReadGlobal("pauseCalls"), ReadGlobal("unpauseCalls")));
		Assert.Empty(resources.ListAll(Token));
	}

	[Fact]
	public void ProcessV2_Resume_ReleasesMcpPauseAndForgetsEveryRecordedPauseOfTheProcess()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + PauseHostStubs);
		(ProcessTools tools, TargetResources resources) = PauseHarness();
		InstallStubs("pid = 88");
		Record(EarlierNamespace, "pause-aaaa11-2", "pause");
		InstallStubs("pid = 77");
		Record(EarlierNamespace, "pause-aaaa11-1", "pause");
		tools.SetPaused(true, Token);

		ProcessPausedResult resumed = tools.SetPaused(false, Token);

		// The earlier activation's pause of this process is undone by the resume; the one of process 88 is not.
		Assert.Equal(new ProcessPausedResult(false), resumed);
		Assert.Equal((false, 2L), (ReadGlobal("paused"), ReadGlobal("unpauseCalls")));
		TargetResourceDescriptor remaining = Assert.Single(resources.ListAll(Token));
		Assert.Equal(("pause-aaaa11-2", true, 88), (remaining.Id, remaining.Orphaned, remaining.ProcessId));
		Assert.Null(ReadGlobal("released"));
	}

	[Fact]
	public void ProcessV2_Resume_AnotherProcessOpened_IsPartialEffectAndKeepsThePauseForAcknowledgement()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + PauseHostStubs);
		(ProcessTools tools, TargetResources resources) = PauseHarness();
		string id = tools.SetPaused(true, Token).ResourceId!;
		InstallStubs("pid = 88");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.SetPaused(false, Token));

		// Resuming now would resume process 88, which was never paused, and leave 77 suspended.
		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.Started),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal((0L, true), (ReadGlobal("unpauseCalls"), ReadGlobal("paused")));
		TargetResourceDescriptor failed = Assert.Single(resources.ListAll(Token));
		Assert.Equal((id, TargetResourceState.CleanupFailed, true, 77),
			(failed.Id, failed.State, failed.RequiresManualRecovery, failed.ProcessId));
		Assert.Equal(id, Assert.Single(resources.Acknowledge([id], Token)).Id);
		Assert.Empty(resources.ListAll(Token));
	}

	[Fact]
	public void ProcessV2_Pause_AfterASwitchInCheatEngine_RecordsTheNewProcessInsteadOfReportingTheOldPause()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + PauseHostStubs);
		(ProcessTools tools, TargetResources resources) = PauseHarness();
		string first = tools.SetPaused(true, Token).ResourceId!;
		// The user resumed process 77 and opened 88 in Cheat Engine's window.
		InstallStubs("paused = false; pid = 88");

		ProcessPausedResult paused = tools.SetPaused(true, Token);
		ProcessPausedResult again = tools.SetPaused(true, Token);

		string second = $"pause-{resources.Namespace}-2";
		Assert.Equal((new ProcessPausedResult(true, second), new ProcessPausedResult(true, second)),
			(paused, again));
		Assert.Equal(2L, ReadGlobal("pauseCalls"));
		Assert.Equal([second, first], resources.ListAll(Token).Select(static resource => resource.Id));

		ReleaseAllResult release = resources.ReleaseAll(Token);

		// Each pause resumes only the process it paused: 88 is opened, so the pause of 77 needs manual recovery.
		Assert.Equal(second, Assert.Single(release.Released).Resource.Id);
		Assert.Equal((first, ResourceReleaseKind.CleanupFailed),
			(release.Failed?.Resource.Id, release.Failed?.Release.Kind));
		Assert.Equal((1L, false), (ReadGlobal("unpauseCalls"), ReadGlobal("paused")));
	}

	[Theory]
	[InlineData("debugging = true; broken = true")]
	[InlineData("pid = 0")]
	public void ProcessV2_Resume_RefusedWithMcpPauseTracked_LeavesThePauseInPlace(string stubs)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + PauseHostStubs);
		(ProcessTools tools, TargetResources resources) = PauseHarness();
		string id = tools.SetPaused(true, Token).ResourceId!;
		InstallStubs(stubs);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.SetPaused(false, Token));

		// The refusal changes nothing: MCP's pause is neither resumed nor forgotten.
		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal((0L, true), (ReadGlobal("unpauseCalls"), ReadGlobal("paused")));
		TargetResourceDescriptor kept = Assert.Single(resources.ListAll(Token));
		Assert.Equal((id, TargetResourceState.Active), (kept.Id, kept.State));
		// Once the refusal is gone, the resume releases MCP's pause and then resumes the opened process.
		InstallStubs("broken = false; pid = 77");
		Assert.Equal(new ProcessPausedResult(false), tools.SetPaused(false, Token));
		Assert.Equal((2L, false), (ReadGlobal("unpauseCalls"), ReadGlobal("paused")));
		Assert.Empty(resources.ListAll(Token));
		LuaFixedScriptAssert.NeverLoadsCode(ProcessPauseScripts.ResumeCheck);
	}

	[Theory]
	[InlineData(true, "pid = 0")]
	[InlineData(true, "debugging = true; broken = true")]
	[InlineData(false, "pid = 0")]
	[InlineData(false, "debugging = true; broken = true")]
	public void ProcessV2_SetPaused_NoProcessOrStoppedDebugger_IsInvalidStateBeforeAnyCall(bool pause, string stubs)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + PauseHostStubs + "\n" + stubs);
		(ProcessTools tools, TargetResources resources) = PauseHarness();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.SetPaused(pause, Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal((0L, 0L), (ReadGlobal("pauseCalls"), ReadGlobal("unpauseCalls")));
		Assert.Empty(resources.ListAll(Token));
	}

	[Theory]
	[InlineData(true, "pause = function() pauseCalls = pauseCalls + 1 end", ToolHostEffect.NotApplied)]
	[InlineData(false, "paused = true; unpause = function() unpauseCalls = unpauseCalls + 1 end",
		ToolHostEffect.Unknown)]
	public void ProcessV2_SetPaused_HostIgnoresTheRequest_IsHostRefusedWithoutARecord(bool pause, string stubs,
		ToolHostEffect effect)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + PauseHostStubs + "\n" + stubs);
		(ProcessTools tools, TargetResources resources) = PauseHarness();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.SetPaused(pause, Token));

		Assert.Equal((ToolErrorKind.HostRefused, effect), (exception.Error.Kind, exception.Error.HostEffect));
		Assert.Empty(resources.ListAll(Token));
	}

	private static (ProcessTools Tools, TargetResources Resources) PauseHarness()
	{
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		TargetResources resources = new(new McpStateLedger(dispatch, TimeProvider.System));
		return (new ProcessTools(dispatch, resources, new TargetTransitionGuards([resources]), CreateProcessFiles()),
			resources);
	}
}
