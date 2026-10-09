using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Finite multi-client and navigation observations; missing native evidence is reported explicitly.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveAdmissionQualification
{
	internal const int Rounds = LiveAdmissionComparison.Rounds;
	internal const int BatchRequestsPerRound = LiveAdmissionComparison.BatchRequestsPerRound;
	internal const int BatchItems = 1024;
	private const string Value = "20260926";
	private static readonly string[] s_gates = ["unsafeLua", "autoAssembler", "kernelAccess", "targetCodeExecution"];

	internal static async Task<LiveAdmissionSession> MeasureAsync(LiveSandboxSession sandbox, LiveMcpClient first)
	{
		LiveMcpClient? second = null;
		LiveAdmissionSession? result = null;
		await LivePerformanceQualification.RunWithCleanupAsync(async () =>
		{
			second = await LiveMcpClient.ConnectGatewayAsync(sandbox.GatewayExecutablePath, sandbox.InstanceDirectory,
				TestContext.Current.CancellationToken);
			LiveMcpClient[] gateways = [first, second];
			foreach (LiveMcpClient gateway in gateways)
			{
				AssertInstances((await gateway.CallToolAsync(CheatEngineToolNames.InstanceList))!, sandbox);
			}
			foreach (LiveSandboxHost host in new[] { sandbox.HostA, sandbox.HostB })
			{
				JsonNode info = (await first.Bind(host.InstanceId).CallToolAsync(CheatEngineToolNames.RuntimeGetInfo))!;
				foreach (string gate in s_gates)
				{
					Assert.False(info["gates"]![gate]!.GetValue<bool>());
				}
			}

			JsonNode[] before = await RetainedStateAsync(first, sandbox);
			List<LiveAdmissionBurst> bursts = [];
			for (int round = 0; round < Rounds; round++)
			{
				// Both package generations execute the same explicit preparation before measured navigation.
				// Region preparation is last because it atomically prepares module names and the region map.
				LiveMcpInstanceClient owned = first.Bind(sandbox.HostA.InstanceId);
				JsonNode modules = (await owned.CallToolAsync(CheatEngineToolNames.ModuleList,
					new Dictionary<string, object?> { ["limit"] = 1 }))!;
				Assert.True(modules["total"]!.GetValue<int>() > 0);
				JsonNode regions = (await owned.CallToolAsync(CheatEngineToolNames.MemoryListRegions,
					new Dictionary<string, object?> { ["limit"] = 1 }))!;
				Assert.Single(regions["regions"]!.AsArray());
				sandbox.Record($"performance_admission_preparation_{round}", new
				{
					moduleListCalls = 1,
					regionListCalls = 1,
					timing = "Explicit source-tool preparation precedes the measured burst; it is not observer latency."
				});
				LiveAdmissionBurst burst = await LiveAdmissionMeasurement.MeasureAsync(
					Requests(sandbox, gateways, round), TestContext.Current.CancellationToken);
				bursts.Add(burst);
				sandbox.Record($"performance_admission_round_{round}", burst);
				Assert.Equal(BatchRequestsPerRound + 5, burst.Samples.Count);
				RequireExpectedOutcomes(burst);
			}

			JsonNode[] after = await RetainedStateAsync(first, sandbox);
			Assert.Equal(before.Length, after.Length);
			for (int index = 0; index < before.Length; index++)
			{
				Assert.True(JsonNode.DeepEquals(before[index], after[index]),
					"Navigation changed retained jobs or resources.");
			}
			result = new LiveAdmissionSession(bursts);
			LiveAdmissionComparison.Validate(result);
			sandbox.Record("performance_admission", new
			{
				independentGateways = 2,
				rounds = Rounds,
				batchRequestsPerRound = BatchRequestsPerRound,
				batchItems = BatchItems,
				batchPayloadBytes = BatchItems * sizeof(int),
				stopwatchFrequency = Stopwatch.Frequency,
				retainedStateUnchanged = true,
				bursts,
				limitations = new List<string>
				{
					"Intervals measure client requests, not native dispatch occupancy. Missing cross-client workload overlap or busy refusal leaves that coverage unqualified.",
					"One sequential UI snapshot per round includes the CE driver's 100 ms timer and file exchange, not pure UI latency.",
					"Every round explicitly prepares modules and regions before timing. The published baseline may cache module completion; the candidate validates prepared data afresh. Empty responses do not prove refusal.",
					"A baseline completion refresh can outlive its response. Candidate navigation reads prepared data; explicit preparation still performs native enumeration and is outside burst timing."
				}
			});
		}, () => second?.DisposeAsync().AsTask() ?? Task.CompletedTask);
		return result ?? throw new InvalidOperationException("Admission observations did not complete.");
	}

	private static List<LiveAdmissionRequest> Requests(LiveSandboxSession sandbox, LiveMcpClient[] gateways, int round)
	{
		List<LiveAdmissionRequest> requests = [];
		for (int index = 0; index < BatchRequestsPerRound; index++)
		{
			int client = index % gateways.Length;
			LiveMcpInstanceClient instance = gateways[client].Bind(sandbox.HostA.InstanceId);
			requests.Add(new LiveAdmissionRequest($"round-{round}-batch-{index}", $"gateway-{client}", "batch",
				async () =>
				{
					LiveMcpToolResult reply = await instance.CallToolRawAsync(CheatEngineToolNames.MemoryReadBatch,
						new Dictionary<string, object?>
						{
							["items"] = Enumerable.Range(0, BatchItems)
								.Select(_ => new { address = sandbox.HostA.TargetAddress, valueType = "int32" }).ToArray()
						});
					LiveAdmissionOutcome outcome = LiveAdmissionMeasurement.ClassifyTool(reply);
					if (outcome.Kind == LiveAdmissionOutcomeKind.Succeeded)
					{
						JsonNode payload = reply.Payload!;
						Assert.Equal(0, payload["failed"]!.GetValue<int>());
						JsonArray items = payload["items"]!.AsArray();
						Assert.Equal(BatchItems, items.Count);
						Assert.All(items, item =>
						{
							Assert.Null(item!["error"]);
							Assert.Equal(Value, item["value"]!.GetValue<string>());
						});
					}
					return outcome;
				}));
		}
		requests.Add(new LiveAdmissionRequest($"round-{round}-discovery", "gateway-0", "discovery", async () =>
		{
			AssertInstances((await gateways[0].CallToolAsync(CheatEngineToolNames.InstanceList))!, sandbox);
			return new LiveAdmissionOutcome(LiveAdmissionOutcomeKind.Succeeded);
		}));
		requests.Add(new LiveAdmissionRequest($"round-{round}-sentinel", "gateway-1", "host-b-sentinel", async () =>
		{
			JsonNode reply = (await gateways[1].Bind(sandbox.HostB.InstanceId).CallToolAsync(CheatEngineToolNames.MemoryRead,
				new Dictionary<string, object?> { ["address"] = sandbox.HostB.TargetAddress, ["valueType"] = "int32" }))!;
			Assert.Equal(Value, reply["value"]!.GetValue<string>());
			return new LiveAdmissionOutcome(LiveAdmissionOutcomeKind.Succeeded);
		}));
		requests.Add(new LiveAdmissionRequest($"round-{round}-ui", "ce-ui-timer", "ui-snapshot", async () =>
		{
			Assert.Equal(8, (await sandbox.ScanUiAsync("A", "snapshot")).Length);
			return new LiveAdmissionOutcome(LiveAdmissionOutcomeKind.Succeeded);
		}));
		requests.Add(new LiveAdmissionRequest($"round-{round}-regions", "gateway-1", "regions-resource", async () =>
		{
			string uri = $"cheatengine://instances/{sandbox.HostA.InstanceId}/regions?offset=0&limit=1";
			ReadResourceResult reply = await gateways[1].ReadResourceAsync(uri);
			TextResourceContents content = Assert.IsType<TextResourceContents>(Assert.Single(reply.Contents));
			Assert.Equal(uri, content.Uri);
			JsonNode payload = JsonNode.Parse(content.Text)!;
			Assert.Single(payload["regions"]!.AsArray());
			Assert.True(payload["total"]!.GetValue<int>() > 0);
			return new LiveAdmissionOutcome(LiveAdmissionOutcomeKind.Succeeded);
		}));
		requests.Add(new LiveAdmissionRequest($"round-{round}-completion", "gateway-0",
			round == 0 ? "module-completion-first" : "module-completion-repeat", async () =>
			{
				string module = sandbox.TargetArchitecture == "x86" ? "dotnet.exe" : "CheatEngine.Mcp.LiveTarget.exe";
				CompleteResult reply = await gateways[0].CompleteModulesAsync(sandbox.HostA.InstanceId, module);
				Assert.InRange(reply.Completion.Values.Count, 0, 100);
				if (reply.Completion.Values.Count == 0)
				{
					return new LiveAdmissionOutcome(LiveAdmissionOutcomeKind.EmptyCompletion);
				}
				Assert.Contains(module, reply.Completion.Values, StringComparer.OrdinalIgnoreCase);
				return new LiveAdmissionOutcome(LiveAdmissionOutcomeKind.Succeeded);
			}));
		return requests;
	}

	private static async Task<JsonNode[]> RetainedStateAsync(LiveMcpClient gateway, LiveSandboxSession sandbox)
	{
		List<JsonNode> state = [];
		foreach (LiveSandboxHost host in new[] { sandbox.HostA, sandbox.HostB })
		{
			LiveMcpInstanceClient instance = gateway.Bind(host.InstanceId);
			state.Add((await instance.CallToolAsync(CheatEngineToolNames.RuntimeListResources))!);
			state.Add((await instance.CallToolAsync(CheatEngineToolNames.RuntimeListJobs))!);
		}
		return [.. state];
	}

	private static void AssertInstances(JsonNode payload, LiveSandboxSession sandbox)
	{
		Assert.Equal(new[] { sandbox.HostA.InstanceId, sandbox.HostB.InstanceId }.Order(StringComparer.Ordinal),
			payload["instances"]!.AsArray().Select(instance => instance!["instanceId"]!.GetValue<string>())
				.Order(StringComparer.Ordinal));
	}

	private static void RequireExpectedOutcomes(LiveAdmissionBurst burst)
	{
		Assert.All(burst.Samples, sample =>
		{
			Assert.True(LiveAdmissionComparison.ExpectedOutcome(sample),
				$"Admission observation '{sample.Id}' failed: {sample.Outcome}.");
		});
	}
}

internal sealed record LiveAdmissionSession(IReadOnlyList<LiveAdmissionBurst> Bursts);
