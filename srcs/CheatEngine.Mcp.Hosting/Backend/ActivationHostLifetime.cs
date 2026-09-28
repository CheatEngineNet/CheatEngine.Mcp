using Microsoft.Extensions.Hosting;

namespace CheatEngine.Mcp.Hosting.Backend;

/// <summary>
///     Replaces the console lifetime inside Cheat Engine: the backend never handles process signals or delays process
///     exit. Client Hosting starts and stops it with the activation.
/// </summary>
internal sealed class ActivationHostLifetime : IHostLifetime
{
	public Task WaitForStartAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
