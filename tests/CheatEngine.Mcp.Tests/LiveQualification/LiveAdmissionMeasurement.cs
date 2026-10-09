using System.Diagnostics;
using System.Text.Json.Nodes;

using ModelContextProtocol;

namespace CheatEngine.Mcp.Tests.LiveQualification;

internal enum LiveAdmissionOutcomeKind
{
	Succeeded,
	Busy,
	Timeout,
	Cancelled,
	EmptyCompletion,
	Failed
}

internal sealed record LiveAdmissionOutcome(LiveAdmissionOutcomeKind Kind, string? ErrorKind = null,
	string? HostEffect = null, string? ExceptionType = null, string? Diagnostic = null,
	string? Operation = null, string? ErrorId = null);

internal sealed record LiveAdmissionRequest(string Id, string ClientId, string Category,
	Func<Task<LiveAdmissionOutcome>> Invoke);

internal sealed record LiveAdmissionSample(string Id, string ClientId, string Category, long StartedTimestamp,
	long FinishedTimestamp, LiveAdmissionOutcome Outcome);

internal sealed record LiveAdmissionBurst(IReadOnlyList<LiveAdmissionSample> Samples, int MaximumConcurrentRequests,
	bool CrossClientOverlap);

/// <summary>Measures finite managed request bursts without asserting native admission from managed scheduling.</summary>
internal static class LiveAdmissionMeasurement
{
	private const int MaximumRequests = 16;
	internal const int MaximumDiagnosticCharacters = 8192;

	internal static async Task<LiveAdmissionBurst> MeasureAsync(IReadOnlyList<LiveAdmissionRequest> requests,
		CancellationToken cancellationToken)
	{
		ValidateRequests(requests);
		cancellationToken.ThrowIfCancellationRequested();

		TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Task<LiveAdmissionSample?>[] pending = requests
			.Select(request => MeasureRequestAsync(request, barrier.Task, cancellationToken))
			.ToArray();
		barrier.SetResult();

		LiveAdmissionSample?[] completed = await Task.WhenAll(pending);
		return Analyze(completed.OfType<LiveAdmissionSample>().ToArray());
	}

	internal static LiveAdmissionBurst Analyze(IReadOnlyList<LiveAdmissionSample> samples)
	{
		ArgumentNullException.ThrowIfNull(samples);
		foreach (LiveAdmissionSample sample in samples)
		{
			if (sample.FinishedTimestamp < sample.StartedTimestamp)
			{
				throw new ArgumentException("Admission samples must use monotonic nondecreasing timestamps.", nameof(samples));
			}
		}

		int maximum = CalculateMaximumConcurrency(samples);
		bool crossClientOverlap = HasCrossClientOverlap(samples);
		return new LiveAdmissionBurst(samples.ToArray(), maximum, crossClientOverlap);
	}

	internal static LiveAdmissionOutcome ClassifyTool(LiveMcpToolResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		if (!result.IsError)
		{
			return new LiveAdmissionOutcome(LiveAdmissionOutcomeKind.Succeeded);
		}

		if (result.Payload is not JsonObject payload
			|| payload["error"] is not JsonObject error
			|| !TryGetString(error["kind"], out string? kind))
		{
			return new LiveAdmissionOutcome(LiveAdmissionOutcomeKind.Failed);
		}

		_ = TryGetString(error["hostEffect"], out string? hostEffect);
		_ = TryGetString(error["message"], out string? message);
		_ = TryGetString(error["operation"], out string? operation);
		_ = TryGetString((error["details"] as JsonObject)?["errorId"], out string? errorId);
		LiveAdmissionOutcome outcome = new(LiveAdmissionOutcomeKind.Failed, kind, hostEffect,
			Diagnostic: Bounded(message, MaximumDiagnosticCharacters), Operation: Bounded(operation, 256), ErrorId: Bounded(errorId, 256));
		if (string.Equals(kind, "busy", StringComparison.Ordinal)
			&& string.Equals(hostEffect, "not_started", StringComparison.Ordinal))
		{
			return outcome with
			{
				Kind = LiveAdmissionOutcomeKind.Busy
			};
		}

		if (string.Equals(kind, "timeout", StringComparison.Ordinal))
		{
			return outcome with
			{
				Kind = LiveAdmissionOutcomeKind.Timeout
			};
		}

		if (string.Equals(kind, "cancelled", StringComparison.Ordinal))
		{
			return outcome with
			{
				Kind = LiveAdmissionOutcomeKind.Cancelled
			};
		}

		return outcome;
	}

