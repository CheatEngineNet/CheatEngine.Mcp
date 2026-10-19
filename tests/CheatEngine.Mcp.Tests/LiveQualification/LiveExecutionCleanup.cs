namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Runs independent target-cleanup attempts without hiding a later host after an earlier failure.</summary>
internal static class LiveExecutionCleanup
{
	internal static async Task ConfirmTargetExitAsync(Action writeStopMarker, Func<Task> waitForExit,
		Action forceTerminate, Func<bool> hasExited, List<Exception> markerFailures)
	{
		try
		{
			writeStopMarker();
		}
		catch (Exception exception)
		{
			markerFailures.Add(exception);
		}

		try
		{
			await waitForExit();
		}
		catch (TimeoutException)
		{
			forceTerminate();
			await waitForExit();
		}

		if (!hasExited())
		{
			throw new InvalidOperationException("The owned target remains live after termination.");
		}
	}

	internal static async Task AttemptAllAsync(IEnumerable<string> names, Func<string, Task> terminate,
		List<Exception> failures)
	{
		foreach (string name in names)
		{
			try
			{
				await terminate(name);
			}
			catch (Exception exception)
			{
				failures.Add(exception);
			}
		}
	}
}
