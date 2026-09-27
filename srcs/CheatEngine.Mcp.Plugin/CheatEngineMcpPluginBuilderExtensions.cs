using CheatEngine.Client.Hosting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NLog.Extensions.Logging;

namespace CheatEngine.Mcp.Plugin;

/// <summary>Composes one Cheat Engine activation: configuration, logging, Client opt-ins, lifecycle module and backend.</summary>
internal static class CheatEngineMcpPluginBuilderExtensions
{
	extension(CheatEnginePluginBuilder builder)
	{
		/// <summary>Registers the MCP plugin in the activation; the returned builder declares the backend's primitives.</summary>
		/// <param name="environment">The process inputs to read.</param>
		/// <returns>The composition builder.</returns>
		internal ICheatEngineMcpBuilder AddCheatEngineMcp(McpPluginEnvironment environment)
		{
			ArgumentNullException.ThrowIfNull(builder);
			ArgumentNullException.ThrowIfNull(environment);
			// The Client binds its own CheatEngineClient section from the same configuration.
			builder.Configuration.AddCheatEngineMcpSettings(builder.PluginDirectory, environment);
			McpFeatureOptions features = McpFeatureOptions.Read(builder.Configuration);
			builder.Logging.ClearProviders();
			builder.Logging.AddNLog(new NLogProviderOptions { ShutdownOnDispose = false },
				static services => services.GetRequiredService<PluginLog>().Factory);
			if (features.EnableUnsafeLua)
			{
				builder.Client.EnableUnsafeLuaExecution();
			}

			if (features.EnableAutoAssembler)
			{
				builder.Client.EnableAutoAssemblerPatches();
			}

			builder.Client.AddModule<McpServerModule>();
			return builder.Services.AddCheatEngineMcpServices(builder.Configuration, environment);
		}
	}
}
