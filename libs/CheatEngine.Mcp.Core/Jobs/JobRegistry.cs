using System.Globalization;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Execution;
using CheatEngine.Mcp.Core.Targets;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Core.Jobs;

/// <summary>
///     The activation's jobs: it issues their ids (<c>kind-namespace-number</c>), enforces
///     <see cref="McpExecutionOptions.MaxJobs" /> atomically, tracks every job in <see cref="TargetResources" />, finds,
///     lists and stops them, and ends them at their TTL.
/// </summary>
/// <remarks>
///     <para>
///         Ids: a malformed id or a kind mismatch is <c>invalid_argument</c>; an unknown id, an expired job, a stopped job
///         (for a poll) or a job of an earlier activation is <c>not_found</c>, never a success. Stopping a job that
///         already
///         ended, or was already stopped within its TTL, succeeds with <see cref="JobStopResult.AlreadyReleased" />.
///     </para>
///     <para>
///         TTL: <see cref="McpExecutionOptions.JobDefaultTtlSeconds" /> by default, at most
///         <see cref="McpExecutionOptions.JobMaxTtlSeconds" /> and never more than 300 seconds, because every job touches
///         the target. A finished job stays pollable until its TTL; a stop or the TTL discards its items.
///     </para>
///     <para>
///         The registry's lock is never held while a job starts, a dispatch runs or a Client call is made. Disposing it,
///         as the activation ends, only asks managed jobs to stop; it never waits for them, and Lua jobs end by their TTL
///         in Cheat Engine's Lua without any Client call.
///     </para>
/// </remarks>
public sealed class JobRegistry : IDisposable
{
	/// <summary>How many items one poll returns at most.</summary>
	public const int MaximumPollItems = LuaJobKernelScripts.MaximumPollItems;

	/// <summary>The longest TTL of any job, in seconds, because every job touches the target.</summary>
	public const int MaximumTimeToLiveSeconds = LuaJobKernelScripts.MaximumTimeToLiveMilliseconds / 1000;

	internal const int MaximumTombstones = 256;
	internal const string StopHint = "Stop finished jobs with runtime_stop_job, or wait for their TTL to end.";

	/// <summary>How long <see cref="Stop" /> waits, off Cheat Engine's main thread, for a managed job's work to end.</summary>
	internal static readonly TimeSpan StopGracePeriod = TimeSpan.FromSeconds(1);

	private readonly ToolDispatch _dispatch;
	private readonly Dictionary<string, McpJob> _jobs = new(StringComparer.Ordinal);
	private readonly Lock _lock = new();
	private readonly McpExecutionOptions _options;
	private readonly TargetResources _resources;
	private readonly Dictionary<string, DateTimeOffset> _stopped = new(StringComparer.Ordinal);
	private readonly TimeProvider _time;
	private bool _disposed;
	private int _reserved;

