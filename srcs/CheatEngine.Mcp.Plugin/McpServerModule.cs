using CheatEngine.Client;
using CheatEngine.Client.Modules;
using CheatEngine.Mcp.Core.Targets;
using CheatEngine.Mcp.Hosting.Backend;
using CheatEngine.Mcp.Plugin.Lua;

namespace CheatEngine.Mcp.Plugin;

/// <summary>Adapts one Cheat Engine activation's lifecycle to its MCP backend.</summary>
/// <remarks>
///     Client Hosting constructs this scoped module while it builds the activation, so every primitive instance in
///     <paramref name="targets" /> is constructed before <see cref="OnEnabled" />; a constructor failure fails the enable.
/// </remarks>
internal sealed class McpServerModule(
	IMcpBackendHostFactory backends,
	McpPrimitiveTargets targets,
	McpStatusIndicator status,
	TargetResources resources,
	PluginLifecycleLuaScope lifecycleLuaScope)
	: ICheatEngineClientModule, ICheatEngineClientModuleDeactivationNotification,
	  ICheatEngineClientResourceCleanup, IDisposable
{
	private const int Created = 0;
	private const int Starting = 1;
	private const int Running = 2;
	private const int Stopping = 3;
	private const int Disposed = 4;

	private IMcpBackendHost? _server;
	private int _lifecycleState;
	private int _reportDisabledStatus;

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
		resources.StopAcceptingTracks();
		IMcpBackendHost? server = Volatile.Read(ref _server);
		server?.StopAccepting();
		if (server is not null && state == Running)
		{
			Volatile.Write(ref _reportDisabledStatus, 1);
		}
	}

	public void OnDeactivated(ICheatEngineClient client)
	{
		if (Interlocked.Exchange(ref _reportDisabledStatus, 0) == 0)
		{
			return;
		}

		// The status menu belongs to the CE session, not this activation. Update its fixed text after all module
		// shutdown callbacks and before releasing activation resources.
		using (lifecycleLuaScope.BeginDeactivationNotification(client))
		{
			client.Dispatcher.Invoke(() => status.Report(client, "Disabled", lifecycleLuaScope: lifecycleLuaScope));
		}
	}

	public void OnReleasingResources(ICheatEngineClient client)
	{
		if (resources.Count == 0 && resources.Ledger is null)
		{
			return;
		}

		// This stage runs after module shutdown and before Client leases drain. Keep normal Client dispatcher admission,
		// while permitting only MCP-owned fixed cleanup scripts through the attached SDK runtime.
		using (lifecycleLuaScope.BeginResourceCleanup(client))
		{
			client.Dispatcher.Invoke(() => resources.ReleaseAll(CancellationToken.None).ThrowIfIncomplete());
		}
	}

	public void Dispose()
	{
		// Hosting releases module-owned resources before Client leases, then disposes modules. Never join HTTP workers on CE's main thread.
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
