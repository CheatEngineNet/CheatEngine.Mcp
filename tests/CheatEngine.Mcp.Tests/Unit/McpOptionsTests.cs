using Microsoft.Extensions.Configuration;

namespace CheatEngine.Mcp.Tests;

[Collection(nameof(SerialTestGroup))]
public sealed class McpOptionsTests
{
	[Fact]
	public void ConfigurationDirectory_ExplicitAbsolutePath_IsolatedFromUserSettings()
	{
		string directory = Path.Combine(Path.GetTempPath(), "CheatEngine.Mcp.LiveTests");
		Assert.Equal(Path.GetFullPath(directory), McpOptions.ResolveConfigurationDirectory(directory));
		Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CheatEngine.Mcp"),
			McpOptions.ResolveConfigurationDirectory(null));
	}

	[Theory]
	[InlineData("")]
	[InlineData("relative/path")]
	public void ConfigurationDirectory_RelativeOverride_IsRejected(string directory)
	{
		Assert.Throws<ArgumentException>(() => McpOptions.ResolveConfigurationDirectory(directory));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Load_Defaults_AreLoopbackAndAllExecutionIsEnabled(bool useBundledDefaults)
	{
		using SettingsFixture fixture = new SettingsFixture();
		if (useBundledDefaults)
		{
			File.Copy(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), Path.Combine(fixture.PluginDirectory, "appsettings.json"));
		}
		McpOptions options = fixture.Load();
		Assert.Equal("http://127.0.0.1:0", options.BaseUrl);
		Assert.Equal("CheatEngine.Mcp", options.ServerName);
		Assert.True(options.EnableUnsafeLua);
		Assert.True(options.EnableAutoAssembler);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	public void Load_ExplicitExecutionOverrides_RespectUserChoice(bool enableLua, bool enableAssembler)
	{
		using SettingsFixture fixture = new SettingsFixture();
		File.Copy(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), Path.Combine(fixture.PluginDirectory, "appsettings.json"));
		File.WriteAllText(Path.Combine(fixture.UserDirectory, "appsettings.json"),
			System.Text.Json.JsonSerializer.Serialize(new
			{
				Mcp = new
				{
					EnableUnsafeLua = enableLua,
					EnableAutoAssembler = enableAssembler
				}
			}));

		McpOptions options = fixture.Load();

		Assert.Equal(enableLua, options.EnableUnsafeLua);
		Assert.Equal(enableAssembler, options.EnableAutoAssembler);
	}

	[Fact]
	public void Load_PluginUserAndEnvironment_UseDocumentedPrecedence()
	{
		using SettingsFixture fixture = new SettingsFixture();
		File.WriteAllText(Path.Combine(fixture.PluginDirectory, "appsettings.json"),
			"""{"Mcp":{"Host":"127.0.0.1","Port":6400,"ServerName":"plugin","EnableUnsafeLua":true}}""");
		File.WriteAllText(Path.Combine(fixture.UserDirectory, "appsettings.json"),
			"""{"Mcp":{"Port":6500,"ServerName":"user"}}""");
		McpOptions options = fixture.Load(name => name == "MCP_PORT" ? "6600" : null);
		Assert.Equal("http://127.0.0.1:6600", options.BaseUrl);
		Assert.Equal("user", options.ServerName);
		Assert.True(options.EnableUnsafeLua);
	}

	[Fact]
	public void Load_ObsoleteConfiguration_DoesNotOverrideAutomaticPortSelection()
	{
		using SettingsFixture fixture = new SettingsFixture();
		string legacyDirectory = Path.Combine(fixture.Root, "CeMCP");
		Directory.CreateDirectory(legacyDirectory);
		string path = Path.Combine(legacyDirectory, "config.json");
		const string original = """{"Host":"localhost","Port":6401,"ServerName":"legacy"}""";
		File.WriteAllText(path, original);
		Assert.Equal(0, fixture.Load().Port);
		File.WriteAllText(Path.Combine(fixture.UserDirectory, "appsettings.json"), "{}");
		Assert.Equal(0, fixture.Load().Port);
		Assert.Equal(original, File.ReadAllText(path));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(65536)]
	public void Validate_InvalidPort_IsRejected(int port)
	{
		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new McpOptions { Port = port }.Validate());
		Assert.Equal("Mcp:Port must be between 0 and 65535 (0 selects a free port).", exception.Message);
	}

	[Theory]
	[InlineData("http://localhost")]
	[InlineData("localhost/path")]
	[InlineData("*")]
	[InlineData("0.0.0.0")]
	[InlineData("192.168.1.1")]
	public void Validate_InvalidHost_IsRejected(string host)
	{
		Assert.Throws<ArgumentException>(() => new McpOptions { Host = host }.Validate());
	}

	[Fact]
	public void Load_InvalidEnvironmentPort_FailsInsteadOfSilentlyBindingAnotherPort()
	{
		using SettingsFixture fixture = new SettingsFixture();
		Assert.Throws<FormatException>(() => fixture.Load(name => name == "MCP_PORT" ? "bad" : null));
	}

	[Fact]
	public void Load_InstanceEnvironment_OverridesSharedSettings()
	{
		using SettingsFixture fixture = new();
		McpOptions options = fixture.Load(name => name switch
		{
			"MCP_INSTANCE_NAME" => "game-a",
			"MCP_INSTANCE_DIRECTORY" => fixture.Root,
			_ => null
		});
		Assert.Equal("game-a", options.InstanceName);
		Assert.Equal(fixture.Root, options.InstanceDirectory);
		Assert.Equal(0, options.Port);
	}

	private sealed class SettingsFixture : IDisposable
	{
		public string Root { get; } = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		public string PluginDirectory => Path.Combine(Root, "plugin");
		public string UserDirectory => Path.Combine(Root, "CheatEngine.Mcp");

		public SettingsFixture()
		{
			Directory.CreateDirectory(PluginDirectory);
			Directory.CreateDirectory(UserDirectory);
		}

		public McpOptions Load(Func<string, string?>? environment = null)
		{
			using ConfigurationManager configuration = new ConfigurationManager();
			return McpOptions.Load(configuration, PluginDirectory, UserDirectory, environment ?? (_ => null));
		}

		public void Dispose() => Directory.Delete(Root, recursive: true);
	}
}
