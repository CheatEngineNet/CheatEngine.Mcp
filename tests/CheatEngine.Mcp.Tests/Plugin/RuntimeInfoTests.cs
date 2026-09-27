using System.Globalization;
using System.Reflection;

using CheatEngine.Client.Hosting;
using CheatEngine.Mcp.Plugin;
using CheatEngine.Mcp.Plugin.Logging;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CheatEngine.Mcp.Tests.Plugin;

[Collection(nameof(SerialTestGroup))]
public sealed class RuntimeInfoTests
{
	[Fact]
	public void CreateRuntimeInfo_PluginDirectory_DescribesThePluginFileThere()
	{
		McpRuntimeInfo runtime = CheatEngineMcpPlugin.CreateRuntimeInfo(AppContext.BaseDirectory);

		Assert.Equal(Path.Combine(AppContext.BaseDirectory, "CheatEngine.Mcp.Plugin.dll"), runtime.RuntimeLocation);
		Assert.True(File.Exists(runtime.Location), runtime.Location);
		Assert.Equal(runtime.RuntimeLocation, runtime.Location);
		Assert.Equal("CheatEngine.Mcp.Plugin", runtime.ApplicationName);
		Assert.Equal(typeof(CheatEngineMcpPlugin).Assembly.GetName().Version?.ToString(), runtime.Version);
		Assert.False(string.IsNullOrWhiteSpace(runtime.Version));
	}

	[Fact]
	public void AddCheatEngineMcpServices_PluginDirectory_RegistersTheIdentityOfThePluginThere()
	{
		string folder = Path.Combine(Path.GetTempPath(), "CheatEngine.Mcp.Tests-PluginFolder");
		ServiceCollection services = new();
		using ConfigurationManager configuration = new();
		using PluginLogSink log = new();

		services.AddCheatEngineMcpServices(configuration,
			new McpPluginEnvironment(Path.GetTempPath(), static _ => null), log, folder);

		McpRuntimeInfo registered = Assert.IsType<McpRuntimeInfo>(Assert.Single(services,
			static service => service.ServiceType == typeof(McpRuntimeInfo)).ImplementationInstance);
		Assert.Equal(CheatEngineMcpPlugin.CreateRuntimeInfo(folder), registered);
		Assert.Equal(Path.Combine(folder, "CheatEngine.Mcp.Plugin.dll"), registered.Location);
	}

	[Theory]
	[InlineData("")]
	[InlineData("relative/plugins")]
	public void AddCheatEngineMcpServices_RelativePluginDirectory_FailsBeforeAnyRegistration(string folder)
	{
		ServiceCollection services = new();
		using ConfigurationManager configuration = new();
		using PluginLogSink log = new();

		Assert.Throws<ArgumentException>(() => services.AddCheatEngineMcpServices(configuration,
			new McpPluginEnvironment(Path.GetTempPath(), static _ => null), log, folder));

		Assert.Empty(services);
	}

	[Fact]
	public void AddCheatEngineMcp_CheatEngineBuilder_DescribesTheClientPluginDirectory()
	{
		string dataDirectory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(dataDirectory);
		using PluginLogSink log = new();
		try
		{
			// Client Hosting creates one builder per enable through its internal constructor.
			CheatEnginePluginBuilder builder = (CheatEnginePluginBuilder) Activator.CreateInstance(
				typeof(CheatEnginePluginBuilder), BindingFlags.Instance | BindingFlags.NonPublic, null,
				[typeof(CheatEngineMcpPlugin).Assembly], CultureInfo.InvariantCulture)!;

			builder.AddCheatEngineMcp(new McpPluginEnvironment(dataDirectory, static _ => null), log);

			McpRuntimeInfo registered = Assert.IsType<McpRuntimeInfo>(Assert.Single(builder.Services,
				static service => service.ServiceType == typeof(McpRuntimeInfo)).ImplementationInstance);
			Assert.Equal(CheatEngineMcpPlugin.CreateRuntimeInfo(builder.PluginDirectory), registered);
		}
		finally
		{
			Directory.Delete(dataDirectory, true);
		}
	}

	[Fact]
	public void ForPlugin_AnyFolder_DerivesTheFileFromTheFolderAndName()
	{
		string folder = Path.Combine(Path.GetTempPath(), "CheatEngine.Mcp.Tests-PluginFolder", "plugins");
		McpRuntimeInfo runtime =
			McpRuntimeInfo.ForPlugin(new AssemblyName("CheatEngine.Mcp.Plugin, Version=2.1.0.0"), folder);

		Assert.Equal(Path.Combine(folder, "CheatEngine.Mcp.Plugin.dll"), runtime.Location);
		Assert.Equal(runtime.Location, runtime.RuntimeLocation);
		Assert.Equal("CheatEngine.Mcp.Plugin", runtime.ApplicationName);
		Assert.Equal("2.1.0.0", runtime.Version);
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("relative/plugins")]
	public void ForPlugin_EmptyOrRelativeDirectory_IsRejected(string directory)
	{
		Assert.Throws<ArgumentException>(() =>
			McpRuntimeInfo.ForPlugin(new AssemblyName("CheatEngine.Mcp.Plugin"), directory));
	}

	[Fact]
	public void ForPlugin_NamelessAssembly_IsRejected()
	{
		Assert.Throws<ArgumentException>(() => McpRuntimeInfo.ForPlugin(new AssemblyName(), AppContext.BaseDirectory));
	}
}
