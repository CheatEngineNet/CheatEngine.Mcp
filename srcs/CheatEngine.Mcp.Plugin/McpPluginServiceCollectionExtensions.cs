using CheatEngine.Mcp.Hosting.Backend;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CheatEngine.Mcp.Plugin;

/// <summary>The plugin's activation services, separate from the Cheat Engine builder so tests can validate them.</summary>
internal static class McpPluginServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>Registers the runtime identity, shared log sink, status indicator and backend.</summary>
		/// <param name="configuration">The activation configuration.</param>
		/// <param name="environment">The process inputs to read.</param>
		/// <returns>The composition builder.</returns>
		internal ICheatEngineMcpBuilder AddCheatEngineMcpServices(IConfiguration configuration,
			McpPluginEnvironment environment)
		{
			ArgumentNullException.ThrowIfNull(services);
			ArgumentNullException.ThrowIfNull(environment);
			services.AddSingleton(CheatEngineMcpPlugin.CreateRuntimeInfo());
			services.AddSingleton(_ => new PluginLog(environment.DataDirectory));
			services.AddSingleton<IMcpBackendLogging>(static provider => provider.GetRequiredService<PluginLog>());
			services.AddSingleton<McpStatusIndicator>();
			return services.AddCheatEngineMcpBackend(configuration);
		}
	}
}
