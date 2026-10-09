using System.Diagnostics;
using System.Text.Json;

namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LiveAdmissionComparisonTests
{
	[Fact]
	public void Compare_RequiresObservedSuccessRefusalOverlapAndThreeBaselines()
	{
		LiveAdmissionSession baseline = Session();

		LiveAdmissionAssessment result = LiveAdmissionComparison.Compare([baseline, baseline, baseline], Session());

		Assert.True(result.Passed);
		Assert.Empty(result.MissingEvidence);
		Assert.Equal(9, result.Metrics.Count);
		Assert.All(result.Metrics, metric => Assert.True(metric.Decision!.Passed));
		using JsonDocument receipt = JsonDocument.Parse(JsonSerializer.Serialize(result));
		Assert.True(receipt.RootElement.GetProperty("Passed").GetBoolean());
		Assert.Throws<InvalidOperationException>(() => LiveAdmissionComparison.Compare([baseline, baseline], Session()));
	}

	[Fact]
	public void Compare_FastSuccessfulRequestsCannotReplaceMissingRefusals()
	{
		LiveAdmissionSession baseline = Session();
		LiveAdmissionSession candidate = Map(Session(), sample => sample with { Outcome = Success() });

		LiveAdmissionAssessment result = LiveAdmissionComparison.Compare([baseline, baseline, baseline], candidate);

		Assert.False(result.Passed);
		Assert.Equal(2, result.MissingEvidence.Count);
		Assert.All(result.Metrics.Where(metric => metric.Outcome == LiveAdmissionOutcomeKind.Busy), metric =>
		{
			Assert.Null(metric.Decision);
			Assert.NotNull(metric.MissingEvidence);
		});
	}

	[Fact]
	public void Compare_OneBaselineMissingAnOutcomeCannotBeReplacedByTheOtherTwo()
	{
		LiveAdmissionSession baseline = Session();
		LiveAdmissionSession incomplete = Map(Session(), sample => sample.Category == "regions-resource"
			? sample with
			{
				Outcome = Success()
			} : sample);

		LiveAdmissionAssessment result = LiveAdmissionComparison.Compare([baseline, incomplete, baseline], Session());

		Assert.False(result.Passed);
		Assert.Contains("regions-resource/Busy", Assert.Single(result.MissingEvidence), StringComparison.Ordinal);
	}

	[Fact]
	public void Compare_EmptyCompletionDoesNotProveSuccessfulNativeListing()
	{
		LiveAdmissionSession baseline = Session();
		LiveAdmissionSession candidate = Map(Session(), sample => sample.Category.StartsWith("module-completion-", StringComparison.Ordinal)
			? sample with
			{
				Outcome = new(LiveAdmissionOutcomeKind.EmptyCompletion)
			} : sample);

		LiveAdmissionAssessment result = LiveAdmissionComparison.Compare([baseline, baseline, baseline], candidate);

		Assert.False(result.Passed);
		Assert.All(result.Metrics.Where(metric => metric.Category.StartsWith("module-completion-", StringComparison.Ordinal)),
			metric => Assert.NotNull(metric.MissingEvidence));
	}

	[Fact]
	public void Compare_UiOverlapCannotReplaceCrossGatewayBatchOverlap()
	{
		LiveAdmissionSession baseline = Session();
		LiveAdmissionSession candidate = Map(Session(), sample => sample.Category == "batch" && sample.ClientId == "gateway-1"
			? sample with
			{
				StartedTimestamp = sample.StartedTimestamp + Ticks(500),
				FinishedTimestamp = sample.FinishedTimestamp + Ticks(500)
			}
			: sample);
		Assert.All(candidate.Bursts, burst => Assert.True(burst.CrossClientOverlap));

		LiveAdmissionAssessment result = LiveAdmissionComparison.Compare([baseline, baseline, baseline], candidate);

		Assert.False(result.Passed);
		Assert.Contains("overlapping batch", Assert.Single(result.MissingEvidence), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("batch", "Succeeded", 150, true)]
	[InlineData("batch", "Succeeded", 151, false)]
	[InlineData("batch", "Busy", 200, true)]
	[InlineData("batch", "Busy", 201, false)]
	[InlineData("regions-resource", "Succeeded", 200, true)]
	[InlineData("regions-resource", "Succeeded", 201, false)]
	public void Compare_AppliesResponseSpecificBudgetAtBoundary(string category, string outcomeName,
		int milliseconds, bool expected)
	{
		LiveAdmissionOutcomeKind outcome = Enum.Parse<LiveAdmissionOutcomeKind>(outcomeName);
		LiveAdmissionSession baseline = Session();
		LiveAdmissionSession candidate = Map(Session(), sample => sample.Category == category && sample.Outcome.Kind == outcome
			? sample with
			{
				FinishedTimestamp = sample.StartedTimestamp + Ticks(milliseconds)
			} : sample);

		LiveAdmissionAssessment result = LiveAdmissionComparison.Compare([baseline, baseline, baseline], candidate);

		Assert.Equal(expected, result.Passed);
		Assert.Equal(expected, Assert.Single(result.Metrics, metric => metric.Category == category && metric.Outcome == outcome).Decision!.Passed);
	}

	[Theory]
	[InlineData(false, 20)]
	[InlineData(true, 8)]
	public void Compare_RejectsUnloadedObserverAndBusyTiming(bool moveBusy, int missingCount)
	{
		LiveAdmissionSession baseline = Session();
		LiveAdmissionSession candidate = Map(Session(), sample =>
			(moveBusy ? sample.Category == "batch" && sample.Outcome.Kind == LiveAdmissionOutcomeKind.Busy : sample.Category != "batch")
				? sample with
				{
					StartedTimestamp = sample.StartedTimestamp + Ticks(500),
					FinishedTimestamp = sample.FinishedTimestamp + Ticks(500)
				}
				: sample);

		LiveAdmissionAssessment result = LiveAdmissionComparison.Compare([baseline, baseline, baseline], candidate);

		Assert.False(result.Passed);
		Assert.Equal(missingCount, result.MissingEvidence.Count);
		Assert.All(result.MissingEvidence, gap => Assert.Contains("not under-load evidence", gap, StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("round")]
	[InlineData("request")]
	[InlineData("duplicate")]
	[InlineData("concurrency")]
	[InlineData("overlap")]
	[InlineData("observer-client")]
	[InlineData("batch-client")]
	[InlineData("busy-effect")]
	[InlineData("timeout")]
	public void Validate_RejectsIncompleteOrMisreportedEvidence(string defect)
	{
		LiveAdmissionSession session = Session();
		LiveAdmissionBurst[] bursts = session.Bursts.ToArray();
		LiveAdmissionSample[] samples = bursts[0].Samples.ToArray();
		switch (defect)
		{
			case "round":
				bursts = bursts[..3];
				break;
			case "request":
				bursts[0] = LiveAdmissionMeasurement.Analyze(samples[..12]);
				break;
			case "duplicate":
				samples[1] = samples[1] with
				{
					Id = samples[0].Id
				};
				bursts[0] = LiveAdmissionMeasurement.Analyze(samples);
				break;
			case "concurrency":
				bursts[0] = bursts[0] with
				{
					MaximumConcurrentRequests = 1
				};
				break;
			case "overlap":
				bursts[0] = bursts[0] with
				{
					CrossClientOverlap = false
				};
				break;
			case "observer-client":
				samples[8] = samples[8] with
				{
					ClientId = "gateway-1"
				};
				bursts[0] = LiveAdmissionMeasurement.Analyze(samples);
				break;
			case "batch-client":
				samples[0] = samples[0] with
				{
					ClientId = "gateway-1"
				};
				bursts[0] = LiveAdmissionMeasurement.Analyze(samples);
				break;
			case "busy-effect":
				samples[0] = samples[0] with
				{
					Outcome = new(LiveAdmissionOutcomeKind.Busy, "busy", "started")
				};
				bursts[0] = LiveAdmissionMeasurement.Analyze(samples);
				break;
			case "timeout":
				samples[0] = samples[0] with
				{
					Outcome = new(LiveAdmissionOutcomeKind.Timeout)
				};
				bursts[0] = LiveAdmissionMeasurement.Analyze(samples);
				break;
		}

		Assert.Throws<InvalidOperationException>(() => LiveAdmissionComparison.Validate(new LiveAdmissionSession(bursts)));
	}

	private static LiveAdmissionSession Session()
	{
		List<LiveAdmissionBurst> bursts = [];
		for (int round = 0; round < 4; round++)
		{
			long started = Ticks(1000 * (round + 1));
			List<LiveAdmissionSample> samples = [];
			for (int index = 0; index < 8; index++)
			{
				samples.Add(new($"{round}-batch-{index}", $"gateway-{index % 2}", "batch", started, started + Ticks(100),
					index < 2 ? Busy() : Success()));
			}
			(string Category, string Client)[] observers =
			[
				("discovery", "gateway-0"), ("host-b-sentinel", "gateway-1"), ("ui-snapshot", "ce-ui-timer"),
				("regions-resource", "gateway-1"), (round == 0 ? "module-completion-first" : "module-completion-repeat", "gateway-0")
			];
			foreach ((string category, string client) in observers)
			{
				samples.Add(new($"{round}-{category}", client, category, started, started + Ticks(100),
					category == "regions-resource" && round % 2 == 0 ? Busy() : Success()));
			}
			bursts.Add(LiveAdmissionMeasurement.Analyze(samples));
		}
		return new LiveAdmissionSession(bursts);
	}

	private static LiveAdmissionSession Map(LiveAdmissionSession session, Func<LiveAdmissionSample, LiveAdmissionSample> transform)
	{
		return new(session.Bursts.Select(burst => LiveAdmissionMeasurement.Analyze(burst.Samples.Select(transform).ToArray())).ToArray());
	}

	private static long Ticks(int milliseconds) => Stopwatch.Frequency * milliseconds / 1000;
	private static LiveAdmissionOutcome Success() => new(LiveAdmissionOutcomeKind.Succeeded);
	private static LiveAdmissionOutcome Busy() => new(LiveAdmissionOutcomeKind.Busy, "busy", "not_started");
}
