using CheatEngine.Mcp.Hosting.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Hosting.Backend;

/// <summary>Registers the per-activation MCP backend in a Cheat Engine activation container.</summary>
public static class CheatEngineMcpBackendServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>
		///     Registers the backend factory, its validated options and the activation-scoped primitive targets. The
		///     activation must also provide <see cref="IMcpBackendLogging" /> and <see cref="McpRuntimeInfo" />; the returned
		///     builder declares the primitives, which it registers as activation-scoped services.
		/// </summary>
		/// <param name="configuration">The activation configuration; its <c>Mcp</c> section is bound.</param>
		/// <returns>The composition builder for the backend's primitives.</returns>
		/// <remarks>
		///     The factory resolves the options when it is constructed, so invalid settings fail while Client Hosting
		///     constructs the activation's modules, before any backend starts.
		/// </remarks>
		public ICheatEngineMcpBuilder AddCheatEngineMcpBackend(IConfiguration configuration)
		{
			ArgumentNullException.ThrowIfNull(services);
			ArgumentNullException.ThrowIfNull(configuration);
			services.AddOptions<McpBackendOptions>().Bind(configuration.GetSection(McpBackendOptions.SectionName));
			services.AddOptions<McpDiscoveryOptions>().Bind(configuration.GetSection(McpDiscoveryOptions.SectionName));
			services.TryAddEnumerable(ServiceDescriptor
				.Singleton<IValidateOptions<McpBackendOptions>, McpBackendOptionsValidator>());
			services.TryAddEnumerable(ServiceDescriptor
				.Singleton<IValidateOptions<McpDiscoveryOptions>, McpDiscoveryOptionsValidator>());
			services.TryAddSingleton<IMcpBackendHostFactory, McpBackendHostFactory>();
			services.AddOptions<CheatEngineMcpPrimitiveOptions>();
			services.TryAddScoped(static scope => McpPrimitiveTargets.Resolve(scope,
				scope.GetRequiredService<IOptions<CheatEngineMcpPrimitiveOptions>>().Value));
			return new CheatEngineMcpBuilder(services, CheatEngineMcpMode.Backend);
		}
	}
}
