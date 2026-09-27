using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     The only writer of the gateway's routing handlers and <c>initialize</c> instructions: tools, the instance list
///     and routed resource templates, routed reads and <c>instanceId</c> completion. The Local documents and prompts are
///     added by <see cref="GatewayLocalPrimitivesSetup" />; the SDK merges them into these lists and completions.
/// </summary>
internal sealed class GatewayServerOptionsSetup(
	GatewayRouter router,
	GatewayResourceRouter resources,
	GatewayCompletionRouter completions,
	IOptions<CheatEngineMcpPrimitiveOptions> manifest) : IConfigureOptions<McpServerOptions>
{
	public void Configure(McpServerOptions options)
	{
		options.Handlers.ListToolsHandler = router.ListToolsAsync;
		options.Handlers.CallToolHandler = router.CallToolAsync;
		options.Handlers.ListResourcesHandler = GatewayResourceRouter.ListResourcesAsync;
		options.Handlers.ListResourceTemplatesHandler = resources.ListResourceTemplatesAsync;
		options.Handlers.ReadResourceHandler = resources.ReadResourceAsync;
		options.Handlers.CompleteHandler = completions.CompleteAsync;
		options.Capabilities ??= new ServerCapabilities();
		options.Capabilities.Tools ??= new ToolsCapability();
		options.ServerInstructions ??= manifest.Value.GatewayInstructions;
	}
}
