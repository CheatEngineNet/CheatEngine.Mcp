using CheatEngine.Mcp.Core.Contract;

using ModelContextProtocol;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>Classifies a routed backend forward without choosing its tool or resource response shape.</summary>
internal static class GatewayForwardingPolicy
{
	/// <summary>Creates the caller-linked deadline used for one backend operation.</summary>
	internal static CancellationTokenSource CreateDeadline(TimeSpan timeout, CancellationToken callerCancellation)
	{
		CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
		deadline.CancelAfter(timeout);
		return deadline;
	}

	/// <summary>
	///     Classifies a failed backend operation. Caller cancellation wins when it races the deadline, so the SDK does
	///     not emit a gateway response for a request the upstream already cancelled.
	/// </summary>
	internal static GatewayForwardingOutcome Classify(Exception exception, CancellationToken callerCancellation,
		CancellationToken deadlineCancellation)
	{
		if (exception is OperationCanceledException && callerCancellation.IsCancellationRequested)
		{
			return new GatewayForwardingOutcome(GatewayForwardingFailureKind.CallerCancelled,
				ToolHostEffect.NotStarted);
		}

		if (exception is OperationCanceledException && deadlineCancellation.IsCancellationRequested)
		{
			return new GatewayForwardingOutcome(GatewayForwardingFailureKind.Timeout, ToolHostEffect.Unknown);
		}

		if (exception is McpProtocolException)
		{
			return new GatewayForwardingOutcome(GatewayForwardingFailureKind.BackendProtocol,
				ToolHostEffect.NotStarted);
		}

		ToolHostEffect effect = exception is HttpRequestException http && BackendHttp.NeverReachedBackend(http)
			? ToolHostEffect.NotStarted
			: ToolHostEffect.Unknown;
		return new GatewayForwardingOutcome(GatewayForwardingFailureKind.TransportFailure, effect);
	}
}

/// <summary>The shared forwarding outcome that each route projects into its own protocol response.</summary>
internal readonly record struct GatewayForwardingOutcome(GatewayForwardingFailureKind Kind, ToolHostEffect HostEffect);

/// <summary>The failure classes common to tool calls and resource reads.</summary>
internal enum GatewayForwardingFailureKind
{
	CallerCancelled,
	Timeout,
	BackendProtocol,
	TransportFailure
}
