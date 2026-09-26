using System.Text.Json;
using System.Text.Json.Nodes;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests;

internal interface ILiveMcpToolClient
{
	public Task<JsonNode?> CallToolAsync(string name, IReadOnlyDictionary<string, object?>? arguments = null);
}

internal sealed class LiveMcpClient : IAsyncDisposable, ILiveMcpToolClient
{
	public const string ProtocolVersion = "2025-06-18";

	private readonly McpClient client;
	private readonly CancellationToken cancellationToken;

	private LiveMcpClient(McpClient client, CancellationToken cancellationToken)
	{
		this.client = client;
		this.cancellationToken = cancellationToken;
	}

	public string NegotiatedProtocolVersion => client.NegotiatedProtocolVersion
		?? throw new InvalidOperationException("The MCP client did not report a negotiated protocol version.");

	public static Task<LiveMcpClient> ConnectAsync(string serverUrl, CancellationToken cancellationToken = default) => ConnectAsync(new HttpClientTransport(new HttpClientTransportOptions
	{
		Endpoint = NormalizeEndpoint(serverUrl),
		Name = "CheatEngine.Mcp.Tests",
		TransportMode = HttpTransportMode.StreamableHttp,
		ConnectionTimeout = TimeSpan.FromSeconds(10)
	}), cancellationToken);

	public static Task<LiveMcpClient> ConnectGatewayAsync(string gatewayExecutable, string instanceDirectory, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(gatewayExecutable);
		if (!Path.IsPathFullyQualified(instanceDirectory))
		{
			throw new ArgumentException("The gateway instance directory must be absolute.", nameof(instanceDirectory));
		}
		return ConnectAsync(new StdioClientTransport(new StdioClientTransportOptions
		{
			Command = gatewayExecutable,
			Arguments = ["--instance-directory", instanceDirectory],
			Name = "CheatEngine.Mcp.LiveGateway",
			WorkingDirectory = Path.GetDirectoryName(gatewayExecutable),
			ShutdownTimeout = TimeSpan.FromSeconds(10)
		}), cancellationToken);
	}

	private static async Task<LiveMcpClient> ConnectAsync(IClientTransport transport, CancellationToken cancellationToken)
	{
		try
		{
			using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
			McpClient client = await McpClient.CreateAsync(transport, CreateOptions(), cancellationToken: timeout.Token);
			return new LiveMcpClient(client, cancellationToken);
		}
		catch
		{
			if (transport is IAsyncDisposable disposable)
			{
				await disposable.DisposeAsync();
			}
			else if (transport is IDisposable synchronousDisposable)
			{
				synchronousDisposable.Dispose();
			}
			throw;
		}
	}

	public async ValueTask<IList<McpClientTool>> ListToolsAsync()
	{
		using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
		return await client.ListToolsAsync(cancellationToken: timeout.Token);
	}

	public LiveMcpInstanceClient Bind(string instanceId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
		return new LiveMcpInstanceClient(this, instanceId);
	}

	public async Task<JsonNode?> CallToolAsync(string name, IReadOnlyDictionary<string, object?>? arguments = null)
	{
		using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
		CallToolResult result = await client.CallToolAsync(name, arguments ?? new Dictionary<string, object?>(), cancellationToken: timeout.Token);
		if (result.IsError == true)
		{
			throw new InvalidOperationException($"Tool '{name}' returned an MCP error: {FormatContent(result)}");
		}
		return ExtractToolPayload(result);
	}

	public ValueTask DisposeAsync() => client.DisposeAsync();

	private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
	{
		CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(15));
		return timeout;
	}

	private static McpClientOptions CreateOptions() => new()
	{
		ClientInfo = new Implementation { Name = "CheatEngine.Mcp.Tests", Version = "2.0.0" },
		Capabilities = new ClientCapabilities(),
		ProtocolVersion = ProtocolVersion
	};

	private static Uri NormalizeEndpoint(string serverUrl)
	{
		string endpoint = serverUrl.EndsWith('/') ? serverUrl : $"{serverUrl}/";
		return new Uri(endpoint, UriKind.Absolute);
	}

	private static JsonNode? ExtractToolPayload(CallToolResult result)
	{
		if (result.StructuredContent is JsonElement structuredContent)
		{
			return JsonNode.Parse(structuredContent.GetRawText());
		}
		string? text = result.Content.OfType<TextContentBlock>().Select(block => block.Text)
			.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
		return string.IsNullOrWhiteSpace(text) ? JsonSerializer.SerializeToNode(result) : ParseTextPayload(text);
	}

	private static JsonNode? ParseTextPayload(string text)
	{
		try
		{
			return JsonNode.Parse(text);
		}
		catch (JsonException) { return JsonValue.Create(text); }
	}

	private static string FormatContent(CallToolResult result)
	{
		string text = string.Join(Environment.NewLine, result.Content.OfType<TextContentBlock>().Select(block => block.Text)
			.Where(value => !string.IsNullOrWhiteSpace(value)));
		return string.IsNullOrWhiteSpace(text) ? JsonSerializer.Serialize(result) : text;
	}
}

internal sealed class LiveMcpInstanceClient(LiveMcpClient client, string instanceId) : ILiveMcpToolClient
{
	public string InstanceId { get; } = instanceId;

	public Task<JsonNode?> CallToolAsync(string name, IReadOnlyDictionary<string, object?>? arguments = null)
	{
		Dictionary<string, object?> routed = arguments is null
			? new Dictionary<string, object?>(StringComparer.Ordinal)
			: new Dictionary<string, object?>(arguments, StringComparer.Ordinal);
		if (!routed.TryAdd("instanceId", InstanceId))
		{
			throw new ArgumentException("Bound gateway calls must not replace their instanceId.", nameof(arguments));
		}
		return client.CallToolAsync(name, routed);
	}
}
