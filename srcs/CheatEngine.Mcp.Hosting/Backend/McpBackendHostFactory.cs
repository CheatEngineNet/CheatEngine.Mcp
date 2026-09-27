using CheatEngine.Mcp.Hosting.Configuration;
using CheatEngine.Mcp.Hosting.Discovery;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Hosting.Backend;

/// <summary>Creates backends from the activation's options, log sink and composed primitives.</summary>
internal sealed class McpBackendHostFactory(
	IOptions<McpBackendOptions> backend,
	IOptions<McpDiscoveryOptions> discovery,
	IMcpBackendLogging logging,
	McpRuntimeInfo runtime,
	IOptions<CheatEngineMcpPrimitiveOptions> manifest) : IMcpBackendHostFactory
{
	// Resolved on construction: invalid settings fail the activation before any backend starts.
	private readonly McpBackendOptions _backend = backend.Value;
	private readonly McpDiscoveryOptions _discovery = discovery.Value;

	public string BaseUrl => _backend.BaseUrl;

	public IMcpBackendHost Create(McpPrimitiveTargets targets, CancellationToken stopping)
	{
		ArgumentNullException.ThrowIfNull(targets);
		InstancePublication publication = new(new InstanceRegistry(_discovery.InstanceDirectory),
			_discovery.InstanceName, runtime.Version);
		return new McpBackendHost(_backend, logging, runtime, manifest.Value, targets, stopping, publication);
	}
}
