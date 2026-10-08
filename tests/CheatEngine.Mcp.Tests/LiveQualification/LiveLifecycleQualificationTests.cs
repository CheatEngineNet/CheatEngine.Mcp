namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LiveLifecycleQualificationTests
{
	[Fact]
	public void PerformanceProfile_UsesRecordedBaselineForCandidateThresholds()
	{
		LiveBaselineProfile baseline = LivePerformanceQualification.Baseline(
		[
			LivePerformanceQualification.Profile([TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20)]),
			LivePerformanceQualification.Profile([TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(30)]),
			LivePerformanceQualification.Profile([TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(40)])
		]);

		LivePerformanceDecision shortCalls = LivePerformanceQualification.EvaluateShortCalls(baseline,
			LivePerformanceQualification.Profile([TimeSpan.FromMilliseconds(139)]));
		LivePerformanceDecision jobs = LivePerformanceQualification.EvaluateBoundedJobs(baseline,
			LivePerformanceQualification.Profile([TimeSpan.FromMilliseconds(61)]));

		Assert.True(shortCalls.Passed);
		Assert.Equal(TimeSpan.FromMilliseconds(140), shortCalls.MaximumP95);
		Assert.False(jobs.Passed);
		Assert.Equal(TimeSpan.FromMilliseconds(60), jobs.MaximumP95);
		Assert.False(LivePerformanceQualification.EvaluateShortCalls(baseline,
			LivePerformanceQualification.Profile([TimeSpan.FromMilliseconds(140).Add(TimeSpan.FromTicks(1))])).Passed);
		LiveBaselineProfile slowerBaseline = LivePerformanceQualification.Baseline(
		[
			LivePerformanceQualification.Profile([TimeSpan.FromMilliseconds(120)]),
			LivePerformanceQualification.Profile([TimeSpan.FromMilliseconds(140)]),
			LivePerformanceQualification.Profile([TimeSpan.FromMilliseconds(130)])
		]);
		Assert.Equal(TimeSpan.FromMilliseconds(280),
			LivePerformanceQualification.EvaluateShortCalls(slowerBaseline,
				LivePerformanceQualification.Profile([TimeSpan.FromMilliseconds(1)])).MaximumP95);
	}

	[Fact]
	public void PerformanceProfile_RejectsCandidateWithoutThreeBaselineRuns()
	{
		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
			LivePerformanceQualification.Baseline([LivePerformanceQualification.Profile([TimeSpan.FromMilliseconds(1)])]));
		Assert.Contains("Exactly 3", exception.Message, StringComparison.Ordinal);
		Assert.Throws<ArgumentException>(() => LivePerformanceQualification.Profile([]));
		Assert.Throws<ArgumentException>(() => LivePerformanceQualification.Profile([TimeSpan.FromTicks(-1)]));
		Assert.Throws<InvalidOperationException>(() => LivePerformanceQualification.Baseline(
			Enumerable.Repeat(LivePerformanceQualification.Profile([TimeSpan.Zero]), 4)));
	}

	[Fact]
	public void SoakSchedule_ContainsEveryFixedWorkloadAndFitsTheReviewedDeadline()
	{
		IReadOnlyList<LiveSoakAction> schedule = LiveSoakQualification.CreateSchedule();
		Assert.Equal(LiveSoakQualification.RequiredShortCalls, schedule.Count(action => action.Kind == LiveSoakActionKind.ShortCall));
		Assert.Equal(LiveSoakQualification.RequiredBoundedJobs, schedule.Count(action => action.Kind == LiveSoakActionKind.BoundedJob));
		Assert.Equal(LiveSoakQualification.RequiredPluginCycles, schedule.Count(action => action.Kind == LiveSoakActionKind.PluginCycle));
		Assert.Equal(LiveSoakQualification.RequiredTargetRestarts, schedule.Count(action => action.Kind == LiveSoakActionKind.TargetRestart));
		LiveSoakAction gatewayRestart = Assert.Single(schedule,
			action => action.Kind == LiveSoakActionKind.GatewayRestart);
		Assert.Equal(LiveSoakActionKind.GatewayRestart, gatewayRestart.Kind);
		Assert.All(schedule, action => Assert.InRange(action.At, TimeSpan.Zero, LiveSoakQualification.WorkloadDuration));
		Assert.Equal(schedule.OrderBy(action => action.At).ThenBy(action => action.Kind), schedule);
		Assert.Equal(LiveSoakQualification.WorkloadDuration, schedule.Max(action => action.At));
		Assert.True(LiveSoakQualification.WorkloadDuration < LiveSoakQualification.MaximumLifetime);
	}

	[Fact]
	public void LifecycleEvidence_DistinguishesInProcessReloadFromHostRestart()
	{
		LiveSandboxHost before = Host("A", 10, 20, "one", "http://127.0.0.1:1001/");
		LiveSandboxHost reloaded = Host("A", 10, 20, "two", "http://127.0.0.1:1002/");
		LiveSandboxHost restarted = Host("A", 11, 21, "three", "http://127.0.0.1:1003/");
		LiveLifecycleQualification.RequirePluginReload(before, reloaded);
		LiveLifecycleQualification.RequireHostRestart(before, restarted, Host("B", 12, 22, "b", "http://127.0.0.1:1004/"));
		Assert.Throws<InvalidOperationException>(() => LiveLifecycleQualification.RequirePluginReload(before, restarted));
	}

	[Fact]
	public void ResourceThresholds_RejectMetricGrowthOverAcceptanceLimits()
	{
		LiveProcessMetrics baseline = new(DateTimeOffset.UtcNow, 1, 100, 1024, 2048, null);
		LiveResourceDecision accepted = LiveSoakQualification.EvaluateResources(baseline,
			baseline with
			{
				HandleCount = 125,
				PrivateBytes = 1024 + (64L * 1024 * 1024)
			});
		LiveResourceDecision rejected = LiveSoakQualification.EvaluateResources(baseline,
			baseline with
			{
				HandleCount = 126
			});
		LiveResourceDecision overPrivateLimit = LiveSoakQualification.EvaluateResources(baseline,
			baseline with
			{
				PrivateBytes = 1024 + (64L * 1024 * 1024) + 1
			});
		LiveResourceDecision changedProcess = LiveSoakQualification.EvaluateResources(baseline,
			baseline with
			{
				ProcessId = 2
			});
		LiveResourceDecision unavailable = LiveSoakQualification.EvaluateResources(baseline,
			baseline with
			{
				CollectionError = "access denied"
			});
		Assert.True(accepted.Passed);
		Assert.False(rejected.Passed);
		Assert.False(overPrivateLimit.Passed);
		Assert.False(changedProcess.Passed);
		Assert.False(unavailable.Passed);
	}

	private static LiveSandboxHost Host(string name, int processId, int targetProcessId, string instanceId, string endpoint) =>
		new(name, processId, targetProcessId, "0x1", "0x2", "0x3", "0x4", "0x5", "plugin", instanceId, endpoint);
}
