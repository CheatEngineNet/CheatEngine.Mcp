using System.Text.Json;

using CheatEngine.Mcp.Core.Jobs;

using KernelPage = CheatEngine.Mcp.Core.Jobs.LuaJobPage;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     The three job strategies on the kernel against the real Lua 5.3 runtime, with CE's clock, timers and threads
///     stubbed: event (callback wrappers), slices (main-thread timer ticks of at most 20 ms) and thread (a worker that
///     hands every result back through <c>synchronize</c>).
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	// A wrapping 32-bit tick count, timers, and threads whose body the test runs on the test thread.
	private const string StrategyHostStubs = JobHostStubs + """

	                                                        getTickCount = function() return clock % 4294967296 end
	                                                        threads = {}
	                                                        createThread = function(body)
	                                                        	if refuseThreads then return nil end
	                                                        	local thread = {Terminated = false, synchronized = 0}
	                                                        	thread.synchronize = function(delivery, ...)
	                                                        		thread.synchronized = thread.synchronized + 1
	                                                        		if beforeSynchronize ~= nil then beforeSynchronize(thread.synchronized) end
	                                                        		return delivery(thread, ...)
	                                                        	end
	                                                        	threads[#threads + 1] = {thread = thread, run = function() return body(thread) end}
	                                                        	return thread
	                                                        end
	                                                        """;

	private static readonly long[] EventItems = [5];
	private static readonly long[] ThreadItems = [100, 200, 300];

	[Fact]
	public void JobEvent_Callback_PushesWhileRunningFailsTheJobOnAnErrorAndGoesIdleAfterwards()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StrategyHostStubs);
		RunKernel("""
		          jobStart(a[1], 'capture-' .. a[1] .. '-1', 'capture', 8, 60000, function(job)
		          	job.onStop = function(_, state) eventStop = state end
		          	callback = jobEvent(job, function(owner, value)
		          		if value == 'boom' then error('handler failed', 0) end
		          		jobPush(owner, value)
		          		return 1
		          	end, 0)
		          end)
		          return true
		          """, OwnNamespace);

		Assert.Equal(1L, EvaluateLua("callback(5)"));
		Assert.Equal(0L, EvaluateLua("callback('boom')"));
		Assert.Equal(0L, EvaluateLua("callback(6)"));

		KernelPage page = PollJob("capture-bbbb22-1", 0, 10);
		Assert.Equal(EventItems, Items(page));
		Assert.Equal((JobState.Failed, "handler failed"), (page.Job.State, page.Job.Error));
		Assert.Equal("failed", ReadGlobal("eventStop"));
	}

	[Fact]
	public void JobSlices_TimerTicks_WorkAtMostTheSliceAcrossTheTickWrapAndCompleteTheJob()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StrategyHostStubs + "\nclock = 4294967280");
		RunKernel("""
		          processed = 0
		          local job = jobStart(a[1], 'dissect-' .. a[1] .. '-1', 'dissect', 64, 60000, function(job)
		          	jobSlices(job, function(owner, expired)
		          		while not expired() do
		          			clock = clock + 5
		          			processed = processed + 1
		          			jobPush(owner, processed)
		          			jobProgress(owner, processed, 10)
		          			if processed >= 10 then return true end
		          		end
		          		return false
		          	end, 50, 20)
		          end)
		          sliceJob = job
		          sliceTimer = job.timer
		          return true
		          """, OwnNamespace);
		Assert.Equal(true,
			EvaluateLua("sliceTimer.Enabled and sliceTimer.Interval == 50 and not sliceTimer.destroyed"));
		Assert.Equal(0L, EvaluateLua("processed"));

		InstallStubs("sliceTimer.OnTimer(sliceTimer)");
		// Four items of 5 ms reach the 20 ms slice, across the 32-bit tick wrap.
		Assert.Equal(4L, EvaluateLua("processed"));
		Assert.Equal(true, EvaluateLua("clock > 4294967296"));
		InstallStubs("sliceTimer.OnTimer(sliceTimer); sliceTimer.OnTimer(sliceTimer)");

		KernelPage page = PollJob("dissect-bbbb22-1", 0, 100);
		Assert.Equal(Enumerable.Range(1, 10).Select(static item => (long) item), Items(page));
		Assert.Equal((JobState.Completed, 10L, 10L), (page.Job.State, page.Job.ProgressDone!.Value,
			page.Job.ProgressTotal!.Value));
		Assert.Equal(true, EvaluateLua("sliceTimer.destroyed and sliceJob.timer == nil"));
		Assert.Equal((3L, 0L), ((long) EvaluateLua("sliceJob.slices")!, (long) EvaluateLua("sliceJob.sliceOverruns")!));

		// A late tick of a destroyed timer does nothing.
		InstallStubs("sliceTimer.OnTimer(sliceTimer)");
		Assert.Equal(10L, EvaluateLua("processed"));
	}

	[Fact]
	public void JobSlices_StopOrStepError_DestroysTheTimerAndOverrunsAreCounted()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StrategyHostStubs);
		RunKernel("""
		          local function start(number, step)
		          	local job = jobStart(a[1], 'search-' .. a[1] .. '-' .. number, 'search', 8, 60000, function(job)
		          		jobSlices(job, step, 100, 10)
		          	end)
		          	return job
		          end
		          slow = start(1, function(owner, expired) clock = clock + 15; return false end)
		          broken = start(2, function() error('disassembler refused', 0) end)
		          return true
		          """, OwnNamespace);

		InstallStubs("slow.timer.OnTimer(slow.timer); broken.timer.OnTimer(broken.timer)");
		Assert.Equal(1L, EvaluateLua("slow.sliceOverruns"));
		KernelPage failed = PollJob("search-bbbb22-2", 0, 1);
		Assert.Equal((JobState.Failed, "disassembler refused"), (failed.Job.State, failed.Job.Error));
		Assert.Equal(true, EvaluateLua("broken.timer == nil"));

		LuaJobStop stopped = ReadKernel(LuaJobKernelScripts.Stop, StateJsonContext.Default.LuaJobStop, OwnNamespace,
			"search-bbbb22-1");
		Assert.Equal((true, true), (stopped.Released, stopped.WasRunning));
		Assert.Equal(true, EvaluateLua("slow.timer == nil and slow.state == 'stopped'"));

		Assert.Contains("slice interval", RunKernelFailure("""
		                                                   jobStart(a[1], 'search-' .. a[1] .. '-3', 'search', 8, 60000, function(job)
		                                                   	jobSlices(job, function() return true end, 49, 20)
		                                                   end)
		                                                   """, OwnNamespace), StringComparison.Ordinal);
		Assert.Contains("A slice may work", RunKernelFailure("""
		                                                     jobStart(a[1], 'search-' .. a[1] .. '-4', 'search', 8, 60000, function(job)
		                                                     	jobSlices(job, function() return true end, 50, 21)
		                                                     end)
		                                                     """, OwnNamespace), StringComparison.Ordinal);
		Assert.Equal("not_found", PollError("search-bbbb22-3", 0, 1).Kind);
	}

	[Fact]
	public void JobThread_Worker_HandsEveryResultBackThroughSynchronizeUntilDone()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StrategyHostStubs);
		RunKernel("""
		          jobStart(a[1], 'dotnetfind-' .. a[1] .. '-1', 'dotnetfind', 8, 60000, function(job)
		          	jobThread(job, function(state)
		          		state.n = (state.n or 0) + 1
		          		return state.n == 3, {state.n * 100}, state.n, 3
		          	end)
		          end)
		          return true
		          """, OwnNamespace);
		KernelPage started = PollJob("dotnetfind-bbbb22-1", 0, 10);
		Assert.Empty(started.Items);
		Assert.Equal(JobState.Running, started.Job.State);

		InstallStubs("threads[1].run()");

		KernelPage page = PollJob("dotnetfind-bbbb22-1", 0, 10);
		Assert.Equal(ThreadItems, Items(page));
		Assert.Equal((JobState.Completed, 3L), (page.Job.State, page.Job.ProgressDone!.Value));
		Assert.Equal(3L, EvaluateLua("threads[1].thread.synchronized"));
		Assert.Equal("CheatEngine.Mcp dotnetfind-bbbb22-1", EvaluateLua("threads[1].thread.Name"));
	}

	[Fact]
	public void JobThread_StopOrProduceError_EndsTheWorkerAtItsNextSynchronizeWithoutWaiting()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StrategyHostStubs);
		RunKernel("""
		          jobStart(a[1], 'monofind-' .. a[1] .. '-1', 'monofind', 8, 60000, function(job)
		          	jobThread(job, function() return false, {1} end)
		          end)
		          jobStart(a[1], 'monofind-' .. a[1] .. '-2', 'monofind', 8, 60000, function(job)
		          	jobThread(job, function() error('pipe closed', 0) end)
		          end)
		          stopFirst = function() jobFinish(jobFind(a[1], 'monofind-' .. a[1] .. '-1'), 'stopped') end
		          return true
		          """, OwnNamespace);

		InstallStubs("beforeSynchronize = function(count) if count == 3 then stopFirst() end end; threads[1].run()");
		KernelPage stopped = PollJob("monofind-bbbb22-1", 0, 10);
		Assert.Equal((JobState.Stopped, 2L), (stopped.Job.State, stopped.Job.Total));
		Assert.Equal(3L, EvaluateLua("threads[1].thread.synchronized"));

		InstallStubs("beforeSynchronize = nil; threads[2].run()");
		KernelPage failed = PollJob("monofind-bbbb22-2", 0, 10);
		Assert.Equal((JobState.Failed, "pipe closed", 0L), (failed.Job.State, failed.Job.Error, failed.Job.Total));

		InstallStubs("refuseThreads = true");
		Assert.Contains("worker thread", RunKernelFailure("""
		                                                  jobStart(a[1], 'monofind-' .. a[1] .. '-3', 'monofind', 8, 60000, function(job)
		                                                  	jobThread(job, function() return true end)
		                                                  end)
		                                                  """, OwnNamespace), StringComparison.Ordinal);
		Assert.Equal("not_found", PollError("monofind-bbbb22-3", 0, 1).Kind);
		InstallStubs("refuseThreads = nil");
		JsonElement terminated = Parse(RunKernel("""
		                                         jobStart(a[1], 'monofind-' .. a[1] .. '-4', 'monofind', 8, 60000, function(job)
		                                         	jobThread(job, function() return false, {7} end)
		                                         end)
		                                         return {count = #threads}
		                                         """, OwnNamespace));
		Assert.Equal(3, terminated.GetProperty("count").GetInt32());
		InstallStubs("threads[3].thread.Terminated = true; threads[3].run()");
		Assert.Equal(1L, PollJob("monofind-bbbb22-4", 0, 10).Job.Total);
	}
}
