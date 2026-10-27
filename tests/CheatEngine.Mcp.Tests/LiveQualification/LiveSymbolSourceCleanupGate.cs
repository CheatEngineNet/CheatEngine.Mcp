namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Serializes the source-only critical cleanup phase until containment has a verified receipt.</summary>
internal sealed class LiveSymbolSourceCleanupGate
{
	private bool _cleaned;

	internal async Task EnsureCleanedAsync(Func<Task> stopOwnedProcessesAsync, Action disposeContainment)
	{
		ArgumentNullException.ThrowIfNull(stopOwnedProcessesAsync);
		ArgumentNullException.ThrowIfNull(disposeContainment);
		if (_cleaned)
		{
			return;
		}

		await stopOwnedProcessesAsync();
		disposeContainment();
		_cleaned = true;
	}
}
