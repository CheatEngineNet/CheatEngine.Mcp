using CheatEngine.Mcp.Hosting.Discovery;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>Registers the gateway's transport-independent routing services.</summary>
internal static class GatewayRoutingServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>Registers the transport-independent routing services and handlers.</summary>
		/// <param name="instanceDirectory">The absolute discovery directory shared with the plugins.</param>
		/// <returns>The same collection.</returns>
		internal IServiceCollection AddCheatEngineGatewayRouting(string instanceDirectory)
		{
			ArgumentNullException.ThrowIfNull(services);
			services.TryAddSingleton(new InstanceRegistry(instanceDirectory));
			services.TryAddSingleton<GatewayToolCatalog>();
			services.TryAddSingleton<GatewayRouter>();
			services.AddOptions<CheatEngineMcpPrimitiveOptions>();
			services.TryAddEnumerable(ServiceDescriptor
				.Singleton<IConfigureOptions<McpServerOptions>, GatewayServerOptionsSetup>());
			return services;
		}
	}
}
