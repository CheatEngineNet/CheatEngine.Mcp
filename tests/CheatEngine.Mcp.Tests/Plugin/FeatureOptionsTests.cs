using System.Globalization;
using System.Reflection;
using System.Text.Json;

using CheatEngine.Client.Assembly;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Lua;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Plugin;
using CheatEngine.Mcp.Plugin.Logging;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Plugin;

/// <summary>The plugin composition reads the switches once and drives the Client opt-ins and the gates from them.</summary>
[Collection(nameof(SerialTestGroup))]
public sealed class FeatureOptionsTests
{
	[Theory]
	[InlineData(true, true, true, true)]
	[InlineData(false, true, false, true)]
	[InlineData(true, false, true, false)]
	[InlineData(false, false, false, false)]
	public void ClientOptIns_FollowTheSameFeatureInstance(bool enableLua, bool enableAssembler,
		bool enableCodeExecution, bool enableKernel)
	{
		string dataDirectory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(dataDirectory);
		using PluginLogSink log = new();
		try
		{
			File.WriteAllText(Path.Combine(dataDirectory, McpPluginConfigurationBuilderExtensions.SettingsFileName),
				JsonSerializer.Serialize(new
				{
					Mcp = new
					{
						EnableUnsafeLua = enableLua,
						EnableAutoAssembler = enableAssembler,
						EnableTargetCodeExecution = enableCodeExecution,
						EnableKernelAccess = enableKernel
					}
				}));
			// Client Hosting creates one builder per enable through its internal constructor.
			CheatEnginePluginBuilder builder = (CheatEnginePluginBuilder) Activator.CreateInstance(
				typeof(CheatEnginePluginBuilder), BindingFlags.Instance | BindingFlags.NonPublic, null,
				[typeof(CheatEngineMcpPlugin).Assembly], CultureInfo.InvariantCulture)!;

			builder.AddCheatEngineMcp(new McpPluginEnvironment(dataDirectory, static _ => null), log);

			ServiceDescriptor registration = Assert.Single(builder.Services,
				static descriptor => descriptor.ServiceType == typeof(IOptions<McpFeatureOptions>));
			McpFeatureOptions features =
				Assert.IsAssignableFrom<IOptions<McpFeatureOptions>>(registration.ImplementationInstance).Value;
			Assert.DoesNotContain(builder.Services, static descriptor =>
				descriptor.ServiceType == typeof(IConfigureOptions<McpFeatureOptions>));
			Assert.Equal((enableLua, enableAssembler, enableCodeExecution, enableKernel),
				(features.EnableUnsafeLua, features.EnableAutoAssembler, features.EnableTargetCodeExecution,
					features.EnableKernelAccess));
			Assert.Equal(features.EnableUnsafeLua,
				builder.Services.Any(static descriptor => descriptor.ServiceType == typeof(IUnsafeLuaClient)));
			Assert.Equal(features.EnableAutoAssembler,
				builder.Services.Any(static descriptor => descriptor.ServiceType == typeof(IAutoAssemblerClient)));
			Assert.Contains(builder.Services, static descriptor =>
				descriptor.ServiceType == typeof(McpFeatureGate) && descriptor.Lifetime == ServiceLifetime.Scoped);
			Assert.Contains(builder.Services, static descriptor =>
				descriptor.ServiceType == typeof(ToolDispatch) && descriptor.Lifetime == ServiceLifetime.Scoped);
		}
		finally
		{
			Directory.Delete(dataDirectory, true);
		}
	}

	[Fact]
	public void ReadFeatureOptions_AbsentSection_EnablesEverySwitch()
	{
		using ConfigurationManager configuration = new();

		McpFeatureOptions features = CheatEngineMcpPluginBuilderExtensions.ReadFeatureOptions(configuration);

		Assert.Equal(new McpFeatureSummary(true, true, true, true),
			new McpFeatureGate(Options.Create(features)).Snapshot());
	}
}
