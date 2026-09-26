using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Gateway;
using CheatEngine.Mcp.Instances;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests;

public sealed class GatewayServerTests
{
	[Fact]
	public async Task ListTools_WithoutPublishedInstances_ExposesStableRoutedCatalog()
	{
		await using GatewayTestHost host = await GatewayTestHost.StartAsync();
		await using McpClient client = await ConnectAsync(host.Endpoint);

		IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(140, tools.Count);
		Assert.Contains(tools, tool => tool.Name == GatewayToolCatalog.ListInstancesToolName);
		Assert.Equal(139, tools.Count(tool => tool.Name != GatewayToolCatalog.ListInstancesToolName));
		Assert.All(GatewayToolCatalog.GetTools().Where(tool => tool.Name != GatewayToolCatalog.ListInstancesToolName),
			tool => Assert.Contains(GatewayToolCatalog.InstanceIdArgumentName, tool.InputSchema.GetProperty("required")
				.EnumerateArray().Select(value => value.GetString())));
	}

	[Fact]
	public async Task CallTool_TwoPublishedInstances_RoutesOnlyToRequestedBackendAndStripsInstanceId()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend first = await FakeBackend.StartAsync(gateway.Registry, "first");
		await using FakeBackend second = await FakeBackend.StartAsync(gateway.Registry, "second");
		await using McpClient client = await ConnectAsync(gateway.Endpoint);

		JsonObject listed = await CallJsonAsync(client, GatewayToolCatalog.ListInstancesToolName,
			new Dictionary<string, object?>());
		string listedText = listed.ToJsonString();
		Assert.Contains(first.Descriptor.InstanceId, listedText, StringComparison.Ordinal);
		Assert.Contains(second.Descriptor.InstanceId, listedText, StringComparison.Ordinal);
		Assert.DoesNotContain(first.Descriptor.AccessToken, listedText, StringComparison.Ordinal);
		Assert.DoesNotContain(second.Descriptor.AccessToken, listedText, StringComparison.Ordinal);
		JsonArray entries = listed["instances"]!.AsArray();
		Assert.Equal(first.Descriptor.InstanceId, entries[0]!["instanceId"]!.GetValue<string>());
		Assert.Equal("first", entries[0]!["name"]!.GetValue<string>());
		Assert.Equal(first.Descriptor.ProcessId, entries[0]!["processId"]!.GetValue<int>());

		JsonObject firstResult = await CallJsonAsync(client, "get_plugin_version", new Dictionary<string, object?>
		{
			[GatewayToolCatalog.InstanceIdArgumentName] = first.Descriptor.InstanceId,
			["marker"] = "first payload"
		});
		JsonObject secondResult = await CallJsonAsync(client, "get_plugin_version", new Dictionary<string, object?>
		{
			[GatewayToolCatalog.InstanceIdArgumentName] = second.Descriptor.InstanceId,
			["marker"] = "second payload"
		});

