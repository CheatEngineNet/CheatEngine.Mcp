using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Jobs;

/// <summary>Where a job stands; the wire value is the <c>snake_case</c> member name.</summary>
[JsonConverter(typeof(ContractEnumConverter<JobState>))]
public enum JobState
{
	/// <summary>The job is working and may add items.</summary>
	Running,

	/// <summary>A stop was requested and the job's work has not ended yet.</summary>
	Stopping,

	/// <summary>The job finished its work; its items stay pollable until its TTL ends.</summary>
	Completed,

	/// <summary>The job failed; <see cref="JobStatus.Error" /> says why, and its items stay pollable until its TTL ends.</summary>
	Failed,

	/// <summary>A caller stopped the job.</summary>
	Stopped,

	/// <summary>The plugin activation ended the job.</summary>
	Cancelled,

	/// <summary>The job's TTL ended.</summary>
	Expired,

	/// <summary>The selected process changed, so the job ended without finishing.</summary>
	TargetChanged
}
