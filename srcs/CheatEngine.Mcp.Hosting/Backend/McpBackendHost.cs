using System.Security.Cryptography;
using System.Text;

using CheatEngine.Mcp.Hosting.Configuration;
using CheatEngine.Mcp.Hosting.Discovery;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Hosting.Backend;

/// <summary>One activation's authenticated loopback MCP backend.</summary>
/// <remarks>
///     Only <see cref="McpBackendHostFactory" /> creates a host, always with its discovery publication and bearer token;
///     tests alone construct one without a publication.
/// </remarks>
internal sealed partial class McpBackendHost(
	McpBackendOptions options,
	IMcpBackendLogging logging,
	McpRuntimeInfo runtime,
	CheatEngineMcpPrimitiveOptions manifest,
	McpPrimitiveTargets targets,
	CancellationToken stopping,
	InstancePublication? publication = null) : IMcpBackendHost
{
	// The provider is the lease on the plugin's log sink: disposed once the web host stopped, never blocking.
	private readonly ILoggerProvider _logProvider = logging.CreateProvider();
	private readonly object _lifecycleGate = new();
	private WebApplication? _app;
	private Task? _startTask;
	private Task? _shutdownTask;
	private int _started;
	private int _logProviderDisposed;
	private int _stopping;

	/// <summary>Whether the listener started and is still accepting requests.</summary>
	public bool IsRunning => Volatile.Read(ref _started) != 0 && Volatile.Read(ref _stopping) == 0;

	/// <summary>The transport container while the backend runs.</summary>
	internal IServiceProvider? Services => _app?.Services;

	private ILogger Logger => field ??= _logProvider.CreateLogger(typeof(McpBackendHost).FullName!);

	/// <inheritdoc />
	public string? Endpoint
	{
		get;
		private set;
	}

	/// <inheritdoc />
	public Task StartAsync()
	{
		TaskCompletionSource<object?> start;
		lock (_lifecycleGate)
		{
			if (_startTask is not null || _shutdownTask is not null)
			{
				return Task.FromException(new InvalidOperationException("The MCP server can only be started once."));
			}

			start = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
			_startTask = start.Task;
		}

		_ = StartAndCleanUpAsync(start);
		return start.Task;
	}

	public void StopAccepting()
	{
		lock (_lifecycleGate)
		{
			Interlocked.Exchange(ref _stopping, 1);
			try
			{
				publication?.Dispose();
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				LogDiscoveryWithdrawalFailed(Logger, exception.GetType().Name);
			}
		}
	}

	/// <inheritdoc />
	public Task StopAsync()
	{
		lock (_lifecycleGate)
		{
			StopAccepting();
			return _shutdownTask ??= StopAfterStartAsync(_startTask);
		}
	}

	private async Task StartAndCleanUpAsync(TaskCompletionSource<object?> start)
	{
		try
		{
			await StartCoreAsync().ConfigureAwait(false);
			start.TrySetResult(null);
		}
		catch (Exception exception)
		{
			await CleanUpFailedStartAsync().ConfigureAwait(false);
			if (exception is OperationCanceledException cancellation)
			{
				start.TrySetCanceled(cancellation.CancellationToken);
			}
			else
			{
				start.TrySetException(exception);
			}
		}
	}

	private async Task CleanUpFailedStartAsync()
	{
		try
		{
			await StopCoreAsync().ConfigureAwait(false);
		}
		catch
		{
			// Preserve the startup failure, which is the lifecycle error the caller can act on.
		}
	}

	private async Task StopAfterStartAsync(Task? startTask)
	{
		if (startTask is not null)
		{
			try
			{
				await startTask.ConfigureAwait(false);
			}
			catch
			{
				// A failed start already releases its listener and logging lease.
			}
		}

		await StopCoreAsync().ConfigureAwait(false);
	}

	private async Task StartCoreAsync()
	{
		if (_app is not null || Volatile.Read(ref _stopping) != 0)
		{
			throw new InvalidOperationException("The MCP server has already started.");
		}

		// Checked again here: never bind a non-loopback or out-of-range address, whoever constructed the host.
		McpOptionsValidation.ThrowIfInvalid(new McpBackendOptionsValidator(), options);
		// An empty builder reads no appsettings, environment variables, command line or hosting startup assembly, so
		// the Cheat Engine process's ambient ASPNETCORE_*, DOTNET_* or Kestrel settings can never add an endpoint,
		// replace the loopback address or switch the environment.
		WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
		{
			ApplicationName = runtime.ApplicationName,
			ContentRootPath = Path.GetDirectoryName(runtime.RuntimeLocation),
			EnvironmentName = Environments.Production
		});
		// JSON escaping/hex encoding can expand the tools' 1 MiB payload bounds substantially.
		builder.WebHost.UseKestrelCore()
			.ConfigureKestrel(static kestrel => kestrel.Limits.MaxRequestBodySize = 8 * 1024 * 1024);
		builder.Services.AddRoutingCore();
		// Cheat Engine owns the process lifetime and Client Hosting owns the activation's.
		builder.Services.AddSingleton<IHostLifetime, ActivationHostLifetime>();
		// The web container never disposes this instance registration; StopCoreAsync does, after the host stopped.
		builder.Logging.SetMinimumLevel(logging.MinimumLevel).AddProvider(_logProvider).AddTokenSafeFloor();
		// The transport container holds no Client service and no primitive instance: tools are borrowed from the
		// activation scope, so HTTP can never construct another Client activation or dispose activation state.
		builder.Services.AddMcpServer(server =>
			{
				server.ServerInfo = new Implementation
				{
					Name = options.ServerName,
					Version = runtime.Version ?? "2.0.0"
				};
				server.ServerInstructions = manifest.BackendInstructions;
			})
			.WithHttpTransport(transport => transport.Stateless = true)
			.WithCheatEnginePrimitives(manifest, McpPrimitiveBinding.FromTargets(targets));
		WebApplication app = builder.Build();
		_app = app;
		// The only address source. Native hostfxr hosts can have an empty AppContext.BaseDirectory, and
		// WebHost.UseUrls unnecessarily resolves the absent web root against it; set the server addresses directly.
		app.Urls.Add(options.BaseUrl);
		app.Use(async (context, next) =>
		{
			if (Volatile.Read(ref _stopping) != 0 || stopping.IsCancellationRequested)
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
			// Publishing sets only the endpoint, so the identity is fixed before the listener starts. A RequestDelegate
			// with source-generated JSON needs neither reflection nor the Request Delegate Generator.
			InstanceIdentity identity = InstanceIdentity.From(publication.Descriptor);
			app.MapGet("/instance", WriteIdentityAsync);

			Task WriteIdentityAsync(HttpContext context)
			{
				return context.Response.WriteAsJsonAsync(identity, HostingJsonContext.Default.InstanceIdentity,
					cancellationToken: context.RequestAborted);
			}
		}

		app.MapMcp();
		await app.StartAsync(stopping).ConfigureAwait(false);
		string endpoint = app.Urls.Single();
		lock (_lifecycleGate)
		{
			if (Volatile.Read(ref _stopping) != 0 || stopping.IsCancellationRequested)
			{
				throw new OperationCanceledException("The MCP server stopped while it was starting.", stopping);
			}

			Endpoint = endpoint;
			publication?.Publish(endpoint);
			Volatile.Write(ref _started, 1);
		}
	}

	private async Task StopCoreAsync()
	{
		StopAccepting();
		WebApplication? app;
		lock (_lifecycleGate)
		{
			app = Interlocked.Exchange(ref _app, null);
		}
		if (app is null)
		{
			DisposeLogProvider();
			return;
		}

		try
		{
			using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
			await app.StopAsync(timeout.Token).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			LogListenerShutdownFailed(Logger, exception.GetType().Name);
		}
		finally
		{
			try
			{
				await app.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				LogHostDisposalFailed(Logger, exception.GetType().Name);
			}
			finally
			{
				DisposeLogProvider();
			}
		}
	}

	private void DisposeLogProvider()
	{
		if (Interlocked.Exchange(ref _logProviderDisposed, 1) == 0)
		{
			_logProvider.Dispose();
		}
	}

	[LoggerMessage(Level = LogLevel.Warning,
		Message = "Could not withdraw instance discovery ({FailureType}); the stopped backend will reject calls.")]
	private static partial void LogDiscoveryWithdrawalFailed(ILogger logger, string failureType);

	[LoggerMessage(Level = LogLevel.Warning, Message = "MCP listener shutdown failed ({FailureType}).")]
	private static partial void LogListenerShutdownFailed(ILogger logger, string failureType);

	[LoggerMessage(Level = LogLevel.Warning, Message = "MCP host disposal failed ({FailureType}).")]
	private static partial void LogHostDisposalFailed(ILogger logger, string failureType);
}