	/// <summary>Creates the activation's registry; nothing touches Cheat Engine until a job starts.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="resources">The activation's target resources, which track every job.</param>
	/// <param name="options">The execution limits; their value is read once.</param>
	/// <param name="time">The clock of TTLs and job ages.</param>
	public JobRegistry(ToolDispatch dispatch, TargetResources resources, IOptions<McpExecutionOptions> options,
		TimeProvider time)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(resources);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(time);
		_dispatch = dispatch;
		_resources = resources;
		_options = options.Value;
		_time = time;
	}

	/// <summary>The activation namespace that every job id carries.</summary>
	public string Namespace => _resources.Namespace;

	/// <summary>How many jobs the activation may retain at once.</summary>
	public int MaxJobs => _options.MaxJobs;

	/// <summary>How many jobs are retained: running, stopping, or finished and still pollable.</summary>
	public int Count
	{
		get
		{
			Purge();
			lock (_lock)
			{
				return _jobs.Count;
			}
		}
	}

	/// <summary>Asks every managed job to stop without waiting for any, as the activation ends.</summary>
	public void Dispose()
	{
		McpJob[] jobs;
		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			jobs = [.. _jobs.Values];
		}

		foreach (McpJob job in jobs)
		{
			job.CancelWithoutJoin();
		}
	}

	/// <summary>Resolves a caller's job lifetime.</summary>
	/// <param name="seconds">The requested lifetime, or <see langword="null" /> for the default.</param>
	/// <param name="parameter">The parameter name reported in an error.</param>
	/// <returns>The lifetime.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_argument</c> when it is out of range.</exception>
	public TimeSpan ResolveTimeToLive(int? seconds, string parameter = "lifetimeSeconds")
	{
		int maximum = Math.Min(_options.JobMaxTtlSeconds, MaximumTimeToLiveSeconds);
		if (seconds is null)
		{
			return TimeSpan.FromSeconds(Math.Min(_options.JobDefaultTtlSeconds, maximum));
		}

		return seconds.Value >= 1 && seconds.Value <= maximum
			? TimeSpan.FromSeconds(seconds.Value)
			: throw CheatEngineToolException.InvalidArgument(parameter,
				string.Create(CultureInfo.InvariantCulture, $"must be between 1 and {maximum} seconds."));
	}

	/// <summary>Resolves a caller's job buffer size.</summary>
	/// <param name="items">The requested number of retained items, or <see langword="null" /> for the configured limit.</param>
	/// <param name="parameter">The parameter name reported in an error.</param>
	/// <returns>The buffer limit.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_argument</c> when it is out of range.</exception>
	public int ResolveBufferLimit(int? items, string parameter = "maximumItems")
	{
		if (items is null)
		{
			return _options.JobBufferLimit;
		}

		return items.Value >= 1 && items.Value <= _options.JobBufferLimit
			? items.Value
			: throw CheatEngineToolException.InvalidArgument(parameter,
				string.Create(CultureInfo.InvariantCulture, $"must be between 1 and {_options.JobBufferLimit}."));
	}

	/// <summary>Checks a poll cursor and page size before any Cheat Engine call.</summary>
	/// <param name="afterSequence">The cursor, 0 or more.</param>
	/// <param name="limit">The page size, 1 to <see cref="MaximumPollItems" />.</param>
	/// <exception cref="CheatEngineToolException"><c>invalid_argument</c> with <c>not_started</c>.</exception>
	public static void ValidatePoll(long afterSequence, int limit)
	{
		if (afterSequence < 0)
		{
			throw CheatEngineToolException.InvalidArgument("afterSequence",
				"must be 0 or the nextAfterSequence of the previous poll.");
		}

		if (limit is < 1 or > MaximumPollItems)
		{
			throw CheatEngineToolException.InvalidArgument("limit",
				string.Create(CultureInfo.InvariantCulture, $"must be between 1 and {MaximumPollItems}."));
		}
	}

	/// <summary>
	///     Starts a Lua job. The slot is reserved atomically, then <paramref name="start" /> runs, without any registry lock
	///     held, to create the job in Cheat Engine: usually one dispatch whose fixed script prepends
	///     <see cref="LuaJobKernelScripts.Strategies" /> and calls <c>jobStart(a[1], a[2], kind, a[3], a[4], setup)</c> with
	///     the
	///     arguments of <see cref="JobStart" />. When <paramref name="start" /> throws, the slot is freed and nothing is
	///     tracked; a Lua job it created anyway ends by its TTL.
	/// </summary>
	/// <typeparam name="TItem">The item type.</typeparam>
	/// <param name="kind">The job kind, a lowercase identifier of at most 32 characters, such as <c>capture</c>.</param>
	/// <param name="timeToLive">The lifetime, from <see cref="ResolveTimeToLive" />.</param>
	/// <param name="bufferLimit">The retained items, from <see cref="ResolveBufferLimit" />.</param>
	/// <param name="itemType">The source-generated metadata of <typeparamref name="TItem" />.</param>
	/// <param name="start">Creates the Lua job with the reserved identity.</param>
	/// <returns>The registered job.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>busy</c> with <c>not_started</c> when <see cref="MaxJobs" /> jobs are
	///     retained.
	/// </exception>
	/// <exception cref="InvalidOperationException">The activation has no state ledger.</exception>
	public LuaJob<TItem> StartLua<TItem>(string kind, TimeSpan timeToLive, int bufferLimit,
		JsonTypeInfo<TItem> itemType, Action<JobStart> start)
	{
		ArgumentNullException.ThrowIfNull(itemType);
		ArgumentNullException.ThrowIfNull(start);
		McpStateLedger ledger = _resources.Ledger ?? throw new InvalidOperationException(
			"Lua jobs need the activation's state ledger.");
		JobStart reserved = Reserve(kind, timeToLive, bufferLimit);
		LuaJob<TItem> job = new(reserved, ledger, itemType);
		try
		{
			start(reserved);
		}
		catch
		{
			Unreserve();
			throw;
		}

		Register(job);
		return job;
	}

	/// <summary>
	///     Starts a managed job on the thread pool. The slot is reserved atomically and the work starts after the job is
	///     registered; it never runs on Cheat Engine's main thread and is never waited for there. Each dispatch the work
	///     makes must stay within the dispatch budget.
	/// </summary>
	/// <typeparam name="TItem">The item type.</typeparam>
	/// <param name="kind">The job kind, a lowercase identifier of at most 32 characters, such as <c>pointermap</c>.</param>
	/// <param name="timeToLive">The lifetime, from <see cref="ResolveTimeToLive" />.</param>
	/// <param name="bufferLimit">The retained items, from <see cref="ResolveBufferLimit" />.</param>
	/// <param name="work">The work; it reports through the writer and observes the token.</param>
	/// <param name="beforeRun">
	///     Runs after the job is registered and before its work is queued. It can publish the job to a dependent state
	///     holder; when it throws, the unstarted job is removed again.
	/// </param>
	/// <returns>The registered, running job.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>busy</c> with <c>not_started</c> when <see cref="MaxJobs" /> jobs are
	///     retained.
	/// </exception>
	public ManagedJob<TItem> StartManaged<TItem>(string kind, TimeSpan timeToLive, int bufferLimit,
		Func<JobWriter<TItem>, CancellationToken, Task> work, Action<ManagedJob<TItem>>? beforeRun = null)
	{
		ArgumentNullException.ThrowIfNull(work);
		JobStart reserved = Reserve(kind, timeToLive, bufferLimit);
		ManagedJob<TItem> job = new(reserved, _time);
		Register(job);
		try
		{
			beforeRun?.Invoke(job);
		}
		catch
		{
			// The job has an identity only internally: no caller saw it because its work never started.
			Remove(job, tombstone: false);
			throw;
		}

		job.Run(work, OnManagedJobEnded, _dispatch.Client.Stopping);
		return job;
	}

	/// <summary>Finds a retained job of <paramref name="kind" />, with no Cheat Engine call.</summary>
	/// <typeparam name="TJob">The expected job type, such as <see cref="LuaJob{TItem}" />.</typeparam>
	/// <param name="jobId">The id a start tool returned.</param>
	/// <param name="kind">The expected kind.</param>
	/// <param name="parameter">The parameter name reported in an error.</param>
	/// <returns>The job.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_argument</c> or <c>not_found</c>, with <c>not_started</c>.</exception>
	public TJob Get<TJob>(string jobId, string kind, string parameter = "jobId") where TJob : McpJob
	{
		ArgumentNullException.ThrowIfNull(kind);
		McpJob job = Find(jobId, kind, parameter, out bool stopped) ?? throw UnknownJob(jobId, stopped);
		return job as TJob ?? throw new InvalidOperationException(
			$"The job {jobId} does not have the expected job type.");
	}

	/// <summary>Finds a retained job of any kind, with no Cheat Engine call.</summary>
	/// <param name="jobId">The id a start tool returned.</param>
	/// <param name="parameter">The parameter name reported in an error.</param>
	/// <returns>The job.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_argument</c> or <c>not_found</c>, with <c>not_started</c>.</exception>
	public McpJob Get(string jobId, string parameter = "jobId")
	{
		return Find(jobId, null, parameter, out bool stopped) ?? throw UnknownJob(jobId, stopped);
	}

	/// <summary>
	///     Stops a job and discards its items; call it outside a dispatch. A Lua job stops in one dispatch; a managed job
	///     is asked to stop, and, off Cheat Engine's main thread, the call waits up to one second for its work to end.
	///     Stopping a job that already ended, or was already stopped within its TTL, succeeds with
	///     <see cref="JobStopResult.AlreadyReleased" />.
	/// </summary>
	/// <param name="jobId">The job id.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <param name="parameter">The parameter name reported in an error.</param>
	/// <returns>What the stop did.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>invalid_argument</c> or <c>not_found</c> with <c>not_started</c>; <c>partial_effect</c> when the stop did not
	///     complete: retryable while a managed job's work is still ending, manual recovery when a Lua cleanup failed.
	/// </exception>
	public JobStopResult Stop(string jobId, CancellationToken cancellationToken, string parameter = "jobId")
	{
		McpJob? job = Find(jobId, null, parameter, out bool stopped);
		if (job is null)
		{
			return stopped ? new JobStopResult(jobId, true, true) : throw UnknownJob(jobId, false);
		}

		ResourceReleaseOutcome outcome = job is ILuaStateBacked
			? _dispatch.Run("job_stop", token => job.Release(token), cancellationToken)
			: job.Release(cancellationToken);
		// A job that had already ended, or whose Lua state was already gone, needed nothing more.
		bool alreadyReleased = outcome.Kind is ResourceReleaseKind.AlreadyReleased
			or ResourceReleaseKind.ExternallyRemoved;
		if (outcome.Kind is ResourceReleaseKind.StopPending && !_dispatch.Client.Dispatcher.IsMainThread &&
			job.Completion.Wait(StopGracePeriod))
		{
			outcome = job.Release(cancellationToken);
			if (outcome.Kind is ResourceReleaseKind.AlreadyReleased)
			{
				// The work ended within the grace period and its end already discarded the job.
				outcome = ResourceReleaseOutcome.Released();
			}
		}

		if (!outcome.IsRetryable)
		{
			Remove(job, outcome.IsComplete);
		}

		if (!outcome.IsComplete)
		{
			ReleasedResource details = new(job.Descriptor, outcome);
			throw CheatEngineToolException.PartialEffect(
				outcome.IsRetryable
					? $"The job {jobId} was asked to stop, but its work has not ended yet."
					: $"The job {jobId} stopped, but its cleanup failed: {job.CleanupError}",
				outcome.HostEffect, details, StateJsonContext.Default.ReleasedResource, outcome.IsRetryable,
				outcome.IsRetryable
					? "Repeat runtime_stop_job shortly."
					: "Recover it manually, then acknowledge it with runtime_release_resources.");
		}

		return new JobStopResult(jobId, true, alreadyReleased);
	}

	/// <summary>
	///     Lists the retained jobs, oldest first; call it outside a dispatch. It makes one dispatch when Lua jobs exist
	///     and none otherwise.
	/// </summary>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <returns>The statuses.</returns>
	public IReadOnlyList<JobStatus> List(CancellationToken cancellationToken)
	{
		Purge();
		McpJob[] jobs;
		lock (_lock)
		{
			jobs =
			[
				.. _jobs.Values.OrderBy(static job => job.CreatedUtc)
					.ThenBy(static job => job.Id, StringComparer.Ordinal)
			];
		}

		Dictionary<string, JobStatus>? observed = null;
		if (_resources.Ledger is { } ledger && Array.Exists(jobs, static job => job is ILuaStateBacked))
		{
			LuaJobStatuses statuses = _dispatch.Run("job_list", ledger.JobStatuses, cancellationToken);
			observed = statuses.Jobs.ToDictionary(static status => status.JobId, StringComparer.Ordinal);
		}

		DateTimeOffset now = _time.GetUtcNow();
		return
		[
			.. jobs.Select(job => job.Describe(
				job is ILuaStateBacked ? observed?.GetValueOrDefault(job.Id) : null, now))
		];
	}

	/// <summary>The <c>not_found</c> error of a job id.</summary>
	/// <param name="jobId">The id.</param>
	/// <param name="stopped">Whether the job was stopped within its TTL.</param>
	/// <returns>The exception to throw.</returns>
	internal static CheatEngineToolException UnknownJob(string jobId, bool stopped)
	{
		return CheatEngineToolException.NotFound(
			stopped
				? $"The job {jobId} was stopped; its results were discarded."
				: $"Unknown or expired job: {jobId}.",
			"Jobs are removed when stopped or when their TTL ends; start a new job, and list jobs with runtime_list_jobs.");
	}

	private McpJob? Find(string jobId, string? kind, string parameter, out bool stopped)
	{
		stopped = false;
		if (!McpStateIds.TryParse(jobId, out string parsedKind, out string parsedNamespace))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"must be a job id, kind-namespace-number, as a start tool returned it.");
		}

		if (kind is not null && !string.Equals(parsedKind, kind, StringComparison.Ordinal))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"{jobId} is a {parsedKind} job, not a {kind} job.");
		}

		if (!string.Equals(parsedNamespace, Namespace, StringComparison.Ordinal))
		{
			throw CheatEngineToolException.NotFound($"The job {jobId} belongs to an earlier plugin activation.",
				"Its state is listed as orphaned by runtime_list_resources; release it with runtime_release_resources.");
		}

		Purge();
		lock (_lock)
		{
			if (_jobs.TryGetValue(jobId, out McpJob? job) && !job.IsEnded &&
				_time.GetUtcNow() < job.ExpiresUtc)
			{
				return job;
			}

			stopped = _stopped.ContainsKey(jobId);
			return null;
		}
	}

	private JobStart Reserve(string kind, TimeSpan timeToLive, int bufferLimit)
	{
		McpStateIds.CheckKind(kind);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeToLive, TimeSpan.Zero);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(timeToLive,
			TimeSpan.FromSeconds(Math.Min(_options.JobMaxTtlSeconds, MaximumTimeToLiveSeconds)));
		ArgumentOutOfRangeException.ThrowIfLessThan(bufferLimit, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(bufferLimit, LuaJobKernelScripts.MaximumBufferItems);
		Purge();
		lock (_lock)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_jobs.Count + _reserved >= _options.MaxJobs)
			{
				throw CheatEngineToolException.Busy(
					string.Create(CultureInfo.InvariantCulture,
						$"{_options.MaxJobs} jobs are already retained; the {kind} job was not started."), StopHint);
			}

			_reserved++;
		}

		return new JobStart(_resources.NextId(kind), Namespace, kind, timeToLive, bufferLimit, _time.GetUtcNow());
	}

	private void Unreserve()
	{
		lock (_lock)
		{
			_reserved--;
		}
	}

	private void Register(McpJob job)
	{
		lock (_lock)
		{
			_reserved--;
			_jobs.Add(job.Id, job);
		}

		// Released by TargetResources: a completed stop leaves a tombstone, a failed cleanup stays with its Lua entry.
		_resources.Track(job, () => Remove(job, job.CleanupError is null));
	}

	/// <summary>Drops a job and its items; a completed stop leaves a tombstone until the job's TTL would have ended.</summary>
	private void Remove(McpJob job, bool tombstone)
	{
		job.Discard();
		lock (_lock)
		{
			if (_jobs.TryGetValue(job.Id, out McpJob? retained) && ReferenceEquals(retained, job))
			{
				_jobs.Remove(job.Id);
			}

			if (tombstone && _stopped.Count < MaximumTombstones)
			{
				_stopped[job.Id] = job.ExpiresUtc;
			}
		}

		_resources.Forget(job);
	}

	private void OnManagedJobEnded(McpJob job)
	{
		// A stopped or expired job's items are discarded; a completed or failed one stays pollable until its TTL.
		if (job.State is JobState.Stopped or JobState.Expired or JobState.Cancelled)
		{
			Remove(job, job.State is JobState.Stopped);
		}
	}

	private void Purge()
	{
		DateTimeOffset now = _time.GetUtcNow();
		List<McpJob> removed = [];
		lock (_lock)
		{
			foreach (KeyValuePair<string, DateTimeOffset> tombstone in _stopped.Where(pair => now >= pair.Value)
						 .ToArray())
			{
				_stopped.Remove(tombstone.Key);
			}

			foreach (McpJob job in _jobs.Values)
			{
				if (job.IsEnded || now >= job.ExpiresUtc)
				{
					removed.Add(job);
				}
			}
		}

		foreach (McpJob job in removed)
		{
			if (!job.IsEnded)
			{
				// A Lua job's TTL is enforced by the kernel's timer; a managed job is asked to stop and is dropped
				// once its work ended, while it keeps blocking a target change.
				job.Expire();
			}

			if (job.IsEnded || !job.HoldsHostState)
			{
				Remove(job, false);
			}
		}
	}
}
