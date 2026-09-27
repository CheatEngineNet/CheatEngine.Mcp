using CheatEngine.Mcp.Plugin;

namespace CheatEngine.Mcp.Tests.Plugin;

public sealed class RuntimeInfoTests
{
	[Fact]
	public void CreateRuntimeInfo_LoadedPlugin_DescribesThePluginAssembly()
	{
		McpRuntimeInfo runtime = CheatEngineMcpPlugin.CreateRuntimeInfo();
		Assert.Equal("CheatEngine.Mcp.Plugin.dll", Path.GetFileName(runtime.RuntimeLocation));
		Assert.Equal("CheatEngine.Mcp.Plugin", runtime.ApplicationName);
		Assert.Equal(runtime.RuntimeLocation, runtime.Location);
		Assert.False(string.IsNullOrWhiteSpace(runtime.Version));
	}
}
