using System.Diagnostics;

namespace CheatEngine.Mcp.Tests.LiveQualification;

internal sealed record LiveAdmissionMetric(string Category, LiveAdmissionOutcomeKind Outcome,
	LivePerformanceDecision? Decision, string? MissingEvidence);

internal sealed record LiveAdmissionAssessment(IReadOnlyList<LiveAdmissionMetric> Metrics,
	IReadOnlyList<string> MissingEvidence)
{
	public bool Passed => MissingEvidence.Count == 0 && Metrics.Count > 0
		&& Metrics.All(static metric => metric.Decision?.Passed == true);
}

/// <summary>Compares observed response classes only; absent refusals never become successful admission evidence.</summary>
internal static class LiveAdmissionComparison
{
	internal const int Rounds = 4;
	internal const int BatchRequestsPerRound = 8;
	internal const int RequestsPerRound = BatchRequestsPerRound + 5;
	private static readonly (string Category, string Client)[] s_observers =
		[("discovery", "gateway-0"), ("host-b-sentinel", "gateway-1"),
		 ("ui-snapshot", "ce-ui-timer"), ("regions-resource", "gateway-1")];

	internal static LiveAdmissionAssessment Compare(IReadOnlyList<LiveAdmissionSession> baselines,
		LiveAdmissionSession candidate)
	{
		ArgumentNullException.ThrowIfNull(baselines);
		if (baselines.Count != LivePerformanceQualification.RequiredBaselineRuns)
		{
			throw new InvalidOperationException("Admission comparisons require exactly three baseline sessions.");
		}
		List<string> missing = [];
		foreach (LiveAdmissionSession session in baselines.Append(candidate))
		{
			Validate(session);
			if (!session.Bursts.Any(burst => LiveAdmissionMeasurement.Analyze(
				burst.Samples.Where(static sample => sample.Category == "batch").ToArray()).CrossClientOverlap))
			{
				missing.Add("A session did not observe overlapping batch requests from both gateways.");
			}
			foreach (LiveAdmissionBurst burst in session.Bursts)
			{
				LiveAdmissionSample[] work = burst.Samples.Where(static sample => sample.Category == "batch"
					&& sample.Outcome.Kind == LiveAdmissionOutcomeKind.Succeeded).ToArray();
				foreach (LiveAdmissionSample sample in burst.Samples.Where(static sample => sample.Category != "batch"
					|| sample.Outcome.Kind == LiveAdmissionOutcomeKind.Busy))
				{
					if (!work.Any(batch => Overlaps(sample, batch)))
					{
						missing.Add($"'{sample.Id}' did not overlap a successful batch request; its latency is not under-load evidence.");
					}
				}
			}
		}

		HashSet<(string Category, LiveAdmissionOutcomeKind Outcome)> keys = baselines.Append(candidate)
			.SelectMany(static session => session.Bursts).SelectMany(static burst => burst.Samples)
			.Select(static sample => (sample.Category, sample.Outcome.Kind)).ToHashSet();
		// Both successful work and a real refusal need their own observations. An empty completion cannot replace either.
		keys.Add(("batch", LiveAdmissionOutcomeKind.Succeeded));
		keys.Add(("batch", LiveAdmissionOutcomeKind.Busy));
		keys.Add(("regions-resource", LiveAdmissionOutcomeKind.Succeeded));
		keys.Add(("regions-resource", LiveAdmissionOutcomeKind.Busy));
		keys.Add(("module-completion-first", LiveAdmissionOutcomeKind.Succeeded));
		keys.Add(("module-completion-repeat", LiveAdmissionOutcomeKind.Succeeded));
		List<LiveAdmissionMetric> metrics = [];
		foreach ((string category, LiveAdmissionOutcomeKind outcome) in keys.OrderBy(static key => key.Category, StringComparer.Ordinal)
			.ThenBy(static key => key.Outcome))
		{
			LiveLatencyProfile?[] baselineProfiles = baselines.Select(session => Profile(session, category, outcome)).ToArray();
			LiveLatencyProfile? candidateProfile = Profile(candidate, category, outcome);
			if (candidateProfile is null || baselineProfiles.Any(static profile => profile is null))
			{
				string reason = $"'{category}/{outcome}' lacks an observation in the candidate or one of the three baselines.";
				missing.Add(reason);
				metrics.Add(new LiveAdmissionMetric(category, outcome, null, reason));
				continue;
			}
			LiveBaselineProfile baseline = LivePerformanceQualification.Baseline(baselineProfiles.Select(static profile => profile!));
			LivePerformanceDecision decision = category == "batch" && outcome == LiveAdmissionOutcomeKind.Succeeded
				? LivePerformanceQualification.EvaluateBoundedJobs(baseline, candidateProfile)
				: LivePerformanceQualification.EvaluateShortCalls(baseline, candidateProfile);
			metrics.Add(new LiveAdmissionMetric(category, outcome, decision, null));
		}
		return new LiveAdmissionAssessment(metrics, missing);
	}