	internal static LiveAdmissionOutcome ClassifyException(Exception exception, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(exception);
		string? kind = null;
		string? hostEffect = null;
		string? operation = null;
		string? errorId = null;
		if (exception is McpProtocolException protocol)
		{
			kind = protocol.Data["kind"] as string;
			hostEffect = protocol.Data["hostEffect"] as string;
			operation = protocol.Data["operation"] as string;
			errorId = protocol.Data["errorId"] as string;
		}
		LiveAdmissionOutcome outcome = new(LiveAdmissionOutcomeKind.Failed, kind, hostEffect,
			exception.GetType().FullName, Bounded(exception.ToString(), MaximumDiagnosticCharacters),
			Bounded(operation, 256), Bounded(errorId, 256));
		if (string.Equals(kind, "busy", StringComparison.Ordinal)
			&& string.Equals(hostEffect, "not_started", StringComparison.Ordinal))
		{
			return outcome with
			{
				Kind = LiveAdmissionOutcomeKind.Busy
			};
		}

		if (string.Equals(kind, "timeout", StringComparison.Ordinal) || exception is TimeoutException)
		{
			return outcome with
			{
				Kind = LiveAdmissionOutcomeKind.Timeout
			};
		}

		if (string.Equals(kind, "cancelled", StringComparison.Ordinal)
			|| (exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
		{
			return outcome with
			{
				Kind = LiveAdmissionOutcomeKind.Cancelled
			};
		}

		return outcome;
	}

	private static string? Bounded(string? text, int maximum)
	{
		return text is null || text.Length <= maximum ? text : text[..(maximum - 3)] + "...";
	}

	private static async Task<LiveAdmissionSample?> MeasureRequestAsync(LiveAdmissionRequest request, Task barrier,
		CancellationToken cancellationToken)
	{
		await barrier.ConfigureAwait(false);
		if (cancellationToken.IsCancellationRequested)
		{
			return null;
		}

		long started = Stopwatch.GetTimestamp();
		LiveAdmissionOutcome outcome;
		try
		{
			outcome = await request.Invoke().ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			outcome = ClassifyException(exception, cancellationToken);
		}

		long finished = Stopwatch.GetTimestamp();
		return new LiveAdmissionSample(request.Id, request.ClientId, request.Category, started, finished, outcome);
	}

	private static void ValidateRequests(IReadOnlyList<LiveAdmissionRequest> requests)
	{
		ArgumentNullException.ThrowIfNull(requests);
		if (requests.Count is < 1 or > MaximumRequests)
		{
			throw new ArgumentOutOfRangeException(nameof(requests), "Admission bursts contain from one through sixteen requests.");
		}

		HashSet<string> ids = new(StringComparer.Ordinal);
		foreach (LiveAdmissionRequest request in requests)
		{
			ArgumentNullException.ThrowIfNull(request);
			if (string.IsNullOrWhiteSpace(request.Id)
				|| string.IsNullOrWhiteSpace(request.ClientId)
				|| string.IsNullOrWhiteSpace(request.Category))
			{
				throw new ArgumentException("Admission request ID, client ID and category must be nonblank.", nameof(requests));
			}

			ArgumentNullException.ThrowIfNull(request.Invoke);
			if (!ids.Add(request.Id))
			{
				throw new ArgumentException("Admission request IDs must be unique.", nameof(requests));
			}
		}
	}

	private static int CalculateMaximumConcurrency(IReadOnlyList<LiveAdmissionSample> samples)
	{
		List<(long Timestamp, int Delta)> events = [];
		foreach (LiveAdmissionSample sample in samples)
		{
			if (sample.StartedTimestamp != sample.FinishedTimestamp)
			{
				events.Add((sample.StartedTimestamp, 1));
				events.Add((sample.FinishedTimestamp, -1));
			}
		}

		int active = 0;
		int maximum = 0;
		foreach ((long _, int delta) in events.OrderBy(static item => item.Timestamp).ThenBy(static item => item.Delta))
		{
			active += delta;
			maximum = Math.Max(maximum, active);
		}

		return maximum;
	}

	private static bool HasCrossClientOverlap(IReadOnlyList<LiveAdmissionSample> samples)
	{
		for (int left = 0; left < samples.Count; left++)
		{
			for (int right = left + 1; right < samples.Count; right++)
			{
				LiveAdmissionSample first = samples[left];
				LiveAdmissionSample second = samples[right];
				if (first.StartedTimestamp != first.FinishedTimestamp
					&& second.StartedTimestamp != second.FinishedTimestamp
					&& !string.Equals(first.ClientId, second.ClientId, StringComparison.Ordinal)
					&& first.StartedTimestamp < second.FinishedTimestamp
					&& second.StartedTimestamp < first.FinishedTimestamp)
				{
					return true;
				}
			}
		}

		return false;
	}

	private static bool TryGetString(JsonNode? node, out string? value)
	{
		value = null;
		try
		{
			value = node?.GetValue<string>();
			return value is not null;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
	}
}
