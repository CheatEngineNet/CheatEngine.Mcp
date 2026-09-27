using CheatEngine.Client;
using CheatEngine.Client.Modules;
using CheatEngine.Mcp.Hosting.Backend;

namespace CheatEngine.Mcp.Plugin;

/// <summary>Adapts one Cheat Engine activation's lifecycle to its MCP backend.</summary>
/// <remarks>
///     Client Hosting constructs this scoped module while it builds the activation, so every primitive instance in
///     <paramref name="targets" /> is constructed before <see cref="OnEnabled" />; a constructor failure fails the enable.
/// </remarks>
internal sealed class McpServerModule(
	IMcpBackendHostFactory backends,
	McpPrimitiveTargets targets,
	McpStatusIndicator status)
	: ICheatEngineClientModule, IDisposable
{
	private IMcpBackendHost? _server;

	public void OnEnabled(ICheatEngineClient client)
	{
		status.Report(client, "Starting");
		IMcpBackendHost? server = null;
		try
		{
			server = backends.Create(targets, client.Stopping);
			server.StartAsync().GetAwaiter().GetResult();
			string endpoint = server.Endpoint
				?? throw new InvalidOperationException("The MCP server started without an endpoint.");
			_server = server;
			server = null;
			status.Report(client, "Enabled", endpoint);
		}
		catch (Exception exception)
		{
			if (server is not null)
			{
				try
				{
					server.StopAccepting();
					server.StopAsync().GetAwaiter().GetResult();
				}
				catch
				{
					// Preserve the startup failure, which is the lifecycle error the caller can act on.
				}
			}

			status.Report(client, "Start failed");
			throw new InvalidOperationException($"Could not start the MCP server at {backends.BaseUrl}.", exception);
		}
	}

	public void OnDisabling(ICheatEngineClient client)
	{
		_server?.StopAccepting();
		if (_server is not null)
		{
			status.Report(client, "Disabled");
		}
		// Hosting now drains its global lease stack in reverse creation order.
	}

	public void Dispose()
	{
		// Hosting disposes modules after draining leases. Never join HTTP workers on CE's main thread.
		IMcpBackendHost? server = Interlocked.Exchange(ref _server, null);
		if (server is not null)
		{
			_ = server.StopAsync().ContinueWith(static task => _ = task.Exception,
				CancellationToken.None,
				TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}
	}
}
