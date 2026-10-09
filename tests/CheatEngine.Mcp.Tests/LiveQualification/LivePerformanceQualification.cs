using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Prepared, unexecuted workload and admission measurements; native qualification remains separate.</summary>
internal static class LivePerformanceQualification
{
	internal const int RequiredBaselineRuns = 3;
	internal static readonly TimeSpan ShortCallAllowance = TimeSpan.FromMilliseconds(100);
	private const string Value = "20260926";
	private static readonly string[] s_aobPatterns = ["D1 2B 7E 93 44 A8 19 C6"];
	private static readonly (string Name, string Hash)[] s_baselineFiles =
	[
		("CheatEngine.Mcp.dll", "5699ECA2ED06461846A977802DF3E4FA7BF52159F50AA1446C4819908A7692AE"),
		("CheatEngine.Mcp.Gateway.exe", "2E0E7F8512D8A8AAC0197E982C7660BFA4336EBDB8981543817B709381C83885")
	];
	private static readonly string[] s_pointerOffsets = ["-10", "20"];
	private static readonly string[] s_runtimeGates = ["unsafeLua", "autoAssembler", "kernelAccess", "targetCodeExecution"];
	private static readonly string[] s_structureFlagValues = ["100", "200"];
	private static readonly string[] s_structureHealthValues = ["7", "7"];
	private static readonly LivePerformanceWorkload[] s_workloads =
	[
		new("memory_read_int32", 100, 5, LivePerformanceBudgetClass.Short, "end-to-end memory_read int32", "one int32"),
		new("memory_read_batch_16", 30, 5, LivePerformanceBudgetClass.Bounded, "end-to-end memory_read_batch", "16 int32 items"),
		new("memory_read_batch_1024", 6, 2, LivePerformanceBudgetClass.Bounded, "end-to-end memory_read_batch", "1024 int32 items (4 KiB)"),
		new("memory_list_regions_64", 12, 2, LivePerformanceBudgetClass.Bounded, "end-to-end memory_list_regions", "committed; limit=64"),
		new("named_scan_64", 3, 1, LivePerformanceBudgetClass.WholeCycle, "end-to-end scan_first, bounded poll and delete", "64 bytes; reset each sample; 15 second post-start poll bound"),
		new("aob_find_64", 12, 2, LivePerformanceBudgetClass.Bounded, "end-to-end aob_find", "64 bytes; limit=2"),
		new("pointer_read_chain", 12, 2, LivePerformanceBudgetClass.Bounded, "end-to-end pointer_read_chain", "two offsets"),
		new("module_symbol_resolution", 12, 2, LivePerformanceBudgetClass.Bounded, "end-to-end module_list and symbol_resolve", "detailed limit=1000; two expressions"),
		new("structure_read", 12, 2, LivePerformanceBudgetClass.Bounded, "end-to-end structure_read", "two owned instances; limit=8"),
		new("structure_dissect_compare", 12, 2, LivePerformanceBudgetClass.Bounded, "end-to-end structure_compare", "two owned instances; limit=8"),
		new("code_dissect_whole_cycle", 3, 1, LivePerformanceBudgetClass.WholeCycle, "end-to-end code_start_dissect, poll, clear and stop", "2 bytes; TTL=30; 10 second poll bound; retained on uncertain start, poll or clear"),
		new("instance_discovery", 12, 2, LivePerformanceBudgetClass.Bounded, "end-to-end instance_list", "two owned instances")
	];

	internal static IReadOnlyList<LivePerformanceWorkload> Workloads => s_workloads;

