using CheatEngine.Mcp.Plugin;

namespace CheatEngine.Mcp.Tests.Support;

internal static class TestRuntime
{
	/// <summary>The runtime identity the plugin composes for its own assembly.</summary>
	internal static McpRuntimeInfo Info
	{
		get;
	} = CheatEngineMcpPlugin.CreateRuntimeInfo();
}
