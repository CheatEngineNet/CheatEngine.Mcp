using CheatEngine.Client.Hosting;
using CheatEngine.Mcp.Core.Execution;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Plugin.Logging;
using CheatEngine.Mcp.Plugin.Lua;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Plugin;

/// <summary>Composes one Cheat Engine activation: configuration, logging, Client opt-ins, lifecycle module and backend.</summary>
internal static class CheatEngineMcpPluginBuilderExtensions
{
	extension(CheatEnginePluginBuilder builder)
	{
		/// <summary>Registers the MCP plugin in the activation; the returned builder declares the backend's primitives.</summary>
		/// <param name="environment">The process inputs to read.</param>
		/// <param name="log">The plugin instance's log sink, shared by every activation of this process.</param>
		/// <returns>The composition builder.</returns>
		internal ICheatEngineMcpBuilder AddCheatEngineMcp(McpPluginEnvironment environment, PluginLogSink log)
		{
			ArgumentNullException.ThrowIfNull(builder);
			ArgumentNullException.ThrowIfNull(environment);
			ArgumentNullException.ThrowIfNull(log);
			// The Client binds its own CheatEngineClient section from the same configuration.
			builder.Configuration.AddCheatEngineMcpSettings(builder.PluginDirectory, environment);
			// Read once: this instance drives both the Client opt-ins below and the MCP feature gates.
			McpFeatureOptions features = ReadFeatureOptions(builder.Configuration);
			// The activation's only provider is the plugin log, which AddCheatEngineMcpServices registers.
			builder.Logging.ClearProviders();
			if (features.EnableUnsafeLua)
			{
				builder.Client.EnableUnsafeLuaExecution();
			}

			if (features.EnableAutoAssembler)
			{
				builder.Client.EnableAutoAssemblerPatches();
			}

			builder.Client.AddModule<McpServerModule>();
			// The Client resolves the folder CE loaded this plugin from; CE's application base directory is not it.
			return builder.Services
				.AddCheatEngineMcpServices(builder.Configuration, environment, log, builder.PluginDirectory)
				.AddCheatEngineMcpExecution(builder.Configuration, features);
		}
	}

	extension(ICheatEngineMcpBuilder mcp)
	{
		/// <summary>
		///     Registers the activation's execution services with the given feature switches, as the only
		///     <see cref="McpFeatureOptions" /> instance, and binds <see cref="McpExecutionOptions" /> from
		///     <c>Mcp:Execution</c>.
		/// </summary>
		/// <param name="configuration">The activation configuration.</param>
		/// <param name="features">The switches that already drove the Client opt-ins.</param>
		/// <returns>The same builder.</returns>
		internal ICheatEngineMcpBuilder AddCheatEngineMcpExecution(IConfiguration configuration,
			McpFeatureOptions features)
		{
			ArgumentNullException.ThrowIfNull(mcp);
			ArgumentNullException.ThrowIfNull(configuration);
			ArgumentNullException.ThrowIfNull(features);
			mcp.Services.AddSingleton(Options.Create(features));
			mcp.Services.AddOptions<McpExecutionOptions>()
				.Bind(configuration.GetSection(McpExecutionOptions.SectionName));
			mcp.Services.TryAddScoped<IFixedLuaExecutor, PluginFixedLuaExecutor>();
			return mcp.AddExecutionServices();
		}
	}

	/// <summary>Binds the feature switches from the flat keys of the <c>Mcp</c> section; absent keys stay enabled.</summary>
	/// <param name="configuration">The activation configuration.</param>
	/// <returns>A new options instance.</returns>
	internal static McpFeatureOptions ReadFeatureOptions(IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		return configuration.GetSection(McpFeatureOptions.SectionName).Get<McpFeatureOptions>()
			   ?? new McpFeatureOptions();
	}
}
