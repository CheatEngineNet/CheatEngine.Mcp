using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     Serves the composition's Local primitives (knowledge documents, workflow bodies and prompts) from the gateway
///     itself: they are static, so they are bound live without any instance and never routed. It runs after the SDK's
///     own setup, so the collections and the error filters are complete when the server is created.
/// </summary>
internal sealed class GatewayLocalPrimitivesSetup(
	IOptions<CheatEngineMcpPrimitiveOptions> manifest,
	IServiceProvider services) : IPostConfigureOptions<McpServerOptions>
{
	public void PostConfigure(string? name, McpServerOptions options)
	{
		if (string.Equals(name, Options.DefaultName, StringComparison.Ordinal))
		{
			McpLocalPrimitives.AddTo(options, manifest.Value, services);
		}
	}
}
