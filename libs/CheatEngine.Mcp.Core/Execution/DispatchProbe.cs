namespace CheatEngine.Mcp.Core.Execution;

/// <summary>
///     Records whether one dispatched body started on Cheat Engine's main thread and, with a clock, when it started and
///     ended. Only an exception raised before <see cref="Entered" /> may claim that nothing was run.
/// </summary>
/// <param name="time">The clock, or <see langword="null" /> when only admission is tracked.</param>
internal sealed class DispatchProbe(TimeProvider? time)
{
	private int _entered;

	/// <summary>Whether the body started.</summary>
	internal bool Entered => Volatile.Read(ref _entered) != 0;

	/// <summary>The timestamp at which the body started.</summary>
	internal long Started
	{
		get;
		private set;
	}

	/// <summary>The timestamp at which the body ended.</summary>
	internal long Finished
	{
		get;
		private set;
	}

	/// <summary>Marks the first line of the body.</summary>
	internal void Enter()
	{
		Started = time?.GetTimestamp() ?? 0;
		Volatile.Write(ref _entered, 1);
	}

	/// <summary>Marks the end of the body, including a failed one.</summary>
	internal void Exit()
	{
		Finished = time?.GetTimestamp() ?? 0;
	}
}
