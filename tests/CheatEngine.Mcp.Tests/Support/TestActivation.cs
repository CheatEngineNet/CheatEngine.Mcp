using CheatEngine.Client;
using CheatEngine.Client.Modules;
using CheatEngine.Mcp.Plugin;
using CheatEngine.Mcp.Plugin.Logging;

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
	private readonly PluginLogSink _log = new();
	private readonly ServiceProvider _provider;
	private readonly IServiceScope _scope;
	private bool _ended;
	private bool _scopeDisposed;

	internal TestActivation(ICheatEngineClient client, IReadOnlyDictionary<string, string?>? settings = null)
	{
		DataDirectory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		_configuration.AddInMemoryCollection(settings);
		ServiceCollection services = new();
		services.AddSingleton(client);
		// The plugin instance's sink in production: it outlives the activation and its backends.
		// The plugin assembly beside the test output stands for the folder Cheat Engine loaded it from.
		ICheatEngineMcpBuilder builder = services.AddCheatEngineMcpServices(_configuration,
			new McpPluginEnvironment(DataDirectory, static _ => null), _log, AppContext.BaseDirectory);
		// What AddCheatEngineMcp adds after the services, with the switches it read once for the Client opt-ins.
		builder.AddCheatEngineMcpExecution(_configuration,
			CheatEngineMcpPluginBuilderExtensions.ReadFeatureOptions(_configuration));
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
		End();
		// A backend still stopping holds the last reference; the writer then closes the file in the data directory.
		if (!_log.Drained.Wait(TimeSpan.FromSeconds(30)))
		{
			throw new TimeoutException("The plugin log was not released and drained.");
		}

		if (Directory.Exists(DataDirectory))
		{
			Directory.Delete(DataDirectory, true);
		}
	}

	/// <summary>Ends the activation, waits until every holder released the plugin log, then reads it.</summary>
	/// <returns>The log text, or an empty text when nothing was logged.</returns>
	internal async Task<string> EndAndReadLogAsync()
	{
		End();
		await _log.Drained.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
		string path = PluginLogSink.FilePathIn(DataDirectory);
		return File.Exists(path)
			? await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)
			: string.Empty;
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

	/// <summary>Disposes the scope, the provider and the configuration as Client Hosting does, then the test's sink.</summary>
	private void End()
	{
		if (_ended)
		{
			return;
		}

		_ended = true;
		DisposeScope();
		_provider.Dispose();
		_configuration.Dispose();
		_log.Dispose();
	}
}
