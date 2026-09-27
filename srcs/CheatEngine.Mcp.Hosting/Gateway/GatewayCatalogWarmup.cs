using Microsoft.Extensions.Hosting;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>Builds the routed catalog before the stdio transport opens, so a schema failure stops the gateway at startup.</summary>
internal sealed class GatewayCatalogWarmup(GatewayToolCatalog catalog, GatewayResourceCatalog resources) : IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken)
	{
		_ = catalog.Tools;
		_ = resources.RoutedTemplates;
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
