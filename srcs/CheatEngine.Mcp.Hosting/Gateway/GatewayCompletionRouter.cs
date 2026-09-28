using CheatEngine.Mcp.Hosting.Discovery;

using Microsoft.Extensions.Logging;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     The gateway's completion handler for routed resource templates. It completes <c>instanceId</c> from the
///     instances whose identity was verified in the last <see cref="IdentityWindow" />, ids only; a variable that
///     declares <c>[AllowedValues]</c> from the gateway's own catalog; and a variable marked
///     <see cref="McpCompletionAttribute" /> by forwarding the request to the instance named by
///     <c>context.arguments.instanceId</c>. Everything else returns no values here; the SDK then appends the allowed
///     values of the Local prompts and templates.
/// </summary>
/// <remarks>
///     A completion fires on every keystroke, so it never errors and never waits long. It is forwarded only to a single
///     active registry record whose identity was verified within <see cref="IdentityWindow" />, so it never probes an
///     identity itself; the backend receives its own template (<c>cheatengine://instance/…</c>) and the other context
///     arguments, and the whole forward, connecting included, is bounded by <see cref="ForwardTimeout" /> (or the call
///     timeout when shorter). Any failure answers no values; only a transport failure evicts the pooled connection.
/// </remarks>
internal sealed partial class GatewayCompletionRouter(
	GatewayOptions options,
	InstanceIdentityVerifier verifier,
	GatewayResourceCatalog catalog,
	InstanceRegistry registry,
	BackendConnectionPool pool,
	ILogger<GatewayCompletionRouter> logger)
{
	/// <summary>The most values one completion returns.</summary>
	internal const int MaximumValues = McpCompletions.MaximumValues;

	/// <summary>How recent a verified identity must be to be offered, or to receive a forwarded completion.</summary>
	internal static readonly TimeSpan IdentityWindow = InstanceIdentityVerifier.VerificationWindow;

	/// <summary>The longest a forwarded completion may take, connecting included.</summary>
	internal static readonly TimeSpan ForwardTimeout = TimeSpan.FromSeconds(2);

	internal async ValueTask<CompleteResult> CompleteAsync(RequestContext<CompleteRequestParams> context,
		CancellationToken cancellationToken)
	{
		Completion completion = new();
		if (context.Params is
			{
				Ref: ResourceTemplateReference { Uri: { } template }, Argument: { } argument
			} request && catalog.TryGetRoutedTemplate(template, out McpCatalogResource? resource))
		{
			if (string.Equals(argument.Name, McpResourceUris.InstanceIdVariable, StringComparison.Ordinal))
			{
				completion = CompleteInstanceId(argument.Value, cancellationToken);
			}
			else if (resource.AllowedValues(argument.Name) is { Count: > 0 } allowed)
			{
				completion = McpCompletions.Match(allowed, argument.Value);
			}
			else if (resource.IsCompletedByInstance(argument.Name))
			{
				completion = await ForwardAsync(request, resource, cancellationToken).ConfigureAwait(false);
			}
		}

		return new CompleteResult { Completion = completion };
	}

	private Completion CompleteInstanceId(string? value, CancellationToken cancellationToken)
	{
		HashSet<string> activeAndConfirmed = registry.ReadActive(cancellationToken)
			.Where(verifier.IsConfirmed).Select(static instance => instance.InstanceId).ToHashSet(StringComparer.Ordinal);
		string prefix = value ?? string.Empty;
		string[] matches = verifier.RecentlyVerified(IdentityWindow)
			.Where(activeAndConfirmed.Contains)
			.Where(instanceId => instanceId.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
		return new Completion
		{
			Values = [.. matches.Take(MaximumValues)],
			Total = matches.Length,
			HasMore = matches.Length > MaximumValues
		};
	}

	private async Task<Completion> ForwardAsync(CompleteRequestParams request, McpCatalogResource resource,
		CancellationToken cancellationToken)
	{
		if (request.Context?.Arguments is not { } arguments ||
			!arguments.TryGetValue(McpResourceUris.InstanceIdVariable, out string? instanceId) ||
			!McpResourceUris.IsInstanceId(instanceId) ||
			!verifier.RecentlyVerified(IdentityWindow).Contains(instanceId, StringComparer.Ordinal))
		{
			// No instance selected yet, or one the gateway has not verified recently: never probe per keystroke.
			return new Completion();
		}

		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(options.CallTimeout < ForwardTimeout ? options.CallTimeout : ForwardTimeout);
		BackendConnectionPool.BackendLease? lease = null;
		try
		{
			InstanceDescriptor[] records = registry.ReadActive(deadline.Token)
				.Where(candidate => string.Equals(candidate.InstanceId, instanceId, StringComparison.Ordinal))
				.ToArray();
			if (records is not [InstanceDescriptor instance])
			{
				return new Completion();
			}

			if (!verifier.IsConfirmed(instance))
			{
				// A republished record needs a regular discovery or routed request before a completion can use it.
				return new Completion();
			}

			lease = await pool.RentAsync(instance, deadline.Token).ConfigureAwait(false);
			CompleteResult result = await lease.Client.CompleteAsync(new CompleteRequestParams
			{
				// The backend form of the same template, and the other variables the client already resolved.
				Ref = new ResourceTemplateReference { Uri = resource.Template.UriTemplate },
				Argument = new Argument { Name = request.Argument.Name, Value = request.Argument.Value },
				Context = new CompleteContext
				{
					Arguments = arguments
						.Where(static pair => !string.Equals(pair.Key, McpResourceUris.InstanceIdVariable,
							StringComparison.Ordinal))
						.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)
				},
				Meta = GatewayMeta.ForBackend(request.Meta)
			}, deadline.Token).ConfigureAwait(false);
			return Bound(result.Completion);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException)
		{
			// Slow is not broken: the connection stays pooled for the next call.
			LogForwardFailed(logger, instanceId, "timeout");
			return new Completion();
		}
		catch (McpProtocolException exception)
		{
			// The backend answered, so the connection is healthy; an older plugin may simply not complete this.
			LogForwardRefused(logger, instanceId, (int) exception.ErrorCode);
			return new Completion();
		}
		catch (InstanceUnavailableException)
		{
			// The pool could not connect and already evicted the connection.
			LogForwardFailed(logger, instanceId, "connect failed");
			return new Completion();
		}
		catch (Exception exception)
		{
			lease?.Evict("completion transport failure");
			string failure = exception.GetType().Name;
			LogForwardFailed(logger, instanceId, failure);
			return new Completion();
		}
		finally
		{
			lease?.Dispose();
		}
	}

	/// <summary>Keeps a backend's answer within the protocol bound, whatever it returned.</summary>
	private static Completion Bound(Completion? completion)
	{
		IList<string> received = completion?.Values ?? [];
		List<string> values = [.. received.Where(static value => value is not null).Take(MaximumValues)];
		int total = Math.Max(completion?.Total ?? received.Count, received.Count);
		return new Completion
		{
			Values = values,
			Total = total,
			HasMore = completion?.HasMore == true || total > values.Count
		};
	}

	[LoggerMessage(Level = LogLevel.Debug,
		Message = "A completion forwarded to instance {InstanceId} offered no values: {Reason}.")]
	private static partial void LogForwardFailed(ILogger logger, string instanceId, string reason);

	[LoggerMessage(Level = LogLevel.Debug,
		Message = "Instance {InstanceId} refused a forwarded completion with JSON-RPC error {Code}.")]
	private static partial void LogForwardRefused(ILogger logger, string instanceId, int code);
}
