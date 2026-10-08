using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Deterministic latency aggregation and acceptance checks for the reviewed live workloads.</summary>
internal static class LivePerformanceQualification
{
	internal const int RequiredBaselineRuns = 3;
	internal static readonly TimeSpan ShortCallAllowance = TimeSpan.FromMilliseconds(100);
	private const int ShortSamples = 100;
	private const int BatchSamples = 30;
	private const int BatchSize = 16;

	[SupportedOSPlatform("windows")]
	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.Equal(LiveQualificationScenario.Performance, inputs.Scenario);
		string baselineDirectory = Path.Combine(inputs.RepositoryRoot, "artifacts", "qualification-baseline", "beta.2", "staged");
		const string baselineVersion = "2.0.0-beta.2+c51a0ec3af373c852cda062a91888ad94fa0cce9";
		Assert.Equal(baselineVersion, FileVersionInfo.GetVersionInfo(Path.Combine(baselineDirectory, "CheatEngine.Mcp.dll")).ProductVersion);
		foreach ((string name, string expectedHash) in new[]
		{
			("CheatEngine.Mcp.dll", "5699ECA2ED06461846A977802DF3E4FA7BF52159F50AA1446C4819908A7692AE"),
			("CheatEngine.Mcp.Gateway.exe", "2E0E7F8512D8A8AAC0197E982C7660BFA4336EBDB8981543817B709381C83885")
		})
		{
			using FileStream binary = File.OpenRead(Path.Combine(baselineDirectory, name));
			Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(binary)));
		}
		LivePerformanceRun[] baselineRuns = new LivePerformanceRun[RequiredBaselineRuns];
		for (int index = 0; index < baselineRuns.Length; index++)
		{
			baselineRuns[index] = await MeasureSessionAsync(inputs, baselineDirectory, index + 1, null);
		}
		LiveBaselineProfile shortBaseline = Baseline(baselineRuns.Select(run => run.ShortCalls));
		LiveBaselineProfile batchBaseline = Baseline(baselineRuns.Select(run => run.BatchCalls));
		for (int index = 0; index < RequiredBaselineRuns; index++)
		{
			_ = await MeasureSessionAsync(inputs, null, index + 1, (shortBaseline, batchBaseline));
		}
	}

	[SupportedOSPlatform("windows")]
	private static async Task<LivePerformanceRun> MeasureSessionAsync(LiveQualificationInputs inputs,
		string? distributionOverride, int runNumber, (LiveBaselineProfile Short, LiveBaselineProfile Batch)? baseline)
	{
		LiveSandboxOptions options = LiveSandboxOptions.Smoke with
		{
			PerformanceQualification = true,
			DistributionDirectoryOverride = distributionOverride
		};
		await using LiveSandboxSession sandbox = await LiveSandboxSession.StartAsync(inputs, options);
		await using LiveMcpClient gateway = await LiveMcpClient.ConnectGatewayAsync(sandbox.GatewayExecutablePath,
			sandbox.InstanceDirectory, TestContext.Current.CancellationToken);
		LiveMcpInstanceClient a = gateway.Bind(sandbox.HostA.InstanceId);
		LiveMcpInstanceClient b = gateway.Bind(sandbox.HostB.InstanceId);
		foreach ((LiveMcpInstanceClient client, LiveSandboxHost host) in new[] { (a, sandbox.HostA), (b, sandbox.HostB) })
		{
			JsonNode runtime = (await client.CallToolAsync(CheatEngineToolNames.RuntimeGetInfo))!;
			foreach (string gate in new[] { "unsafeLua", "autoAssembler", "kernelAccess", "targetCodeExecution" })
			{
				Assert.False(runtime["gates"]![gate]!.GetValue<bool>());
			}
			JsonNode opened = (await client.CallToolAsync(CheatEngineToolNames.ProcessAttach,
				new Dictionary<string, object?> { ["process"] = host.TargetProcessId.ToString(CultureInfo.InvariantCulture) }))!;
			Assert.Equal(host.TargetProcessId, opened["processId"]!.GetValue<int>());
		}

		IReadOnlyDictionary<string, object?> shortArguments = new Dictionary<string, object?>
		{
			["address"] = sandbox.HostA.TargetAddress,
			["valueType"] = "int32"
		};
		IReadOnlyDictionary<string, object?> batchArguments = new Dictionary<string, object?>
		{
			["items"] = Enumerable.Range(0, BatchSize)
				.Select(_ => new { address = sandbox.HostA.TargetAddress, valueType = "int32" }).ToArray()
		};
		async Task ShortCall()
		{
			JsonNode result = (await a.CallToolAsync(CheatEngineToolNames.MemoryRead, shortArguments))!;
			Assert.Equal("20260926", result["value"]!.GetValue<string>());
		}
		async Task BatchCall()
		{
			JsonNode result = (await a.CallToolAsync(CheatEngineToolNames.MemoryReadBatch, batchArguments))!;
			Assert.Equal(0, result["failed"]!.GetValue<int>());
			JsonArray items = result["items"]!.AsArray();
			Assert.Equal(BatchSize, items.Count);
			Assert.All(items, item => Assert.Equal("20260926", item!["value"]!.GetValue<string>()));
		}
		// The same five warm-up pairs are excluded from every recorded profile.
		for (int index = 0; index < 5; index++)
		{
			await ShortCall();
			await BatchCall();
		}
		LiveLatencyProfile shortCalls = await MeasureAsync(ShortSamples, ShortCall, TestContext.Current.CancellationToken);
		LiveLatencyProfile batchCalls = await MeasureAsync(BatchSamples, BatchCall, TestContext.Current.CancellationToken);
		JsonNode unaffected = (await b.CallToolAsync(CheatEngineToolNames.MemoryRead,
			new Dictionary<string, object?> { ["address"] = sandbox.HostB.TargetAddress, ["valueType"] = "int32" }))!;
		Assert.Equal("20260926", unaffected["value"]!.GetValue<string>());
		LivePerformanceDecision? shortDecision = baseline is { } first ? EvaluateShortCalls(first.Short, shortCalls) : null;
		LivePerformanceDecision? batchDecision = baseline is { } second ? EvaluateBoundedJobs(second.Batch, batchCalls) : null;
		sandbox.Record("performance", new
		{
			kind = baseline is null ? "published_beta2_baseline" : "candidate",
			runNumber,
			productVersion = FileVersionInfo.GetVersionInfo(sandbox.GatewayExecutablePath).ProductVersion,
			method = "Stopwatch full stdio request, response parse and value assertion; five unrecorded warm-up pairs; nearest-rank p95; maximum of three baseline p95s",
			shortWorkload = "memory_read int32",
			shortCalls,
			batchWorkload = "memory_read_batch of 16 int32 requests",
			batchCalls,
			baselineShort = baseline?.Short,
			baselineBatch = baseline?.Batch,
			shortDecision,
			batchDecision,
			limitation = "These results qualify the two recorded workloads only; retained jobs, completions and blocking-native calls are separate."
		});
		if (shortDecision is not null && batchDecision is not null)
		{
			Assert.True(shortDecision.Passed, $"Short-call p95 {shortDecision.ObservedP95} exceeds {shortDecision.MaximumP95}.");
			Assert.True(batchDecision.Passed, $"Batch p95 {batchDecision.ObservedP95} exceeds {batchDecision.MaximumP95}.");
		}
		sandbox.MarkPassed();
		return new LivePerformanceRun(shortCalls, batchCalls);
	}

	internal static LiveLatencyProfile Profile(IEnumerable<TimeSpan> samples)
	{
		ArgumentNullException.ThrowIfNull(samples);
		long[] ticks = samples.Select(sample => sample.Ticks).Order().ToArray();
		if (ticks.Length == 0 || ticks.Any(value => value < 0))
		{
			throw new ArgumentException("Latency samples must be non-empty and non-negative.", nameof(samples));
		}

		return new LiveLatencyProfile(ticks.Length, TimeSpan.FromTicks(ticks[(ticks.Length - 1) / 2]),
			TimeSpan.FromTicks(ticks[(int) Math.Ceiling(ticks.Length * 0.95) - 1]), TimeSpan.FromTicks(ticks[^1]));
	}

	internal static LiveBaselineProfile Baseline(IEnumerable<LiveLatencyProfile> runs)
	{
		ArgumentNullException.ThrowIfNull(runs);
		LiveLatencyProfile[] captured = runs.ToArray();
		if (captured.Length != RequiredBaselineRuns)
		{
			throw new InvalidOperationException($"Exactly {RequiredBaselineRuns} separately recorded baseline runs are required.");
		}

		if (captured.Any(run => run.SampleCount == 0))
		{
			throw new InvalidOperationException("A baseline run contained no latency samples.");
		}

		return new LiveBaselineProfile(captured, captured.Max(run => run.P95));
	}

	internal static LivePerformanceDecision EvaluateShortCalls(LiveBaselineProfile baseline, LiveLatencyProfile candidate)
	{
		ArgumentNullException.ThrowIfNull(baseline);
		ArgumentNullException.ThrowIfNull(candidate);
		TimeSpan threshold = Max(Times(baseline.P95, 2), baseline.P95 + ShortCallAllowance);
		return new LivePerformanceDecision(candidate.P95 <= threshold, candidate.P95, threshold);
	}

	internal static LivePerformanceDecision EvaluateBoundedJobs(LiveBaselineProfile baseline, LiveLatencyProfile candidate)
	{
		ArgumentNullException.ThrowIfNull(baseline);
		ArgumentNullException.ThrowIfNull(candidate);
		TimeSpan threshold = Times(baseline.P95, 1.5);
		return new LivePerformanceDecision(candidate.P95 <= threshold, candidate.P95, threshold);
	}

	internal static async Task<LiveLatencyProfile> MeasureAsync(int count, Func<Task> operation,
		CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
		ArgumentNullException.ThrowIfNull(operation);
		TimeSpan[] samples = new TimeSpan[count];
		for (int index = 0; index < samples.Length; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			long started = Stopwatch.GetTimestamp();
			await operation();
			samples[index] = Stopwatch.GetElapsedTime(started);
		}

		return Profile(samples);
	}

	private static TimeSpan Times(TimeSpan value, double multiplier) => TimeSpan.FromTicks(
		checked((long) Math.Ceiling(value.Ticks * multiplier)));

	private static TimeSpan Max(TimeSpan first, TimeSpan second) => first >= second ? first : second;
}

internal sealed record LiveLatencyProfile(int SampleCount, TimeSpan P50, TimeSpan P95, TimeSpan Maximum);

internal sealed record LiveBaselineProfile(IReadOnlyList<LiveLatencyProfile> Runs, TimeSpan P95);

internal sealed record LivePerformanceDecision(bool Passed, TimeSpan ObservedP95, TimeSpan MaximumP95);

internal sealed record LivePerformanceRun(LiveLatencyProfile ShortCalls, LiveLatencyProfile BatchCalls);
