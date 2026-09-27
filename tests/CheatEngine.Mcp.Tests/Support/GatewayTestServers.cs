using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Support;

/// <summary>
///     The gateway's routing services behind a stateless streamable HTTP listener, so tests drive the real router
///     with any protocol version over real loopback transport.
/// </summary>
internal sealed class GatewayTestHost : IAsyncDisposable
{
	private readonly WebApplication _application;
	private readonly string _directory;

	private GatewayTestHost(WebApplication application, string directory)
	{
		_application = application;
		_directory = directory;
		Registry = new InstanceRegistry(directory);
	}

	internal string Endpoint => _application.Urls.Single();

	internal InstanceRegistry Registry
	{
		get;
	}

	internal IServiceProvider Services => _application.Services;

	public async ValueTask DisposeAsync()
	{
		await _application.StopAsync(TestContext.Current.CancellationToken);
		await _application.DisposeAsync();
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, true);
		}
	}

	/// <summary>Starts a gateway whose registry is a fresh temporary directory.</summary>
	/// <param name="callTimeout">The per-call timeout; the production default when omitted.</param>
	/// <param name="logs">A provider that receives every gateway log line at <paramref name="minimumLevel" />.</param>
	/// <param name="minimumLevel">The gateway's minimum log level.</param>
	/// <param name="extraPrimitives">Primitives composed after the production ones, such as a routed resource probe.</param>
	internal static async Task<GatewayTestHost> StartAsync(TimeSpan? callTimeout = null, ILoggerProvider? logs = null,
		LogLevel minimumLevel = LogLevel.Information, IEnumerable<CheatEngineMcpPrimitive>? extraPrimitives = null)
	{
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Gateway.{Guid.NewGuid():N}");
		WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
		builder.Logging.ClearProviders();
		if (logs is not null)
		{
			builder.Logging.SetMinimumLevel(minimumLevel).AddProvider(logs);
		}

		builder.Services.AddMcpServer().WithHttpTransport(transport => transport.Stateless = true);
		builder.Services.AddCheatEngineGatewayRouting(
			new GatewayOptions(directory, callTimeout ?? GatewayOptions.DefaultCallTimeout));
		// The production floor: the stdio composition applies it too.
		builder.Logging.AddTokenSafeFloor();
		builder.Services.Configure<CheatEngineMcpPrimitiveOptions>(manifest =>
		{
			foreach (CheatEngineMcpPrimitive primitive in TestComposition.GatewayManifest.Primitives
						 .Concat(extraPrimitives ?? []))
			{
				manifest.Add(primitive);
			}

			// The production composition couples primitive types with their source-generated JSON metadata. The test host
			// reconstructs that manifest so its strict catalog exercises the same metadata contract as the gateway.
			foreach (IJsonTypeInfoResolver resolver in TestComposition.GatewayManifest.JsonResolvers)
			{
				manifest.AddJsonResolver(resolver);
			}
		});
		WebApplication application = builder.Build();
		application.Urls.Add(LoopbackEndpoints.NewEndpoint());
		application.MapMcp();
		await application.StartAsync(TestContext.Current.CancellationToken);
		return new GatewayTestHost(application, directory);
	}

	/// <summary>Connects an upstream MCP client, as an AI client would.</summary>
	/// <param name="protocolVersion">The protocol version the upstream client requests.</param>
	/// <param name="loggers">The client's loggers, when a test captures them.</param>
	internal async Task<McpClient> ConnectAsync(string protocolVersion = "2025-06-18", ILoggerFactory? loggers = null)
	{
		return await McpClient.CreateAsync(new HttpClientTransport(
				new HttpClientTransportOptions
				{
					Endpoint = new Uri(Endpoint),
					Name = "CheatEngine.Mcp.Gateway.Tests",
					TransportMode = HttpTransportMode.StreamableHttp,
					ConnectionTimeout = TimeSpan.FromSeconds(10)
				}, loggers),
			new McpClientOptions
			{
				ClientInfo = new Implementation { Name = "CheatEngine.Mcp.Gateway.Tests", Version = "2.0.0" },
				Capabilities = new ClientCapabilities(),
				ProtocolVersion = protocolVersion
			}, loggers, TestContext.Current.CancellationToken);
	}
}

/// <summary>
///     A published backend double: it authenticates like the real backend, serves <c>/instance</c>, and records what
///     the gateway forwards (arguments, <c>_meta</c>, <c>initialize</c> handshakes and cancellation).
/// </summary>
internal sealed class FakeBackend : IAsyncDisposable
{
	private readonly WebApplication _application;
	private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _forwarded = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly InstanceRegistry _registry;
	private int _forwardedCalls;
	private int _identityProbes;
	private int _initializeRequests;
	private string? _recordPath;

