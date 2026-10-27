using CheatEngine.Mcp.Core.Contract;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Hosting;

public sealed class GatewayForwardingPolicyTests
{
	[Fact]
	public void Classify_CallerAndDeadlineCancelled_CallerCancellationWins()
	{
		using CancellationTokenSource caller = new();
		using CancellationTokenSource deadline = new();
		caller.Cancel();
		deadline.Cancel();

		GatewayForwardingOutcome outcome = GatewayForwardingPolicy.Classify(
			new OperationCanceledException(deadline.Token), caller.Token, deadline.Token);

		Assert.Equal(GatewayForwardingFailureKind.CallerCancelled, outcome.Kind);
	}

	[Fact]
	public void Classify_DeadlineCancelled_ReturnsTimeoutWithUnknownHostEffect()
	{
		using CancellationTokenSource caller = new();
		using CancellationTokenSource deadline = new();
		deadline.Cancel();

		GatewayForwardingOutcome outcome = GatewayForwardingPolicy.Classify(
			new OperationCanceledException(deadline.Token), caller.Token, deadline.Token);

		Assert.Equal(GatewayForwardingFailureKind.Timeout, outcome.Kind);
		Assert.Equal(ToolHostEffect.Unknown, outcome.HostEffect);
	}

	[Fact]
	public void Classify_BackendProtocolError_PreservesProtocolOutcome()
	{
		GatewayForwardingOutcome outcome = GatewayForwardingPolicy.Classify(
			new McpProtocolException("Backend rejected the parameters.", McpErrorCode.InvalidParams),
			CancellationToken.None, CancellationToken.None);

		Assert.Equal(GatewayForwardingFailureKind.BackendProtocol, outcome.Kind);
	}

	[Fact]
	public void Classify_TransportFailure_ReturnsUnknownHostEffect()
	{
		GatewayForwardingOutcome outcome = GatewayForwardingPolicy.Classify(new HttpRequestException(),
			CancellationToken.None, CancellationToken.None);

		Assert.Equal(GatewayForwardingFailureKind.TransportFailure, outcome.Kind);
		Assert.Equal(ToolHostEffect.Unknown, outcome.HostEffect);
	}

	[Theory]
	[InlineData(HttpRequestError.ConnectionError, ToolHostEffect.NotStarted)]
	[InlineData(HttpRequestError.NameResolutionError, ToolHostEffect.NotStarted)]
	[InlineData(HttpRequestError.ProxyTunnelError, ToolHostEffect.NotStarted)]
	[InlineData(HttpRequestError.SecureConnectionError, ToolHostEffect.NotStarted)]
	[InlineData(HttpRequestError.ResponseEnded, ToolHostEffect.Unknown)]
	public void Classify_HttpTransportFailure_PreservesBackendAdmissionEffect(HttpRequestError requestError,
		ToolHostEffect expectedEffect)
	{
		GatewayForwardingOutcome outcome = GatewayForwardingPolicy.Classify(new HttpRequestException(requestError),
			CancellationToken.None, CancellationToken.None);

		Assert.Equal(GatewayForwardingFailureKind.TransportFailure, outcome.Kind);
		Assert.Equal(expectedEffect, outcome.HostEffect);
	}

	[Fact]
	public void CreateDeadline_CallerCancellationCancelsLinkedDeadline()
	{
		using CancellationTokenSource caller = new();
		using CancellationTokenSource deadline = GatewayForwardingPolicy.CreateDeadline(TimeSpan.FromDays(1), caller.Token);
		Assert.False(deadline.IsCancellationRequested);

		caller.Cancel();

		Assert.True(deadline.IsCancellationRequested);
	}
}
