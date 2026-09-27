using CheatEngine.Mcp.Core.Targets;

namespace CheatEngine.Mcp.Core.Jobs;

/// <summary>
///     A job of the activation: work that runs across calls, buffers items for non-consuming polls and is tracked as a
///     target resource until it is stopped or its TTL ends. Create one through <see cref="JobRegistry" />.
/// </summary>
/// <remarks>
///     A finished job keeps its items pollable until its TTL; a stop or the TTL discards them, after which its id is
///     unknown (<c>not_found</c>). A job that is running, stopping, or whose cleanup failed holds host state and blocks
///     a target change.
/// </remarks>
public abstract class McpJob : ITargetResource
{
	private int _discarded;

	private protected McpJob(JobStart start)
	{
		ArgumentNullException.ThrowIfNull(start);
		Start = start;
	}

	/// <summary>The job's identity and limits.</summary>
	public JobStart Start
	{
		get;
	}

	/// <summary>The job id, <c>kind-namespace-number</c>.</summary>
	public string Id => Start.Id;

	/// <summary>The job kind.</summary>
	public string Kind => Start.Kind;

	/// <summary>When the job started.</summary>
	public DateTimeOffset CreatedUtc => Start.CreatedUtc;

	/// <summary>When the job's TTL ends.</summary>
	public DateTimeOffset ExpiresUtc => Start.ExpiresUtc;

	/// <summary>The job's last known state.</summary>
	public abstract JobState State
	{
		get;
	}

	/// <summary>Why the job's cleanup failed, when it did.</summary>
	public abstract string? CleanupError
	{
		get;
	}

	/// <summary>Whether the job ran to its end and a later release does nothing more.</summary>
	internal virtual bool HasEnded => State is not (JobState.Running or JobState.Stopping);

	/// <summary>The job's work, for a managed job; completed at once for a Lua job. Never wait for it on CE's thread.</summary>
	internal virtual Task Completion => Task.CompletedTask;

	/// <inheritdoc />
	public TargetResourceDescriptor Descriptor => new(Id, Kind, TargetResourceCategory.Job,
		CleanupError is not null
			? TargetResourceState.CleanupFailed
			: State is JobState.Stopping
				? TargetResourceState.StopPending
				: HoldsHostState
					? TargetResourceState.Active
					: TargetResourceState.Ended,
		CreatedUtc, CleanupError: CleanupError, RequiresManualRecovery: CleanupError is not null);

	/// <inheritdoc />
	/// <remarks>A job has ended once it was stopped, expired or discarded; its id is then unknown.</remarks>
	public bool IsEnded => Volatile.Read(ref _discarded) != 0;

	/// <inheritdoc />
	public bool HoldsHostState => CleanupError is not null || !HasEnded;

	/// <summary>
	///     Stops the job and discards its items. Call it inside a dispatch; it never throws for a failed cleanup, and a
	///     managed job whose work has not ended yet reports <see cref="ResourceReleaseKind.StopPending" />.
	/// </summary>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>What the stop did.</returns>
	public abstract ResourceReleaseOutcome Release(CancellationToken cancellationToken);

	/// <summary>The job's status; for a Lua job, from the status the kernel reported, if any.</summary>
	/// <param name="observed">The kernel's status of a Lua job, or <see langword="null" /> when it no longer exists.</param>
	/// <param name="now">The current time.</param>
	/// <returns>The status.</returns>
	internal abstract JobStatus Describe(JobStatus? observed, DateTimeOffset now);

	/// <summary>Ends the job because its TTL ended; a Lua job's TTL is enforced by the kernel's own timer.</summary>
	internal abstract void Expire();

	/// <summary>Asks the job's work to stop without waiting for it, as the activation ends.</summary>
	internal virtual void CancelWithoutJoin()
	{
	}

	/// <summary>Marks the job discarded; its items are dropped.</summary>
	/// <returns><see langword="true" /> the first time.</returns>
	internal bool Discard()
	{
		if (Interlocked.Exchange(ref _discarded, 1) != 0)
		{
			return false;
		}

		OnDiscarded();
		return true;
	}

	/// <summary>Drops the job's retained items.</summary>
	private protected virtual void OnDiscarded()
	{
	}

	/// <summary>A status with the job's managed identity and clock.</summary>
	/// <param name="state">The state.</param>
	/// <param name="now">The current time.</param>
	/// <param name="buffered">The retained items.</param>
	/// <param name="total">The produced items.</param>
	/// <param name="dropped">The evicted items.</param>
	/// <returns>The status.</returns>
	private protected JobStatus Status(JobState state, DateTimeOffset now, long buffered, long total, long dropped)
	{
		long age = Math.Max(0, (long) (now - CreatedUtc).TotalMilliseconds);
		long expiresIn = Math.Max(0, (long) (ExpiresUtc - now).TotalMilliseconds);
		return new JobStatus(Id, Kind, state, age, expiresIn, buffered, total, dropped, CreatedUtc,
			CleanupError: CleanupError, RequiresManualRecovery: CleanupError is not null);
	}
}