	private FakeBackend(WebApplication application, InstanceRegistry registry, InstanceDescriptor descriptor)
	{
		_application = application;
		_registry = registry;
		Descriptor = descriptor;
		ReportedIdentity = descriptor;
	}

	/// <summary>The current registry record, including the token the backend accepts.</summary>
	internal InstanceDescriptor Descriptor
	{
		get;
		private set;
	}

	internal int ForwardedCallCount => Volatile.Read(ref _forwardedCalls);

	/// <summary>The <c>initialize</c> handshakes the backend received.</summary>
	internal int InitializeCount => Volatile.Read(ref _initializeRequests);

	/// <summary>The protocol version each <c>initialize</c> requested.</summary>
	internal ConcurrentQueue<string> InitializeProtocolVersions
	{
		get;
	} = new();

	internal Dictionary<string, JsonElement> LastArguments
	{
		get;
	} = new(StringComparer.Ordinal);

	/// <summary>The <c>_meta</c> of every forwarded <c>tools/call</c>, in arrival order; <see langword="null" /> when absent.</summary>
	internal ConcurrentQueue<JsonObject?> ReceivedMeta
	{
		get;
	} = new();

	/// <summary>What <c>/instance</c> reports; tests change it to simulate an identity mismatch.</summary>
	internal InstanceDescriptor ReportedIdentity
	{
		get;
		set;
	}

	/// <summary>Whether a forwarded call blocks until its request is cancelled.</summary>
	internal bool HangUntilCancelled
	{
		get;
		set;
	}

	/// <summary>Completes when a hanging call observed its cancellation.</summary>
	internal Task Cancelled => _cancelled.Task;

	/// <summary>Replaces the default echo result; it may also throw, as a failing backend handler would.</summary>
	internal Func<CallToolResult>? Respond
	{
		get;
		set;
	}

	/// <summary>Whether a forwarded call aborts its HTTP connection after the backend received it.</summary>
	internal bool AbortConnection
	{
		get;
		set;
	}

	/// <summary>The backend URI of every forwarded <c>resources/read</c>, in arrival order.</summary>
	internal ConcurrentQueue<string> ReadUris
	{
		get;
	} = new();

	/// <summary>How many times <c>/instance</c> was probed.</summary>
	internal int IdentityProbeCount => Volatile.Read(ref _identityProbes);

	/// <summary>
	///     Replaces the default read result: the backend URI as JSON plus a nested <c>cheatengine://instance/…</c>
	///     content. It may throw, as a failing backend resource would.
	/// </summary>
	internal Func<string, ReadResourceResult>? RespondToRead
	{
		get;
		set;
	}

	public async ValueTask DisposeAsync()
	{
		Withdraw();
		await _application.StopAsync(TestContext.Current.CancellationToken);
		await _application.DisposeAsync();
	}

	/// <summary>The arguments a routed call to this backend needs.</summary>
	internal Dictionary<string, object?> RoutedArguments()
	{
		return new Dictionary<string, object?> { ["instanceId"] = Descriptor.InstanceId };
	}

