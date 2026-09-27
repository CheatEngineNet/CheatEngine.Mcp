using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using CheatEngine.Mcp.Hosting.Discovery;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>Routes each tool call to the one verified Cheat Engine instance its instanceId names.</summary>
internal sealed class GatewayRouter(InstanceRegistry registry, GatewayToolCatalog catalog)
{
	private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	internal ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> _, CancellationToken __)
	{
		return ValueTask.FromResult(new ListToolsResult { Tools = catalog.Tools.ToList() });
	}

	internal async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> context,
		CancellationToken cancellationToken)
	{
		CallToolRequestParams request = context.Params;
		if (string.Equals(request.Name, GatewayToolCatalog.ListInstancesToolName, StringComparison.Ordinal))
		{
			return await ListInstancesAsync(cancellationToken).ConfigureAwait(false);
		}

		if (!catalog.Tools.Any(tool => string.Equals(tool.Name, request.Name, StringComparison.Ordinal)))
		{
			return Error($"Tool '{request.Name}' is not available from this gateway.");
		}

		if (request.Arguments is null || !request.Arguments.TryGetValue(GatewayToolCatalog.InstanceIdArgumentName,
			    out JsonElement instanceIdValue) || instanceIdValue.ValueKind != JsonValueKind.String
		    || string.IsNullOrWhiteSpace(instanceIdValue.GetString()))
		{
			return Error("A non-empty instanceId returned by list_instances is required.");
		}

		InstanceDescriptor instance;
		try
		{
			instance = registry.Find(instanceIdValue.GetString()!);
			await ConfirmIdentityAsync(instance, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException
			                                  or JsonException
			                                  or TaskCanceledException && !cancellationToken.IsCancellationRequested)
		{
			return Error($"Cheat Engine instance is unavailable: {exception.Message}");
		}

		Dictionary<string, JsonElement> forwardedArguments = request.Arguments
			.Where(argument =>
				!string.Equals(argument.Key, GatewayToolCatalog.InstanceIdArgumentName, StringComparison.Ordinal))
			.ToDictionary(argument => argument.Key, argument => argument.Value, StringComparer.Ordinal);
		CallToolRequestParams forwardedRequest = new()
		{
			Name = request.Name,
			Arguments = forwardedArguments,
			Meta = request.Meta,
			InputResponses = request.InputResponses,
			RequestState = request.RequestState
		};
		try
		{
			await using HttpClientTransport transport = CreateTransport(instance);
			await using McpClient client = await McpClient
				.CreateAsync(transport, CreateClientOptions(), cancellationToken: cancellationToken)
				.ConfigureAwait(false);
			return await client.CallToolAsync(forwardedRequest, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is HttpRequestException or TimeoutException or TaskCanceledException
			                                  or ClientTransportClosedException &&
		                                  !cancellationToken.IsCancellationRequested)
		{
			return Error(
				$"Cheat Engine instance is unavailable while forwarding '{request.Name}': {exception.Message}");
		}
	}

	private async Task<CallToolResult> ListInstancesAsync(CancellationToken cancellationToken)
	{
		InstanceListEntry?[] discovered = [];
		bool discoveryIncomplete = false;
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(ConnectionTimeout);
		try
		{
			InstanceDescriptor[] instances = registry.ReadActive(deadline.Token).ToArray();
			discovered = new InstanceListEntry?[instances.Length];
			await Parallel.ForEachAsync(Enumerable.Range(0, instances.Length),
				new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = deadline.Token },
				async (index, token) =>
				{
					discovered[index] = await ProbeInstanceAsync(instances[index], token).ConfigureAwait(false);
				}).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (deadline.IsCancellationRequested &&
		                                         !cancellationToken.IsCancellationRequested)
		{
			// Return verified candidates within the discovery budget; never imply that a partial list is complete.
			discoveryIncomplete = true;
		}

		cancellationToken.ThrowIfCancellationRequested();
		InstanceListEntry[] available = discovered.Where(instance => instance is not null)
			.Select(instance => instance!).ToArray();
		JsonElement payload =
			JsonSerializer.SerializeToElement(new { instances = available, discoveryIncomplete }, JsonOptions);
		string textPayload = payload.GetRawText();
		return new CallToolResult
		{
			StructuredContent = payload, Content = [new TextContentBlock { Text = textPayload }]
		};
	}

	private static async Task<InstanceListEntry?> ProbeInstanceAsync(InstanceDescriptor instance,
		CancellationToken cancellationToken)
	{
		try
		{
			await ConfirmIdentityAsync(instance, cancellationToken).ConfigureAwait(false);
			return new InstanceListEntry(instance.InstanceId, instance.Name, instance.ProcessId,
				instance.PluginVersion);
		}
		catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException
			                                  or JsonException
			                                  or TaskCanceledException && !cancellationToken.IsCancellationRequested)
		{
			// A registry record is only a candidate. Suppress unavailable instances without exposing its token.
			return null;
		}
	}

	private static async Task ConfirmIdentityAsync(InstanceDescriptor instance, CancellationToken cancellationToken)
	{
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(ConnectionTimeout);
		using HttpClient client = CreateLocalHttpClient(new Uri(instance.Endpoint));
		using HttpRequestMessage request = new(HttpMethod.Get, "instance");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", instance.AccessToken);
		using HttpResponseMessage response = await client
			.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
			.ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException(
				$"The instance identity endpoint returned HTTP {(int) response.StatusCode}.");
		}

		InstanceIdentity? identity = await response.Content.ReadFromJsonAsync<InstanceIdentity>(timeout.Token)
			.ConfigureAwait(false);
		if (identity is null || identity.InstanceId != instance.InstanceId ||
		    identity.ActivationId != instance.ActivationId
		    || identity.ProcessId != instance.ProcessId ||
		    identity.ProcessStartUtcTicks != instance.ProcessStartUtcTicks
		    || identity.PluginVersion != instance.PluginVersion)
		{
			throw new InvalidOperationException("The instance identity did not match its active registry record.");
		}
	}

	private static HttpClientTransport CreateTransport(InstanceDescriptor instance)
	{
		Uri endpoint = new(instance.Endpoint);
		return new HttpClientTransport(
			new HttpClientTransportOptions
			{
				Endpoint = endpoint,
				Name = "CheatEngine.Mcp.Gateway",
				TransportMode = HttpTransportMode.StreamableHttp,
				ConnectionTimeout = ConnectionTimeout,
				AdditionalHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["Authorization"] = $"Bearer {instance.AccessToken}"
				}
			}, CreateLocalHttpClient(endpoint), null, true);
	}

	private static HttpClient CreateLocalHttpClient(Uri endpoint)
	{
		return new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
		{
			BaseAddress = endpoint, Timeout = Timeout.InfiniteTimeSpan
		};
	}

	private static McpClientOptions CreateClientOptions()
	{
		return new McpClientOptions
		{
			ClientInfo = new Implementation { Name = "CheatEngine.Mcp.Gateway", Version = "2.0.0" },
			Capabilities = new ClientCapabilities(),
			ProtocolVersion = "2025-06-18"
		};
	}

	private static CallToolResult Error(string message)
	{
		return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = message }] };
	}

	private sealed record InstanceIdentity(
		string InstanceId,
		Guid ActivationId,
		int ProcessId,
		long ProcessStartUtcTicks,
		string PluginVersion);

	private sealed record InstanceListEntry(string InstanceId, string Name, int ProcessId, string PluginVersion);
}
