using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Lua;
using CheatEngine.Mcp.Core.Targets;

namespace CheatEngine.Mcp.Core.Jobs;

/// <summary>
///     A job whose state lives in Cheat Engine's Lua under the activation namespace: a fixed start script creates it with
///     the kernel's <c>jobCreate</c> or <c>jobStart</c> and feeds it from breakpoint callbacks (event), a main-thread
///     timer
///     (slices) or a worker thread (thread). Its TTL is enforced by the kernel's own timer, even after a plugin disable.
/// </summary>
/// <typeparam name="TItem">The item type, deserialized from each item the kernel buffered.</typeparam>
public sealed class LuaJob<TItem> : McpJob, ILuaStateBacked
{
	private readonly JsonTypeInfo<TItem> _itemType;
	private readonly McpStateLedger _ledger;
	private string? _cleanupError;
	private int _state = (int) JobState.Running;

	internal LuaJob(JobStart start, McpStateLedger ledger, JsonTypeInfo<TItem> itemType)
		: base(start)
	{
		ArgumentNullException.ThrowIfNull(ledger);
		ArgumentNullException.ThrowIfNull(itemType);
		_ledger = ledger;
		_itemType = itemType;
	}

	/// <inheritdoc />
	public override JobState State => (JobState) Volatile.Read(ref _state);

	/// <inheritdoc />
	public override string? CleanupError => Volatile.Read(ref _cleanupError);

	/// <inheritdoc />
	string ILuaStateBacked.StateId => Id;

	/// <inheritdoc />
	void ILuaStateBacked.Observe(LuaStateEntry? entry)
	{
		if (entry is null)
		{
			Observe(JobState.Expired, null);
			Discard();
			return;
		}

		Observe(JobStates.Parse(entry.State), entry.CleanupError);
	}

	/// <summary>
	///     Reads the items after <paramref name="afterSequence" /> in one dispatch. The poll is read-only and idempotent;
	///     call it outside a dispatch.
	/// </summary>
	/// <param name="afterSequence">0 first, then the previous page's <see cref="JobPoll{TItem}.NextAfterSequence" />.</param>
	/// <param name="limit">1 to <see cref="JobRegistry.MaximumPollItems" /> items.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <param name="operation">The operation name used in statistics, logs and errors, such as the tool name.</param>
	/// <returns>The page.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>invalid_argument</c> for a bad cursor or limit, <c>not_found</c> once the job was stopped or expired.
	/// </exception>
	public JobPoll<TItem> Poll(long afterSequence, int limit, CancellationToken cancellationToken,
		string operation = "job_poll")
	{
		JobRegistry.ValidatePoll(afterSequence, limit);
		ThrowIfDiscarded();
		LuaJobPage page;
		try
		{
			page = _ledger.Dispatch.RunLua(operation, LuaJobKernelScripts.Poll, StateJsonContext.Default.LuaJobPage,
				cancellationToken, _ledger.Namespace, Id, afterSequence, limit);
		}
		catch (CheatEngineToolException exception) when (exception.Error.Kind is ToolErrorKind.NotFound)
		{
			// The kernel's TTL timer removed it; the managed handle follows.
			Observe(JobState.Expired, null);
			Discard();
			throw;
		}

		Observe(page.Job.State, page.Job.CleanupError);
		TItem[] items = new TItem[page.Items.Length];
		for (int index = 0; index < items.Length; index++)
		{
			items[index] = ReadItem(page.Items[index], operation);
		}

		return new JobPoll<TItem>(page.Job with { CreatedUtc = CreatedUtc }, items, page.FirstSequence,
			page.NextAfterSequence, page.More, page.Dropped);
	}

	/// <summary>Reads the job's status in one dispatch; call it outside a dispatch.</summary>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <param name="operation">The operation name used in statistics, logs and errors.</param>
	/// <returns>The status.</returns>
	/// <exception cref="CheatEngineToolException"><c>not_found</c> once the job was stopped or expired.</exception>
	public JobStatus GetStatus(CancellationToken cancellationToken, string operation = "job_status")
	{
		ThrowIfDiscarded();
		LuaJobStatuses statuses = _ledger.Dispatch.RunLua(operation, LuaJobKernelScripts.Statuses,
			StateJsonContext.Default.LuaJobStatuses, cancellationToken, _ledger.Namespace);
		JobStatus? observed = Array.Find(statuses.Jobs,
			status => string.Equals(status.JobId, Id, StringComparison.Ordinal));
		if (observed is null)
		{
			Observe(JobState.Expired, null);
			Discard();
			throw JobRegistry.UnknownJob(Id, false);
		}

		return Describe(observed, _ledger.Time.GetUtcNow());
	}

	/// <inheritdoc />
	/// <remarks>
	///     Stops the Lua job, which runs its strategy and cleanup hooks once, and removes it with its items. A cleanup that
	///     raised an error leaves the job in Lua for manual recovery and an acknowledgement.
	/// </remarks>
	public override ResourceReleaseOutcome Release(CancellationToken cancellationToken)
	{
		if (IsEnded)
		{
			return CleanupError is null
				? ResourceReleaseOutcome.AlreadyReleased()
				: ResourceReleaseOutcome.CleanupFailed();
		}

		LuaJobStop stop = _ledger.StopJob(Id, cancellationToken);
		if (!stop.Found)
		{
			Observe(JobState.Expired, null);
			Discard();
			return ResourceReleaseOutcome.AlreadyReleased();
		}

		Observe(stop.State is null ? JobState.Stopped : JobStates.Parse(stop.State), stop.CleanupError);
		Discard();
		if (stop.Released)
		{
			return stop.WasRunning ? ResourceReleaseOutcome.Released() : ResourceReleaseOutcome.AlreadyReleased();
		}

		return ResourceReleaseOutcome.CleanupFailed();
	}

	/// <inheritdoc />
	internal override JobStatus Describe(JobStatus? observed, DateTimeOffset now)
	{
		if (observed is null)
		{
			// The kernel no longer has the job: its TTL timer removed it, so the handle follows.
			if (!IsEnded)
			{
				Observe(JobState.Expired, null);
				Discard();
			}

			return Status(State, now, 0, 0, 0);
		}

		Observe(observed.State, observed.CleanupError);
		return observed with { CreatedUtc = CreatedUtc };
	}

	/// <inheritdoc />
	internal override void Expire()
	{
		if (State is JobState.Running)
		{
			Observe(JobState.Expired, null);
		}

		Discard();
	}

	private void Observe(JobState state, string? cleanupError)
	{
		Volatile.Write(ref _state, (int) state);
		if (cleanupError is not null)
		{
			Volatile.Write(ref _cleanupError, cleanupError);
		}
	}

	private void ThrowIfDiscarded()
	{
		if (IsEnded)
		{
			throw JobRegistry.UnknownJob(Id, State is JobState.Stopped);
		}
	}

	private TItem ReadItem(JsonElement item, string operation)
	{
		try
		{
			return item.Deserialize(_itemType) ?? throw new JsonException("A job item was null.");
		}
		catch (JsonException exception)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.Internal,
				LuaToolRuntime.ContractViolationMessage, operation, ToolHostEffect.Completed, false,
				CheatEngineToolException.InternalHint), exception);
		}
	}
}
