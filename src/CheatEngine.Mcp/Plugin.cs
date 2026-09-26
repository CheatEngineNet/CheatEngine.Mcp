using CheatEngine.Client.Hosting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NLog.Extensions.Logging;

namespace CheatEngine.Mcp;

/// <summary>The managed entry point generated and loaded by Cheat Engine.</summary>
[SDK.Annotations.Plugin.CheatEnginePlugin("CheatEngine.Mcp")]
public sealed class Plugin : CheatEngineClientPlugin
{
	protected override void Configure(CheatEnginePluginBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		McpOptions options = McpOptions.Load(builder.Configuration, builder.PluginDirectory,
			McpOptions.ConfigurationDirectory, Environment.GetEnvironmentVariable);
		builder.Services.AddSingleton(options);
		builder.Services.AddSingleton<PluginLog>();
		builder.Logging.ClearProviders();
		builder.Logging.AddNLog(new NLogProviderOptions { ShutdownOnDispose = false },
			services => services.GetRequiredService<PluginLog>().Factory);
		if (options.EnableUnsafeLua)
		{
			builder.Client.EnableUnsafeLuaExecution();
		}
		if (options.EnableAutoAssembler)
		{
			builder.Client.EnableAutoAssemblerPatches();
		}
		builder.Client.AddModule<McpModule>();
	}
}
