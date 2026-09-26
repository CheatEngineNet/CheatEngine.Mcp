using System.Security.Cryptography;
using System.Text;

using CheatEngine.Client;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Lua;
using CheatEngine.Mcp.Instances;
using CheatEngine.Mcp.Tools;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NLog.Extensions.Logging;

namespace CheatEngine.Mcp;

internal sealed class McpServer(ICheatEngineClient client, McpOptions options, PluginLog log,
	IUnsafeLuaClient? unsafeLua = null, IAutoAssemblerClient? autoAssembler = null, InstancePublication? publication = null)
{
	private WebApplication? _app;
	private int _stopping;
	private readonly IDisposable _logLease = log.Acquire();
	private readonly object _shutdownGate = new object();
	private Task? _shutdownTask;
	private bool _started;

	public bool IsRunning => _started && Volatile.Read(ref _stopping) == 0;
	internal string? Endpoint
	{
		get; private set;
	}

	public async Task StartAsync()
	{
		try
		{
			await StartCoreAsync().ConfigureAwait(false);
		}
		catch
		{
			await StopAsync().ConfigureAwait(false);
			throw;
		}
	}

	private async Task StartCoreAsync()
	{
		if (_app is not null || Volatile.Read(ref _stopping) != 0)
		{
			throw new InvalidOperationException("The MCP server has already started.");
		}
		options.Validate();
		WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			Args = [],
			ApplicationName = typeof(McpServer).Assembly.GetName().Name,
			ContentRootPath = Path.GetDirectoryName(typeof(McpServer).Assembly.Location)
		});
		builder.Logging.ClearProviders();
		builder.Logging.AddNLog(new NLogProviderOptions { ShutdownOnDispose = false }, _ => log.Factory);
		// Register the activation's instances; HTTP must never construct another Client provider or epoch.
		builder.Services.AddSingleton(client);
		builder.Services.AddSingleton<TargetResources>();
		builder.Services.AddSingleton<LuaDebuggerCaptureGuard>();
		if (unsafeLua is not null)
		{
			builder.Services.AddSingleton(unsafeLua);
		}
		if (autoAssembler is not null)
		{
			builder.Services.AddSingleton(autoAssembler);
		}
		IMcpServerBuilder mcp = builder.Services.AddMcpServer(server =>
		{
			server.ServerInfo = new()
			{
				Name = options.ServerName,
				Version = typeof(McpServer).Assembly.GetName().Version?.ToString() ?? "2.0.0"
			};
		})
		.WithHttpTransport(transport => transport.Stateless = true)
		;
		ToolCatalog.Configure(mcp);
		// JSON escaping/hex encoding can expand the tools' 1 MiB payload bounds substantially.
		builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 8 * 1024 * 1024);
		WebApplication app = builder.Build();
		_app = app;
		// Native hostfxr hosts can have an empty AppContext.BaseDirectory. WebHost.UseUrls
		// unnecessarily resolves the absent web root against it; set the server addresses directly.
		app.Urls.Add(options.BaseUrl);
		app.Use(async (context, next) =>
		{
			if (Volatile.Read(ref _stopping) != 0 || client.Stopping.IsCancellationRequested)
			{
				context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
				return;
			}
			if (publication is not null && !CryptographicOperations.FixedTimeEquals(
				Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString()),
				Encoding.UTF8.GetBytes("Bearer " + publication.Descriptor.AccessToken)))
			{
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				return;
			}
			await next(context).ConfigureAwait(false);
		});
		if (publication is not null)
		{
			app.MapGet("/instance", () => new
			{
				publication.Descriptor.InstanceId,
				publication.Descriptor.ActivationId,
				publication.Descriptor.ProcessId,
				publication.Descriptor.ProcessStartUtcTicks,
				publication.Descriptor.PluginVersion
			});
		}
		app.MapMcp();
		await app.StartAsync(client.Stopping).ConfigureAwait(false);
		Endpoint = app.Urls.Single();
		publication?.Publish(Endpoint);
		_started = true;
	}

	public void StopAccepting()
	{
		Interlocked.Exchange(ref _stopping, 1);
		try
		{
			publication?.Dispose();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			log.Factory.GetCurrentClassLogger().Warn(exception, "Could not withdraw instance discovery; the stopped backend will reject calls.");
		}
	}

	public Task StopAsync()
	{
		lock (_shutdownGate)
		{
			return _shutdownTask ??= StopCoreAsync();
		}
	}

	private async Task StopCoreAsync()
	{
		StopAccepting();
		WebApplication? app = Interlocked.Exchange(ref _app, null);
		if (app is null)
		{
			_logLease.Dispose();
			return;
		}
		try
		{
			using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			await app.StopAsync(timeout.Token).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			log.Factory.GetCurrentClassLogger().Warn(exception, "MCP listener shutdown failed.");
		}
		finally
		{
			try
			{
				await app.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				log.Factory.GetCurrentClassLogger().Warn(exception, "MCP host disposal failed.");
			}
			finally
			{
				_logLease.Dispose();
			}
		}
	}
}
