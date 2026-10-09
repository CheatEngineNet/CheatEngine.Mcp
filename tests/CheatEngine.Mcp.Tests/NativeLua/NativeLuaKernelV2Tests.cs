using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tools.Kernel;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>Native Lua coverage for DBVM preconditions, initialization and watch draining.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private const string WatchJobId = "kernelwatch-bbbb22-1";

	// DBVM's watch API, whose log hands each queued batch over once, as DBVM empties its log per full retrieval.
	private const string DbvmWatchStubs = """
	                                      retrieveCalls = 0
	                                      batches = {}
	                                      dbvm_initialized = function() return true end
	                                      dbvm_watch_reads = function() return 5 end
	                                      dbvm_watch_writes = function() return 5 end
	                                      dbvm_watch_executes = function() return 5 end
	                                      dbvm_watch_disable = function() return true end
	                                      dbvm_watch_retrievelog = function(_)
	                                      	retrieveCalls = retrieveCalls + 1
	                                      	local batch = table.remove(batches, 1)
	                                      	if batch == 'fail' then return nil, 'inactive id' end
	                                      	return batch or {}
	                                      end
	                                      """;

	[Fact]
	public void KernelStatus_ReportsBooleanStatesAndOmitsUnavailableOrInvalidAnswers()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("dbk_initialized = function() return true end; dbvm_initialized = function() return false end");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		KernelStatus reported = dispatch.RunLua(CheatEngineToolNames.KernelGetStatus, KernelScripts.GetStatus,
			KernelJsonContext.Default.KernelStatus, Token);

		InstallStubs("dbk_initialized = function() error('not available') end; dbvm_initialized = function() return 1 end");
		KernelStatus unavailable = dispatch.RunLua(CheatEngineToolNames.KernelGetStatus, KernelScripts.GetStatus,
			KernelJsonContext.Default.KernelStatus, Token);
		InstallStubs("dbk_initialized = nil; dbvm_initialized = nil");
		KernelStatus missing = dispatch.RunLua(CheatEngineToolNames.KernelGetStatus, KernelScripts.GetStatus,
			KernelJsonContext.Default.KernelStatus, Token);

		Assert.Equal(((bool?) true, (bool?) false), (reported.DbkInitialized, reported.DbvmInitialized));
		Assert.Equal(((bool?) null, (bool?) null), (unavailable.DbkInitialized, unavailable.DbvmInitialized));
		Assert.Equal(((bool?) null, (bool?) null), (missing.DbkInitialized, missing.DbvmInitialized));
	}

	[Fact]
	public void KernelV2_PhysicalOperations_RefuseAnUninitializedDbvmBeforeTheDeviceCall()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             readCalls = 0
		             writeCalls = 0
		             watchCalls = 0
		             dbvm_initialized = function() return false end
		             dbvm_readPhysicalMemory = function() readCalls = readCalls + 1; return {0} end
		             dbvm_writePhysicalMemory = function() writeCalls = writeCalls + 1; return true end
		             dbvm_watch_reads = function() watchCalls = watchCalls + 1; return 1 end
		             dbvm_watch_writes = function() watchCalls = watchCalls + 1; return 1 end
		             dbvm_watch_executes = function() watchCalls = watchCalls + 1; return 1 end
		             dbvm_watch_disable = function() return true end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		CheatEngineToolException read = Assert.Throws<CheatEngineToolException>(() => dispatch.RunLua(
			"kernel_read_physical", KernelScripts.ReadPhysical, KernelJsonContext.Default.KernelPhysicalRead, Token,
			0x1000UL, 1));
		CheatEngineToolException write = Assert.Throws<CheatEngineToolException>(() => dispatch.RunLua(
			"kernel_write_physical", KernelScripts.WritePhysical, KernelJsonContext.Default.KernelPhysicalWrite, Token,
			0x1000UL, new byte[] { 0xAA }));
		CheatEngineToolException watch = Assert.Throws<CheatEngineToolException>(() => dispatch.RunLua(
			"kernel_start_watch", KernelScripts.StartWatch, KernelJsonContext.Default.LuaKernelWatchArmed, Token,
			"bbbb22", "kernelwatch-bbbb22-1", 2, 3_000, 0, 0x1000UL, 1, 0, 1));

		Assert.All([read, write, watch], static exception =>
			Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
				(exception.Error.Kind, exception.Error.HostEffect)));
		Assert.Equal((0L, 0L, 0L), (ReadGlobal("readCalls"), ReadGlobal("writeCalls"), ReadGlobal("watchCalls")));
	}

	[Fact]
	public void KernelV2_DrainWatch_PushesEveryRetrievedEntryBecauseDbvmEmptiesItsLog()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + DbvmWatchStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		Assert.Equal(5, StartWatch(dispatch, 4).WatchId);
		// The second batch is as long as the first: a cumulative log index would have skipped all of it.
		InstallStubs("""
		             batches = {
		             	{{RIP = 1, RAX = 10}, {RIP = 2}, {RIP = 3}},
		             	{{RIP = 4}, {rip = 5}, {RIP = 6, CR3 = 0x1AB000}},
		             	{}
		             }
		             """);

		LuaKernelWatchDrain first = DrainWatch(dispatch);
		LuaKernelWatchDrain second = DrainWatch(dispatch);
		LuaKernelWatchDrain empty = DrainWatch(dispatch);
		LuaJobPage page = PollJob(WatchJobId, 0, 10);
		KernelWatchEvent[] events =
		[
			.. page.Items.Select(static item => item.Deserialize(KernelJsonContext.Default.KernelWatchEvent)!)
		];

		Assert.Equal([(true, 3L), (true, 3L), (true, 0L)],
			new[] { first, second, empty }.Select(static drain => (drain.Found, drain.Retrieved)));
		Assert.Equal(3L, ReadGlobal("retrieveCalls"));
		// A ring of four keeps the newest four of the six events and counts the two it evicted.
		Assert.Equal((3L, 6L, 2L, 6L, false), (page.FirstSequence, page.Job.Total, page.Dropped,
			page.NextAfterSequence, page.More));
		Assert.Equal([3L, 4L, 5L, 6L], events.Select(static item => item.SourceIndex));
		Assert.Equal(("3", "5", "6", "1AB000"), (events[0].Rip, events[2].Rip, events[3].Rip, events[3].Cr3));
	}

	[Fact]
	public void KernelV2_DrainWatch_RefusedRetrievalAndEndedJobs_DoNotTouchTheRing()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + DbvmWatchStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		StartWatch(dispatch, 8);
		InstallStubs("batches = {'fail'}");

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() => DrainWatch(dispatch));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Started),
			(refused.Error.Kind, refused.Error.HostEffect));
		Assert.Equal(0L, PollJob(WatchJobId, 0, 10).Job.Total);

		RunKernel("jobFinish(jobFind(a[1], a[2]), 'completed'); return true", OwnNamespace, WatchJobId);
		InstallStubs("retrieveCalls = 0; batches = {{{RIP = 1}}}");
		LuaKernelWatchDrain finished = DrainWatch(dispatch);
		Assert.Equal((true, 0L, 0L), (finished.Found, finished.Retrieved, ReadGlobal("retrieveCalls")));

		ReadKernel(LuaJobKernelScripts.Stop, StateJsonContext.Default.LuaJobStop, OwnNamespace, WatchJobId);
		LuaKernelWatchDrain removed = DrainWatch(dispatch);
		Assert.Equal((false, 0L, 0L), (removed.Found, removed.Retrieved, ReadGlobal("retrieveCalls")));
	}

	[Fact]
	public void KernelV2_InitializeDbvm_WithoutOffload_LoadsNothingAndReportsNotStarted()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             initializeCalls = 0
		             running = false
		             dbvm_initialize = function(offload, reason)
		             	initializeCalls = initializeCalls + 1
		             	observedOffload = offload
		             	observedReason = reason
		             	return running
		             end
		             dbvm_initialized = function() return running end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			InitializeDbvm(dispatch, false, "Watch a page"));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(refused.Error.Kind, refused.Error.HostEffect));
		Assert.Contains("loads nothing", refused.Error.Message, StringComparison.Ordinal);
		Assert.Equal(1L, ReadGlobal("initializeCalls"));
		Assert.Equal(false, ReadGlobal("observedOffload"));
		Assert.Null(ReadGlobal("observedReason"));

		InstallStubs("running = true");
		Assert.Equal(new KernelDbvmInitialization(true, false, "Watch a page"),
			InitializeDbvm(dispatch, false, "Watch a page"));
	}

	[Fact]
	public void KernelV2_InitializeDbvm_WithOffload_ShowsCheatEnginesWarningBeforeTheReason()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             running = false
		             accept = true
		             dbvm_initialize = function(offload, prompt)
		             	observedOffload = offload
		             	observedPrompt = prompt
		             	running = accept
		             	return running
		             end
		             dbvm_initialized = function() return running end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		KernelDbvmInitialization withReason = InitializeDbvm(dispatch, true, "Watch a page");
		string? reasonPrompt = (string?) ReadGlobal("observedPrompt");
		KernelDbvmInitialization withoutReason = InitializeDbvm(dispatch, true, null);
		string? warningPrompt = (string?) ReadGlobal("observedPrompt");
		InstallStubs("translate = function(text) return 'Traduit: ' .. text end");
		InitializeDbvm(dispatch, true, "Watch a page");
		string? translatedPrompt = (string?) ReadGlobal("observedPrompt");

		Assert.Equal(new KernelDbvmInitialization(true, true, "Watch a page"), withReason);
		Assert.Equal(new KernelDbvmInitialization(true, true, null), withoutReason);
		Assert.Equal(true, ReadGlobal("observedOffload"));
		Assert.Equal(KernelScripts.DbvmCrashWarning + "\n\nReason given by the MCP client: Watch a page",
			reasonPrompt);
		Assert.Equal(KernelScripts.DbvmCrashWarning, warningPrompt);
		Assert.Equal("Traduit: " + KernelScripts.DbvmCrashWarning + "\n\nReason given by the MCP client: Watch a page",
			translatedPrompt);

		InstallStubs("running = false; accept = false");
		CheatEngineToolException declined = Assert.Throws<CheatEngineToolException>(() =>
			InitializeDbvm(dispatch, true, "Watch a page"));
		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Unknown),
			(declined.Error.Kind, declined.Error.HostEffect));
	}

	/// <summary>Arms the watch job in the tool's argument order with a ring of <paramref name="buffer" />.</summary>
	private static LuaKernelWatchArmed StartWatch(ToolDispatch dispatch, int buffer)
	{
		return dispatch.RunLua(CheatEngineToolNames.KernelStartWatch, KernelScripts.StartWatch,
			KernelJsonContext.Default.LuaKernelWatchArmed, Token, OwnNamespace, WatchJobId, buffer, 60_000, 1,
			0x1000UL, 4, 0, 16);
	}

	private static LuaKernelWatchDrain DrainWatch(ToolDispatch dispatch)
	{
		return dispatch.RunLua(CheatEngineToolNames.KernelPollWatch, KernelScripts.DrainWatch,
			KernelJsonContext.Default.LuaKernelWatchDrain, Token, OwnNamespace, WatchJobId);
	}

	private static KernelDbvmInitialization InitializeDbvm(ToolDispatch dispatch, bool offload, string? reason)
	{
		return dispatch.RunLua(CheatEngineToolNames.KernelInitializeDbvm, KernelScripts.InitializeDbvm,
			KernelJsonContext.Default.KernelDbvmInitialization, Token, offload, KernelScripts.DbvmCrashWarning, reason);
	}
}
