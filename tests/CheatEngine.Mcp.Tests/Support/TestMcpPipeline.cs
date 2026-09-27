using System.IO.Pipelines;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Support;

/// <summary>
///     A real MCP server composed with <c>WithCheatEnginePrimitives</c> and a real client, connected through in-memory
///     pipes, so tests observe exactly what goes over the wire.
/// </summary>
internal sealed class TestMcpPipeline : IAsyncDisposable
{
	internal const string DefaultProtocolVersion = "2025-06-18";
	private readonly McpServer _server;
	private readonly Task _serverRun;

	private readonly ServiceProvider _services;
	private readonly CancellationTokenSource _stop;

	private TestMcpPipeline(ServiceProvider services, McpServer server, Task serverRun, CancellationTokenSource stop,
		McpClient client)
	{
		_services = services;
		_server = server;
		_serverRun = serverRun;
		_stop = stop;
		Client = client;
	}

	internal McpClient Client
	{
		get;
	}

	/// <summary>The manifest of the contract probes, with their JSON metadata registered.</summary>
	internal static CheatEngineMcpPrimitiveOptions ProbeManifest
	{
		get;
	} = CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog, static builder => builder
		.AddToolType<ContractProbeTool>().AddResourceType<ContractProbeResource>()
		.AddJsonTypeInfoResolver(TestJsonContext.Default));

	public async ValueTask DisposeAsync()
	{
		await Client.DisposeAsync();
		await _stop.CancelAsync();
		try
		{
			await _serverRun;
		}
		catch (OperationCanceledException)
		{
			// The server stops through its token.
		}

		await _server.DisposeAsync();
		_stop.Dispose();
		await _services.DisposeAsync();
	}

	internal static Task<TestMcpPipeline> StartAsync(string protocolVersion = DefaultProtocolVersion,
		bool strictJson = false)
	{
		// The probes are static, so the catalog binding invokes them without any activation target.
		return StartAsync(ProbeManifest, McpPrimitiveBinding.Catalog, protocolVersion, strictJson);
	}

	/// <summary>Serves a manifest through a binding, such as an activation's live targets.</summary>
	internal static async Task<TestMcpPipeline> StartAsync(CheatEngineMcpPrimitiveOptions manifest,
		McpPrimitiveBinding binding, string protocolVersion = DefaultProtocolVersion, bool strictJson = false)
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddMcpServer().WithCheatEnginePrimitives(manifest, binding, strictJson);
		ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true, ValidateScopes = true
		});
		CancellationTokenSource stop = new();
		try
		{
			ILoggerFactory loggers = provider.GetRequiredService<ILoggerFactory>();
			McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
			Pipe clientToServer = new();
			Pipe serverToClient = new();
			McpServer server = McpServer.Create(
				new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream(),
					"CheatEngine.Mcp.Tests", loggers), options, loggers, provider);
			Task serverRun = server.RunAsync(stop.Token);
			McpClient client = await McpClient.CreateAsync(
				new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream(), loggers),
				new McpClientOptions
				{
					ClientInfo = new Implementation { Name = "CheatEngine.Mcp.Tests", Version = "2.0.0" },
					ProtocolVersion = protocolVersion
				}, loggers, TestContext.Current.CancellationToken);
			return new TestMcpPipeline(provider, server, serverRun, stop, client);
		}
		catch
		{
			await stop.CancelAsync();
			stop.Dispose();
			await provider.DisposeAsync();
			throw;
		}
	}

	/// <summary>Calls a tool with arguments written as one JSON object, so tests control the exact wire values.</summary>
	internal async Task<CallToolResult> CallAsync(string tool, string argumentsJson = "{}")
	{
		using JsonDocument arguments = JsonDocument.Parse(argumentsJson);
		Dictionary<string, JsonElement> values = arguments.RootElement.EnumerateObject()
			.ToDictionary(static property => property.Name, static property => property.Value.Clone(),
				StringComparer.Ordinal);
		return await Client.CallToolAsync(new CallToolRequestParams { Name = tool, Arguments = values },
			TestContext.Current.CancellationToken);
	}

	/// <summary>Asserts the failure shape and returns the envelope's error.</summary>
	internal static ToolError AssertError(CallToolResult result, ToolErrorKind kind)
	{
		Assert.True(result.IsError);
		Assert.Null(result.StructuredContent);
		Assert.True(ToolErrorResults.TryRead(result, out ToolError? error),
			"The only content of a failed call must be the {\"error\":{...}} envelope.");
		Assert.Equal(kind, error.Kind);
		return error;
	}
}
