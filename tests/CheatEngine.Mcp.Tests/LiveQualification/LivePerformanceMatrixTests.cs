namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LivePerformanceMatrixTests
{
	[Fact]
	public void Matrix_DefinesEveryFixed901WorkloadWithBoundedSamples()
	{
		IReadOnlyList<LivePerformanceWorkload> matrix = LivePerformanceQualification.Workloads;

		Assert.Equal(matrix.Count, matrix.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count());
		Assert.All(matrix, item =>
		{
			Assert.True(item.SampleCount > 0);
			Assert.True(item.WarmupCount >= 0);
			Assert.False(string.IsNullOrWhiteSpace(item.Method));
			Assert.False(string.IsNullOrWhiteSpace(item.Limits));
		});
		Assert.Contains(matrix, item => item.Name == "memory_read_batch_16" && item.Limits.Contains("16", StringComparison.Ordinal));
		Assert.Contains(matrix, item => item.Name == "memory_read_batch_1024" && item.Limits.Contains("4 KiB", StringComparison.Ordinal));
		Assert.Contains(matrix, item => item.Name == "code_dissect_whole_cycle" && item.BudgetClass == LivePerformanceBudgetClass.WholeCycle);
		Assert.Contains(matrix, item => item.Name == "named_scan_64" && item.BudgetClass == LivePerformanceBudgetClass.WholeCycle);
	}

	[Fact]
	public void Pair_RefusesBaselineMissingOneMatrixWorkload()
	{
		LivePerformanceRun run = Run(TimeSpan.FromMilliseconds(10));
		Dictionary<string, LiveBaselineProfile> baseline = LivePerformanceQualification.Baselines([run, run, run])
			.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
		baseline.Remove("aob_find_64");

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
			LivePerformanceQualification.Pair(baseline, run));

		Assert.Contains("cannot count as a pass", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Pair_UsesShortAllowanceOnlyForShortWorkload()
	{
		LivePerformanceRun baselineRun = Run(TimeSpan.FromMilliseconds(100));
		IReadOnlyDictionary<string, LiveBaselineProfile> baseline = LivePerformanceQualification.Baselines(
			[baselineRun, baselineRun, baselineRun]);
		LivePerformanceRun candidate = Run(TimeSpan.FromMilliseconds(151));

		IReadOnlyList<LivePerformanceComparison> comparisons = LivePerformanceQualification.Pair(baseline, candidate);

		Assert.True(Assert.Single(comparisons, item => item.Name == "memory_read_int32").Decision.Passed);
		Assert.False(Assert.Single(comparisons, item => item.Name == "aob_find_64").Decision.Passed);
	}

	[Fact]
	public void Pair_RefusesCandidateWithWrongFixedSampleCount()
	{
		LivePerformanceRun valid = Run(TimeSpan.FromMilliseconds(10));
		Dictionary<string, LiveLatencyProfile> malformed = valid.Workloads.ToDictionary(
			static pair => pair.Key,
			static pair => pair.Value,
			StringComparer.Ordinal);
		malformed["memory_read_batch_16"] = LivePerformanceQualification.Profile([TimeSpan.FromMilliseconds(10)]);

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
			LivePerformanceQualification.Pair(LivePerformanceQualification.Baselines([valid, valid, valid]),
				new LivePerformanceRun(malformed)));

		Assert.Contains("wrong fixed sample count", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Pair_RefusesBaselineWithWrongFixedSampleCount()
	{
		LivePerformanceRun run = Run(TimeSpan.FromMilliseconds(10));
		Dictionary<string, LiveBaselineProfile> baseline = LivePerformanceQualification.Baselines([run, run, run])
			.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
		LiveLatencyProfile malformed = LivePerformanceQualification.Profile([TimeSpan.FromMilliseconds(10)]);
		baseline["memory_read_batch_16"] = new LiveBaselineProfile([malformed, malformed, malformed], malformed.P95);

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
			LivePerformanceQualification.Pair(baseline, run));

		Assert.Contains("valid fixed matrix profile", exception.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public async Task RunWithCleanupAsync_PreservesBothFailuresAndAlwaysReleases(bool operationFails, bool cleanupFails)
	{
		InvalidOperationException operationFailure = new("scan failed");
		InvalidOperationException cleanupFailure = new("delete failed");
		List<string> calls = [];

		Exception? result = await Record.ExceptionAsync(() => LivePerformanceQualification.RunWithCleanupAsync(
			() =>
			{
				calls.Add("scan");
				return operationFails ? Task.FromException(operationFailure) : Task.CompletedTask;
			},
			() =>
			{
				calls.Add("delete");
				return cleanupFails ? Task.FromException(cleanupFailure) : Task.CompletedTask;
			}));

		Assert.Equal(["scan", "delete"], calls);
		if (cleanupFails)
		{
			AggregateException combined = Assert.IsType<AggregateException>(result);
			Assert.Equal(operationFails ? new Exception[] { operationFailure, cleanupFailure } : [cleanupFailure],
				combined.InnerExceptions);
		}
		else
		{
			Assert.Same(operationFails ? operationFailure : null, result);
		}
	}

	private static LivePerformanceRun Run(TimeSpan value)
	{
		Dictionary<string, LiveLatencyProfile> profiles = LivePerformanceQualification.Workloads.ToDictionary(
			static workload => workload.Name,
			workload => LivePerformanceQualification.Profile(Enumerable.Repeat(value, workload.SampleCount)),
			StringComparer.Ordinal);
		return LivePerformanceQualification.CreateRun(profiles);
	}
}
