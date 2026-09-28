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
	private const int Created = 0;
	private const int Starting = 1;
	private const int Running = 2;
	private const int Stopping = 3;
	private const int Disposed = 4;

	private IMcpBackendHost? _server;
	private int _lifecycleState;

	public void OnEnabled(ICheatEngineClient client)
	{
		if (Interlocked.CompareExchange(ref _lifecycleState, Starting, Created) != Created)
		{
			throw new InvalidOperationException("The MCP server module can only be enabled once.");
		}

		status.Report(client, "Starting");
		IMcpBackendHost? server = null;
		try
		{
			server = backends.Create(targets, client.Stopping);
			server.StartAsync().GetAwaiter().GetResult();
			string endpoint = server.Endpoint
							  ?? throw new InvalidOperationException("The MCP server started without an endpoint.");
			if (Interlocked.CompareExchange(ref _server, server, null) is not null)
			{
				throw new InvalidOperationException("The MCP server module already owns a backend.");
			}

			server = null;
			if (Interlocked.CompareExchange(ref _lifecycleState, Running, Starting) != Starting)
			{
				IMcpBackendHost? stoppingServer = Interlocked.Exchange(ref _server, null);
				if (stoppingServer is not null)
				{
					StopWithoutWaiting(stoppingServer);
				}

				throw new InvalidOperationException("The MCP server stopped while it was starting.");
			}

			status.Report(client, "Enabled", endpoint);
		}
		catch (Exception exception)
		{
			Interlocked.CompareExchange(ref _lifecycleState, Stopping, Starting);
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
		int state = Interlocked.Exchange(ref _lifecycleState, Stopping);
		IMcpBackendHost? server = Volatile.Read(ref _server);
		server?.StopAccepting();
		if (server is not null && state == Running)
		{
			status.Report(client, "Disabled");
		}
		// Hosting now drains its global lease stack in reverse creation order.
	}

	public void Dispose()
	{
		// Hosting disposes modules after draining leases. Never join HTTP workers on CE's main thread.
		Interlocked.Exchange(ref _lifecycleState, Disposed);
		IMcpBackendHost? server = Interlocked.Exchange(ref _server, null);
		if (server is not null)
		{
			StopWithoutWaiting(server);
		}
	}

	private static void StopWithoutWaiting(IMcpBackendHost server)
	{
		server.StopAccepting();
		_ = server.StopAsync().ContinueWith(static task => _ = task.Exception,
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
	}
}
