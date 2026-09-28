using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

using KernelPage = CheatEngine.Mcp.Core.Jobs.LuaJobPage;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed partial class NativeLuaToolRuntimeTests
{
	private const string OwnNamespace = "bbbb22";
	private const string EarlierNamespace = "aaaa11";

	// CE's clock and timers, replaced by globals that the test drives.
	private const string JobHostStubs = """
	                                    clock = 0
	                                    getTickCount = function() return clock end
	                                    timers = {}
	                                    createTimer = function(owner, enabled)
	                                    	local timer = {Owner = owner, Enabled = enabled, Interval = 0, destroyed = false}
	                                    	timer.destroy = function() timer.destroyed = true; timer.Enabled = false end
	                                    	timers[#timers + 1] = timer
	                                    	return timer
	                                    end
	                                    """;

	private static readonly long[] FirstTwo = [10, 20];
	private static readonly long[] LastThree = [30, 40, 50];
	private static readonly long[] Appended = [60];
	private static readonly long[] Shifted = [40, 50, 60];
	private static readonly long[] OnlyFirst = [10];

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void LuaJobKernel_CreatePushAndPoll_UsesANonConsumingSequenceCursor()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs);

		string id = CreateJob("capture", 10, 120000, 5);
		Assert.Equal("capture-bbbb22-1", id);
		Assert.Equal("capture-bbbb22-2", CreateJob("capture", 10, 120000, 0));

		KernelPage first = PollJob(id, 0, 2);
		Assert.Equal(FirstTwo, Items(first));
		Assert.Equal((2L, true, 1L, 5L, 0L), (first.NextAfterSequence, first.More, first.FirstSequence,
			first.Job.Total, first.Dropped));
		Assert.Equal((JobState.Running, "capture", id), (first.Job.State, first.Job.Kind, first.Job.JobId));
		KernelPage rest = PollJob(id, first.NextAfterSequence, 10);
		Assert.Equal(LastThree, Items(rest));
		Assert.Equal((5L, false), (rest.NextAfterSequence, rest.More));
		KernelPage caughtUp = PollJob(id, 5, 10);
		Assert.Empty(caughtUp.Items);
		Assert.Equal((5L, false), (caughtUp.NextAfterSequence, caughtUp.More));

		// Polls are read-only: repeating one returns the same page and leaves the job unchanged.
		string poll = LuaToolRuntime.BuildSource(LuaJobKernelScripts.Poll, [OwnNamespace, id, 0L, 2L]);
		Assert.Equal(CopyJson(poll).Json, CopyJson(poll).Json);
		Assert.Equal(FirstTwo, Items(PollJob(id, 0, 2)));

		RunKernel("assert(jobPush(jobFind(a[1], a[2]), 60)); return true", OwnNamespace, id);
		KernelPage appended = PollJob(id, 5, 10);
		Assert.Equal(Appended, Items(appended));
		Assert.Equal((6L, 120000L, 6L), (appended.NextAfterSequence, appended.Job.ExpiresInMs, appended.Job.Buffered));
	}

	[Fact]
	public void LuaJobKernel_InvalidCursorOrUnknownJob_IsADeclaredError()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs);
		string id = CreateJob("capture", 10, 120000, 3);

		Assert.Equal("invalid_argument", PollError(id, 4, 10).Kind);
		Assert.Equal("invalid_argument", PollError(id, -1, 10).Kind);
		Assert.Equal("invalid_argument", PollError(id, 0, 0).Kind);
		Assert.Equal("invalid_argument", PollError(id, 0, LuaJobKernelScripts.MaximumPollItems + 1).Kind);
		LuaScriptError unknown = PollError("capture-bbbb22-9", 0, 10);
		Assert.Equal(("not_found", "not_started"), (unknown.Kind, unknown.HostEffect));
		Assert.Equal("not_found",
			CopyJson(LuaToolRuntime.BuildSource(LuaJobKernelScripts.Poll, [EarlierNamespace, id, 0L, 10L]))
				.Error!.Kind);
	}

	[Fact]
	public void LuaJobKernel_BufferLimit_EvictsTheOldestItemsAndCountsThem()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs);
		string id = CreateJob("trace", 3, 120000, 5);

		KernelPage retained = PollJob(id, 0, 10);
		Assert.Equal(LastThree, Items(retained));
		Assert.Equal((3L, 5L, 2L, 5L, 3L), (retained.FirstSequence, retained.Job.Total, retained.Dropped,
			retained.NextAfterSequence, retained.Job.Buffered));
		KernelPage behind = PollJob(id, 1, 1);
		Assert.Equal(new long[] { 30 }, Items(behind));
		Assert.Equal((3L, true), (behind.NextAfterSequence, behind.More));

		RunKernel("assert(jobPush(jobFind(a[1], a[2]), 60)); return true", OwnNamespace, id);
		KernelPage shifted = PollJob(id, 3, 10);
		Assert.Equal(Shifted, Items(shifted));
		Assert.Equal((4L, 3L), (shifted.FirstSequence, shifted.Dropped));
	}

	[Fact]
	public void LuaJobKernel_Finish_IsIdempotentRunsStrategyThenOwnerHooksAndKeepsItemsPollable()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs);
		string id = CreateJob("search", 10, 120000, 1);

		JsonElement finished = Parse(RunKernel("""
		                                       local job = jobFind(a[1], a[2])
		                                       hooks = {}
		                                       job.strategyStop = function(_, state) hooks[#hooks + 1] = 'strategy:' .. state end
		                                       job.onStop = function(_, state) hooks[#hooks + 1] = 'owner:' .. state end
		                                       return {first = jobFinish(job, 'completed'), second = jobFinish(job, 'failed', 'late'),
		                                       	pushed = jobPush(job, 20), hooks = table.concat(hooks, ',')}
		                                       """, OwnNamespace, id));

		Assert.True(finished.GetProperty("first").GetBoolean());
		Assert.False(finished.GetProperty("second").GetBoolean());
		Assert.False(finished.GetProperty("pushed").GetBoolean());
		Assert.Equal("strategy:completed,owner:completed", finished.GetProperty("hooks").GetString());
		KernelPage page = PollJob(id, 0, 10);
		Assert.Equal(JobState.Completed, page.Job.State);
		Assert.Equal(OnlyFirst, Items(page));
		Assert.Null(page.Job.Error);
		Assert.Contains("A job finishes as",
			RunKernelFailure("jobFinish(jobFind(a[1], a[2]), 'bogus')", OwnNamespace, id), StringComparison.Ordinal);
	}

	[Fact]
	public void LuaJobKernel_TimeToLive_ExpiresJobsThroughTheInjectedClockAndTimer()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\nclock = 1000");
		string id = CreateJob("watch", 10, 5000, 2);
		RunKernel(
			"jobFind(a[1], a[2]).onStop = function(_, state) stops = (stops or 0) + 1; lastStop = state end; return true",
			OwnNamespace, id);
		Assert.Equal(1L, EvaluateLua("#timers"));
		Assert.Equal(true, EvaluateLua("timers[1].Enabled and timers[1].Interval == 1000"));

		InstallStubs("clock = 5999");
		Assert.Equal(new LuaStateSweep(0, 0, 0, 0, 1, 0, 0, 0), SweepJobs());
		Assert.Equal(1L, PollJob(id, 0, 10).Job.ExpiresInMs);
		Assert.Null(ReadGlobal("stops"));

		InstallStubs("clock = 6000; timers[1].OnTimer(timers[1])");
		Assert.Equal("not_found", PollError(id, 0, 10).Kind);
		Assert.Equal(1L, ReadGlobal("stops"));
		Assert.Equal("expired", ReadGlobal("lastStop"));
		Assert.Equal(true, EvaluateLua("timers[1].destroyed and rawget(_G, '__cheatengine_mcp_state').sweeper == nil"));

		// Elapsed time wraps with CE's 32-bit tick count.
		InstallStubs("clock = 4294967000");
		string wrapped = CreateJob("watch", 10, 1000, 0);
		Assert.Equal(2L, EvaluateLua("#timers"));
		InstallStubs("clock = 703");
		Assert.Equal(1, SweepJobs().Retained);
		Assert.Equal(1L, PollJob(wrapped, 0, 10).Job.ExpiresInMs);
		InstallStubs("clock = 704");
		Assert.Equal(1, SweepJobs().Removed);
	}

	[Fact]
	public void LuaJobKernel_OrphanSweep_RemovesExpiredJobsAndEmptiedNamespacesOfEarlierActivations()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs);
		string shortOrphan = CreateJob("capture", 10, 1000, 1, EarlierNamespace);
		string longOrphan = CreateJob("capture", 10, 60000, 1, EarlierNamespace);
		RunKernel("jobFind(a[1], a[2]).onStop = function(_, state) orphanStop = state end; return true",
			EarlierNamespace, shortOrphan);
		string own = CreateJob("capture", 10, 60000, 1);
		Assert.Equal("capture-aaaa11-2", longOrphan);

		InstallStubs("clock = 2000");
		Assert.Equal(new LuaStateSweep(1, 1, 1, 0, 2, 1, 0, 0), SweepJobs());
		Assert.Equal("expired", ReadGlobal("orphanStop"));

		InstallStubs("clock = 70000");
		Assert.Equal(new LuaStateSweep(2, 2, 1, 1, 0, 0, 0, 0), SweepJobs());
		Assert.Equal(true, EvaluateLua("rawget(_G, '__cheatengine_mcp_state').namespaces.aaaa11 == nil"));
		Assert.Equal(1L, EvaluateLua("rawget(_G, '__cheatengine_mcp_state').namespaceCount"));
		Assert.Equal("not_found", PollError(own, 0, 10).Kind);
		// The sweeping activation keeps its own namespace, so its numbering never restarts.
		Assert.Equal("capture-bbbb22-2", CreateJob("capture", 10, 60000, 0));
	}

	[Fact]
	public void LuaJobKernel_FailedCleanup_RetainsTheJobForManualRecovery()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs);
		string id = CreateJob("capture", 10, 1000, 1);
		RunKernel("jobFind(a[1], a[2]).onStop = function() error('breakpoint removal refused', 0) end; return true",
			OwnNamespace, id);

		InstallStubs("clock = 1000");
		Assert.Equal(new LuaStateSweep(1, 0, 0, 0, 1, 0, 1, 0), SweepJobs());
		KernelPage page = PollJob(id, 0, 10);
		Assert.Equal((JobState.Expired, "breakpoint removal refused", true),
			(page.Job.State, page.Job.CleanupError, page.Job.RequiresManualRecovery));
		Assert.Equal(OnlyFirst, Items(page));
		// Only an awaiting-recovery job remains, so the sweeper timer is released.
		Assert.Equal(true, EvaluateLua("timers[1].destroyed"));
		LuaJobStop stop = ReadKernel(LuaJobKernelScripts.Stop, StateJsonContext.Default.LuaJobStop, OwnNamespace, id);
		Assert.Equal((true, false, false, "breakpoint removal refused"),
			(stop.Found, stop.WasRunning, stop.Released, stop.CleanupError));
	}

	[Fact]
	public void LuaJobKernel_StopAndStatuses_ReleaseOnceAndReportEveryJobOfTheNamespace()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\ngetOpenedProcessID = function() return 4242 end");
		string running = CreateJob("capture", 10, 60000, 2);
		string done = CreateJob("trace", 10, 60000, 1);
		RunKernel("jobFinish(jobFind(a[1], a[2]), 'completed'); return true", OwnNamespace, done);
		CreateJob("capture", 10, 60000, 0, EarlierNamespace);

		LuaJobStatuses statuses = ReadKernel(LuaJobKernelScripts.Statuses, StateJsonContext.Default.LuaJobStatuses,
			OwnNamespace);
		Assert.Equal(2, statuses.Jobs.Length);
		JobStatus first = statuses.Jobs.Single(status => status.JobId == running);
		Assert.Equal((JobState.Running, 2L, 4242), (first.State, first.Total, first.ProcessId!.Value));

		LuaJobStop stopped = ReadKernel(LuaJobKernelScripts.Stop, StateJsonContext.Default.LuaJobStop, OwnNamespace,
			running);
		Assert.Equal((true, true, true, "stopped"),
			(stopped.Found, stopped.WasRunning, stopped.Released, stopped.State));
		LuaJobStop again = ReadKernel(LuaJobKernelScripts.Stop, StateJsonContext.Default.LuaJobStop, OwnNamespace,
			running);
		Assert.False(again.Found);
		LuaJobStop ended = ReadKernel(LuaJobKernelScripts.Stop, StateJsonContext.Default.LuaJobStop, OwnNamespace,
			done);
		Assert.Equal((true, false, true), (ended.Found, ended.WasRunning, ended.Released));
		Assert.Empty(ReadKernel(LuaJobKernelScripts.Statuses, StateJsonContext.Default.LuaJobStatuses, OwnNamespace)
			.Jobs);
	}

	[Fact]
	public void LuaJobKernel_PushAndPoll_CostIsIndependentOfBufferSizeAndHistory()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs);

		JsonElement cost = Parse(RunKernel("""
		                                   local function cost(action)
		                                   	local count = 0
		                                   	debug.sethook(function() count = count + 1 end, '', 1)
		                                   	action()
		                                   	debug.sethook()
		                                   	return count
		                                   end
		                                   local small = jobCreate(a[1], 'probe-' .. a[1] .. '-1', 'probe', 16, 120000)
		                                   for i = 1, 16 do jobPush(small, i) end
		                                   local large = jobCreate(a[1], 'probe-' .. a[1] .. '-2', 'probe', 65536, 120000)
		                                   for i = 1, 200000 do jobPush(large, i) end
		                                   return {
		                                   	smallPoll = cost(function() jobPoll(small, 4, 10) end),
		                                   	largePoll = cost(function() jobPoll(large, 150000, 10) end),
		                                   	behindPoll = cost(function() jobPoll(large, 0, 10) end),
		                                   	widePoll = cost(function() jobPoll(large, 150000, 1000) end),
		                                   	smallPush = cost(function() jobPush(small, 17) end),
		                                   	largePush = cost(function() jobPush(large, 200001) end),
		                                   	largeDropped = large.dropped, largeFirst = large.first}
		                                   """, OwnNamespace));

		long smallPoll = cost.GetProperty("smallPoll").GetInt64();
		Assert.Equal(smallPoll, cost.GetProperty("largePoll").GetInt64());
		Assert.Equal(smallPoll, cost.GetProperty("behindPoll").GetInt64());
		// The measure is sensitive: a hundred times more items costs at least ten times more.
		Assert.True(cost.GetProperty("widePoll").GetInt64() > 10 * smallPoll, cost.GetRawText());
		Assert.Equal(cost.GetProperty("smallPush").GetInt64(), cost.GetProperty("largePush").GetInt64());
		Assert.Equal(200001 - 65536, cost.GetProperty("largeDropped").GetInt64());
		Assert.Equal(200001 - 65536 + 1, cost.GetProperty("largeFirst").GetInt64());
	}

	[Fact]
	public void LuaJobKernel_InvalidCreation_FailsBeforeAnyStateOrTimerExists()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs);

		Assert.Contains("bufferLimit",
			RunKernelFailure("jobCreate(a[1], 'capture-' .. a[1] .. '-1', 'capture', 0, 1000)", OwnNamespace),
			StringComparison.Ordinal);
		Assert.Contains("TTL",
			RunKernelFailure("jobCreate(a[1], 'capture-' .. a[1] .. '-1', 'capture', 1, 300001)", OwnNamespace),
			StringComparison.Ordinal);
		Assert.Contains("kind",
			RunKernelFailure("jobCreate(a[1], 'Capture-' .. a[1] .. '-1', 'Capture', 1, 1000)", OwnNamespace),
			StringComparison.Ordinal);
		Assert.Contains("namespace", RunKernelFailure("jobCreate(a[1], 'capture-NS-1', 'capture', 1, 1000)", "NS-1"),
			StringComparison.Ordinal);
		Assert.Contains("kind-namespace-number",
			RunKernelFailure("jobCreate(a[1], 'capture-' .. a[1] .. '-01', 'capture', 1, 1000)", OwnNamespace),
			StringComparison.Ordinal);
		Assert.Contains("kind-namespace-number",
			RunKernelFailure("jobCreate(a[1], 'trace-' .. a[1] .. '-1', 'capture', 1, 1000)", OwnNamespace),
			StringComparison.Ordinal);
		Assert.Equal(0L, EvaluateLua("#timers"));
		Assert.Equal(true, EvaluateLua("rawget(_G, '__cheatengine_mcp_state') == nil"));

		RunKernel("for i = 1, 64 do jobCreate(a[1], 'probe-' .. a[1] .. '-' .. i, 'probe', 1, 1000) end; return true",
			OwnNamespace);
		Assert.Contains("already in use",
			RunKernelFailure("jobCreate(a[1], 'probe-' .. a[1] .. '-64', 'probe', 1, 1000)", OwnNamespace),
			StringComparison.Ordinal);
		Assert.Contains("At most 64",
			RunKernelFailure("jobCreate(a[1], 'probe-' .. a[1] .. '-65', 'probe', 1, 1000)", OwnNamespace),
			StringComparison.Ordinal);
		Assert.Equal(1L, EvaluateLua("#timers"));
	}

	[Fact]
	public void LuaJobKernel_StartWhoseSetupFails_RunsTheHooksRemovesTheJobAndRaises()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs);

		string failure = RunKernelFailure("""
		                                  jobStart(a[1], 'capture-' .. a[1] .. '-1', 'capture', 8, 1000, function(job)
		                                  	job.onStop = function(_, state) setupStop = state end
		                                  	error('breakpoint slots are full', 0)
		                                  end)
		                                  """, OwnNamespace);

		Assert.Equal("breakpoint slots are full", failure);
		Assert.Equal("failed", ReadGlobal("setupStop"));
		Assert.Equal("not_found", PollError("capture-bbbb22-1", 0, 1).Kind);
		JsonElement started = Parse(RunKernel("""
		                                      local job = jobStart(a[1], 'capture-' .. a[1] .. '-2', 'capture', 8, 1000, function(job)
		                                      	jobPush(job, 'armed')
		                                      end)
		                                      return {id = job.id, state = job.state, last = job.last}
		                                      """, OwnNamespace));
		Assert.Equal(("capture-bbbb22-2", "running", 1L), (started.GetProperty("id").GetString(),
			started.GetProperty("state").GetString(), started.GetProperty("last").GetInt64()));
	}

	private static string CreateJob(string kind, int bufferLimit, int timeToLive, int items,
		string jobNamespace = OwnNamespace)
	{
		JsonElement created = Parse(RunKernel("""
		                                      local store = stateStore(a[1], false)
		                                      local id = a[2] .. '-' .. a[1] .. '-' .. ((store and store.order or 0) + 1)
		                                      local job = jobCreate(a[1], id, a[2], a[3], a[4])
		                                      for i = 1, a[5] do jobPush(job, i * 10) end
		                                      return {id = job.id}
		                                      """, jobNamespace, kind, bufferLimit, timeToLive, items));
		return created.GetProperty("id").GetString()!;
	}

	private static KernelPage PollJob(string id, long afterSequence, long limit)
	{
		return ReadKernel(LuaJobKernelScripts.Poll, StateJsonContext.Default.LuaJobPage, OwnNamespace, id,
			afterSequence, limit);
	}

	private static long[] Items(KernelPage page)
	{
		return [.. page.Items.Select(static item => item.GetInt64())];
	}

	private static LuaScriptError PollError(string id, long afterSequence, long limit)
	{
		JsonCopy copy = CopyJson(LuaToolRuntime.BuildSource(LuaJobKernelScripts.Poll,
			[OwnNamespace, id, afterSequence, limit]));
		Assert.NotNull(copy.Error);
		return copy.Error;
	}

	private static LuaStateSweep SweepJobs()
	{
		return ReadKernel(LuaJobKernelScripts.Sweep, StateJsonContext.Default.LuaStateSweep, OwnNamespace);
	}

	private static T ReadKernel<T>(string script, JsonTypeInfo<T> type,
		params object?[] arguments)
	{
		LuaJsonResult<T> result = ReadJson(LuaToolRuntime.BuildSource(script, arguments), type);
		Assert.False(result.IsError, result.Error?.Message);
		return result.Value;
	}

	private static string RunKernel(string body, params object?[] arguments)
	{
		return CopyJson(LuaToolRuntime.BuildSource(LuaJobKernelScripts.Strategies + "\n" + body, arguments)).Json;
	}

	private static string RunKernelFailure(string body, params object?[] arguments)
	{
		using LuaRuntimeOperation operation = AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		byte[] source = Encoding.UTF8.GetBytes(
			LuaToolRuntime.BuildSource(LuaJobKernelScripts.Strategies + "\n" + body, arguments));
		LuaStatus status = state.TryExecute(source, 0, "=CheatEngine.Mcp/job_kernel"u8);
		Assert.False(status.IsOk, "The kernel call was expected to raise a Lua error.");
		return LuaError.FromStack(state, status).Message;
	}

	private static JsonElement Parse(string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}
}
