using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>What the pointer capture and search jobs share: their kinds, dispatch retries and end states.</summary>
internal static class PointerJobs
{
	/// <summary>The job kind of a map capture: ids read <c>pointermap-namespace-n</c>.</summary>
	internal const string MapKind = "pointermap";

	/// <summary>The job kind of a path search: ids read <c>pointerscan-namespace-n</c>.</summary>
	internal const string ScanKind = "pointerscan";

	/// <summary>The jobs report progress only; their results live in the pointer store.</summary>
	internal const int BufferLimit = 1;

	internal const string ActivationEnded = "The plugin activation ended.";

	/// <summary>How long a job waits before it retries a dispatch refused as busy.</summary>
	internal static readonly TimeSpan BusyRetryDelay = TimeSpan.FromMilliseconds(20);

	/// <summary>
	///     Refuses a new job before any Cheat Engine call when the registry is full; the registry's own atomic check
	///     still decides.
	/// </summary>
	/// <param name="jobs">The activation's jobs.</param>
	/// <param name="kind">The job kind.</param>
	/// <exception cref="CheatEngineToolException"><c>busy</c> with <c>not_started</c>.</exception>
	internal static void EnsureCapacity(JobRegistry jobs, string kind)
	{
		if (jobs.Count >= jobs.MaxJobs)
		{
			throw CheatEngineToolException.Busy(
				$"{jobs.MaxJobs} jobs are already retained; the {kind} job was not started.",
				"Stop finished jobs with runtime_stop_job, or wait for their TTL to end.");
		}
	}

	/// <summary>
	///     Runs one dispatch of a job, waiting and retrying while the activation's concurrent dispatches are all taken, so
	///     tool calls keep priority over background work.
	/// </summary>
	/// <typeparam name="T">The body's result.</typeparam>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="operation">The operation name.</param>
	/// <param name="body">The dispatch body.</param>
	/// <param name="cancellationToken">The job's token.</param>
	/// <returns>The body's result.</returns>
	internal static async Task<T> DispatchAsync<T>(ToolDispatch dispatch, string operation,
		Func<CancellationToken, T> body, CancellationToken cancellationToken)
	{
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				return dispatch.Run(operation, body, cancellationToken);
			}
			catch (CheatEngineToolException exception) when (exception.Error.Kind is ToolErrorKind.Busy &&
														 exception.Error.HostEffect is ToolHostEffect.NotStarted)
			{
				await Task.Delay(BusyRetryDelay, cancellationToken).ConfigureAwait(false);
			}
		}
	}

	/// <summary>
	///     Stops a map's or scan's job before the map or scan is deleted: a running job is stopped and waited for up to the
	///     registry's grace period; a job that already ended is only dropped from the registry.
	/// </summary>
	/// <param name="jobs">The activation's jobs.</param>
	/// <param name="job">The job, or <see langword="null" /> when none was started.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <returns><see langword="true" /> when a running job was stopped.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>partial_effect</c>, retryable, when the job's work has not ended yet; the caller keeps the map or scan.
	/// </exception>
	internal static bool StopIfKnown(JobRegistry jobs, McpJob? job, CancellationToken cancellationToken)
	{
		if (job is null)
		{
			return false;
		}

		try
		{
			return !jobs.Stop(job.Id, cancellationToken).AlreadyReleased;
		}
		catch (CheatEngineToolException exception) when (exception.Error.Kind is ToolErrorKind.NotFound)
		{
			// Its TTL ended: the registry already forgot it.
			return false;
		}
	}

	/// <summary>Classifies why a job's work ended with an exception.</summary>
	/// <param name="exception">The exception.</param>
	/// <param name="client">The activation's Client.</param>
	/// <param name="expired">Whether the job's TTL has ended.</param>
	/// <param name="cancellationToken">The job's token.</param>
	/// <returns>The end state and a caller-safe reason.</returns>
	internal static (PointerJobState State, string Message) Classify(Exception exception, ICheatEngineClient client,
		bool expired, CancellationToken cancellationToken)
	{
		if (client.Stopping.IsCancellationRequested || exception is ObjectDisposedException ||
			exception is CheatEngineClientException { Failure.Kind: CheatEngineFailureKind.ActivationExpired } ||
			exception is CheatEngineToolException { Error.Kind: ToolErrorKind.Stopping })
		{
			return (PointerJobState.Cancelled, ActivationEnded);
		}

		if (cancellationToken.IsCancellationRequested && exception is OperationCanceledException
				or CheatEngineToolException { Error.Kind: ToolErrorKind.Cancelled })
		{
			return expired
				? (PointerJobState.Expired, "The job's TTL ended before it finished; the results so far are kept.")
				: (PointerJobState.Stopped, "The job was stopped before it finished; the results so far are kept.");
		}

		if (exception is CheatEngineToolException { Error.Kind: ToolErrorKind.TargetChanged } changed)
		{
			return (PointerJobState.TargetChanged, changed.Error.Message);
		}

		return (PointerJobState.Failed, exception is CheatEngineToolException contract
			? contract.Error.Message
			: "The job failed unexpectedly.");
	}
}
