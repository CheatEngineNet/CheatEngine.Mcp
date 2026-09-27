using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Targets;

namespace CheatEngine.Mcp.Core.Jobs;

/// <summary>
///     A job whose work is managed code on a thread-pool task: pure computation, or a sequence of short dispatches that
///     each stay within the dispatch budget. It is never awaited or joined on Cheat Engine's main thread.
/// </summary>
/// <remarks>
///     <para>
///         The work's token is cancelled by a caller's stop, by the TTL and when the activation begins to stop
///         (<see cref="ICheatEngineClient.Stopping" />). Cancellation is requested asynchronously, so no continuation of
///         the
///         work ever runs inline on the thread that asked, which may be Cheat Engine's main thread.
///     </para>
///     <para>
///         The work reports through its <see cref="JobWriter{TItem}" /> and ends by returning (completed) or throwing:
///         cancellation ends it as stopped, expired or cancelled; the end of the activation (<c>stopping</c>, an expired
///         activation or a disposed service) as cancelled; <c>target_changed</c> as target changed; anything else as
///         failed, with only a caller-safe message.
///     </para>
/// </remarks>
/// <typeparam name="TItem">The item type.</typeparam>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
	Justification =
		"The source has no timer and no wait handle, so it holds no unmanaged resource; it is only ever cancelled, never disposed, because a stop may race the end of the work.")]
public sealed class ManagedJob<TItem> : McpJob
{
	internal const string ActivationEndedMessage = "The plugin activation ended.";
	internal const string UnexpectedMessage = "The job failed unexpectedly.";

	private const int NoStop = 0;
	private const int CallerStop = 1;
	private const int Expiry = 2;
	private const int ActivationEnd = 3;

	private readonly CancellationTokenSource _cancellation = new();
	private readonly TimeProvider _time;
	private readonly JobWriter<TItem> _writer;
	private Task _completion = Task.CompletedTask;
	private string? _error;
	private int _state = (int) JobState.Running;
	private int _stopReason;

	internal ManagedJob(JobStart start, TimeProvider time)
		: base(start)
	{
		ArgumentNullException.ThrowIfNull(time);
		_time = time;
		_writer = new JobWriter<TItem>(start.BufferLimit);
	}

	/// <inheritdoc />
	public override JobState State => (JobState) Volatile.Read(ref _state);

	/// <inheritdoc />
	/// <remarks>A managed job holds no host state once its work ended, so its cleanup never fails.</remarks>
	public override string? CleanupError => null;

	/// <summary>Why the job failed or ended early, when it did.</summary>
	public string? Error => Volatile.Read(ref _error);

	/// <inheritdoc />
	internal override Task Completion => Volatile.Read(ref _completion);

	/// <summary>Reads the items after <paramref name="afterSequence" />; read-only and idempotent, with no Cheat Engine call.</summary>
	/// <param name="afterSequence">0 first, then the previous page's <see cref="JobPoll{TItem}.NextAfterSequence" />.</param>
	/// <param name="limit">1 to <see cref="JobRegistry.MaximumPollItems" /> items.</param>
	/// <returns>The page.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>invalid_argument</c> for a bad cursor or limit, <c>not_found</c> once the job was stopped or expired.
	/// </exception>
	public JobPoll<TItem> Poll(long afterSequence, int limit)
	{
		JobRegistry.ValidatePoll(afterSequence, limit);
		if (IsEnded)
		{
			throw JobRegistry.UnknownJob(Id, State is JobState.Stopped);
		}

		JobWriterPage<TItem> page = _writer.Read(afterSequence, limit);
		return new JobPoll<TItem>(GetStatus(), page.Items, page.FirstSequence, page.NextAfterSequence, page.More,
			page.Dropped);
	}

	/// <summary>The job's status, with no Cheat Engine call.</summary>
	/// <returns>The status.</returns>
	public JobStatus GetStatus()
	{
		return Describe(null, _time.GetUtcNow());
	}

	/// <inheritdoc />
	/// <remarks>
	///     It asks the work to stop and never waits: while the work has not ended, the outcome is
	///     <see cref="ResourceReleaseKind.StopPending" />, which a later release completes.
	/// </remarks>
	public override ResourceReleaseOutcome Release(CancellationToken cancellationToken)
	{
		if (IsEnded)
		{
			return ResourceReleaseOutcome.AlreadyReleased();
		}

		bool running = !HasEnded;
		if (running)
		{
			RequestStop(CallerStop);
			if (!HasEnded)
			{
				return ResourceReleaseOutcome.StopPending();
			}
		}

		Discard();
		return running ? ResourceReleaseOutcome.Released() : ResourceReleaseOutcome.AlreadyReleased();
	}

