namespace CheatEngine.Mcp.Tests.Hosting;

/// <summary>Cleans up the temporary directory of a test that started the gateway executable.</summary>
internal static class GatewayTestDirectory
{
	private const int MaximumAttempts = 20;

	private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

	/// <summary>
	///     Deletes the directory tree, retrying for about five seconds while an exiting gateway still holds a registry
	///     file in it; a tree that stays locked is left in the temp folder rather than failing the test it served.
	/// </summary>
	/// <param name="path">The test's own temporary directory.</param>
	/// <returns>A task that completes once the tree is gone or the attempts are spent.</returns>
	internal static async Task DeleteAsync(string path)
	{
		for (int attempt = 1; Directory.Exists(path); attempt++)
		{
			try
			{
				Directory.Delete(path, true);
				return;
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException &&
											  attempt < MaximumAttempts)
			{
				// The cleanup must not observe the test's cancellation: it runs after the test body.
				await Task.Delay(RetryDelay, CancellationToken.None);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				// Still locked after every attempt: the tree stays in the temp folder.
				return;
			}
		}
	}
}
