using System.ComponentModel;

namespace CheatEngine.Mcp.Core.Jobs;

/// <summary>What stopping a job did.</summary>
/// <param name="JobId">The job id.</param>
/// <param name="Released">Whether the job and its host state are released; a stop that did not complete is an error.</param>
/// <param name="AlreadyReleased">Whether the job had already ended or been stopped, so nothing more was done.</param>
public sealed record JobStopResult(
	[property: Description("The job id.")] string JobId,
	[property: Description("Whether the job and its host state are released.")]
	bool Released,
	[property: Description("Whether the job had already ended or been stopped, so nothing more was done.")]
	bool AlreadyReleased);
