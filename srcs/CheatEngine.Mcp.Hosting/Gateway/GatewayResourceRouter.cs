using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Hosting.Discovery;

using Microsoft.Extensions.Logging;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     The gateway's resource handlers. The SDK serves the Local documents and appends them to these lists; these
///     handlers add <c>cheatengine://instances</c>, the concrete live resources of every verified instance and the
///     routed <c>cheatengine://instances/{instanceId}/…</c> templates, and read a routed URI from exactly the instance
///     it names.
/// </summary>
/// <remarks>
///     <para>
///         The resource list is read from the registry and the identity cache only
///         (<see cref="GatewayLiveInstances" />), never with an identity check, and is immediately stale (<c>ttlMs</c>
///         0); <see cref="GatewayResourceListMonitor" /> sends <c>notifications/resources/list_changed</c> when its
///         instances change.
///     </para>
///     <para>
///         A routed read strips the prefix (a pure swap to <c>cheatengine://instance/…</c>), refuses an unknown path
///         without contacting any backend, verifies the backend identity like a tool call, reads once and rewrites the
///         content URIs back. It never retries and never reads from another instance.
///     </para>
/// </remarks>
internal sealed partial class GatewayResourceRouter(
	GatewayOptions options,
	GatewayBackendConnector connector,
	GatewayInstanceTool instances,
	GatewayResourceCatalog catalog,
	GatewayLiveInstances live,
	ILogger<GatewayResourceRouter> logger)
{
	/// <summary>
	///     Lists <c>cheatengine://instances</c>, then the concrete live resources of each listed instance in their
	///     gateway form, such as <c>cheatengine://instances/{instanceId}/process</c>; the SDK appends the Local
	///     documents.
	/// </summary>
	/// <param name="_">The request; the list has a single page.</param>
	/// <param name="cancellationToken">The request's cancellation.</param>
	/// <returns>The list, private and immediately stale.</returns>
	internal ValueTask<ListResourcesResult> ListResourcesAsync(RequestContext<ListResourcesRequestParams> _,
		CancellationToken cancellationToken)
	{
		IReadOnlyList<InstanceListEntry> listed = live.ListForClient(cancellationToken);
		return ValueTask.FromResult(new ListResourcesResult
		{
			Resources =
			[
				GatewayResourceCatalog.CreateInstancesResource(),
				.. listed.SelectMany(catalog.CreateLiveResources)
			],
			TimeToLive = TimeSpan.Zero,
			CacheScope = CacheScope.Private
		});
	}

	internal ValueTask<ListResourceTemplatesResult> ListResourceTemplatesAsync(
		RequestContext<ListResourceTemplatesRequestParams> _, CancellationToken __)
	{
		return ValueTask.FromResult(new ListResourceTemplatesResult
		{
			ResourceTemplates = [.. catalog.RoutedTemplates],
			TimeToLive = GatewayRouter.CatalogTimeToLive,
			CacheScope = CacheScope.Private
		});
	}

	/// <summary>Reads a URI no Local resource matched: the instance list or a routed live resource.</summary>
	/// <param name="context">The request.</param>
	/// <param name="cancellationToken">The request's cancellation.</param>
	/// <returns>The contents, with backend URIs in their gateway form.</returns>
	internal async ValueTask<ReadResourceResult> ReadResourceAsync(RequestContext<ReadResourceRequestParams> context,
		CancellationToken cancellationToken)
	{
		string uri = context.Params?.Uri ?? throw new McpProtocolException("resources/read requires a uri.",
			McpErrorCode.InvalidParams);
		string? version = context.Server.NegotiatedProtocolVersion;
		if (McpResourceUris.IsGatewayInstances(uri))
		{
			return await ReadInstancesAsync(cancellationToken).ConfigureAwait(false);
		}

		if (McpResourceUris.IsInstance(uri))
		{
			throw GatewayErrors.ResourceNotFound(uri,
				$"Through the gateway, read {McpResourceUris.GatewayInstancesPrefix}{{instanceId}}/ followed by the same " +
				$"path; {McpResourceUris.GatewayInstances} lists the instance ids.", version);
		}

		if (!McpResourceUris.TryParseGateway(uri, out string? instanceId, out string? backendUri))
		{
			throw GatewayErrors.ResourceNotFound(uri,
				"List the resources and resource templates, and use their URIs exactly; live URIs need a current " +
				$"instanceId from {McpResourceUris.GatewayInstances}.", version);
		}

		if (!catalog.IsRouted(backendUri))
		{
			// Decided from the catalog alone: an unknown path never reaches a backend or its identity endpoint.
			throw GatewayErrors.ResourceNotFound(uri,
				"List the resource templates for the live paths; query parameters must follow the template's order, " +
				"such as ?offset=0&limit=10.", version);
		}

		BackendConnectionPool.BackendLease lease;
		try
		{
			lease = await connector.ConnectAsync(instanceId, cancellationToken).ConfigureAwait(false);
		}
		catch (GatewayRoutingException exception)
		{
			LogUnavailable(logger, instanceId, exception.Message);
			throw GatewayErrors.ResourceFailure(ToolErrorKind.InstanceUnavailable,
				$"Cheat Engine instance '{instanceId}' is unavailable: {exception.Message}", ToolHostEffect.NotStarted,
				GatewayErrors.RediscoverHint, version);
		}

		using (lease)
		{
			ReadResourceResult result = await ForwardAsync(lease, context.Params!, backendUri, instanceId, version,
				cancellationToken).ConfigureAwait(false);
			foreach (ResourceContents contents in result.Contents)
			{
				contents.Uri = McpResourceUris.RewriteContentUri(contents.Uri, instanceId);
			}

			return result;
		}
	}

	private async Task<ReadResourceResult> ForwardAsync(BackendConnectionPool.BackendLease lease,
		ReadResourceRequestParams request, string backendUri, string instanceId, string? version,
		CancellationToken cancellationToken)
	{
		ReadResourceRequestParams forwarded = new() { Uri = backendUri, Meta = GatewayMeta.ForBackend(request.Meta) };
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(options.CallTimeout);
		try
		{
			return await lease.Client.ReadResourceAsync(forwarded, deadline.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException) when (deadline.IsCancellationRequested)
		{
			lease.Evict("read timeout");
			LogUnavailable(logger, instanceId, "read timeout");
			throw GatewayErrors.ResourceFailure(ToolErrorKind.Timeout,
				$"Cheat Engine instance '{instanceId}' did not answer within {options.CallTimeout.TotalSeconds:0} s.",
				ToolHostEffect.Unknown, "A resource read changes nothing; read it again when the instance responds.",
				version);
		}
		catch (McpProtocolException exception)
		{
			// The backend answered with a JSON-RPC error: same data, code chosen for the upstream version.
			throw GatewayErrors.FromBackend(exception, version);
		}
		catch (Exception exception)
		{
			lease.Evict("transport failure");
			string reason = exception is HttpRequestException failure
				? BackendHttp.Describe(failure)
				: $"The connection failed while reading ({exception.GetType().Name}).";
			LogUnavailable(logger, instanceId, reason);
			throw GatewayErrors.ResourceFailure(ToolErrorKind.InstanceUnavailable,
				$"Cheat Engine instance '{instanceId}' is unavailable: {reason}",
				exception is HttpRequestException http && BackendHttp.NeverReachedBackend(http)
					? ToolHostEffect.NotStarted
					: ToolHostEffect.Unknown, GatewayErrors.RediscoverHint, version);
		}
	}

	private async Task<ReadResourceResult> ReadInstancesAsync(CancellationToken cancellationToken)
	{
		InstanceListResult listed = await instances.ListAsync(cancellationToken).ConfigureAwait(false);
		return new ReadResourceResult
		{
			Contents =
			[
				new TextResourceContents
				{
					Uri = McpResourceUris.GatewayInstances,
					MimeType = McpResourceUris.JsonMimeType,
					Text = JsonSerializer.Serialize(listed, HostingJsonContext.Default.InstanceListResult)
				}
			],
			TimeToLive = TimeSpan.Zero,
			CacheScope = CacheScope.Private
		};
	}

	[LoggerMessage(Level = LogLevel.Warning,
		Message = "Instance {InstanceId} is unavailable for a resource read: {Reason}")]
	private static partial void LogUnavailable(ILogger logger, string instanceId, string reason);
}
