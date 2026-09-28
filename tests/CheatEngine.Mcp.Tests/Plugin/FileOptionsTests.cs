using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Plugin;
using CheatEngine.Mcp.Plugin.Logging;
using CheatEngine.Mcp.Tests.Core;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Plugin;

/// <summary>The plugin binds <c>Mcp:Files</c> once, fails the enable on an invalid root and composes the file policy.</summary>
[Collection(nameof(SerialTestGroup))]
public sealed class FileOptionsTests : IDisposable
{
	private readonly McpFilePathsTests.Scratch _scratch = new();

	public void Dispose()
	{
		_scratch.Dispose();
	}

	[Fact]
	public void ReadFileOptions_BundledDefaults_RefuseEveryWrite()
	{
		string plugin = _scratch.CreateFolder("plugin");
		File.Copy(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), Path.Combine(plugin, "appsettings.json"));
		using ConfigurationManager configuration = Layer(plugin, null);

		McpFileOptions options = McpPluginServiceCollectionExtensions.ReadFileOptions(configuration);

		Assert.Empty(options.AllowedRoots);
		using ConfigurationManager empty = new();
		Assert.Empty(McpPluginServiceCollectionExtensions.ReadFileOptions(empty).AllowedRoots);
	}

	[Fact]
	public void ReadFileOptions_UserRoots_AreBoundOverTheShippedEmptyList()
	{
		string plugin = _scratch.CreateFolder("plugin");
		File.Copy(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), Path.Combine(plugin, "appsettings.json"));
		using ConfigurationManager configuration = Layer(plugin,
			"""{"Mcp":{"Files":{"AllowedRoots":["C:\\Dumps","D:/Saved/"]}}}""");

		McpFileOptions options = McpPluginServiceCollectionExtensions.ReadFileOptions(configuration);

		string[] expected = ["C:\\Dumps", "D:/Saved/"];
		Assert.Equal(expected, options.AllowedRoots);
	}

	[Theory]
	[InlineData("dumps", "Mcp:Files:AllowedRoots:0 must be an absolute path with a drive letter.")]
	[InlineData("\\\\server\\share", "Mcp:Files:AllowedRoots:0 must not be a UNC or device path")]
	[InlineData("C:\\dumps\\CON", "Mcp:Files:AllowedRoots:0 must not name a Windows device")]
	public void AddCheatEngineMcpServices_InvalidRoot_FailsTheEnableBeforeRegistering(string root, string message)
	{
		using ConfigurationManager configuration = new();
		configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:Files:AllowedRoots:0"] = root });
		ServiceCollection services = new();
		using PluginLogSink log = new();

		OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() =>
			services.AddCheatEngineMcpServices(configuration,
				new McpPluginEnvironment(_scratch.Root, static _ => null), log, AppContext.BaseDirectory));

		Assert.Equal(typeof(McpFileOptions), exception.OptionsType);
		Assert.Contains(exception.Failures, failure => failure.StartsWith(message, StringComparison.Ordinal));
		Assert.Empty(services);
	}

	[Fact]
	public void Activation_FilePolicy_ProtectsTheBoundRegistryAndTheDataDirectory()
	{
		string registry = _scratch.CreateFolder("instances");
		string root = _scratch.CreateFolder("dumps");
		using TestActivation activation = new(ClientTestDouble.Client(),
			new Dictionary<string, string?>
			{
				["Mcp:InstanceDirectory"] = registry,
				["Mcp:Files:AllowedRoots:0"] = root
			});

		McpFilePaths paths = activation.Services.GetRequiredService<McpFilePaths>();

		string[] roots = [root];
		Assert.Same(paths, activation.Services.GetRequiredService<McpFilePaths>());
		Assert.Equal(roots, paths.AllowedRoots);
		Assert.Equal(roots, activation.Services.GetRequiredService<IOptions<McpFileOptions>>().Value.AllowedRoots);
		Assert.Equal(Path.Combine(root, "dump.bin"), paths.RequireWrite(Path.Combine(root, "dump.bin"), "test_tool"));
		Assert.Equal(ToolErrorKind.InvalidArgument, Assert.Throws<CheatEngineToolException>(() =>
			paths.RequireRead(Path.Combine(registry, "instance.json"), "test_tool")).Error.Kind);
		Assert.Equal(ToolErrorKind.InvalidArgument, Assert.Throws<CheatEngineToolException>(() =>
			paths.RequireRead(Path.Combine(activation.DataDirectory, "appsettings.json"), "test_tool")).Error.Kind);
	}

	private ConfigurationManager Layer(string pluginDirectory, string? userSettings)
	{
		string user = _scratch.CreateFolder("user");
		if (userSettings is not null)
		{
			File.WriteAllText(Path.Combine(user, "appsettings.json"), userSettings);
		}

		ConfigurationManager configuration = new();
		configuration.AddCheatEngineMcpSettings(pluginDirectory, new McpPluginEnvironment(user, static _ => null));
		return configuration;
	}
}
