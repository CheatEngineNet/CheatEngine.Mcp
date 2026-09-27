using CheatEngine.Client.Hosting;
using CheatEngine.Mcp.Prompts;
using CheatEngine.Mcp.Resources;
using CheatEngine.Mcp.Tools;
using CheatEngine.SDK.Annotations.Plugin;

namespace CheatEngine.Mcp.Plugin;

/// <summary>The managed entry point generated and loaded by Cheat Engine.</summary>
[CheatEnginePlugin("CheatEngine.Mcp")]
public sealed class CheatEngineMcpPlugin : CheatEngineClientPlugin
{
	protected override void Configure(CheatEnginePluginBuilder builder)
	{
		ComposePrimitives(builder.AddCheatEngineMcp(McpPluginEnvironment.Current));
	}

	/// <summary>The primitives this plugin serves; the gateway executable composes the same chain.</summary>
	internal static ICheatEngineMcpBuilder ComposePrimitives(ICheatEngineMcpBuilder builder)
	{
		return builder
			.AddTools()
			.AddResources()
			.AddPrompts();
	}

	/// <summary>Describes this plugin assembly.</summary>
	internal static McpRuntimeInfo CreateRuntimeInfo()
	{
		return McpRuntimeInfo.FromAssembly(typeof(CheatEngineMcpPlugin).Assembly);
	}
}
