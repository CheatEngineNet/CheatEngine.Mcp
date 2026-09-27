using CheatEngine.Client;
using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>
///     The pointer path search: pure managed work on a stored map, run by a managed job off Cheat Engine's main thread.
///     A stop keeps the paths found so far.
/// </summary>
internal static class PointerSearchJob
{
	/// <summary>The search job's work.</summary>
	/// <param name="client">The activation's Client, whose stopping token tells an activation end from a stop.</param>
	/// <param name="map">The searched map.</param>
	/// <param name="options">The target and limits.</param>
	/// <param name="scan">The scan's slot.</param>
	/// <param name="writer">The job's progress writer, in visited nodes.</param>
	/// <param name="expires">When the job's TTL ends.</param>
	/// <param name="time">The clock of the TTL.</param>
	/// <param name="cancellationToken">The job's token.</param>
	/// <returns>The work, already complete when it returns.</returns>
	internal static Task Run(ICheatEngineClient client, PointerMap map, PointerSearchOptions options,
		PointerScanSlot scan, JobWriter<int> writer, DateTimeOffset expires, TimeProvider time,
		CancellationToken cancellationToken)
	{
		PointerSearchResult result;
		try
		{
			result = map.Search(options, (nodes, count) =>
			{
				scan.Report(nodes, count);
				writer.Progress(nodes, options.MaximumNodes);
			}, cancellationToken);
		}
		catch (Exception exception)
		{
			(PointerJobState state, string message) = PointerJobs.Classify(exception, client,
				time.GetUtcNow() >= expires, cancellationToken);
			scan.Fail(state is PointerJobState.Stopped or PointerJobState.Expired ? PointerJobState.Failed : state,
				message);
			throw;
		}

		if (!result.Cancelled)
		{
			scan.End(PointerJobState.Ready, result, map.Incomplete, null);
			return Task.CompletedTask;
		}

		(PointerJobState ended, string reason) = PointerJobs.Classify(
			new OperationCanceledException(cancellationToken), client, time.GetUtcNow() >= expires, cancellationToken);
		if (ended is PointerJobState.Stopped or PointerJobState.Expired)
		{
			scan.End(ended, result, map.Incomplete, reason);
		}
		else
		{
			scan.Fail(ended, reason);
		}

		// The job ends as stopped, expired or cancelled, as its registry decides.
		cancellationToken.ThrowIfCancellationRequested();
		return Task.CompletedTask;
	}
}
