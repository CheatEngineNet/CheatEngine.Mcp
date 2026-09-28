using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     The only writer of the gateway's routing handlers and <c>initialize</c> instructions: tools, the instance list,
///     the live resources of verified instances and routed resource templates, routed reads and the completion of
///     routed templates. The Local documents and prompts are added by <see cref="GatewayLocalPrimitivesSetup" />; the
///     SDK merges them into these lists and completions. The completions capability is advertised explicitly, like a
///     live backend does. The resource collection always exists, even without Local documents, because
///     <see cref="GatewayResourceListMonitor" /> raises <c>notifications/resources/list_changed</c> through it; the SDK
///     then advertises <c>resources.listChanged</c> on every transport that can deliver it.
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
		options.Handlers.ListResourcesHandler = resources.ListResourcesAsync;
		options.Handlers.ListResourceTemplatesHandler = resources.ListResourceTemplatesAsync;
		options.Handlers.ReadResourceHandler = resources.ReadResourceAsync;
		options.Handlers.CompleteHandler = completions.CompleteAsync;
		options.Capabilities ??= new ServerCapabilities();
		options.Capabilities.Tools ??= new ToolsCapability();
		options.Capabilities.Completions ??= new CompletionsCapability();
		options.ResourceCollection ??= [];
		options.ServerInstructions ??= manifest.Value.GatewayInstructions;
	}
}
