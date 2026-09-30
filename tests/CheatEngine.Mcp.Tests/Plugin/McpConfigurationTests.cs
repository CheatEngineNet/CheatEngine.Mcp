using System.Text.Json;

using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Plugin;
using CheatEngine.Mcp.Plugin.Logging;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Plugin;

[Collection(nameof(SerialTestGroup))]
public sealed class McpConfigurationTests
{
	[Fact]
	public void DataDirectory_ExplicitAbsolutePath_IsolatedFromUserSettings()
	{
		string directory = Path.Combine(Path.GetTempPath(), "CheatEngine.Mcp.LiveTests");
		Assert.Equal(Path.GetFullPath(directory),
			McpPluginEnvironment.From(name => name == "MCP_DATA_DIRECTORY" ? directory : null).DataDirectory);
		Assert.Equal(
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CheatEngine.Mcp"),
			McpPluginEnvironment.From(static _ => null).DataDirectory);
	}

	[Theory]
	[InlineData("")]
	[InlineData("relative/path")]
	public void DataDirectory_RelativeOverride_IsRejected(string directory)
	{
		Assert.Throws<ArgumentException>(() => McpPluginEnvironment.ResolveDataDirectory(directory));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Load_Defaults_AreLoopbackAndAllExecutionIsEnabled(bool useExampleSettings)
	{
		using SettingsFixture fixture = new();
		if (useExampleSettings)
		{
			fixture.CopyExampleSettings();
		}

		McpSettings settings = fixture.Load();
		Assert.Equal("http://127.0.0.1:0", settings.Backend.BaseUrl);
		Assert.Equal("CheatEngine.Mcp", settings.Backend.ServerName);
		Assert.Equal($"Cheat Engine {Environment.ProcessId}", settings.Discovery.InstanceName);
		Assert.Equal(InstanceRegistry.DefaultDirectory, settings.Discovery.InstanceDirectory);
		Assert.True(settings.Features.EnableUnsafeLua);
		Assert.True(settings.Features.EnableAutoAssembler);
		Assert.True(settings.Features.EnableTargetCodeExecution);
		Assert.True(settings.Features.EnableKernelAccess);
		Assert.Equal(LogLevel.Information, settings.Logging.MinimumLevel);
		Assert.Equal(100, settings.Execution.DispatchBudgetMilliseconds);
		Assert.Equal(4, settings.Execution.MaxConcurrentDispatches);
		Assert.Equal(16, settings.Execution.MaxJobs);
		Assert.Equal(120, settings.Execution.JobDefaultTtlSeconds);
		Assert.Equal(300, settings.Execution.JobMaxTtlSeconds);
		Assert.Equal(4096, settings.Execution.JobBufferLimit);
	}

	[Theory]
	[InlineData(false, false, true, true)]
	[InlineData(false, true, false, true)]
	[InlineData(true, false, true, false)]
	[InlineData(true, true, false, false)]
	public void Load_ExplicitFeatureSwitches_RespectUserChoice(bool enableLua, bool enableAssembler,
		bool enableCodeExecution, bool enableKernel)
	{
		using SettingsFixture fixture = new();
		fixture.CopyExampleSettings();
		fixture.WriteUserSettings(JsonSerializer.Serialize(new
		{
			Mcp = new
			{
				EnableUnsafeLua = enableLua,
				EnableAutoAssembler = enableAssembler,
				EnableTargetCodeExecution = enableCodeExecution,
				EnableKernelAccess = enableKernel
			}
		}));

		McpSettings settings = fixture.Load();

		Assert.Equal(enableLua, settings.Features.EnableUnsafeLua);
		Assert.Equal(enableAssembler, settings.Features.EnableAutoAssembler);
		Assert.Equal(enableCodeExecution, settings.Features.EnableTargetCodeExecution);
		Assert.Equal(enableKernel, settings.Features.EnableKernelAccess);
	}

	[Fact]
	public void Load_ExecutionSection_BindsEveryLimit()
	{
		using SettingsFixture fixture = new();
		fixture.CopyExampleSettings();
		fixture.WriteUserSettings("""
		                          {"Mcp":{"Execution":{"DispatchBudgetMilliseconds":250,"MaxConcurrentDispatches":2,
		                          "MaxJobs":8,"JobDefaultTtlSeconds":30,"JobMaxTtlSeconds":60,"JobBufferLimit":512}}}
		                          """);

		McpExecutionOptions execution = fixture.Load().Execution;

		Assert.Equal(250, execution.DispatchBudgetMilliseconds);
		Assert.Equal(2, execution.MaxConcurrentDispatches);
		Assert.Equal(8, execution.MaxJobs);
		Assert.Equal(30, execution.JobDefaultTtlSeconds);
		Assert.Equal(60, execution.JobMaxTtlSeconds);
		Assert.Equal(512, execution.JobBufferLimit);
	}

	[Theory]
	[InlineData("""{"DispatchBudgetMilliseconds":0}""",
		"Mcp:Execution:DispatchBudgetMilliseconds must be between 1 and 10000.")]
	[InlineData("""{"MaxConcurrentDispatches":65}""",
		"Mcp:Execution:MaxConcurrentDispatches must be between 1 and 64.")]
	[InlineData("""{"MaxJobs":0}""", "Mcp:Execution:MaxJobs must be between 1 and 64.")]
	[InlineData("""{"JobMaxTtlSeconds":301}""", "Mcp:Execution:JobMaxTtlSeconds must be between 1 and 300.")]
	[InlineData("""{"JobBufferLimit":65537}""", "Mcp:Execution:JobBufferLimit must be between 1 and 65536.")]
	[InlineData("""{"JobDefaultTtlSeconds":200,"JobMaxTtlSeconds":100}""",
		"Mcp:Execution:JobDefaultTtlSeconds must not exceed Mcp:Execution:JobMaxTtlSeconds.")]
	public void Load_InvalidExecutionLimits_AreRejected(string execution, string message)
	{
		using SettingsFixture fixture = new();
		fixture.WriteUserSettings("""{"Mcp":{"Execution":""" + execution + "}}");

		OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => fixture.Load());

		Assert.Equal(typeof(McpExecutionOptions), exception.OptionsType);
		Assert.Contains(exception.Failures, failure => failure.EndsWith(message, StringComparison.Ordinal));
	}

