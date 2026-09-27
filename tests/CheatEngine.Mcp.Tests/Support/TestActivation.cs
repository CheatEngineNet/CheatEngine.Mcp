using CheatEngine.Client;
using CheatEngine.Client.Modules;
using CheatEngine.Mcp.Plugin;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Support;

/// <summary>
///     The plugin's real activation services over a Client double, validated like Client Hosting validates them
///     (ValidateOnBuild and ValidateScopes) and resolved from one activation scope. Settings are <c>Mcp:*</c> keys.
/// </summary>
internal sealed class TestActivation : IDisposable
{
	private readonly ConfigurationManager _configuration = new();
	private readonly ServiceProvider _provider;
	private readonly IServiceScope _scope;
	private bool _scopeDisposed;

	internal TestActivation(ICheatEngineClient client, IReadOnlyDictionary<string, string?>? settings = null)
	{
		DataDirectory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		_configuration.AddInMemoryCollection(settings);
		ServiceCollection services = new();
		services.AddSingleton(client);
		ICheatEngineMcpBuilder builder = services.AddCheatEngineMcpServices(_configuration,
			new McpPluginEnvironment(DataDirectory, static _ => null));
		CheatEngineMcpPlugin.ComposePrimitives(builder);
		// What builder.Client.AddModule<McpServerModule>() registers in Client Hosting.
		services.TryAddEnumerable(ServiceDescriptor.Scoped<ICheatEngineClientModule, McpServerModule>());
		_provider = services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true, ValidateScopes = true
		});
		_scope = _provider.CreateScope();
	}

	internal string DataDirectory
	{
		get;
	}

	internal IServiceProvider Services => _scope.ServiceProvider;

	internal CheatEngineMcpPrimitiveOptions Manifest =>
		_provider.GetRequiredService<IOptions<CheatEngineMcpPrimitiveOptions>>().Value;

	internal McpPrimitiveTargets Targets => Services.GetRequiredService<McpPrimitiveTargets>();

	internal McpServerModule Module => (McpServerModule) Services.GetServices<ICheatEngineClientModule>().Single();

	internal PluginLog Log => _provider.GetRequiredService<PluginLog>();

	public void Dispose()
	{
		DisposeScope();
		_provider.Dispose();
		_configuration.Dispose();
		if (Directory.Exists(DataDirectory))
		{
			Directory.Delete(DataDirectory, true);
		}
	}

	/// <summary>Ends the activation scope, as Client Hosting does after its lease drain.</summary>
	internal void DisposeScope()
	{
		if (!_scopeDisposed)
		{
			_scopeDisposed = true;
			_scope.Dispose();
		}
	}
}
