namespace CheatEngine.Mcp.Core.Jobs;

/// <summary>Reads the job states the Lua kernel reports.</summary>
internal static class JobStates
{
	/// <summary>Parses a kernel state.</summary>
	/// <param name="state">The <c>snake_case</c> state, such as <c>running</c> or <c>target_changed</c>.</param>
	/// <returns>The state; an unknown one reads as <see cref="JobState.Failed" />, which never claims the job still runs.</returns>
	internal static JobState Parse(string? state)
	{
		return state switch
		{
			"running" => JobState.Running,
			"stopping" => JobState.Stopping,
			"completed" => JobState.Completed,
			"failed" => JobState.Failed,
			"stopped" => JobState.Stopped,
			"cancelled" => JobState.Cancelled,
			"expired" => JobState.Expired,
			"target_changed" => JobState.TargetChanged,
			_ => JobState.Failed
		};
	}
}
