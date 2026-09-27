using System.Collections.Concurrent;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;

using KernelPage = CheatEngine.Mcp.Core.Jobs.LuaJobPage;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>
///     The job registry: atomic <c>MaxJobs</c>, id classification, idempotent stops, TTLs, managed-job cancellation and
///     Lua jobs over canned kernel answers.
/// </summary>
public sealed class JobRegistryTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
	private static readonly long[] LuaItems = [10, 20];
	private static readonly long[] Retained = [3, 4, 5];
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task StartManaged_ConcurrentStarts_NeverExceedMaxJobsAndRefuseTheRestAsBusy()
	{
		StateTestHarness harness = new(new McpExecutionOptions { MaxJobs = 4 });
		ConcurrentBag<ManagedJob<long>> started = [];
		ConcurrentBag<CheatEngineToolException> refused = [];
		int ran = 0;

		Parallel.For(0, 32, _ =>
		{
			try
			{
				started.Add(harness.Jobs.StartManaged<long>("probe", TimeSpan.FromSeconds(60), 8,
					async (_, token) =>
					{
						Interlocked.Increment(ref ran);
						await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
					}));
			}
			catch (CheatEngineToolException exception)
			{
				refused.Add(exception);
			}
		});

		Assert.Equal(4, started.Count);
		Assert.Equal(28, refused.Count);
		Assert.All(refused, error => Assert.Equal((ToolErrorKind.Busy, ToolHostEffect.NotStarted, true),
			(error.Error.Kind, error.Error.HostEffect, error.Error.Retryable)));
		Assert.Equal(4, harness.Jobs.Count);
		Assert.Equal(4, harness.Resources.Count);
		harness.Jobs.Dispose();
		await Task.WhenAll(started.Select(static job => job.Completion)).WaitAsync(Wait, Token);
		Assert.Equal(4, Volatile.Read(ref ran));
		Assert.All(started, job => Assert.Equal(JobState.Cancelled, job.State));
	}

	[Fact]
	public async Task Get_JobIds_ClassifyMalformedMismatchedForeignAndUnknownIds()
	{
		StateTestHarness harness = new();
		ManagedJob<long> job = harness.Jobs.StartManaged<long>("probe", TimeSpan.FromSeconds(60), 8,
			static (_, _) => Task.CompletedTask);
		await job.Completion.WaitAsync(Wait, Token);
		string own = harness.Jobs.Namespace;

		Assert.Equal($"probe-{own}-1", job.Id);
		Assert.Same(job, harness.Jobs.Get<ManagedJob<long>>(job.Id, "probe"));
		Assert.Equal(ToolErrorKind.InvalidArgument, Refusal(() => harness.Jobs.Get("probe-1")).Kind);
		Assert.Equal(ToolErrorKind.InvalidArgument, Refusal(() => harness.Jobs.Get("Probe-abc-1")).Kind);
		Assert.Equal(ToolErrorKind.InvalidArgument,
			Refusal(() => harness.Jobs.Get<ManagedJob<long>>(job.Id, "capture")).Kind);
		ToolError foreign = Refusal(() => harness.Jobs.Get("probe-0badf00d-1"));
		Assert.Equal(ToolErrorKind.NotFound, foreign.Kind);
		Assert.Contains("earlier plugin activation", foreign.Message, StringComparison.Ordinal);
		Assert.Equal(ToolErrorKind.NotFound, Refusal(() => harness.Jobs.Get($"probe-{own}-2")).Kind);
		Assert.Equal(ToolErrorKind.NotFound, Refusal(() => harness.Jobs.Stop($"probe-{own}-2", Token)).Kind);
		Assert.Equal("jobId", Refusal(() => harness.Jobs.Get("x")).Details!.Value.GetProperty("parameter").GetString());
	}

	[Fact]
	public async Task Stop_RunningManagedJob_StopsItDiscardsItemsAndARepeatedStopIsAlreadyReleased()
	{
		StateTestHarness harness = new(onMainThread: false);
		ManagedJob<long> job = harness.Jobs.StartManaged<long>("probe", TimeSpan.FromSeconds(60), 8,
			static async (writer, token) =>
			{
				writer.Add(1);
				await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
			});
		SpinWait.SpinUntil(() => job.GetStatus().Total == 1, Wait);

		JobStopResult stopped = harness.Jobs.Stop(job.Id, Token);

		Assert.Equal((job.Id, true, false), (stopped.JobId, stopped.Released, stopped.AlreadyReleased));
		Assert.Equal(JobState.Stopped, job.State);
		Assert.Equal(0, harness.Resources.Count);
		ToolError poll = Refusal(() => job.Poll(0, 10));
		Assert.Equal(ToolErrorKind.NotFound, poll.Kind);
		Assert.Contains("was stopped", poll.Message, StringComparison.Ordinal);
		Assert.Equal((true, true), (harness.Jobs.Stop(job.Id, Token).Released,
			harness.Jobs.Stop(job.Id, Token).AlreadyReleased));
	}

	[Fact]
	public async Task Stop_OnCheatEngineMainThread_NeverWaitsAndReportsARetryableStop()
	{
		StateTestHarness harness = new(onMainThread: true);
		using SemaphoreSlim gate = new(0);
		ManagedJob<long> job = harness.Jobs.StartManaged<long>("probe", TimeSpan.FromSeconds(60), 8,
			async (_, token) =>
			{
				try
				{
					await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
				}
				finally
				{
					await gate.WaitAsync(Wait, Token).ConfigureAwait(false);
				}
			});

		CheatEngineToolException pending =
			Assert.Throws<CheatEngineToolException>(() => harness.Jobs.Stop(job.Id, Token));

		Assert.Equal((ToolErrorKind.PartialEffect, true), (pending.Error.Kind, pending.Error.Retryable));
		Assert.Equal("stop_pending",
			pending.Error.Details!.Value.GetProperty("release").GetProperty("kind").GetString());
		Assert.Equal(JobState.Stopping, job.State);
		Assert.Equal(1, harness.Resources.Count);
		gate.Release();
		await job.Completion.WaitAsync(Wait, Token);
		Assert.Equal(JobState.Stopped, job.State);
		Assert.True(harness.Jobs.Stop(job.Id, Token).AlreadyReleased);
		Assert.Equal(0, harness.Resources.Count);
	}

	[Fact]
	public async Task Stop_FinishedJob_SucceedsAsAlreadyReleasedAndDiscardsItsItems()
	{
		StateTestHarness harness = new();
		ManagedJob<long> job = harness.Jobs.StartManaged<long>("probe", TimeSpan.FromSeconds(60), 8,
			static (writer, _) =>
			{
				writer.Add(7);
				return Task.CompletedTask;
			});
		await job.Completion.WaitAsync(Wait, Token);
		Assert.Equal(JobState.Completed, job.State);
		harness.Resources.EnsureCanChangeTarget(new TargetTransition(1, 2), Token);

		JobStopResult stopped = harness.Jobs.Stop(job.Id, Token);

		Assert.True(stopped.AlreadyReleased);
		Assert.Equal(ToolErrorKind.NotFound, Refusal(() => job.Poll(0, 1)).Kind);
	}

	[Fact]
	public async Task Poll_ManagedJob_IsNonConsumingIdempotentAndReportsEvictions()
	{
		StateTestHarness harness = new();
		ManagedJob<long> job = harness.Jobs.StartManaged<long>("probe", TimeSpan.FromSeconds(60), 3,
			static (writer, _) =>
			{
				for (long item = 1; item <= 5; item++)
				{
					writer.Add(item);
				}

				writer.Progress(5, 5);
				return Task.CompletedTask;
			});
		await job.Completion.WaitAsync(Wait, Token);

		JobPoll<long> page = job.Poll(0, 10);

		Assert.Equal(Retained, page.Items);
		Assert.Equal((3L, 5L, false, 2L), (page.FirstSequence, page.NextAfterSequence, page.More, page.Dropped));
		Assert.Equal((JobState.Completed, 3L, 5L, 5L), (page.Job.State, page.Job.Buffered, page.Job.Total,
			page.Job.ProgressDone!.Value));
		Assert.Equal(page.Items, job.Poll(0, 10).Items);
		Assert.Equal(harness.Time.GetUtcNow(), page.Job.CreatedUtc);
		Assert.Equal(ToolErrorKind.InvalidArgument, Refusal(() => job.Poll(6, 1)).Kind);
	}

	[Fact]
	public async Task Ttl_EndedTimeToLive_MakesTheJobUnknownAndForgetsItsResource()
	{
		StateTestHarness harness = new();
		ManagedJob<long> job = harness.Jobs.StartManaged<long>("probe", TimeSpan.FromSeconds(30), 8,
			static (_, _) => Task.CompletedTask);
		await job.Completion.WaitAsync(Wait, Token);
		harness.Time.Advance(TimeSpan.FromSeconds(29));
		Assert.Same(job, harness.Jobs.Get(job.Id));
		Assert.Equal(1000L, job.GetStatus().ExpiresInMs);

		harness.Time.Advance(TimeSpan.FromSeconds(1));

		Assert.Equal(ToolErrorKind.NotFound, Refusal(() => harness.Jobs.Get(job.Id)).Kind);
		Assert.Equal(ToolErrorKind.NotFound, Refusal(() => harness.Jobs.Stop(job.Id, Token)).Kind);
		Assert.Equal(0, harness.Resources.Count);
		Assert.Equal(0, harness.Jobs.Count);
	}

	[Fact]
	public void ResolveLimits_CallerValues_UseDefaultsAndCaps()
	{
		StateTestHarness harness = new(new McpExecutionOptions { JobMaxTtlSeconds = 60, JobBufferLimit = 100 });

		Assert.Equal(TimeSpan.FromSeconds(60), harness.Jobs.ResolveTimeToLive(null));
		Assert.Equal(TimeSpan.FromSeconds(10), harness.Jobs.ResolveTimeToLive(10));
		Assert.Equal("lifetimeSeconds", Refusal(() => harness.Jobs.ResolveTimeToLive(61)).Details!.Value
			.GetProperty("parameter").GetString());
		Assert.Equal(ToolErrorKind.InvalidArgument, Refusal(() => harness.Jobs.ResolveTimeToLive(0)).Kind);
		Assert.Equal(100, harness.Jobs.ResolveBufferLimit(null));
		Assert.Equal(ToolErrorKind.InvalidArgument,
			Refusal(() => harness.Jobs.ResolveBufferLimit(101, "maximumHits")).Kind);
		Assert.Equal(TimeSpan.FromSeconds(120), new StateTestHarness().Jobs.ResolveTimeToLive(null));
		Assert.Equal(ToolErrorKind.InvalidArgument,
			Refusal(() => new StateTestHarness().Jobs.ResolveTimeToLive(301)).Kind);
		Assert.Equal(ToolErrorKind.InvalidArgument, Refusal(() => JobRegistry.ValidatePoll(-1, 1)).Kind);
		Assert.Equal(ToolErrorKind.InvalidArgument, Refusal(() => JobRegistry.ValidatePoll(0, 0)).Kind);
		Assert.Equal(ToolErrorKind.InvalidArgument,
			Refusal(() => JobRegistry.ValidatePoll(0, JobRegistry.MaximumPollItems + 1)).Kind);
		Assert.Throws<ArgumentOutOfRangeException>(() => harness.Jobs.StartManaged<long>("probe",
			TimeSpan.FromSeconds(61), 8, static (_, _) => Task.CompletedTask));
	}

	[Fact]
	public async Task ManagedJob_ActivationStopping_EndsTheJobAsCancelled()
	{
		StateTestHarness harness = new();
		ManagedJob<long> job = harness.Jobs.StartManaged<long>("probe", TimeSpan.FromSeconds(60), 8,
			static (_, token) => Task.Delay(Timeout.Infinite, token));

		harness.Stopping.Cancel();

		await job.Completion.WaitAsync(Wait, Token);
		Assert.Equal(JobState.Cancelled, job.State);
		Assert.False(job.HoldsHostState);
		harness.Resources.EnsureCanChangeTarget(new TargetTransition(1, 2), Token);
	}

	[Theory]
	[InlineData("fault", JobState.Failed, ManagedJob<long>.UnexpectedMessage)]
	[InlineData("contract", JobState.Failed, "The map is gone.")]
	[InlineData("target", JobState.TargetChanged, "The process changed.")]
	[InlineData("stopping", JobState.Cancelled, "The activation is stopping.")]
	[InlineData("disposed", JobState.Cancelled, ManagedJob<long>.ActivationEndedMessage)]
	public async Task ManagedJob_WorkFailure_EndsWithACallerSafeStateAndMessage(string failure, JobState state,
		string message)
	{
		StateTestHarness harness = new();
		ManagedJob<long> job = harness.Jobs.StartManaged<long>("probe", TimeSpan.FromSeconds(60), 8,
			(_, _) => throw Failure(failure));

		await job.Completion.WaitAsync(Wait, Token);

		Assert.Equal((state, message), (job.State, job.Error));
		Assert.Equal(message, job.GetStatus().Error);
		Assert.Equal(state is JobState.Cancelled ? 0 : 1, harness.Jobs.Count);
	}

	[Fact]
	public async Task ReleaseAll_RunningManagedJob_IsStopPendingUntilItsWorkEnds()
	{
		StateTestHarness harness = new(withLedger: false);
		using SemaphoreSlim gate = new(0);
		ManagedJob<long> job = harness.Jobs.StartManaged<long>("probe", TimeSpan.FromSeconds(60), 8,
			async (_, token) =>
			{
				try
				{
					await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
				}
				finally
				{
					await gate.WaitAsync(Wait, Token).ConfigureAwait(false);
				}
			});
		Assert.Equal(TargetResourceState.Active, Assert.Single(harness.Resources.List()).State);

		ReleaseAllResult pending = harness.Resources.ReleaseAll(Token);

		Assert.Equal((ResourceReleaseKind.StopPending, true),
			(pending.Failed!.Release.Kind, pending.Failed.Release.IsRetryable));
		Assert.Equal(TargetResourceState.StopPending, Assert.Single(pending.Remaining).State);
		gate.Release();
		await job.Completion.WaitAsync(Wait, Token);
		Assert.True(harness.Resources.ReleaseAll(Token).IsComplete);
		Assert.Equal(0, harness.Jobs.Count);
		Assert.True(harness.Jobs.Stop(job.Id, Token).AlreadyReleased);
	}

	[Fact]
	public void StartLua_StartFails_FreesTheSlotAndTracksNothing()
	{
		StateTestHarness harness = new(new McpExecutionOptions { MaxJobs = 1 });

		Assert.Throws<InvalidOperationException>(() => harness.Jobs.StartLua("capture", TimeSpan.FromSeconds(60), 8,
			StateTestJsonContext.Default.Int64, static _ => throw new InvalidOperationException("start failed")));

		Assert.Equal((0, 0), (harness.Jobs.Count, harness.Resources.Count));
		LuaJob<long> job = harness.Jobs.StartLua("capture", TimeSpan.FromSeconds(60), 8,
			StateTestJsonContext.Default.Int64, static _ =>
			{
			});
		Assert.Equal($"capture-{harness.Jobs.Namespace}-2", job.Id);
		Assert.Throws<InvalidOperationException>(() => new StateTestHarness(withLedger: false).Jobs.StartLua("capture",
			TimeSpan.FromSeconds(60), 8, StateTestJsonContext.Default.Int64, static _ =>
			{
			}));
	}

	[Fact]
	public void LuaJob_Poll_TypesItemsInOneDispatchAndKeepsTheManagedStartTime()
	{
		StateTestHarness harness = new();
		JobStart? reserved = null;
		LuaJob<long> job = harness.Jobs.StartLua("capture", TimeSpan.FromSeconds(60), 16,
			StateTestJsonContext.Default.Int64, start => reserved = start);
		harness.Answer(source =>
		{
			Assert.Contains($"\"{job.Id}\"", source, StringComparison.Ordinal);
			return new KernelPage(new JobStatus(job.Id, "capture", JobState.Running, 5, 59995, 2, 2, 0),
				[Json("10"), Json("20")], 1, 2, false, 0);
		});
		int before = harness.Dispatches;

		JobPoll<long> page = job.Poll(0, 10, Token);

		Assert.Equal(LuaItems, page.Items);
		Assert.Equal((1L, 2L, false, 0L), (page.FirstSequence, page.NextAfterSequence, page.More, page.Dropped));
		Assert.Equal(job.CreatedUtc, page.Job.CreatedUtc);
		Assert.Equal(before + 1, harness.Dispatches);
		Assert.Equal("job_poll", harness.LuaCalls.Last().Operation);
		Assert.Equal((job.Id, harness.Jobs.Namespace, 16, 60000),
			(reserved!.Id, reserved.Namespace, reserved.BufferLimit, reserved.TimeToLiveMilliseconds));
		Assert.Equal(ToolErrorKind.InvalidArgument, Refusal(() => job.Poll(-1, 10, Token)).Kind);
		Assert.Equal(before + 1, harness.Dispatches);
	}

	[Fact]
	public void LuaJob_BrokenItem_IsAnInternalContractViolationWithACompletedEffect()
	{
		StateTestHarness harness = new();
		LuaJob<long> job = harness.Jobs.StartLua("capture", TimeSpan.FromSeconds(60), 16,
			StateTestJsonContext.Default.Int64, static _ =>
			{
			});
		harness.Answer(_ => new KernelPage(new JobStatus(job.Id, "capture", JobState.Running, 5, 1, 1, 1, 0),
			[Json("\"text\"")], 1, 1, false, 0));

		ToolError error = Refusal(() => job.Poll(0, 10, Token));

		Assert.Equal((ToolErrorKind.Internal, ToolHostEffect.Completed), (error.Kind, error.HostEffect));
	}

	[Fact]
	public void LuaJob_Stop_ReleasesTheKernelJobAndARepeatedStopIsAlreadyReleased()
	{
		StateTestHarness harness = new();
		LuaJob<long> job = harness.Jobs.StartLua("capture", TimeSpan.FromSeconds(60), 16,
			StateTestJsonContext.Default.Int64, static _ =>
			{
			});
		harness.Answer(_ => new LuaJobStop(true, true, true, "stopped"));

		JobStopResult stopped = harness.Jobs.Stop(job.Id, Token);

		Assert.Equal((true, false), (stopped.Released, stopped.AlreadyReleased));
		Assert.Equal("mcp_job_stop", harness.LuaCalls.Last().Operation);
		Assert.Equal(JobState.Stopped, job.State);
		Assert.True(harness.Jobs.Stop(job.Id, Token).AlreadyReleased);
		Assert.Single(harness.LuaCalls);
		Assert.Equal(0, harness.Resources.Count);
	}

	[Fact]
	public void LuaJob_StopWithFailedCleanup_IsAPartialEffectThatNeedsManualRecovery()
	{
		StateTestHarness harness = new();
		LuaJob<long> job = harness.Jobs.StartLua("capture", TimeSpan.FromSeconds(60), 16,
			StateTestJsonContext.Default.Int64, static _ =>
			{
			});
		harness.Answer(_ => new LuaJobStop(true, true, false, "stopped", "breakpoint removal refused"));

		CheatEngineToolException
			error = Assert.Throws<CheatEngineToolException>(() => harness.Jobs.Stop(job.Id, Token));

		Assert.Equal((ToolErrorKind.PartialEffect, false), (error.Error.Kind, error.Error.Retryable));
		Assert.Contains("breakpoint removal refused", error.Error.Message, StringComparison.Ordinal);
		Assert.True(error.Error.Details!.Value.GetProperty("release").GetProperty("requiresManualRecovery")
			.GetBoolean());
		Assert.Equal(ToolErrorKind.NotFound, Refusal(() => harness.Jobs.Stop(job.Id, Token)).Kind);
		Assert.Equal(0, harness.Resources.Count);
	}

	[Fact]
	public void LuaJob_PollOfASweptJob_IsNotFoundAndDropsTheHandle()
	{
		StateTestHarness harness = new();
		LuaJob<long> job = harness.Jobs.StartLua("capture", TimeSpan.FromSeconds(60), 16,
			StateTestJsonContext.Default.Int64, static _ =>
			{
			});
		harness.Declare<KernelPage>("not_found", "Unknown or expired job.");

		Assert.Equal(ToolErrorKind.NotFound, Refusal(() => job.Poll(0, 10, Token)).Kind);

		Assert.True(job.IsEnded);
		Assert.Equal(ToolErrorKind.NotFound, Refusal(() => harness.Jobs.Get(job.Id)).Kind);
		Assert.Equal(0, harness.Resources.Count);
	}

	[Fact]
	public async Task List_LuaAndManagedJobs_UsesOneDispatchForTheLuaStatuses()
	{
		StateTestHarness harness = new();
		ManagedJob<long> managed = harness.Jobs.StartManaged<long>("probe", TimeSpan.FromSeconds(60), 8,
			static (_, _) => Task.CompletedTask);
		await managed.Completion.WaitAsync(Wait, Token);
		harness.Time.Advance(TimeSpan.FromSeconds(1));
		LuaJob<long> lua = harness.Jobs.StartLua("capture", TimeSpan.FromSeconds(60), 16,
			StateTestJsonContext.Default.Int64, static _ =>
			{
			});
		harness.Answer(_ => new LuaJobStatuses([new JobStatus(lua.Id, "capture", JobState.Completed, 3, 57, 4, 4, 0)]));
		int before = harness.Dispatches;

		IReadOnlyList<JobStatus> jobs = harness.Jobs.List(Token);

		Assert.Equal(before + 1, harness.Dispatches);
		Assert.Equal([managed.Id, lua.Id], jobs.Select(static status => status.JobId).ToArray());
		Assert.Equal((JobState.Completed, JobState.Completed), (jobs[0].State, jobs[1].State));
		Assert.Equal(lua.CreatedUtc, jobs[1].CreatedUtc);
		Assert.False(lua.HoldsHostState);
		Assert.Empty(new StateTestHarness().Jobs.List(Token));
	}

	private static Exception Failure(string failure)
	{
		return failure switch
		{
			"fault" => new InvalidOperationException(@"C:\secret\path"),
			"contract" => CheatEngineToolException.NotFound("The map is gone."),
			"target" => new CheatEngineToolException(new ToolError(ToolErrorKind.TargetChanged,
				"The process changed.", null, ToolHostEffect.Unknown, false)),
			"stopping" => new CheatEngineToolException(new ToolError(ToolErrorKind.Stopping,
				"The activation is stopping.", null, ToolHostEffect.NotStarted, false)),
			_ => new ObjectDisposedException("scope")
		};
	}

	private static ToolError Refusal(Action call)
	{
		return Assert.Throws<CheatEngineToolException>(call).Error;
	}

	private static ToolError Refusal<T>(Func<T> call)
	{
		return Assert.Throws<CheatEngineToolException>(() => call()).Error;
	}

	private static JsonElement Json(string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}
}
