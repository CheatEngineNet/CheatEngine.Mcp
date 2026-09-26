using CheatEngine.Client;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Modules;
using CheatEngine.Mcp.Instances;

namespace CheatEngine.Mcp;

internal sealed class McpModule(McpOptions options, PluginLog log,
	IUnsafeLuaClient? unsafeLua = null, IAutoAssemblerClient? autoAssembler = null) : ICheatEngineClientModule, IDisposable
{
	private McpServer? _server;

	public void OnEnabled(ICheatEngineClient client)
	{
		try
		{
			InstancePublication publication = new(new InstanceRegistry(options.InstanceDirectory), options.InstanceName);
			McpServer server = new McpServer(client, options, log, unsafeLua, autoAssembler, publication);
			server.StartAsync().GetAwaiter().GetResult();
			_server = server;
		}
		catch (Exception exception)
		{
			throw new InvalidOperationException($"Could not start the MCP server at {options.BaseUrl}.", exception);
		}
	}

	public void OnDisabling(ICheatEngineClient client)
	{
		_server?.StopAccepting();
		// Hosting now drains its global lease stack in reverse creation order.
	}

	public void Dispose()
	{
		// Hosting disposes modules after draining leases. Never join HTTP workers on CE's main thread.
		McpServer? server = Interlocked.Exchange(ref _server, null);
		if (server is not null)
		{
			_ = server.StopAsync().ContinueWith(static task => _ = task.Exception,
				CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}
	}
}