	internal static async Task<FakeBackend> StartAsync(InstanceRegistry registry, string name)
	{
		using Process process = Process.GetCurrentProcess();
		Guid activation = Guid.NewGuid();
		InstanceDescriptor descriptor = new($"ce-{process.Id}-{activation:N}", name, activation, process.Id,
			process.StartTime.ToUniversalTime().Ticks, "http://127.0.0.1:0/", RandomNumberGenerator.GetHexString(64),
			"2.0.0");
		WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
		builder.Logging.ClearProviders();
		FakeBackend? backend = null;
		builder.Services.AddHttpContextAccessor();
		builder.Services.AddMcpServer()
			.WithHttpTransport(transport => transport.Stateless = true)
			.WithMessageFilters(filters => filters.AddIncomingFilter(next => async (context, cancellationToken) =>
			{
				if (context.JsonRpcMessage is JsonRpcRequest { Method: RequestMethods.Initialize } initialize)
				{
					Interlocked.Increment(ref backend!._initializeRequests);
					backend.InitializeProtocolVersions.Enqueue(
						initialize.Params?["protocolVersion"]?.GetValue<string>() ?? string.Empty);
				}

				await next(context, cancellationToken);
			}))
			.WithListToolsHandler(static (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = [] }))
			.WithCallToolHandler((context, cancellationToken) => backend!.ForwardAsync(context.Params!,
				cancellationToken))
			.WithReadResourceHandler((context, _) => ValueTask.FromResult(backend!.Read(context.Params!.Uri)));
		WebApplication application = builder.Build();
		application.Use(async (context, next) =>
		{
			if (!string.Equals(context.Request.Headers.Authorization, $"Bearer {backend!.Descriptor.AccessToken}",
					StringComparison.Ordinal))
			{
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				return;
			}

			await next(context);
		});
		application.MapGet("/instance", context =>
		{
			Interlocked.Increment(ref backend!._identityProbes);
			return context.Response.WriteAsJsonAsync(InstanceIdentity.From(backend.ReportedIdentity),
				HostingJsonContext.Default.InstanceIdentity, cancellationToken: context.RequestAborted);
		});
		application.MapMcp();
		application.Urls.Add(LoopbackEndpoints.NewEndpoint());
		backend = new FakeBackend(application, registry, descriptor);
		await application.StartAsync(TestContext.Current.CancellationToken);
		backend.Descriptor = descriptor with
		{
			Endpoint = new Uri(application.Urls.Single()).AbsoluteUri
		};
		backend.ReportedIdentity = backend.Descriptor;
		backend._recordPath = registry.Publish(backend.Descriptor);
		return backend;
	}

	/// <summary>Rewrites this activation's registry record with a new token, as a changed record would appear.</summary>
	internal void RepublishWithNewToken()
	{
		Withdraw();
		Descriptor = Descriptor with
		{
			AccessToken = RandomNumberGenerator.GetHexString(64)
		};
		_recordPath = _registry.Publish(Descriptor);
	}

	/// <summary>Stops the listener but keeps the registry record, as a crashed or unloaded backend would.</summary>
	internal async Task StopListeningAsync()
	{
		await _application.StopAsync(TestContext.Current.CancellationToken);
	}

	internal Task WaitUntilForwardedAsync(CancellationToken cancellationToken)
	{
		return _forwarded.Task.WaitAsync(cancellationToken);
	}

	private void Withdraw()
	{
		string? path = Interlocked.Exchange(ref _recordPath, null);
		if (path is not null)
		{
			File.Delete(path);
		}
	}

	private ReadResourceResult Read(string uri)
	{
		ReadUris.Enqueue(uri);
		if (RespondToRead is { } respond)
		{
			return respond(uri);
		}

		return new ReadResourceResult
		{
			Contents =
			[
				new TextResourceContents
				{
					Uri = uri,
					MimeType = "application/json",
					Text = new JsonObject { ["backend"] = Descriptor.InstanceId, ["uri"] = uri }.ToJsonString()
				},
				new TextResourceContents
				{
					Uri = "cheatengine://instance/probes/nested", MimeType = "application/json", Text = "{}"
				}
			]
		};
	}

	private async ValueTask<CallToolResult> ForwardAsync(CallToolRequestParams request,
		CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _forwardedCalls);
		ReceivedMeta.Enqueue(request.Meta?.DeepClone().AsObject());
		lock (LastArguments)
		{
			LastArguments.Clear();
			foreach ((string key, JsonElement value) in request.Arguments ?? new Dictionary<string, JsonElement>())
			{
				LastArguments[key] = value.Clone();
			}
		}

		_forwarded.TrySetResult();
		if (AbortConnection)
		{
			_application.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.Abort();
		}

		if (HangUntilCancelled || AbortConnection)
		{
			try
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				_cancelled.TrySetResult();
				throw;
			}
		}

		if (Respond is { } respond)
		{
			return respond();
		}

		JsonObject arguments = new();
		lock (LastArguments)
		{
			foreach ((string key, JsonElement value) in LastArguments)
			{
				arguments[key] = JsonNode.Parse(value.GetRawText());
			}
		}

		JsonObject payload = new()
		{
			["backend"] = Descriptor.InstanceId,
			["arguments"] = arguments
		};
		using JsonDocument document = JsonDocument.Parse(payload.ToJsonString());
		return new CallToolResult
		{
			StructuredContent = document.RootElement.Clone(),
			Content = [new TextContentBlock { Text = payload.ToJsonString() }]
		};
	}
}

/// <summary>Free literal loopback endpoints for test listeners.</summary>
internal static class LoopbackEndpoints
{
	internal static string NewEndpoint()
	{
		using TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint) listener.LocalEndpoint).Port;
		return $"http://127.0.0.1:{port}/";
	}
}