	/// <inheritdoc />
	internal override JobStatus Describe(JobStatus? observed, DateTimeOffset now)
	{
		(long buffered, long total, long dropped, long? done, long? all) = _writer.Counters();
		return Status(State, now, buffered, total, dropped) with
		{
			ProgressDone = done,
			ProgressTotal = all,
			Error = Error
		};
	}

	/// <inheritdoc />
	internal override void Expire()
	{
		RequestStop(Expiry);
		if (HasEnded)
		{
			Discard();
		}
	}

	/// <inheritdoc />
	internal override void CancelWithoutJoin()
	{
		RequestStop(ActivationEnd);
	}

	/// <summary>Starts the work on the thread pool.</summary>
	/// <param name="work">The work.</param>
	/// <param name="ended">Runs on the work's thread once the job reached its final state.</param>
	/// <param name="stopping">The activation's stopping token.</param>
	internal void Run(Func<JobWriter<TItem>, CancellationToken, Task> work, Action<McpJob> ended,
		CancellationToken stopping)
	{
		ArgumentNullException.ThrowIfNull(work);
		ArgumentNullException.ThrowIfNull(ended);
		Volatile.Write(ref _completion,
			Task.Run(() => RunAsync(work, ended, stopping), CancellationToken.None));
	}

	/// <inheritdoc />
	private protected override void OnDiscarded()
	{
		_writer.Discard();
	}

	private async Task RunAsync(Func<JobWriter<TItem>, CancellationToken, Task> work, Action<McpJob> ended,
		CancellationToken stopping)
	{
		CancellationToken token = _cancellation.Token;
		JobState final;
		string? error = null;
		using (stopping.Register(static state => ((ManagedJob<TItem>) state!).RequestStop(ActivationEnd), this))
		{
			try
			{
				await work(_writer, token).ConfigureAwait(false);
				final = JobState.Completed;
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				final = Interrupted();
			}
			catch (ObjectDisposedException)
			{
				final = JobState.Cancelled;
				error = ActivationEndedMessage;
			}
			catch (CheatEngineToolException exception) when (exception.Error.Kind is ToolErrorKind.Stopping)
			{
				final = JobState.Cancelled;
				error = exception.Error.Message;
			}
			catch (CheatEngineToolException exception) when (exception.Error.Kind is ToolErrorKind.Cancelled &&
															 token.IsCancellationRequested)
			{
				final = Interrupted();
			}
			catch (CheatEngineToolException exception) when (exception.Error.Kind is ToolErrorKind.TargetChanged)
			{
				final = JobState.TargetChanged;
				error = exception.Error.Message;
			}
			catch (CheatEngineToolException exception)
			{
				final = JobState.Failed;
				error = exception.Error.Message;
			}
			catch (CheatEngineClientException exception) when (exception.Failure.Kind is
																   CheatEngineFailureKind.ActivationExpired)
			{
				final = JobState.Cancelled;
				error = ActivationEndedMessage;
			}
			catch (Exception)
			{
				// Never report internal details; the work's own failures use CheatEngineToolException.
				final = JobState.Failed;
				error = UnexpectedMessage;
			}
		}

		Volatile.Write(ref _error, error);
		_writer.Close();
		Volatile.Write(ref _state, (int) final);
		ended(this);
	}

	private JobState Interrupted()
	{
		return Volatile.Read(ref _stopReason) switch
		{
			CallerStop => JobState.Stopped,
			Expiry => JobState.Expired,
			_ => JobState.Cancelled
		};
	}

	/// <summary>Asks a running job to stop; the first reason wins. Cancellation callbacks run on the thread pool.</summary>
	/// <param name="reason">Why it stops.</param>
	private void RequestStop(int reason)
	{
		Interlocked.CompareExchange(ref _stopReason, reason, NoStop);
		if (Interlocked.CompareExchange(ref _state, (int) JobState.Stopping, (int) JobState.Running) ==
			(int) JobState.Running)
		{
			_ = _cancellation.CancelAsync();
		}
	}
}
