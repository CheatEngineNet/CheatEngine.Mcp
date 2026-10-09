using System.Text.Json.Nodes;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LiveAdmissionMeasurementTests
{
	private static readonly TimeSpan s_coordinationTimeout = TimeSpan.FromSeconds(5);

	[Fact]
	public async Task MeasureAsync_WaitsForEveryPeerAfterOneFails()
	{
		TaskCompletionSource peerEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource releasePeer = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Task<LiveAdmissionBurst> measuring = LiveAdmissionMeasurement.MeasureAsync(
			[
				Request("failure", "client-a", () => throw new InvalidOperationException("not reported")),
				Request("peer", "client-b", async () =>
				{
					peerEntered.SetResult();
					await releasePeer.Task;
					return Success();
				})
			], CancellationToken.None);

		LiveAdmissionBurst? burst = null;
		try
		{
			await peerEntered.Task.WaitAsync(s_coordinationTimeout, TestContext.Current.CancellationToken);
			Assert.False(measuring.IsCompleted);
		}
		finally
		{
			releasePeer.TrySetResult();
			burst = await measuring.WaitAsync(s_coordinationTimeout, CancellationToken.None);
		}

		LiveAdmissionBurst settled = burst ?? throw new InvalidOperationException("The measured burst did not settle.");
		Assert.Equal(2, settled.Samples.Count);
		Assert.Equal(LiveAdmissionOutcomeKind.Failed, Assert.Single(settled.Samples, sample => sample.Id == "failure").Outcome.Kind);
		Assert.Contains("not reported", Assert.Single(settled.Samples, sample => sample.Id == "failure").Outcome.Diagnostic, StringComparison.Ordinal);
		Assert.Equal(LiveAdmissionOutcomeKind.Succeeded, Assert.Single(settled.Samples, sample => sample.Id == "peer").Outcome.Kind);
	}

	[Fact]
	public async Task MeasureAsync_ValidatesEveryRequestBeforeInvokingAny()
	{
		int invoked = 0;
		LiveAdmissionRequest first = Request("duplicate", "client-a", () =>
		{
			invoked++;
			return Task.FromResult(Success());
		});
		LiveAdmissionRequest second = Request("duplicate", "client-b", () => Task.FromResult(Success()));

		await Assert.ThrowsAsync<ArgumentException>(() =>
			LiveAdmissionMeasurement.MeasureAsync([first, second], CancellationToken.None));

		Assert.Equal(0, invoked);
	}

	[Fact]
	public async Task MeasureAsync_RejectsMoreThanSixteenRequestsBeforeInvokingAny()
	{
		int invoked = 0;
		LiveAdmissionRequest[] requests = Enumerable.Range(0, 17)
			.Select(index => Request(index.ToString(System.Globalization.CultureInfo.InvariantCulture), "client-a", () =>
			{
				invoked++;
				return Task.FromResult(Success());
			}))
			.ToArray();

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
			LiveAdmissionMeasurement.MeasureAsync(requests, CancellationToken.None));

		Assert.Equal(0, invoked);
	}

	[Fact]
	public async Task MeasureAsync_PreStartCancellationInvokesNothing()
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		int invoked = 0;

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			LiveAdmissionMeasurement.MeasureAsync([Request("one", "client-a", () =>
			{
				invoked++;
				return Task.FromResult(Success());
			})], cancellation.Token));

		Assert.Equal(0, invoked);
	}

	[Fact]
	public async Task MeasureAsync_ActiveCallerCancellationSettlesAndClassifiesTheInvocation()
	{
		using CancellationTokenSource cancellation = new();
		TaskCompletionSource cancelledEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource peerEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource releasePeer = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Task<LiveAdmissionBurst> measuring = LiveAdmissionMeasurement.MeasureAsync(
			[
				Request("cancelled", "client-a", async () =>
				{
					cancelledEntered.SetResult();
					await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
					return Success();
				}),
				Request("peer", "client-b", async () =>
				{
					peerEntered.SetResult();
					await releasePeer.Task;
					return Success();
				})
			], cancellation.Token);

		LiveAdmissionBurst? burst = null;
		try
		{
			await Task.WhenAll(cancelledEntered.Task, peerEntered.Task).WaitAsync(s_coordinationTimeout, TestContext.Current.CancellationToken);
			cancellation.Cancel();
			Assert.False(measuring.IsCompleted);
		}
		finally
		{
			cancellation.Cancel();
			releasePeer.TrySetResult();
			burst = await measuring.WaitAsync(s_coordinationTimeout, CancellationToken.None);
		}

		LiveAdmissionBurst settled = burst ?? throw new InvalidOperationException("The measured burst did not settle.");
		Assert.Equal(LiveAdmissionOutcomeKind.Cancelled,
			Assert.Single(settled.Samples, sample => sample.Id == "cancelled").Outcome.Kind);
		Assert.Equal(LiveAdmissionOutcomeKind.Succeeded,
			Assert.Single(settled.Samples, sample => sample.Id == "peer").Outcome.Kind);
	}

	[Fact]
	public void Analyze_UsesHalfOpenIntervalsAndDistinctClientOverlap()
	{
		LiveAdmissionSample first = Sample("one", "client-a", 10, 20);
		LiveAdmissionSample second = Sample("two", "client-b", 20, 30);
		LiveAdmissionSample third = Sample("three", "client-c", 15, 25);
		LiveAdmissionSample empty = Sample("empty", "client-d", 20, 20);

		LiveAdmissionBurst halfOpen = LiveAdmissionMeasurement.Analyze([first, second]);
		LiveAdmissionBurst overlap = LiveAdmissionMeasurement.Analyze([first, third]);
		LiveAdmissionBurst emptyOverlap = LiveAdmissionMeasurement.Analyze([Sample("outer", "client-a", 10, 30), empty]);

		Assert.Equal(1, halfOpen.MaximumConcurrentRequests);
		Assert.False(halfOpen.CrossClientOverlap);
		Assert.Equal(2, overlap.MaximumConcurrentRequests);
		Assert.True(overlap.CrossClientOverlap);
		Assert.Equal(1, emptyOverlap.MaximumConcurrentRequests);
		Assert.False(emptyOverlap.CrossClientOverlap);
	}

	[Fact]
	public void ClassifyTool_RequiresExactBusyHostEffectAndRejectsMalformedErrors()
	{
		LiveAdmissionOutcome busy = LiveAdmissionMeasurement.ClassifyTool(Error("busy", "not_started"));
		LiveAdmissionOutcome wrongEffect = LiveAdmissionMeasurement.ClassifyTool(Error("busy", "started"));
		LiveAdmissionOutcome malformed = LiveAdmissionMeasurement.ClassifyTool(new LiveMcpToolResult(true,
			new JsonObject { ["error"] = new JsonObject { ["kind"] = 42 } }, null));
		LiveAdmissionOutcome scalar = LiveAdmissionMeasurement.ClassifyTool(new LiveMcpToolResult(true,
			JsonValue.Create("malformed"), null));
		LiveAdmissionOutcome array = LiveAdmissionMeasurement.ClassifyTool(new LiveMcpToolResult(true,
			new JsonArray(), null));

		Assert.Equal(LiveAdmissionOutcomeKind.Busy, busy.Kind);
		Assert.Equal(LiveAdmissionOutcomeKind.Failed, wrongEffect.Kind);
		Assert.Equal(LiveAdmissionOutcomeKind.Failed, malformed.Kind);
		Assert.Equal(LiveAdmissionOutcomeKind.Failed, scalar.Kind);
		Assert.Equal(LiveAdmissionOutcomeKind.Failed, array.Kind);
	}

	[Fact]
	public async Task Classifiers_DistinguishProtocolToolFailureAndEmptyCompletion()
	{
		McpProtocolException protocol = new("busy", McpErrorCode.InternalError);
		protocol.Data["kind"] = "busy";
		protocol.Data["hostEffect"] = "not_started";
		InvalidOperationException arbitrary = new();
		arbitrary.Data["kind"] = "busy";
		arbitrary.Data["hostEffect"] = "not_started";

		LiveAdmissionOutcome protocolOutcome = LiveAdmissionMeasurement.ClassifyException(protocol, CancellationToken.None);
		LiveAdmissionOutcome arbitraryOutcome = LiveAdmissionMeasurement.ClassifyException(arbitrary, CancellationToken.None);
		LiveAdmissionOutcome timeoutOutcome = LiveAdmissionMeasurement.ClassifyTool(Error("timeout", "not_started"));
		LiveAdmissionOutcome cancelledOutcome = LiveAdmissionMeasurement.ClassifyTool(Error("cancelled", "not_started"));
		LiveAdmissionOutcome toolFailure = LiveAdmissionMeasurement.ClassifyTool(Error("host_refused", "not_started"));
		LiveAdmissionBurst empty = await LiveAdmissionMeasurement.MeasureAsync(
			[Request("empty", "client-a", () => Task.FromResult(new LiveAdmissionOutcome(LiveAdmissionOutcomeKind.EmptyCompletion)))],
			CancellationToken.None);

		Assert.Equal(LiveAdmissionOutcomeKind.Busy, protocolOutcome.Kind);
		Assert.Equal("busy", protocolOutcome.ErrorKind);
		Assert.Equal(LiveAdmissionOutcomeKind.Failed, arbitraryOutcome.Kind);
		Assert.Equal(LiveAdmissionOutcomeKind.Timeout, timeoutOutcome.Kind);
		Assert.Equal(LiveAdmissionOutcomeKind.Cancelled, cancelledOutcome.Kind);
		Assert.Equal(LiveAdmissionOutcomeKind.Failed, toolFailure.Kind);
		Assert.Equal(LiveAdmissionOutcomeKind.EmptyCompletion, Assert.Single(empty.Samples).Outcome.Kind);
	}

	[Fact]
	public void ClassifyException_UnexpectedOperationCancellationIsFailed()
	{
		LiveAdmissionOutcome outcome = LiveAdmissionMeasurement.ClassifyException(
			new OperationCanceledException(), CancellationToken.None);

		Assert.Equal(LiveAdmissionOutcomeKind.Failed, outcome.Kind);
	}

	[Fact]
	public void Classifiers_RetainBoundedDiagnosticsAndProtocolCorrelation()
	{
		McpProtocolException exception = new("outer failure", new InvalidOperationException("distinguishing inner cause"), McpErrorCode.InternalError);
		exception.Data["operation"] = "resources/read";
		exception.Data["errorId"] = "correlation-123";
		LiveAdmissionOutcome protocol = LiveAdmissionMeasurement.ClassifyException(exception, CancellationToken.None);
		LiveAdmissionOutcome oversized = LiveAdmissionMeasurement.ClassifyException(
			new InvalidOperationException(new string('x', 9000)), CancellationToken.None);
		LiveMcpToolResult error = Error("host_refused", "not_started");
		error.Payload!["error"]!["message"] = "tool distinction";
		error.Payload["error"]!["operation"] = "memory_read_batch";
		error.Payload["error"]!["details"] = new JsonObject { ["errorId"] = "tool-123" };
		LiveAdmissionOutcome tool = LiveAdmissionMeasurement.ClassifyTool(error);

		Assert.Contains("outer failure", protocol.Diagnostic, StringComparison.Ordinal);
		Assert.Contains("distinguishing inner cause", protocol.Diagnostic, StringComparison.Ordinal);
		Assert.Equal("resources/read", protocol.Operation);
		Assert.Equal("correlation-123", protocol.ErrorId);
		Assert.Equal(LiveAdmissionMeasurement.MaximumDiagnosticCharacters, oversized.Diagnostic!.Length);
		Assert.EndsWith("...", oversized.Diagnostic, StringComparison.Ordinal);
		Assert.Equal("tool distinction", tool.Diagnostic);
		Assert.Equal("memory_read_batch", tool.Operation);
		Assert.Equal("tool-123", tool.ErrorId);
	}

	private static LiveAdmissionRequest Request(string id, string clientId, Func<Task<LiveAdmissionOutcome>> invoke)
	{
		return new LiveAdmissionRequest(id, clientId, "admission", invoke);
	}

	private static LiveAdmissionSample Sample(string id, string clientId, long started, long finished)
	{
		return new LiveAdmissionSample(id, clientId, "admission", started, finished, Success());
	}

	private static LiveAdmissionOutcome Success()
	{
		return new LiveAdmissionOutcome(LiveAdmissionOutcomeKind.Succeeded);
	}

	private static LiveMcpToolResult Error(string kind, string hostEffect)
	{
		return new LiveMcpToolResult(true, new JsonObject
		{
			["error"] = new JsonObject
			{
				["kind"] = kind,
				["hostEffect"] = hostEffect
			}
		}, null);
	}
}
