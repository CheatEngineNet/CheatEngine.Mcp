using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     The only writer of the gateway's tool handlers; it never registers MCP primitives, so initialize stays
///     unchanged.
/// </summary>
internal sealed class GatewayServerOptionsSetup(GatewayRouter router) : IConfigureOptions<McpServerOptions>
{
	public void Configure(McpServerOptions options)
	{
		options.Handlers.ListToolsHandler = router.ListToolsAsync;
		options.Handlers.CallToolHandler = router.CallToolAsync;
		options.Capabilities ??= new ServerCapabilities();
		options.Capabilities.Tools ??= new ToolsCapability();
	}
}
