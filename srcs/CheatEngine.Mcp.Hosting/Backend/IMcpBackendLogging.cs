using Microsoft.Extensions.Logging;

namespace CheatEngine.Mcp.Hosting.Backend;

/// <summary>The plugin-owned log sink that a backend host shares without owning it.</summary>
public interface IMcpBackendLogging
{
	/// <summary>Keeps the shared sink alive until the returned lease is disposed, even after the activation ends.</summary>
	/// <returns>The lease.</returns>
	public IDisposable Acquire();

	/// <summary>Creates a provider over the shared sink; disposing it never shuts the sink down.</summary>
	/// <returns>A logger provider for the backend host.</returns>
	public ILoggerProvider CreateProvider();
}
