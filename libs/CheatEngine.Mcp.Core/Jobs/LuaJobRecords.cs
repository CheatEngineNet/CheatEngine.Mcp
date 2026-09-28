using System.Text.Json;

namespace CheatEngine.Mcp.Core.Jobs;

/// <summary>One page of a Lua job as the kernel's <c>jobPoll</c> returns it; items stay raw JSON until the job types them.</summary>
/// <param name="Job">The job's status.</param>
/// <param name="Items">The items after the cursor.</param>
/// <param name="FirstSequence">The oldest retained sequence.</param>
/// <param name="NextAfterSequence">The cursor for the next poll.</param>
/// <param name="More">Whether retained items remain.</param>
/// <param name="Dropped">How many items were evicted.</param>
internal sealed record LuaJobPage(
	JobStatus Job,
	JsonElement[] Items,
	long FirstSequence,
	long NextAfterSequence,
	bool More,
	long Dropped);

/// <summary>The statuses of every Lua job of one namespace.</summary>
/// <param name="Jobs">The statuses.</param>
internal sealed record LuaJobStatuses(JobStatus[] Jobs);

/// <summary>What the kernel's stop script did.</summary>
/// <param name="Found">Whether the job still existed in Lua.</param>
/// <param name="WasRunning">Whether it was running before the stop.</param>
/// <param name="Released">Whether its cleanup completed and it was removed with its results.</param>
/// <param name="State">Its state after the stop.</param>
/// <param name="CleanupError">Why its cleanup failed; it is then retained for manual recovery.</param>
internal sealed record LuaJobStop(
	bool Found,
	bool WasRunning,
	bool Released,
	string? State = null,
	string? CleanupError = null);

/// <summary>What one sweep of the Lua state root did.</summary>
/// <param name="Expired">Running jobs ended because their TTL ended.</param>
/// <param name="Removed">Jobs removed with their results.</param>
/// <param name="OrphansRemoved">Removed jobs of earlier activations.</param>
/// <param name="NamespacesRemoved">Emptied namespaces of earlier activations removed.</param>
/// <param name="Retained">Jobs still retained.</param>
/// <param name="OrphansRetained">Retained jobs of earlier activations.</param>
/// <param name="AwaitingRecovery">Retained jobs whose cleanup failed.</param>
/// <param name="ResourcesReleased">Recorded resources released because their TTL ended.</param>
internal sealed record LuaStateSweep(
	int Expired,
	int Removed,
	int OrphansRemoved,
	int NamespacesRemoved,
	int Retained,
	int OrphansRetained,
	int AwaitingRecovery,
	int ResourcesReleased);
