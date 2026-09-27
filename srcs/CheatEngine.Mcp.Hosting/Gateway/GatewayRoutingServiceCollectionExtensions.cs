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
		/// <param name="options">The resolved gateway options, whose instance directory the plugins share.</param>
		/// <returns>The same collection.</returns>
		internal IServiceCollection AddCheatEngineGatewayRouting(GatewayOptions options)
		{
			ArgumentNullException.ThrowIfNull(services);
			ArgumentNullException.ThrowIfNull(options);
			services.TryAddSingleton(options);
			services.TryAddSingleton(new InstanceRegistry(options.InstanceDirectory));
			services.TryAddSingleton(TimeProvider.System);
			services.TryAddSingleton<InstanceIdentityVerifier>();
			services.TryAddSingleton<BackendConnectionPool>();
			services.TryAddSingleton<GatewayInstanceTool>();
			services.TryAddSingleton<GatewayToolCatalog>();
			services.TryAddSingleton<GatewayRouter>();
			services.AddOptions<CheatEngineMcpPrimitiveOptions>();
			services.TryAddEnumerable(ServiceDescriptor
				.Singleton<IConfigureOptions<McpServerOptions>, GatewayServerOptionsSetup>());
			return services;
		}
	}
}
