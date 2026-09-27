using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Hosting;
using CheatEngine.Mcp.Plugin.Logging;
using CheatEngine.Mcp.Prompts;
using CheatEngine.Mcp.Resources;
using CheatEngine.Mcp.Tools;
using CheatEngine.SDK.Annotations.Plugin;

namespace CheatEngine.Mcp.Plugin;

/// <summary>The managed entry point generated and loaded by Cheat Engine.</summary>
[CheatEnginePlugin("CheatEngine.Mcp")]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
	Justification = "The SDK never disposes a plugin; the sink holds no handle, each generation closes its file.")]
public sealed class CheatEngineMcpPlugin : CheatEngineClientPlugin
{
	// One log file per CE process. The SDK constructs this plugin once and reuses the instance across enables, so the
	// sink is instance state that spans activations, never static.
	private readonly PluginLogSink _log = new();

	protected override void Configure(CheatEnginePluginBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ComposePrimitives(builder.AddCheatEngineMcp(McpPluginEnvironment.Current, _log));
	}

	/// <summary>The primitives this plugin serves; the gateway executable composes the same chain.</summary>
	internal static ICheatEngineMcpBuilder ComposePrimitives(ICheatEngineMcpBuilder builder)
	{
		return builder
			.AddTools()
			.AddResources()
			.AddPrompts();
	}

	/// <summary>Describes this plugin assembly as the file CE loaded from <paramref name="pluginDirectory" />.</summary>
	/// <param name="pluginDirectory">The absolute folder that holds the plugin assembly.</param>
	internal static McpRuntimeInfo CreateRuntimeInfo(string pluginDirectory)
	{
		return McpRuntimeInfo.ForPlugin(typeof(CheatEngineMcpPlugin).Assembly.GetName(), pluginDirectory);
	}
}
