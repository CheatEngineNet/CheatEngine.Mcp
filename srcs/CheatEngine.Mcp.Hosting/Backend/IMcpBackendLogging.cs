using Microsoft.Extensions.Logging;

namespace CheatEngine.Mcp.Hosting.Backend;

/// <summary>The plugin-owned log sink that a backend host shares without owning it.</summary>
public interface IMcpBackendLogging
{
	/// <summary>The level the plugin configured; the backend's web host filters at this level.</summary>
	public LogLevel MinimumLevel
	{
		get;
	}

	/// <summary>
	///     Creates a provider that keeps the shared sink open until the provider is disposed, even after the activation
	///     ends. Disposal never blocks and never closes the sink for other holders.
	/// </summary>
	/// <returns>A logger provider for the backend host, which disposes it once its web host stopped.</returns>
	public ILoggerProvider CreateProvider();
}
