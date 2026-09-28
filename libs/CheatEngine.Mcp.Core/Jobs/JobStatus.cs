using System.ComponentModel;

namespace CheatEngine.Mcp.Core.Jobs;

/// <summary>The status of one job.</summary>
/// <param name="JobId">The job id, <c>kind-namespace-number</c>.</param>
/// <param name="Kind">The job kind.</param>
/// <param name="State">Where the job stands.</param>
/// <param name="AgeMs">Milliseconds since the job started.</param>
/// <param name="ExpiresInMs">Milliseconds until its TTL ends and its results are discarded.</param>
/// <param name="Buffered">How many items are retained for polling.</param>
/// <param name="Total">How many items the job produced; the last sequence number.</param>
/// <param name="Dropped">How many of the oldest items were evicted because the buffer was full.</param>
/// <param name="CreatedUtc">When the job started, when known.</param>
/// <param name="ProgressDone">How much work is done, in the job's own unit.</param>
/// <param name="ProgressTotal">How much work there is, when known.</param>
/// <param name="Error">Why the job failed or ended early.</param>
/// <param name="CleanupError">Why its cleanup failed.</param>
/// <param name="RequiresManualRecovery">Whether something may remain that only manual recovery can remove.</param>
/// <param name="ProcessId">The process selected when the job started, when known.</param>
public sealed record JobStatus(
	[property: Description("The job id.")] string JobId,
	[property: Description("The job kind.")]
	string Kind,
	[property: Description("Where the job stands.")]
	JobState State,
	[property: Description("Milliseconds since the job started.")]
	long AgeMs,
	[property: Description("Milliseconds until its TTL ends and its results are discarded.")]
	long ExpiresInMs,
	[property: Description("How many items are retained for polling.")]
	long Buffered,
	[property: Description("How many items the job produced; the last sequence number.")]
	long Total,
	[property: Description("How many of the oldest items were evicted because the buffer was full.")]
	long Dropped,
	[property: Description("When the job started, in UTC, when known.")]
	DateTimeOffset? CreatedUtc = null,
	[property: Description("How much work is done, in the job's own unit.")]
	long? ProgressDone = null,
	[property: Description("How much work there is, when known.")]
	long? ProgressTotal = null,
	[property: Description("Why the job failed or ended early.")]
	string? Error = null,
	[property: Description("Why its cleanup failed; it then needs manual recovery and an acknowledgement.")]
	string? CleanupError = null,
	[property: Description("Whether something may remain that only manual recovery can remove.")]
	bool RequiresManualRecovery = false,
	[property: Description("The process selected when the job started, when known.")]
	int? ProcessId = null);
