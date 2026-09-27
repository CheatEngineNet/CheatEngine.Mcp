using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Hosting.Backend;
using CheatEngine.Mcp.Hosting.Configuration;
using CheatEngine.Mcp.Plugin.Logging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Plugin;

/// <summary>The plugin's activation services, separate from the Cheat Engine builder so tests can validate them.</summary>
internal static class McpPluginServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>
		///     Registers the runtime identity of the plugin in <paramref name="pluginDirectory" />, the plugin log and its
		///     provider, the host-file policy, the status indicator and the backend.
		/// </summary>
		/// <param name="configuration">The activation configuration.</param>
		/// <param name="environment">The process inputs to read.</param>
		/// <param name="log">The plugin instance's log sink, shared by every activation of this process.</param>
		/// <param name="pluginDirectory">The absolute folder the plugin assembly was loaded from.</param>
		/// <returns>The composition builder.</returns>
		/// <exception cref="OptionsValidationException">The log level or a <c>Mcp:Files</c> root is invalid.</exception>
		internal ICheatEngineMcpBuilder AddCheatEngineMcpServices(IConfiguration configuration,
			McpPluginEnvironment environment, PluginLogSink log, string pluginDirectory)
		{
			ArgumentNullException.ThrowIfNull(services);
			ArgumentNullException.ThrowIfNull(environment);
			ArgumentNullException.ThrowIfNull(log);
			// Read before the first registration: a relative or blank folder fails the enable here.
			McpRuntimeInfo runtime = CheatEngineMcpPlugin.CreateRuntimeInfo(pluginDirectory);
			LogLevel level = PluginLoggingOptions.Read(configuration).MinimumLevel;
			McpFileOptions files = ReadFileOptions(configuration);
			services.AddSingleton(runtime);
			services.AddSingleton(_ => new PluginLog(log, environment.DataDirectory, level));
			services.AddSingleton<IMcpBackendLogging>(static provider => provider.GetRequiredService<PluginLog>());
			// A provider rule outranks LoggerFilterOptions.MinLevel, whatever Client Hosting configures.
			services.AddLogging(logging => logging.AddFilter<PluginLoggerProvider>(null, level));
			services.AddSingleton<ILoggerProvider>(static provider =>
				provider.GetRequiredService<PluginLog>().CreateProvider());
			services.AddSingleton(Options.Create(files));
			// The registry directory is the bound, validated one the backend publishes this activation in.
			services.AddSingleton(provider => new McpFilePaths(files,
				provider.GetRequiredService<IOptions<McpDiscoveryOptions>>().Value.InstanceDirectory,
				environment.DataDirectory));
			services.AddSingleton<McpStatusIndicator>();
			return services.AddCheatEngineMcpBackend(configuration);
		}
	}

	/// <summary>
	///     Binds <c>Mcp:Files</c> with the generated binder and validates it, while Cheat Engine configures the activation,
	///     so an invalid root fails the enable; an absent section refuses every write.
	/// </summary>
	/// <param name="configuration">The activation configuration.</param>
	/// <returns>A new, valid options instance.</returns>
	/// <exception cref="OptionsValidationException">A root is blank, relative, a UNC or device path, or repeated.</exception>
	internal static McpFileOptions ReadFileOptions(IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		McpFileOptions options = configuration.GetSection(McpFileOptions.SectionName).Get<McpFileOptions>()
								 ?? new McpFileOptions();
		return new McpFileOptionsValidator().ThrowIfInvalid(options);
	}
}