	[Fact]
	public void Load_PluginUserAndEnvironment_UseDocumentedPrecedence()
	{
		using SettingsFixture fixture = new();
		fixture.WritePluginSettings(
			"""{"Mcp":{"Host":"127.0.0.1","Port":6400,"ServerName":"plugin","EnableUnsafeLua":true}}""");
		fixture.WriteUserSettings("""{"Mcp":{"Port":6500,"ServerName":"user"}}""");
		McpSettings settings = fixture.Load(name => name == "MCP_PORT" ? "6600" : null);
		Assert.Equal("http://127.0.0.1:6600", settings.Backend.BaseUrl);
		Assert.Equal("user", settings.Backend.ServerName);
		Assert.True(settings.Features.EnableUnsafeLua);
	}

	[Fact]
	public void Load_ObsoleteConfiguration_DoesNotOverrideAutomaticPortSelection()
	{
		using SettingsFixture fixture = new();
		string legacyDirectory = Path.Combine(fixture.Root, "CeMCP");
		Directory.CreateDirectory(legacyDirectory);
		string path = Path.Combine(legacyDirectory, "config.json");
		const string original = """{"Host":"localhost","Port":6401,"ServerName":"legacy"}""";
		File.WriteAllText(path, original);
		Assert.Equal(0, fixture.Load().Backend.Port);
		fixture.WriteUserSettings("{}");
		Assert.Equal(0, fixture.Load().Backend.Port);
		Assert.Equal(original, File.ReadAllText(path));
	}

	[Theory]
	[InlineData("-1")]
	[InlineData("65536")]
	public void Load_InvalidPort_IsRejected(string port)
	{
		using SettingsFixture fixture = new();
		OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() =>
			fixture.Load(name => name == "MCP_PORT" ? port : null));
		Assert.Equal(typeof(McpBackendOptions), exception.OptionsType);
		Assert.Contains(exception.Failures, failure =>
			failure.EndsWith("Mcp:Port must be between 0 and 65535 (0 selects a free port).",
				StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("http://localhost")]
	[InlineData("localhost/path")]
	[InlineData("*")]
	[InlineData("0.0.0.0")]
	[InlineData("192.168.1.1")]
	public void Load_InvalidHost_IsRejected(string host)
	{
		using SettingsFixture fixture = new();
		OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() =>
			fixture.Load(name => name == "MCP_HOST" ? host : null));
		Assert.Contains(exception.Failures, failure => failure.EndsWith(
			"Mcp:Host must be 127.0.0.1; the gateway connects to private local backends.", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("""{"Mcp":{"ServerName":" "}}""", "Mcp:ServerName is required.")]
	[InlineData("""{"Mcp":{"InstanceName":""}}""", "Mcp:InstanceName is required.")]
	[InlineData("""{"Mcp":{"InstanceDirectory":"relative/instances"}}""",
		"Mcp:InstanceDirectory must be an absolute directory.")]
	public void Load_InvalidUserSettings_AreRejected(string json, string message)
	{
		using SettingsFixture fixture = new();
		fixture.WriteUserSettings(json);
		OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => fixture.Load());
		Assert.Contains(exception.Failures, failure => failure.EndsWith(message, StringComparison.Ordinal));
	}

	[Fact]
	public void Load_LongInstanceName_IsRejected()
	{
		using SettingsFixture fixture = new();
		OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() =>
			fixture.Load(name => name == "MCP_INSTANCE_NAME" ? new string('x', 129) : null));
		Assert.Contains(exception.Failures, failure =>
			failure.EndsWith("Mcp:InstanceName must be at most 128 characters.", StringComparison.Ordinal));
	}

