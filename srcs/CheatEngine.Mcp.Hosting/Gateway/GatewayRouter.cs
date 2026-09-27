using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;

using Microsoft.Extensions.Logging;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>Routes each tool call to the one verified Cheat Engine instance its instanceId names, and never elsewhere.</summary>
/// <remarks>
///     Each call re-reads the registry, verifies the backend identity, leases the pooled client of that exact record
///     (<see cref="GatewayBackendConnector" />) and forwards once. Any failure evicts the client and is reported in the v2
///     error envelope; nothing is re-sent.
/// </remarks>
internal sealed partial class GatewayRouter(
	GatewayOptions options,
	GatewayBackendConnector connector,
	GatewayInstanceTool instances,
	GatewayToolCatalog catalog,
	ILogger<GatewayRouter> logger)
{
	/// <summary>How long clients may cache the listing: it is fixed for the life of the gateway process.</summary>
	internal static readonly TimeSpan CatalogTimeToLive = TimeSpan.FromHours(1);

	internal ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> _, CancellationToken __)
	{
		return ValueTask.FromResult(new ListToolsResult
		{
			Tools = catalog.Tools.ToList(),
			TimeToLive = CatalogTimeToLive,
			CacheScope = CacheScope.Private
		});
	}

	internal async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> context,
		CancellationToken cancellationToken)
	{
		CallToolRequestParams request = context.Params
										?? throw new McpProtocolException("tools/call requires params.",
											McpErrorCode.InvalidParams);
		if (string.Equals(request.Name, GatewayToolCatalog.InstanceListToolName, StringComparison.Ordinal))
		{
			return await instances.CallAsync(cancellationToken).ConfigureAwait(false);
		}

		if (!catalog.IsRouted(request.Name))
		{
			return GatewayErrors.UnknownTool(request.Name);
		}

		if (request.Arguments is null
			|| !request.Arguments.TryGetValue(GatewayToolCatalog.InstanceIdArgumentName, out JsonElement routing)
			|| routing.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(routing.GetString()))
		{
			return GatewayErrors.MissingInstanceId(request.Name);
		}

		string instanceId = routing.GetString()!;
		BackendConnectionPool.BackendLease lease;
		try
		{
			lease = await connector.ConnectAsync(instanceId, cancellationToken).ConfigureAwait(false);
		}
		catch (GatewayRoutingException exception) when (exception.UnknownInstance)
		{
			return GatewayErrors.UnknownInstance(request.Name, instanceId);
		}
		catch (GatewayRoutingException exception)
		{
			LogUnavailable(logger, instanceId, request.Name, exception.Message);
			return GatewayErrors.Unavailable(request.Name, instanceId, exception.Message, ToolHostEffect.NotStarted);
		}

		using (lease)
		{
			return await ForwardAsync(lease, request, instanceId, cancellationToken).ConfigureAwait(false);
		}
	}

	private async Task<CallToolResult> ForwardAsync(BackendConnectionPool.BackendLease lease,
		CallToolRequestParams request, string instanceId, CancellationToken cancellationToken)
	{
		// Only the tool's own arguments and trace context travel: the routing argument, protocol-reserved _meta, the
		// upstream progress token and MRTR state belong to the upstream session, not to this hop.
		CallToolRequestParams forwarded = new()
		{
			Name = request.Name,
			Arguments = request.Arguments!
				.Where(static argument => !string.Equals(argument.Key, GatewayToolCatalog.InstanceIdArgumentName,
					StringComparison.Ordinal))
				.ToDictionary(static argument => argument.Key, static argument => argument.Value,
					StringComparer.Ordinal),
			Meta = GatewayMeta.ForBackend(request.Meta)
		};
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(options.CallTimeout);
		try
		{
			// A backend result, including its own isError result, passes through unchanged, except that links to the
			// backend's live resources gain the instance prefix so the upstream client can read them through here.
			CallToolResult result = await lease.Client.CallToolAsync(forwarded, deadline.Token).ConfigureAwait(false);
			RewriteResourceUris(result.Content, instanceId);
			return result;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// The upstream cancelled: the SDK sends no response, and the backend already saw the cancellation.
			throw;
		}
		catch (OperationCanceledException) when (deadline.IsCancellationRequested)
		{
			lease.Evict("call timeout");
			LogTimeout(logger, instanceId, request.Name, options.CallTimeout.TotalSeconds);
			return GatewayErrors.Timeout(request.Name, instanceId, options.CallTimeout);
		}
		catch (McpProtocolException)
		{
			// The backend answered with a JSON-RPC error; the upstream receives the same error.
			throw;
		}
		catch (Exception exception)
		{
			lease.Evict("transport failure");
			ToolHostEffect effect = exception is HttpRequestException http && BackendHttp.NeverReachedBackend(http)
				? ToolHostEffect.NotStarted
				: ToolHostEffect.Unknown;
			string reason = exception is HttpRequestException failure
				? BackendHttp.Describe(failure)
				: $"The connection failed while forwarding the call ({exception.GetType().Name}).";
			LogUnavailable(logger, instanceId, request.Name, reason);
			return GatewayErrors.Unavailable(request.Name, instanceId, reason, effect);
		}
	}

	/// <summary>Rewrites the backend live URIs of resource links and embedded resources to their gateway form.</summary>
	/// <param name="content">A backend result's content blocks.</param>
	/// <param name="instanceId">The instance that produced them.</param>
	internal static void RewriteResourceUris(IList<ContentBlock>? content, string instanceId)
	{
		foreach (ContentBlock block in content ?? [])
		{
			switch (block)
			{
				case ResourceLinkBlock link:
					link.Uri = McpResourceUris.RewriteContentUri(link.Uri, instanceId);
					break;
				case EmbeddedResourceBlock { Resource: { } embedded }:
					embedded.Uri = McpResourceUris.RewriteContentUri(embedded.Uri, instanceId);
					break;
			}
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Instance {InstanceId} is unavailable for {Tool}: {Reason}")]
	private static partial void LogUnavailable(ILogger logger, string instanceId, string tool, string reason);

	[LoggerMessage(Level = LogLevel.Warning,
		Message = "Instance {InstanceId} did not answer {Tool} within {Seconds} s; its outcome is unknown.")]
	private static partial void LogTimeout(ILogger logger, string instanceId, string tool, double seconds);
}