	[SupportedOSPlatform("windows")]
	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.Equal(LiveQualificationScenario.Performance, inputs.Scenario);
		string baselineDirectory = Path.Combine(inputs.RepositoryRoot, "artifacts", "qualification-baseline", "beta.2", "staged");
		VerifyBaseline(baselineDirectory);
		LivePerformanceRun[] baselineRuns = new LivePerformanceRun[RequiredBaselineRuns];
		for (int index = 0; index < baselineRuns.Length; index++)
		{
			baselineRuns[index] = await MeasureSessionAsync(inputs, baselineDirectory, index + 1, null, null);
		}
		IReadOnlyDictionary<string, LiveBaselineProfile> baseline = Baselines(baselineRuns);
		LiveAdmissionSession[] admissionBaseline = baselineRuns.Select(static run => run.Admission
			?? throw new InvalidOperationException("Baseline admission evidence is missing.")).ToArray();
		for (int index = 0; index < RequiredBaselineRuns; index++)
		{
			_ = await MeasureSessionAsync(inputs, null, index + 1, baseline, admissionBaseline);
		}
	}

	private static void VerifyBaseline(string directory)
	{
		const string version = "2.0.0-beta.2+c51a0ec3af373c852cda062a91888ad94fa0cce9";
		Assert.Equal(version, FileVersionInfo.GetVersionInfo(Path.Combine(directory, "CheatEngine.Mcp.dll")).ProductVersion);
		foreach ((string name, string hash) in s_baselineFiles)
		{
			using FileStream binary = File.OpenRead(Path.Combine(directory, name));
			Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(binary)));
		}
	}

	[SupportedOSPlatform("windows")]
	private static async Task<LivePerformanceRun> MeasureSessionAsync(LiveQualificationInputs inputs, string? distribution,
		int runNumber, IReadOnlyDictionary<string, LiveBaselineProfile>? baseline,
		IReadOnlyList<LiveAdmissionSession>? admissionBaseline)
	{
		LiveSandboxOptions options = LiveSandboxOptions.Smoke with
		{
			PerformanceQualification = true,
			DistributionDirectoryOverride = distribution
		};
		LiveSandboxSession? sandbox = null;
		LiveMcpClient? gateway = null;
		LivePerformanceFixture? fixture = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		Dictionary<string, LiveLatencyProfile>? captured = null;
		LivePerformanceRun? result = null;
		try
		{
			sandbox = await LiveSandboxSession.StartAsync(inputs, options);
			gateway = await LiveMcpClient.ConnectGatewayAsync(sandbox.GatewayExecutablePath,
				sandbox.InstanceDirectory, TestContext.Current.CancellationToken);
			LiveMcpInstanceClient a = gateway.Bind(sandbox.HostA.InstanceId);
			LiveMcpInstanceClient b = gateway.Bind(sandbox.HostB.InstanceId);
			await AttachAndVerifyGatesAsync(sandbox, a, b);
			fixture = await LivePerformanceFixture.CreateAsync(sandbox, gateway, a);
			captured = [];
			foreach (LivePerformanceWorkload workload in Workloads)
			{
				Func<Task> operation = fixture.GetOperation(workload.Name);
				for (int warmup = 0; warmup < workload.WarmupCount; warmup++)
				{
					await operation();
				}

				captured.Add(workload.Name, await MeasureAsync(workload.SampleCount, operation, TestContext.Current.CancellationToken));
			}

			LivePerformanceRun run = CreateRun(captured ?? throw new InvalidOperationException("No performance profiles were recorded."));
			IReadOnlyList<LivePerformanceComparison>? comparisons = baseline is null ? null : Pair(baseline, run);
			sandbox.Record("performance", new
			{
				kind = baseline is null ? "published_beta2_baseline" : "candidate",
				runNumber,
				productVersion = FileVersionInfo.GetVersionInfo(sandbox.GatewayExecutablePath).ProductVersion,
				matrix = Workloads,
				profiles = run.Workloads,
				baseline,
				comparisons,
				limitation = "End-to-end 9.01 workflow measurements, including client validation and the listed polling/cleanup steps. These do not isolate native execution time or qualify 9.03, 9.05, or the complete output contract."
			});
			if (comparisons is not null)
			{
				Assert.All(comparisons, comparison => Assert.True(comparison.Decision.Passed,
					$"{comparison.Name} p95 {comparison.Decision.ObservedP95} exceeds {comparison.Decision.MaximumP95}."));
			}

			JsonNode unaffected = (await b.CallToolAsync(CheatEngineToolNames.MemoryRead,
				new Dictionary<string, object?> { ["address"] = sandbox.HostB.TargetAddress, ["valueType"] = "int32" }))!;
			Assert.Equal(Value, unaffected["value"]!.GetValue<string>());
			LiveAdmissionSession admission = await LiveAdmissionQualification.MeasureAsync(sandbox, gateway);
			if (admissionBaseline is not null)
			{
				LiveAdmissionAssessment assessment = LiveAdmissionComparison.Compare(admissionBaseline, admission);
				sandbox.Record("performance_admission_comparison", assessment);
				Assert.True(assessment.Passed,
					"Admission timing exceeded its baseline budget or required overlap/refusal evidence was missing. See the comparison receipt.");
			}
			result = run with
			{
				Admission = admission
			};
		}
		catch (Exception exception)
		{
			failure = exception;
		}
		finally
		{
			if (fixture is not null)
			{
				await CleanupAsync(cleanup, async () => await fixture.DisposeAsync());
			}

			await LiveLifecycleQualification.DisposeOrderedAsync(
				failure,
				gateway is null ? null : gateway.DisposeAsync,
				sandbox is null ? null : exceptions =>
				{
					sandbox.Record("performance_gateway_cleanup_failure", exceptions.Select(exception => exception.ToString()).ToArray());
					return ValueTask.CompletedTask;
				},
				sandbox is null ? null : () =>
				{
					sandbox.MarkPassed();
					return ValueTask.CompletedTask;
				},
				sandbox is null ? null : sandbox.DisposeAsync,
				cleanup);
		}

		ThrowWithCleanup(failure, cleanup);
		return result ?? throw new InvalidOperationException("Performance session did not produce a run.");
	}

	[SupportedOSPlatform("windows")]
	private static async Task AttachAndVerifyGatesAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient a, LiveMcpInstanceClient b)
	{
		await AttachAndVerifyGatesAsync(sandbox.HostA, a);
		await AttachAndVerifyGatesAsync(sandbox.HostB, b);
	}

	[SupportedOSPlatform("windows")]
	private static async Task AttachAndVerifyGatesAsync(LiveSandboxHost host, LiveMcpInstanceClient client)
	{
		JsonNode runtime = (await client.CallToolAsync(CheatEngineToolNames.RuntimeGetInfo))!;
		foreach (string gate in s_runtimeGates)
		{
			Assert.False(runtime["gates"]![gate]!.GetValue<bool>());
		}

		JsonNode attached = (await client.CallToolAsync(CheatEngineToolNames.ProcessAttach,
			new Dictionary<string, object?> { ["process"] = host.TargetProcessId.ToString(CultureInfo.InvariantCulture) }))!;
		Assert.Equal(host.TargetProcessId, attached["processId"]!.GetValue<int>());
	}

	internal static LivePerformanceRun CreateRun(IReadOnlyDictionary<string, LiveLatencyProfile> workloads)
	{
		ArgumentNullException.ThrowIfNull(workloads);
		string[] expected = Workloads.Select(static item => item.Name).Order(StringComparer.Ordinal).ToArray();
		if (!expected.SequenceEqual(workloads.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
		{
			throw new InvalidOperationException("Performance matrix must contain every supported workload exactly once.");
		}

		foreach (LivePerformanceWorkload workload in Workloads)
		{
			if (workloads[workload.Name].SampleCount != workload.SampleCount)
			{
				throw new InvalidOperationException($"'{workload.Name}' has the wrong fixed sample count.");
			}
		}

		return new LivePerformanceRun(new Dictionary<string, LiveLatencyProfile>(workloads, StringComparer.Ordinal));
	}

	internal static IReadOnlyDictionary<string, LiveBaselineProfile> Baselines(IEnumerable<LivePerformanceRun> runs)
	{
		ArgumentNullException.ThrowIfNull(runs);
		LivePerformanceRun[] captured = runs.ToArray();
		if (captured.Length != RequiredBaselineRuns)
		{
			throw new InvalidOperationException($"Exactly {RequiredBaselineRuns} separately recorded baseline runs are required.");
		}

		foreach (LivePerformanceRun run in captured)
		{
			ValidateRun(run);
		}

		return Workloads.ToDictionary(
			workload => workload.Name,
			workload => Baseline(captured.Select(run => RequireProfile(run, workload.Name))),
			StringComparer.Ordinal);
	}

	internal static IReadOnlyList<LivePerformanceComparison> Pair(IReadOnlyDictionary<string, LiveBaselineProfile> baseline, LivePerformanceRun candidate)
	{
		ArgumentNullException.ThrowIfNull(baseline);
		ArgumentNullException.ThrowIfNull(candidate);
		ValidateRun(candidate);
		string[] expected = Workloads.Select(static item => item.Name).Order(StringComparer.Ordinal).ToArray();
		if (!expected.SequenceEqual(baseline.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
		{
			throw new InvalidOperationException("Unsupported, missing or extra baseline workloads cannot count as a pass.");
		}

		return Workloads.Select(workload =>
		{
			LiveBaselineProfile profile = baseline[workload.Name];
			ValidateBaselineProfile(workload, profile);
			LivePerformanceDecision decision = workload.BudgetClass == LivePerformanceBudgetClass.Short
				? EvaluateShortCalls(profile, candidate.Workloads[workload.Name])
				: EvaluateBoundedJobs(profile, candidate.Workloads[workload.Name]);
			return new LivePerformanceComparison(workload.Name, decision);
		}).ToArray();
	}

	internal static LiveLatencyProfile Profile(IEnumerable<TimeSpan> samples)
	{
		ArgumentNullException.ThrowIfNull(samples);
		long[] ticks = samples.Select(sample => sample.Ticks).Order().ToArray();
		if (ticks.Length == 0 || ticks.Any(value => value < 0))
		{
			throw new ArgumentException("Latency samples must be non-empty and non-negative.", nameof(samples));
		}

		return new LiveLatencyProfile(ticks.Length, TimeSpan.FromTicks(ticks[(ticks.Length - 1) / 2]), TimeSpan.FromTicks(ticks[(int) Math.Ceiling(ticks.Length * .95) - 1]), TimeSpan.FromTicks(ticks[^1]));
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
		return new(candidate.P95 <= threshold, candidate.P95, threshold);
	}

	internal static LivePerformanceDecision EvaluateBoundedJobs(LiveBaselineProfile baseline, LiveLatencyProfile candidate)
	{
		ArgumentNullException.ThrowIfNull(baseline);
		ArgumentNullException.ThrowIfNull(candidate);
		TimeSpan threshold = Times(baseline.P95, 1.5);
		return new(candidate.P95 <= threshold, candidate.P95, threshold);
	}

	internal static async Task<LiveLatencyProfile> MeasureAsync(int count, Func<Task> operation, CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
		ArgumentNullException.ThrowIfNull(operation);
		TimeSpan[] samples = new TimeSpan[count];
		for (int index = 0; index < count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			long started = Stopwatch.GetTimestamp();
			await operation();
			samples[index] = Stopwatch.GetElapsedTime(started);
		}
		return Profile(samples);
	}

	private static LiveLatencyProfile RequireProfile(LivePerformanceRun run, string workloadName)
	{
		if (!run.Workloads.TryGetValue(workloadName, out LiveLatencyProfile? profile) || profile is null)
		{
			throw new InvalidOperationException($"Baseline is missing '{workloadName}'.");
		}

		return profile;
	}

	private static void ValidateRun(LivePerformanceRun run)
	{
		ArgumentNullException.ThrowIfNull(run);
		_ = CreateRun(run.Workloads);
	}

	private static void ValidateBaselineProfile(LivePerformanceWorkload workload, LiveBaselineProfile profile)
	{
		ArgumentNullException.ThrowIfNull(profile);
		if (profile.Runs.Count != RequiredBaselineRuns
			|| profile.Runs.Any(run => run.SampleCount != workload.SampleCount)
			|| profile.P95 != profile.Runs.Max(run => run.P95))
		{
			throw new InvalidOperationException($"Baseline for '{workload.Name}' is not a valid fixed matrix profile.");
		}
	}

	private static TimeSpan Times(TimeSpan value, double multiplier)
	{
		return TimeSpan.FromTicks(checked((long) Math.Ceiling(value.Ticks * multiplier)));
	}

	private static TimeSpan Max(TimeSpan first, TimeSpan second)
	{
		return first >= second ? first : second;
	}

	private static void ThrowWithCleanup(Exception? failure, List<Exception> cleanup)
	{
		if (failure is not null)
		{
			if (cleanup.Count != 0)
			{
				throw new AggregateException([failure, .. cleanup]);
			}

			ExceptionDispatchInfo.Capture(failure).Throw();
		}
		if (cleanup.Count != 0)
		{
			throw new AggregateException(cleanup);
		}
	}

	[SupportedOSPlatform("windows")]
	private sealed class LivePerformanceFixture : IAsyncDisposable
	{
		private const string Scanner = "performance-9-01-scan";
		private readonly LiveSandboxSession _sandbox;
		private readonly LiveMcpClient _gateway;
		private readonly LiveMcpInstanceClient _instance;
		private readonly List<string> _allocations = [];
		private string _named = null!;
		private string _aob = null!;
		private string _dissect = null!;
		private string _structureA = null!;
		private string _structureB = null!;
		private string _module = null!;
		private string _moduleBase = null!;
		private bool _structureCreated;
		private bool _retainDissectBuffer;
		private LivePerformanceFixture(LiveSandboxSession sandbox, LiveMcpClient gateway, LiveMcpInstanceClient instance)
		{
			_sandbox = sandbox;
			_gateway = gateway;
			_instance = instance;
		}

		public static async Task<LivePerformanceFixture> CreateAsync(LiveSandboxSession sandbox, LiveMcpClient gateway, LiveMcpInstanceClient instance)
		{
			LivePerformanceFixture fixture = new(sandbox, gateway, instance);
			Exception? failure = null;
			try
			{
				fixture._named = await fixture.AllocateAsync("performance-9-01-named", 64);
				await fixture.WriteAsync(fixture._named, "int32", Value);
				fixture._aob = await fixture.AllocateAsync("performance-9-01-aob", 64);
				await fixture.WriteAsync(At(fixture._aob, 16), "bytes", "D1 2B 7E 93 44 A8 19 C6");
				fixture._dissect = await fixture.AllocateAsync("performance-9-01-dissect", 64);
				await fixture.WriteAsync(fixture._dissect, "bytes", "90 C3");
				fixture._structureA = await fixture.AllocateAsync("performance-9-01-structure-a", 16);
				fixture._structureB = await fixture.AllocateAsync("performance-9-01-structure-b", 16);
				await fixture.WriteAsync(fixture._structureA, "int32", "7");
				await fixture.WriteAsync(At(fixture._structureA, 4), "uint16", "100");
				await fixture.WriteAsync(fixture._structureB, "int32", "7");
				await fixture.WriteAsync(At(fixture._structureB, 4), "uint16", "200");
				fixture._structureCreated = true;
				JsonNode structure = await fixture.CallAsync(CheatEngineToolNames.StructureCreate, new Dictionary<string, object?>
				{
					["name"] = "Performance901Structure",
					["elements"] = new object?[]
					{
						new Dictionary<string, object?> { ["offset"] = "0", ["name"] = "health", ["valueType"] = "int32" },
						new Dictionary<string, object?> { ["offset"] = "4", ["name"] = "flags", ["valueType"] = "uint16" }
					}
				});
				Assert.Equal(2, structure["elementCount"]!.GetValue<int>());
				JsonNode modules = await fixture.CallAsync(CheatEngineToolNames.ModuleList, new Dictionary<string, object?> { ["format"] = "detailed", ["limit"] = 1000 });
				string expected = sandbox.TargetArchitecture == "x86" ? "dotnet.exe" : "CheatEngine.Mcp.LiveTarget.exe";
				JsonNode module = Assert.Single(modules["modules"]!.AsArray(), item => string.Equals(item!["name"]!.GetValue<string>(), expected, StringComparison.OrdinalIgnoreCase))!;
				fixture._module = module["name"]!.GetValue<string>();
				fixture._moduleBase = module["base"]!.GetValue<string>();
				return fixture;
			}
			catch (Exception exception)
			{
				failure = exception;
			}
			List<Exception> cleanup = [];
			try
			{
				await fixture.DisposeAsync();
			}
			catch (Exception exception)
			{
				cleanup.Add(exception);
			}
			ThrowWithCleanup(failure, cleanup);
			throw new InvalidOperationException("Fixture setup did not complete.");
		}

		public Func<Task> GetOperation(string name) => name switch
		{
			"memory_read_int32" => ReadAsync,
			"memory_read_batch_16" => () => BatchAsync(16),
			"memory_read_batch_1024" => () => BatchAsync(1024),
			"memory_list_regions_64" => RegionsAsync,
			"named_scan_64" => ScanAsync,
			"aob_find_64" => AobAsync,
			"pointer_read_chain" => PointerAsync,
			"module_symbol_resolution" => ModuleAsync,
			"structure_read" => StructureReadAsync,
			"structure_dissect_compare" => StructureCompareAsync,
			"code_dissect_whole_cycle" => DissectAsync,
			"instance_discovery" => InstancesAsync,
			_ => throw new InvalidOperationException($"Unsupported workload '{name}'.")
		};

		public async ValueTask DisposeAsync()
		{
			List<Exception> cleanup = [];
			if (_structureCreated)
			{
				await CleanupAsync(cleanup, DeleteStructureAsync);
			}

			await CleanupAsync(cleanup, DeleteScannerAsync);
			if (_retainDissectBuffer)
			{
				await CleanupAsync(cleanup, RecordRetainedDissectBufferAsync);
			}

			foreach (string allocation in _allocations.AsEnumerable().Reverse())
			{
				if (!(_retainDissectBuffer && allocation == "performance-9-01-dissect"))
				{
					await CleanupAsync(cleanup, () => FreeAllocationAsync(allocation));
				}
			}

			ThrowWithCleanup(null, cleanup);
		}

		private async Task ReadAsync()
		{
			JsonNode result = await CallAsync(CheatEngineToolNames.MemoryRead, new Dictionary<string, object?> { ["address"] = _sandbox.HostA.TargetAddress, ["valueType"] = "int32" });
			Assert.Equal(Value, result["value"]!.GetValue<string>());
		}
		private async Task BatchAsync(int count)
		{
			JsonNode result = await CallAsync(CheatEngineToolNames.MemoryReadBatch, new Dictionary<string, object?> { ["items"] = Enumerable.Range(0, count).Select(_ => new { address = _sandbox.HostA.TargetAddress, valueType = "int32" }).ToArray() });
			Assert.Equal(0, result["failed"]!.GetValue<int>());
			JsonArray items = result["items"]!.AsArray();
			Assert.Equal(count, items.Count);
			Assert.All(items, item =>
			{
				Assert.Equal(Value, item!["value"]!.GetValue<string>());
				Assert.Null(item["error"]);
			});
		}
		private async Task RegionsAsync()
		{
			JsonNode result = await CallAsync(CheatEngineToolNames.MemoryListRegions, new Dictionary<string, object?> { ["state"] = "committed", ["limit"] = 64 });
			Assert.NotEmpty(result["regions"]!.AsArray());
			Assert.True(result["total"]!.GetValue<int>() > 0);
		}
		private async Task ScanAsync()
		{
			await DeleteScannerAsync();
			await RunWithCleanupAsync(async () =>
			{
				await CallAsync(CheatEngineToolNames.ScanFirst, new Dictionary<string, object?> { ["scannerName"] = Scanner, ["valueType"] = "int32", ["comparison"] = "exact", ["value"] = Value, ["startAddress"] = _named, ["endAddress"] = At(_named, 64), ["writable"] = "required", ["executable"] = "excluded", ["copyOnWrite"] = "any", ["alignment"] = 4, ["includeMapped"] = false });
				await WaitForScanAsync();
				JsonNode results = await CallAsync(CheatEngineToolNames.ScanListResults, new Dictionary<string, object?> { ["scannerName"] = Scanner, ["startIndex"] = 0, ["maximumResults"] = 8 });
				Assert.Contains(results["results"]!.AsArray(), item => item!["value"]!.GetValue<string>() == Value);
			}, DeleteScannerAsync);
		}
		private async Task AobAsync()
		{
			JsonNode found = await CallAsync(CheatEngineToolNames.AobFind, new Dictionary<string, object?> { ["patterns"] = s_aobPatterns, ["startAddress"] = _aob, ["endAddress"] = At(_aob, 56), ["writable"] = "required", ["executable"] = "excluded", ["copyOnWrite"] = "excluded", ["alignment"] = 4, ["limit"] = 2, ["includeMapped"] = false });
			JsonNode result = Assert.Single(found["results"]!.AsArray())!;
			Assert.Equal(At(_aob, 16), Assert.Single(result["matches"]!.AsArray())!.GetValue<string>());
		}
		private async Task PointerAsync()
		{
			JsonNode result = await CallAsync(CheatEngineToolNames.PointerReadChain, new Dictionary<string, object?> { ["base"] = _sandbox.HostA.PointerRootAddress, ["offsets"] = s_pointerOffsets, ["valueType"] = "int32" });
			Assert.Equal(Value, result["value"]!.GetValue<string>());
		}
		private async Task ModuleAsync()
		{
			JsonNode modules = await CallAsync(CheatEngineToolNames.ModuleList, new Dictionary<string, object?> { ["format"] = "detailed", ["limit"] = 1000 });
			Assert.Contains(modules["modules"]!.AsArray(), item => item!["name"]!.GetValue<string>() == _module);
			JsonNode symbols = await CallAsync(CheatEngineToolNames.SymbolResolve, new Dictionary<string, object?> { ["expressions"] = new[] { _module, _module + "+0" }, ["shallow"] = true });
			JsonArray items = symbols["items"]!.AsArray();
			Assert.Equal(2, items.Count);
			Assert.Equal(_module, items[0]!["expression"]!.GetValue<string>());
			Assert.Equal(_module + "+0", items[1]!["expression"]!.GetValue<string>());
			Assert.All(items, item =>
			{
				Assert.Null(item!["error"]);
				Assert.Equal(_moduleBase, item["address"]!.GetValue<string>());
			});
		}
		private async Task StructureReadAsync()
		{
			JsonNode result = await CallAsync(CheatEngineToolNames.StructureRead, new Dictionary<string, object?> { ["name"] = "Performance901Structure", ["addresses"] = new[] { _structureA, _structureB }, ["offset"] = 0, ["limit"] = 8, ["format"] = "detailed" });
			JsonArray elements = result["elements"]!.AsArray();
			Assert.Equal(2, elements.Count);
			Assert.Equal("health", elements[0]!["name"]!.GetValue<string>());
			Assert.Equal(s_structureHealthValues, elements[0]!["values"]!.AsArray().Select(item => item!.GetValue<string>()));
			Assert.Equal("flags", elements[1]!["name"]!.GetValue<string>());
			Assert.Equal(s_structureFlagValues, elements[1]!["values"]!.AsArray().Select(item => item!.GetValue<string>()));
		}
		private async Task StructureCompareAsync()
		{
			JsonNode result = await CallAsync(CheatEngineToolNames.StructureCompare, new Dictionary<string, object?> { ["groupA"] = new[] { _structureA }, ["groupB"] = new[] { _structureB }, ["structureName"] = "Performance901Structure", ["granularity"] = 4, ["interpretAs"] = "auto", ["mode"] = "discriminate", ["offset"] = 0, ["limit"] = 8 });
			JsonNode row = Assert.Single(result["rows"]!.AsArray())!;
			Assert.Equal(("4", "flags", "discriminator"), (row["offset"]!.GetValue<string>(), row["name"]!.GetValue<string>(), row["classification"]!.GetValue<string>()));
		}
		private async Task InstancesAsync()
		{
			JsonNode result = (await _gateway.CallToolAsync(CheatEngineToolNames.InstanceList))!;
			Assert.Equal(2, result["instances"]!.AsArray().Count);
		}

		private async Task DissectAsync()
		{
			_retainDissectBuffer = true;
			string? jobId = null;
			Exception? failure = null;
			List<Exception> cleanup = [];
			try
			{
				JsonNode start = await CallAsync(CheatEngineToolNames.CodeStartDissect, new Dictionary<string, object?> { ["address"] = _dissect, ["size"] = 2, ["lifetimeSeconds"] = 30 });
				jobId = start["jobId"]!.GetValue<string>();
				JsonNode poll = await PollAsync(jobId);
				Assert.Null(poll["job"]!["error"]);
				Assert.Null(poll["job"]!["cleanupError"]);
				JsonNode item = Assert.Single(poll["items"]!.AsArray())!;
				Assert.Equal(("dissect", 2), (item["kind"]!.GetValue<string>(), item["size"]!.GetValue<int>()));
				await CallAsync(CheatEngineToolNames.CodeClearDissect);
				_retainDissectBuffer = false;
			}
			catch (Exception exception)
			{
				failure = exception;
			}
			finally
			{
				if (jobId is not null)
				{
					await CleanupAsync(cleanup, () => StopJobAsync(jobId));
				}
			}
			ThrowWithCleanup(failure, cleanup);
		}

		private async Task<JsonNode> PollAsync(string jobId)
		{
			Dictionary<string, object?> arguments = new()
			{
				["jobId"] = jobId,
				["afterSequence"] = 0,
				["limit"] = 8
			};
			Stopwatch deadline = Stopwatch.StartNew();
			while (true)
			{
				JsonNode poll = await CallAsync(CheatEngineToolNames.CodePollJob, arguments);
				string state = poll["job"]!["state"]!.GetValue<string>();
				if (state == "completed")
				{
					return poll;
				}

				Assert.Equal("running", state);
				Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10), "The bounded code-dissect job did not finish.");
				await Task.Delay(25, TestContext.Current.CancellationToken);
			}
		}
		private async Task StopJobAsync(string jobId)
		{
			JsonNode result = await CallAsync(CheatEngineToolNames.RuntimeStopJob, new Dictionary<string, object?> { ["jobId"] = jobId });
			Assert.True(result["released"]!.GetValue<bool>());
		}
		private async Task WaitForScanAsync()
		{
			Stopwatch deadline = Stopwatch.StartNew();
			while (true)
			{
				JsonNode status = await CallAsync(CheatEngineToolNames.ScanGetStatus, new Dictionary<string, object?> { ["scannerName"] = Scanner });
				string state = status["state"]!.GetValue<string>();
				if (state != "Scanning")
				{
					Assert.Equal("ResultsReady", state);
					return;
				}
				Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(15), "The named scan did not finish.");
				await Task.Delay(50, TestContext.Current.CancellationToken);
			}
		}
		private async Task<string> AllocateAsync(string name, int size)
		{
			_allocations.Add(name);
			JsonNode result = await CallAsync(CheatEngineToolNames.MemoryAllocate, new Dictionary<string, object?> { ["name"] = name, ["size"] = size });
			Assert.Equal(size, result["size"]!.GetValue<long>());
			Assert.False(result["executable"]!.GetValue<bool>());
			return result["address"]!.GetValue<string>();
		}
		private async Task WriteAsync(string address, string type, string value)
		{
			JsonNode result = await CallAsync(CheatEngineToolNames.MemoryWrite, new Dictionary<string, object?> { ["address"] = address, ["valueType"] = type, ["value"] = value, ["verify"] = true });
			Assert.True(result["verified"]!.GetValue<bool>());
		}
		private async Task DeleteScannerAsync()
		{
			LiveMcpToolResult result = await _instance.CallToolRawAsync(CheatEngineToolNames.ScanDelete, new Dictionary<string, object?> { ["scannerName"] = Scanner });
			if (result.IsError)
			{
				Assert.Equal("not_found", result.Payload!["error"]!["kind"]!.GetValue<string>());
			}
		}
		private async Task DeleteStructureAsync()
		{
			LiveMcpToolResult result = await _instance.CallToolRawAsync(CheatEngineToolNames.StructureDelete,
				new Dictionary<string, object?> { ["name"] = "Performance901Structure" });
			ThrowIfUnexpectedCleanupError(result, CheatEngineToolNames.StructureDelete);
		}
		private async Task FreeAllocationAsync(string allocation)
		{
			LiveMcpToolResult result = await _instance.CallToolRawAsync(CheatEngineToolNames.MemoryFree,
				new Dictionary<string, object?> { ["name"] = allocation });
			ThrowIfUnexpectedCleanupError(result, CheatEngineToolNames.MemoryFree);
		}
		private Task RecordRetainedDissectBufferAsync()
		{
			_sandbox.Record("performance_retained_dissect_buffer", new
			{
				allocation = "performance-9-01-dissect",
				reason = "The dissect start, poll, validation or clear outcome was uncertain; the owned target shutdown retains the buffer."
			});
			return Task.CompletedTask;
		}
		private static void ThrowIfUnexpectedCleanupError(LiveMcpToolResult result, string tool)
		{
			if (!result.IsError || result.Payload?["error"]?["kind"]?.GetValue<string>() == "not_found")
			{
				return;
			}

			throw new InvalidOperationException($"Cleanup tool '{tool}' failed: {result.Payload?.ToJsonString() ?? "<empty>"}.");
		}
		private async Task<JsonNode> CallAsync(string tool, IReadOnlyDictionary<string, object?>? args = null) => await _instance.CallToolAsync(tool, args) ?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
	}

	internal static async Task RunWithCleanupAsync(Func<Task> operation, Func<Task> release)
	{
		ArgumentNullException.ThrowIfNull(operation);
		ArgumentNullException.ThrowIfNull(release);
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			await operation();
		}
		catch (Exception exception)
		{
			failure = exception;
		}
		await CleanupAsync(cleanup, release);
		ThrowWithCleanup(failure, cleanup);
	}

	private static async Task CleanupAsync(List<Exception> cleanup, Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception exception)
		{
			cleanup.Add(exception);
		}
	}
	private static string At(string address, int offset)
	{
		ulong value = ulong.Parse(address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address[2..] : address, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
		return checked(value + (ulong) offset).ToString("X", CultureInfo.InvariantCulture);
	}
}

internal enum LivePerformanceBudgetClass
{
	Short, Bounded, WholeCycle
}
internal sealed record LivePerformanceWorkload(string Name, int SampleCount, int WarmupCount, LivePerformanceBudgetClass BudgetClass, string Method, string Limits);
internal sealed record LiveLatencyProfile(int SampleCount, TimeSpan P50, TimeSpan P95, TimeSpan Maximum);
internal sealed record LiveBaselineProfile(IReadOnlyList<LiveLatencyProfile> Runs, TimeSpan P95);
internal sealed record LivePerformanceDecision(bool Passed, TimeSpan ObservedP95, TimeSpan MaximumP95);
internal sealed record LivePerformanceRun(IReadOnlyDictionary<string, LiveLatencyProfile> Workloads)
{
	internal LiveAdmissionSession? Admission
	{
		get; init;
	}
	public LiveLatencyProfile ShortCalls => Workloads["memory_read_int32"];
	public LiveLatencyProfile BatchCalls => Workloads["memory_read_batch_16"];
}
internal sealed record LivePerformanceComparison(string Name, LivePerformanceDecision Decision);
