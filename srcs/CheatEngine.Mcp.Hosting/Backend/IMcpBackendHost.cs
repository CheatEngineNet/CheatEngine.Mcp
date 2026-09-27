namespace CheatEngine.Mcp.Hosting.Backend;

/// <summary>One activation's MCP backend, driven by the plugin's lifecycle module.</summary>
public interface IMcpBackendHost
{
	/// <summary>The bound endpoint, once started.</summary>
	public string? Endpoint
	{
		get;
	}

	/// <summary>Starts the listener, then publishes discovery; a failed start releases everything it acquired.</summary>
	/// <returns>The start operation.</returns>
	public Task StartAsync();

	/// <summary>Rejects new requests with 503 and withdraws discovery; I/O only, safe on Cheat Engine's main thread.</summary>
	public void StopAccepting();

	/// <summary>Stops the listener within a bounded time and disposes the host; repeated calls share one task.</summary>
	/// <returns>The shutdown operation.</returns>
	public Task StopAsync();
}