	internal static void Validate(LiveAdmissionSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		if (session.Bursts.Count != Rounds)
		{
			throw new InvalidOperationException("An admission session must contain all four fixed rounds.");
		}
		HashSet<string> ids = new(StringComparer.Ordinal);
		for (int round = 0; round < session.Bursts.Count; round++)
		{
			LiveAdmissionBurst burst = session.Bursts[round];
			if (burst.Samples.Count != RequestsPerRound || burst.Samples.Any(sample =>
				string.IsNullOrWhiteSpace(sample.Id) || !ids.Add(sample.Id)))
			{
				throw new InvalidOperationException("Each admission round must contain thirteen uniquely identified started requests.");
			}
			LiveAdmissionBurst measured = LiveAdmissionMeasurement.Analyze(burst.Samples);
			if (measured.MaximumConcurrentRequests != burst.MaximumConcurrentRequests || measured.CrossClientOverlap != burst.CrossClientOverlap)
			{
				throw new InvalidOperationException("Admission concurrency must match the recorded request intervals.");
			}
			foreach (string client in new[] { "gateway-0", "gateway-1" })
			{
				if (burst.Samples.Count(sample => sample.Category == "batch" && sample.ClientId == client) != BatchRequestsPerRound / 2)
				{
					throw new InvalidOperationException("Each gateway must issue exactly four batch requests per round.");
				}
			}
			foreach ((string category, string client) in s_observers.Append(
				(round == 0 ? "module-completion-first" : "module-completion-repeat", "gateway-0")))
			{
				if (burst.Samples.Count(sample => sample.Category == category && sample.ClientId == client) != 1)
				{
					throw new InvalidOperationException($"The fixed admission observer '{category}' is missing or repeated.");
				}
			}
			if (burst.Samples.Any(static sample => !ExpectedOutcome(sample)))
			{
				throw new InvalidOperationException("Admission observations contain unexpected failures or incomplete requests.");
			}
		}
	}

	internal static bool ExpectedOutcome(LiveAdmissionSample sample)
	{
		return sample.Outcome.Kind == LiveAdmissionOutcomeKind.Succeeded
			|| (sample.Outcome.Kind == LiveAdmissionOutcomeKind.Busy && sample.Outcome.ErrorKind == "busy"
				&& sample.Outcome.HostEffect == "not_started" && sample.Category is "batch" or "regions-resource")
			|| (sample.Outcome.Kind == LiveAdmissionOutcomeKind.EmptyCompletion
				&& sample.Category.StartsWith("module-completion-", StringComparison.Ordinal));
	}

	private static LiveLatencyProfile? Profile(LiveAdmissionSession session, string category, LiveAdmissionOutcomeKind outcome)
	{
		TimeSpan[] durations = session.Bursts.SelectMany(static burst => burst.Samples)
			.Where(sample => sample.Category == category && sample.Outcome.Kind == outcome)
			.Select(static sample => Stopwatch.GetElapsedTime(sample.StartedTimestamp, sample.FinishedTimestamp)).ToArray();
		return durations.Length == 0 ? null : LivePerformanceQualification.Profile(durations);
	}

	private static bool Overlaps(LiveAdmissionSample sample, LiveAdmissionSample work)
	{
		return sample.StartedTimestamp < sample.FinishedTimestamp && work.StartedTimestamp < work.FinishedTimestamp
			&& sample.StartedTimestamp < work.FinishedTimestamp && work.StartedTimestamp < sample.FinishedTimestamp;
	}
}