		Assert.Equal(first.Descriptor.InstanceId, firstResult["backend"]?.GetValue<string>());
		Assert.Equal(second.Descriptor.InstanceId, secondResult["backend"]?.GetValue<string>());
		Assert.Equal("first payload", first.LastArguments["marker"].GetString());
		Assert.Equal("second payload", second.LastArguments["marker"].GetString());
		Assert.DoesNotContain(GatewayToolCatalog.InstanceIdArgumentName, first.LastArguments.Keys);
		Assert.DoesNotContain(GatewayToolCatalog.InstanceIdArgumentName, second.LastArguments.Keys);
		Assert.Equal(1, first.ForwardedCallCount);
		Assert.Equal(1, second.ForwardedCallCount);
	}

	[Fact]
	public async Task CallTool_MissingUnknownOrStaleInstance_DoesNotForward()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "only");
		await using McpClient client = await ConnectAsync(gateway.Endpoint);

		CallToolResult missing = await client.CallToolAsync("get_plugin_version", new Dictionary<string, object?>(),
			cancellationToken: TestContext.Current.CancellationToken);
		CallToolResult unknown = await client.CallToolAsync("get_plugin_version", new Dictionary<string, object?>
		{
			[GatewayToolCatalog.InstanceIdArgumentName] = "ce-404-00000000000000000000000000000000"
		}, cancellationToken: TestContext.Current.CancellationToken);
		backend.ReportedIdentity = backend.Descriptor with
		{
			ActivationId = Guid.NewGuid()
		};
		CallToolResult stale = await client.CallToolAsync("get_plugin_version", new Dictionary<string, object?>
		{
			[GatewayToolCatalog.InstanceIdArgumentName] = backend.Descriptor.InstanceId
		}, cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(missing.IsError);
		Assert.True(unknown.IsError);
		Assert.True(stale.IsError);
		Assert.Equal(0, backend.ForwardedCallCount);
	}

	[Fact]
	public async Task CallTool_CancelledForward_DoesNotRetryAnotherInstance()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend selected = await FakeBackend.StartAsync(gateway.Registry, "selected");
		await using FakeBackend other = await FakeBackend.StartAsync(gateway.Registry, "other");
		await using McpClient client = await ConnectAsync(gateway.Endpoint);
		selected.DelayForwarding = true;
		using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));

		Task<CallToolResult> call = client.CallToolAsync("get_plugin_version", new Dictionary<string, object?>
		{
			[GatewayToolCatalog.InstanceIdArgumentName] = selected.Descriptor.InstanceId
		}, cancellationToken: cancellation.Token).AsTask();
		await selected.WaitUntilForwardedAsync(TestContext.Current.CancellationToken);
		cancellation.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await call);
		Assert.Equal(1, selected.ForwardedCallCount);
		Assert.Equal(0, other.ForwardedCallCount);
	}

	private static async Task<McpClient> ConnectAsync(string endpoint) => await McpClient.CreateAsync(new HttpClientTransport(
		new HttpClientTransportOptions
		{
			Endpoint = new Uri(endpoint),
			Name = "CheatEngine.Mcp.Gateway.Tests",
			TransportMode = HttpTransportMode.StreamableHttp,
			ConnectionTimeout = TimeSpan.FromSeconds(10)
		}), new McpClientOptions
		{
			ClientInfo = new Implementation { Name = "CheatEngine.Mcp.Gateway.Tests", Version = "2.0.0" },
			Capabilities = new ClientCapabilities(),
			ProtocolVersion = "2025-06-18"
		}, cancellationToken: TestContext.Current.CancellationToken);

	private static async Task<JsonObject> CallJsonAsync(McpClient client, string name,
		IReadOnlyDictionary<string, object?> arguments)
	{
		CallToolResult result = await client.CallToolAsync(name, arguments, cancellationToken: TestContext.Current.CancellationToken);
		Assert.NotEqual(true, result.IsError);
		Assert.True(result.StructuredContent is JsonElement);
		return JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!.AsObject();
	}

	private sealed class GatewayTestHost : IAsyncDisposable
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

		internal static async Task<GatewayTestHost> StartAsync()
		{
			string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Gateway.{Guid.NewGuid():N}");
			WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
			builder.Logging.ClearProviders();
			GatewayServer server = new(new InstanceRegistry(directory));
			Microsoft.Extensions.DependencyInjection.IMcpServerBuilder mcpServer = builder.Services.AddMcpServer()
				.WithHttpTransport(transport => transport.Stateless = true);
			server.Configure(mcpServer);
			WebApplication application = builder.Build();
			application.Urls.Add(NewLoopbackEndpoint());
			application.MapMcp();
			await application.StartAsync(TestContext.Current.CancellationToken);
			return new GatewayTestHost(application, directory);
		}

		public async ValueTask DisposeAsync()
		{
			await _application.StopAsync(TestContext.Current.CancellationToken);
			await _application.DisposeAsync();
			if (Directory.Exists(_directory))
			{
				Directory.Delete(_directory, recursive: true);
			}
		}
	}

	private sealed class FakeBackend : IAsyncDisposable
	{
		private readonly WebApplication _application;
		private readonly InstancePublication _publication;

		private FakeBackend(WebApplication application, InstancePublication publication)
		{
			_application = application;
			_publication = publication;
			ReportedIdentity = publication.Descriptor;
		}

		internal InstanceDescriptor Descriptor => _publication.Descriptor;
		internal int ForwardedCallCount
		{
			get; private set;
		}
		internal Dictionary<string, JsonElement> LastArguments { get; } = new(StringComparer.Ordinal);
		internal InstanceDescriptor ReportedIdentity
		{
			get; set;
		}
		internal bool DelayForwarding
		{
			get; set;
		}
		private readonly TaskCompletionSource _forwarded = new(TaskCreationOptions.RunContinuationsAsynchronously);

		internal static async Task<FakeBackend> StartAsync(InstanceRegistry registry, string name)
		{
			InstancePublication publication = new(registry, name);
			WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
			builder.Logging.ClearProviders();
			FakeBackend? backend = null;
			Microsoft.Extensions.DependencyInjection.IMcpServerBuilder mcpServer = builder.Services.AddMcpServer()
				.WithHttpTransport(transport => transport.Stateless = true)
				.WithListToolsHandler(static (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = [] }))
				.WithCallToolHandler((context, cancellationToken) => backend!.ForwardAsync(context.Params, cancellationToken));
			WebApplication application = builder.Build();
			application.Use(async (context, next) =>
			{
				if (!string.Equals(context.Request.Headers.Authorization, $"Bearer {publication.Descriptor.AccessToken}", StringComparison.Ordinal))
				{
					context.Response.StatusCode = StatusCodes.Status401Unauthorized;
					return;
				}
				await next(context);
			});
			application.MapGet("/instance", () => Results.Json(backend!.ReportedIdentity));
			application.MapMcp();
			application.Urls.Add(NewLoopbackEndpoint());
			backend = new FakeBackend(application, publication);
			await application.StartAsync(TestContext.Current.CancellationToken);
			publication.Publish(backend._application.Urls.Single());
			return backend;
		}

		internal Task WaitUntilForwardedAsync(CancellationToken cancellationToken) => _forwarded.Task.WaitAsync(cancellationToken);

		private async ValueTask<CallToolResult> ForwardAsync(CallToolRequestParams request, CancellationToken cancellationToken)
		{
			ForwardedCallCount++;
			_forwarded.TrySetResult();
			LastArguments.Clear();
			foreach ((string key, JsonElement value) in request.Arguments ?? new Dictionary<string, JsonElement>())
			{
				LastArguments[key] = value;
			}
			if (DelayForwarding)
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			}
			return new CallToolResult
			{
				StructuredContent = JsonSerializer.SerializeToElement(new
				{
					backend = Descriptor.InstanceId,
					arguments = LastArguments.ToDictionary(pair => pair.Key, pair => JsonNode.Parse(pair.Value.GetRawText()))
				})
			};
		}

		public async ValueTask DisposeAsync()
		{
			_publication.Dispose();
			await _application.StopAsync(TestContext.Current.CancellationToken);
			await _application.DisposeAsync();
		}
	}

	private static string NewLoopbackEndpoint()
	{
		using TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint) listener.LocalEndpoint).Port;
		return $"http://127.0.0.1:{port}/";
	}
}
