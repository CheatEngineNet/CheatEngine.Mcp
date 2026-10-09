using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Fixed workload and resource bounds for the explicitly authorized two-hour live soak.</summary>
internal static class LiveSoakQualification
{
	internal static readonly TimeSpan WorkloadDuration = TimeSpan.FromHours(2);
	internal static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(135);
	internal const int RequiredShortCalls = 1_000;
	internal const int RequiredBoundedJobs = 20;
	internal const int RequiredPluginCycles = 30;
	internal const int RequiredTargetRestarts = 10;
	internal const int MaximumHandleGrowth = 25;
	internal const long MaximumPrivateBytesGrowth = 64L * 1024 * 1024;

	[SupportedOSPlatform("windows")]
	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.Equal(LiveQualificationScenario.Soak, inputs.Scenario);
		await using LiveSandboxSession sandbox = await LiveSandboxSession.StartAsync(inputs,
			new LiveSandboxOptions(false, false) { SoakQualification = true, UseSoakLifetime = true });
		await using LiveLifecycleSession lifecycle = await LiveLifecycleSession.ConnectAsync(sandbox);
		await lifecycle.AttachBothAsync();
		await lifecycle.VerifyBothAsync();
		foreach (string name in new[] { "A", "B" })
		{
			LiveSandboxHost host = name == "A" ? sandbox.HostA : sandbox.HostB;
			await VerifyBoundedJobAsync(lifecycle.Gateway.Bind(host.InstanceId), host);
			await lifecycle.CyclePluginAsync(name);
		}
		await RequireEmptyAsync(sandbox, lifecycle);
		TimeSpan idleInterval = TimeSpan.FromSeconds(15);
		await Task.Delay(idleInterval, TestContext.Current.CancellationToken);
		LiveProcessMetrics baselineA = sandbox.CaptureMetrics("A");
		LiveProcessMetrics baselineB = sandbox.CaptureMetrics("B");
		Assert.Null(baselineA.CollectionError);
		Assert.Null(baselineB.CollectionError);
		sandbox.Record("soak_baseline", new
		{
			baselineA,
			baselineB,
			idleSeconds = idleInterval.TotalSeconds
		});
		Dictionary<LiveSoakActionKind, int> counts = Enum.GetValues<LiveSoakActionKind>().ToDictionary(kind => kind, _ => 0);
		Stopwatch elapsed = Stopwatch.StartNew();
		try
		{
			foreach (LiveSoakAction action in CreateSchedule())
			{
				TimeSpan remaining = action.At - elapsed.Elapsed;
				if (remaining > TimeSpan.Zero)
				{
					await Task.Delay(remaining, TestContext.Current.CancellationToken);
				}

				Assert.True(elapsed.Elapsed < MaximumLifetime - TimeSpan.FromMinutes(5), "The soak exceeded its workload deadline.");
				string name = counts[action.Kind] % 2 == 0 ? "A" : "B";
				LiveSandboxHost host = name == "A" ? sandbox.HostA : sandbox.HostB;
				switch (action.Kind)
				{
					case LiveSoakActionKind.ShortCall:
						JsonNode value = (await lifecycle.Gateway.Bind(host.InstanceId).CallToolAsync(CheatEngineToolNames.MemoryRead,
							new Dictionary<string, object?> { ["address"] = host.TargetAddress, ["valueType"] = "int32" }))!;
						Assert.Equal("20260926", value["value"]!.GetValue<string>());
						break;
					case LiveSoakActionKind.BoundedJob:
						await VerifyBoundedJobAsync(lifecycle.Gateway.Bind(host.InstanceId), host);
						break;
					case LiveSoakActionKind.PluginCycle:
						await lifecycle.CyclePluginAsync(name);
						break;
					case LiveSoakActionKind.TargetRestart:
						await sandbox.RestartTargetAsync(name);
						await lifecycle.AttachBothAsync();
						await lifecycle.VerifyBothAsync();
						break;
					case LiveSoakActionKind.GatewayRestart:
						await lifecycle.RestartGatewayAsync();
						await lifecycle.VerifyBothAsync();
						break;
					default:
						throw new InvalidOperationException("Unknown fixed soak action.");
				}
				counts[action.Kind]++;
				if (action.Kind != LiveSoakActionKind.ShortCall || counts[action.Kind] % 20 == 0)
				{
					sandbox.Record("soak_progress", new
					{
						elapsedSeconds = elapsed.Elapsed.TotalSeconds,
						counts = counts.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value)
					});
				}
			}
			Assert.True(elapsed.Elapsed >= WorkloadDuration);
			Assert.Equal(RequiredShortCalls, counts[LiveSoakActionKind.ShortCall]);
			Assert.Equal(RequiredBoundedJobs, counts[LiveSoakActionKind.BoundedJob]);
			Assert.Equal(RequiredPluginCycles, counts[LiveSoakActionKind.PluginCycle]);
			Assert.Equal(RequiredTargetRestarts, counts[LiveSoakActionKind.TargetRestart]);
			Assert.Equal(1, counts[LiveSoakActionKind.GatewayRestart]);
			await lifecycle.VerifyBothAsync();
			await RequireEmptyAsync(sandbox, lifecycle);
			await Task.Delay(idleInterval, TestContext.Current.CancellationToken);
			LiveProcessMetrics finalA = sandbox.CaptureMetrics("A");
			LiveProcessMetrics finalB = sandbox.CaptureMetrics("B");
			LiveResourceDecision decisionA = EvaluateResources(baselineA, finalA);
			LiveResourceDecision decisionB = EvaluateResources(baselineB, finalB);
			sandbox.Record("soak_final", new
			{
				elapsedSeconds = elapsed.Elapsed.TotalSeconds,
				counts,
				finalA,
				finalB,
				decisionA,
				decisionB
			});
			Assert.True(decisionA.Passed, $"Host A exceeded resource limits: {decisionA}");
			Assert.True(decisionB.Passed, $"Host B exceeded resource limits: {decisionB}");
			sandbox.MarkPassed();
		}
		catch (Exception exception)
		{
			sandbox.Record("soak_failure", new
			{
				elapsedSeconds = elapsed.Elapsed.TotalSeconds,
				counts,
				error = exception.ToString()
			});
			throw;
		}
	}

	[SupportedOSPlatform("windows")]
	private static async Task VerifyBoundedJobAsync(LiveMcpInstanceClient instance, LiveSandboxHost host)
	{
		JsonNode start = (await instance.CallToolAsync(CheatEngineToolNames.CodeStartSearch,
			new Dictionary<string, object?>
			{
				["address"] = host.TargetAddress + "+10",
				["size"] = 16,
				["textContains"] = "nop",
				["lifetimeSeconds"] = 30,
				["maximumResults"] = 32
			}))!;
		string jobId = start["jobId"]!.GetValue<string>();
		try
		{
			Dictionary<string, object?> arguments = new()
			{
				["jobId"] = jobId,
				["afterSequence"] = 0,
				["limit"] = 32
			};
			Stopwatch deadline = Stopwatch.StartNew();
			JsonNode poll;
			do
			{
				poll = (await instance.CallToolAsync(CheatEngineToolNames.CodePollJob, arguments))!;
				string state = poll["job"]!["state"]!.GetValue<string>();
				if (state == "completed")
				{
					break;
				}

				Assert.Equal("running", state);
				Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10), "The bounded data-only decode job did not finish.");
				await Task.Delay(25, TestContext.Current.CancellationToken);
			} while (true);
			Assert.Null(poll["job"]!["error"]);
			Assert.Null(poll["job"]!["cleanupError"]);
			Assert.False(poll["job"]!["requiresManualRecovery"]!.GetValue<bool>());
			Assert.Equal(16, poll["items"]!.AsArray().Count);
			Assert.Equal(0, poll["dropped"]!.GetValue<long>());
			Assert.Equal(16, poll["nextAfterSequence"]!.GetValue<long>());
			Assert.False(poll["more"]!.GetValue<bool>());
			JsonNode replay = (await instance.CallToolAsync(CheatEngineToolNames.CodePollJob, arguments))!;
			Assert.True(JsonNode.DeepEquals(poll["items"], replay["items"]), "Polling consumed or changed retained job items.");
		}
		finally
		{
			JsonNode stopped = (await instance.CallToolAsync(CheatEngineToolNames.RuntimeStopJob,
				new Dictionary<string, object?> { ["jobId"] = jobId }))!;
			Assert.True(stopped["released"]!.GetValue<bool>());
		}
	}

	[SupportedOSPlatform("windows")]
	private static async Task RequireEmptyAsync(LiveSandboxSession sandbox, LiveLifecycleSession lifecycle)
	{
		foreach (LiveSandboxHost host in new[] { sandbox.HostA, sandbox.HostB })
		{
			LiveMcpInstanceClient instance = lifecycle.Gateway.Bind(host.InstanceId);
			JsonNode jobs = (await instance.CallToolAsync(CheatEngineToolNames.RuntimeListJobs))!;
			JsonNode resources = (await instance.CallToolAsync(CheatEngineToolNames.RuntimeListResources))!;
			Assert.Empty(jobs["jobs"]!.AsArray());
			Assert.Empty(resources["resources"]!.AsArray());
		}
	}

	internal static IReadOnlyList<LiveSoakAction> CreateSchedule()
	{
		List<LiveSoakAction> schedule = [];
		AddEvenly(schedule, LiveSoakActionKind.ShortCall, RequiredShortCalls, TimeSpan.FromMinutes(1), WorkloadDuration);
		AddEvenly(schedule, LiveSoakActionKind.BoundedJob, RequiredBoundedJobs, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(116));
		AddEvenly(schedule, LiveSoakActionKind.PluginCycle, RequiredPluginCycles, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(110));
		AddEvenly(schedule, LiveSoakActionKind.TargetRestart, RequiredTargetRestarts, TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(112));
		schedule.Add(new LiveSoakAction(LiveSoakActionKind.GatewayRestart, TimeSpan.FromMinutes(60)));
		return schedule.OrderBy(action => action.At).ThenBy(action => action.Kind).ToArray();
	}

	internal static LiveResourceDecision EvaluateResources(LiveProcessMetrics baseline, LiveProcessMetrics final)
	{
		if (baseline.CollectionError is not null || final.CollectionError is not null)
		{
			return new LiveResourceDecision(false, "Process metrics were unavailable.", 0, 0);
		}
		if (baseline.ProcessId <= 0 || baseline.ProcessId != final.ProcessId)
		{
			return new LiveResourceDecision(false, "Process metrics belong to different owned CE processes.", 0, 0);
		}
		if (baseline.CapturedAt > final.CapturedAt || baseline.HandleCount < 0 || final.HandleCount < 0
			|| baseline.PrivateBytes < 0 || final.PrivateBytes < 0 || baseline.WorkingSetBytes < 0
			|| final.WorkingSetBytes < 0)
		{
			return new LiveResourceDecision(false, "Process metrics are invalid or out of chronological order.", 0, 0);
		}

		long handleGrowth = final.HandleCount - baseline.HandleCount;
		long privateBytesGrowth = final.PrivateBytes - baseline.PrivateBytes;
		return new LiveResourceDecision(handleGrowth <= MaximumHandleGrowth && privateBytesGrowth <= MaximumPrivateBytesGrowth,
			null, handleGrowth, privateBytesGrowth);
	}

	private static void AddEvenly(List<LiveSoakAction> schedule, LiveSoakActionKind kind, int count,
		TimeSpan first, TimeSpan last)
	{
		for (int index = 0; index < count; index++)
		{
			double ratio = count == 1 ? 0 : (double) index / (count - 1);
			schedule.Add(new LiveSoakAction(kind, first + TimeSpan.FromTicks((long) ((last - first).Ticks * ratio))));
		}
	}
}

internal enum LiveSoakActionKind
{
	ShortCall,
	BoundedJob,
	PluginCycle,
	TargetRestart,
	GatewayRestart
}

internal sealed record LiveSoakAction(LiveSoakActionKind Kind, TimeSpan At);

internal sealed record LiveResourceDecision(bool Passed, string? Failure, long HandleGrowth, long PrivateBytesGrowth);