	[Fact]
	public void Load_InvalidEnvironmentPort_FailsInsteadOfSilentlyBindingAnotherPort()
	{
		using SettingsFixture fixture = new();

		FormatException exception = Assert.Throws<FormatException>(() =>
			fixture.Load(name => name == "MCP_PORT" ? "bad" : null));

		Assert.Equal("MCP_PORT must be an integer from 0 to 65535.", exception.Message);
	}

	[Fact]
	public void Load_InstanceEnvironment_OverridesSharedSettings()
	{
		using SettingsFixture fixture = new();
		fixture.WriteUserSettings("""{"Mcp":{"InstanceName":"shared","InstanceDirectory":"C:\\shared"}}""");
		McpSettings settings = fixture.Load(name => name switch
		{
			"MCP_INSTANCE_NAME" => "game-a",
			"MCP_INSTANCE_DIRECTORY" => fixture.Root,
			_ => null
		});
		Assert.Equal("game-a", settings.Discovery.InstanceName);
		Assert.Equal(fixture.Root, settings.Discovery.InstanceDirectory);
		Assert.Equal(0, settings.Backend.Port);
	}

	[Theory]
	[InlineData("Debug", LogLevel.Debug)]
	[InlineData("trace", LogLevel.Trace)]
	[InlineData("Warning", LogLevel.Warning)]
	[InlineData("None", LogLevel.None)]
	public void Load_LogMinimumLevel_OverridesTheInformationDefault(string value, LogLevel expected)
	{
		using SettingsFixture fixture = new();
		fixture.CopyExampleSettings();
		fixture.WriteUserSettings(
			JsonSerializer.Serialize(new
			{
				Mcp = new
				{
					Logging = new
					{
						MinimumLevel = value
					}
				}
			}));

		Assert.Equal(expected, fixture.Load().Logging.MinimumLevel);
	}

	[Fact]
	public void Load_UndefinedLogLevelNumber_IsRejected()
	{
		using SettingsFixture fixture = new();
		fixture.WriteUserSettings("""{"Mcp":{"Logging":{"MinimumLevel":"42"}}}""");

		OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => fixture.Load());

		Assert.Equal(typeof(PluginLoggingOptions), exception.OptionsType);
		Assert.Contains(exception.Failures, failure => failure.StartsWith("Mcp:Logging:MinimumLevel must be",
			StringComparison.Ordinal));
	}

	[Fact]
	public void Load_UnknownLogLevelName_FailsInsteadOfFallingBackToTheDefault()
	{
		using SettingsFixture fixture = new();
		fixture.WriteUserSettings("""{"Mcp":{"Logging":{"MinimumLevel":"Verbose"}}}""");

		Assert.Throws<InvalidOperationException>(() => fixture.Load());
	}

	private sealed record McpSettings(
		McpBackendOptions Backend,
		McpDiscoveryOptions Discovery,
		McpFeatureOptions Features,
		PluginLoggingOptions Logging,
		McpExecutionOptions Execution);

	private sealed class SettingsFixture : IDisposable
	{
		public SettingsFixture()
		{
			Directory.CreateDirectory(PluginDirectory);
			Directory.CreateDirectory(UserDirectory);
		}

		public string Root
		{
			get;
		} = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");

		private string PluginDirectory => Path.Combine(Root, "plugin");
		private string UserDirectory => Path.Combine(Root, "CheatEngine.Mcp");

		public void Dispose()
		{
			Directory.Delete(Root, true);
		}

		public void CopyExampleSettings()
		{
			File.Copy(Path.Combine(RepositoryPaths.Root, "srcs", "CheatEngine.Mcp.Plugin", "appsettings.json"),
				Path.Combine(PluginDirectory, "appsettings.json"));
		}

		public void WritePluginSettings(string json)
		{
			File.WriteAllText(Path.Combine(PluginDirectory, "appsettings.json"), json);
		}

		public void WriteUserSettings(string json)
		{
			File.WriteAllText(Path.Combine(UserDirectory, "appsettings.json"), json);
		}

		/// <summary>Layers the sources as the plugin does and resolves the options through the backend registration.</summary>
		public McpSettings Load(Func<string, string?>? environment = null)
		{
			using ConfigurationManager configuration = new();
			configuration.AddCheatEngineMcpSettings(PluginDirectory,
				new McpPluginEnvironment(UserDirectory, environment ?? (static _ => null)));
			ServiceCollection services = new();
			services.AddCheatEngineMcpBackend(configuration).AddCheatEngineMcpExecution(configuration,
				CheatEngineMcpPluginBuilderExtensions.ReadFeatureOptions(configuration));
			using ServiceProvider provider = services.BuildServiceProvider();
			return new McpSettings(provider.GetRequiredService<IOptions<McpBackendOptions>>().Value,
				provider.GetRequiredService<IOptions<McpDiscoveryOptions>>().Value,
				provider.GetRequiredService<IOptions<McpFeatureOptions>>().Value,
				PluginLoggingOptions.Read(configuration),
				provider.GetRequiredService<IOptions<McpExecutionOptions>>().Value);
		}
	}
}
