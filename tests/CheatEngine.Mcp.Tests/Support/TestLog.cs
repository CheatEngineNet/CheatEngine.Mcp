using CheatEngine.Mcp.Plugin;

namespace CheatEngine.Mcp.Tests.Support;

/// <summary>Releases a test's plugin log and waits until its background writer closed the file.</summary>
internal static class TestLog
{
	/// <summary>Disposes the log, then waits for the drain, so the log folder can be deleted.</summary>
	/// <param name="log">The log, whose providers were already disposed.</param>
	internal static async Task ReleaseAsync(PluginLog log)
	{
		log.Dispose();
		await log.Sink.Drained.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
	}
}
