using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[Collection(LiveQualificationSerialGroup.Name)]
[Trait("Category", "LiveQualification")]
[Trait("Session", "S0")]
[SupportedOSPlatform("windows")]
public sealed class McpLiveQualificationTests(LiveQualificationFixture fixture)
{
	private static readonly TimeSpan McpTimeout = TimeSpan.FromSeconds(15);

	[Fact]
	public async Task OneGateway_TwoOwnedInstances_RoutesStateAndSurvivesOneHostShutdown()
	{
		LiveQualificationInputs inputs = fixture.RequireAuthorization();
		await using LiveSandboxSession sandbox = await LiveSandboxSession.StartAsync(inputs);
		await using LiveMcpClient gateway = await LiveMcpClient.ConnectGatewayAsync(sandbox.GatewayExecutablePath,
				sandbox.InstanceDirectory,
				TestContext.Current.CancellationToken)
			.WaitAsync(McpTimeout, TestContext.Current.CancellationToken);
		int? recordId = null;
		bool speedhackAttempted = false;
		bool scenarioCompleted = false;
		string step = "gateway tool discovery";
		LiveMcpInstanceClient? instanceA = null;
		try
		{
			string[] expectedToolNames = ToolContractTests.GetToolNames().Append(CheatEngineToolNames.InstanceList)
				.Order(StringComparer.Ordinal).ToArray();
			string[] actualToolNames = (await gateway.ListToolsAsync().AsTask()
					.WaitAsync(McpTimeout, TestContext.Current.CancellationToken))
				.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray();
			Assert.Equal(expectedToolNames, actualToolNames);
			sandbox.Record("gateway_tool_discovery", new
			{
				count = actualToolNames.Length
			});

			step = "gateway instance discovery";
			JsonArray instances = (await GatewayCallAsync(gateway, CheatEngineToolNames.InstanceList))["instances"]!.AsArray();
			Assert.Equal(2, instances.Count);
			AssertInstance(instances, sandbox.HostA);
			AssertInstance(instances, sandbox.HostB);
			Assert.NotEqual(sandbox.HostA.InstanceId, sandbox.HostB.InstanceId);
			Assert.NotEqual(new Uri(sandbox.HostA.Endpoint).Port, new Uri(sandbox.HostB.Endpoint).Port);
			sandbox.Record("gateway_instances",
				new
				{
					A = new
					{
						sandbox.HostA.Name,
						sandbox.HostA.InstanceId,
						port = new Uri(sandbox.HostA.Endpoint).Port
					},
					B = new
					{
						sandbox.HostB.Name,
						sandbox.HostB.InstanceId,
						port = new Uri(sandbox.HostB.Endpoint).Port
					}
				});

			instanceA = gateway.Bind(sandbox.HostA.InstanceId);
			LiveMcpInstanceClient instanceB = gateway.Bind(sandbox.HostB.InstanceId);

			step = "instance runtime information";
			JsonNode runtimeA = await SuccessfulCallAsync(instanceA, CheatEngineToolNames.RuntimeGetInfo);
			JsonNode runtimeB = await SuccessfulCallAsync(instanceB, CheatEngineToolNames.RuntimeGetInfo);
			Assert.Equal(Path.GetFileName(sandbox.HostA.PluginPath), runtimeA["pluginFileName"]!.GetValue<string>());
			Assert.Equal(Path.GetFileName(sandbox.HostB.PluginPath), runtimeB["pluginFileName"]!.GetValue<string>());
			AssertRuntimeLocation(runtimeA, sandbox.HostA.PluginPath);
			AssertRuntimeLocation(runtimeB, sandbox.HostB.PluginPath);
			Assert.True(runtimeA["epoch"]!.GetValue<long>() > 0);
			Assert.True(runtimeB["epoch"]!.GetValue<long>() > 0);
			sandbox.Record("runtime", new
			{
				A = runtimeA.DeepClone(),
				B = runtimeB.DeepClone()
			});

			step = "instance A target attach";
			await OpenTargetAsync(instanceA, sandbox.HostA);
			AssertMemoryValue(await ReadMemoryAsync(instanceA, sandbox.HostA.TargetAddress), 20260926);
			await WriteMemoryAsync(instanceA, sandbox.HostA.TargetAddress, 20260927);
			AssertMemoryValue(await ReadMemoryAsync(instanceA, sandbox.HostA.TargetAddress), 20260927);

			step = "instance B target isolation";
			await OpenTargetAsync(instanceB, sandbox.HostB);
			AssertMemoryValue(await ReadMemoryAsync(instanceB, sandbox.HostB.TargetAddress), 20260926);
			JsonNode addressListB = await SuccessfulCallAsync(instanceB, CheatEngineToolNames.RecordList);
			Assert.Empty(addressListB["records"]!.AsArray());
			JsonNode speedB = await SuccessfulCallAsync(instanceB, CheatEngineToolNames.SpeedhackGetState);
			Assert.Equal(1.0, speedB["speed"]!.GetValue<double>(), 4);

			step = "main UI and independent scanner isolation";
			await AssertScannerIsolationAsync(sandbox, instanceA, instanceB);

			step = "disassembly columns and retained allocation";
			await AssertDisassemblyAsync(sandbox, instanceA);

			step = "instance A address-list creation";
			JsonNode created = await SuccessfulCallAsync(instanceA, CheatEngineToolNames.RecordCreate,
				new Dictionary<string, object?>
				{
					["records"] = new[]
					{
						new Dictionary<string, object?>
						{
							["description"] = "CheatEngine.Mcp live target A",
							["address"] = sandbox.HostA.TargetAddress,
							["value"] = "20260928"
						}
					}
				});
			JsonNode createdRecord = Assert.Single(created["records"]!.AsArray())!;
			recordId = createdRecord["id"]!.GetValue<int>();
			Assert.Equal("CheatEngine.Mcp live target A", createdRecord["description"]!.GetValue<string>());

			step = "instance A address-list freeze";
			await SuccessfulCallAsync(instanceA, CheatEngineToolNames.RecordUpdate,
				new Dictionary<string, object?>
				{
					["updates"] = new[]
					{
						new Dictionary<string, object?> { ["id"] = recordId, ["value"] = "20260929" }
					}
				});
			await SuccessfulCallAsync(instanceA, CheatEngineToolNames.RecordSetActive,
				new Dictionary<string, object?> { ["ids"] = new[] { recordId }, ["active"] = true });
			await WriteMemoryAsync(instanceA, sandbox.HostA.TargetAddress, 20260930);
			await AssertEventuallyMemoryValueAsync(instanceA, sandbox.HostA.TargetAddress, 20260929);
			AssertMemoryValue(await ReadMemoryAsync(instanceB, sandbox.HostB.TargetAddress), 20260926);
			Assert.Empty((await SuccessfulCallAsync(instanceB, CheatEngineToolNames.RecordList))["records"]!.AsArray());

			step = "instance A speedhack";
			speedhackAttempted = true;
			await AssertSpeedAsync(instanceA, 0.5);
			Assert.Equal(1.0,
				(await SuccessfulCallAsync(instanceB, CheatEngineToolNames.SpeedhackGetState))["speed"]!.GetValue<double>(),
				4);
			await AssertSpeedAsync(instanceA, 2.0);
			await AssertSpeedAsync(instanceA, 1.0);
			Assert.Equal(1.0,
				(await SuccessfulCallAsync(instanceB, CheatEngineToolNames.SpeedhackGetState))["speed"]!.GetValue<double>(),
				4);

			step = "instance A address-list unfreeze and deletion";
			int existingRecordId =
				recordId ?? throw new InvalidOperationException("The active memory record was not retained.");
			await SuccessfulCallAsync(instanceA, CheatEngineToolNames.RecordSetActive,
				new Dictionary<string, object?> { ["ids"] = new[] { existingRecordId }, ["active"] = false });
			await WriteMemoryAsync(instanceA, sandbox.HostA.TargetAddress, 20260931);
			await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
			AssertMemoryValue(await ReadMemoryAsync(instanceA, sandbox.HostA.TargetAddress), 20260931);
			await SuccessfulCallAsync(instanceA, CheatEngineToolNames.RecordDelete,
				new Dictionary<string, object?> { ["ids"] = new[] { existingRecordId } });
			Assert.DoesNotContain((await SuccessfulCallAsync(instanceA, CheatEngineToolNames.RecordList))["records"]!.AsArray(),
				entry => entry!["id"]!.GetValue<int>() == existingRecordId);
			recordId = null;

			step = "instance B shutdown";
			await sandbox.StopHostAsync("B");
			await AssertEventuallyInstancesAsync(gateway, sandbox.HostA.InstanceId);
			await Assert.ThrowsAsync<InvalidOperationException>(async () =>
				await instanceB.CallToolAsync(CheatEngineToolNames.RuntimeGetInfo));
			AssertMemoryValue(await ReadMemoryAsync(instanceA, sandbox.HostA.TargetAddress), 20260931);
			sandbox.Record("instance_b_unavailable_instance_a_healthy",
				new
				{
					AInstanceId = sandbox.HostA.InstanceId,
					BInstanceId = sandbox.HostB.InstanceId
				});

			scenarioCompleted = true;
		}
		catch (Exception exception)
		{
			sandbox.Record("failure", new
			{
				step,
				exception = exception.ToString()
			});
			throw;
		}
		finally
		{
			List<Exception> cleanupFailures = [];
			if (instanceA is not null && recordId is { } existingRecordId)
			{
				try
				{
					await CleanupMemoryRecordAsync(instanceA, existingRecordId);
				}
				catch (Exception exception)
				{
					cleanupFailures.Add(exception);
				}
			}

			if (instanceA is not null && speedhackAttempted)
			{
				try
				{
					await RestoreSpeedAsync(instanceA);
				}
				catch (Exception exception)
				{
					cleanupFailures.Add(exception);
				}
			}

			if (cleanupFailures.Count > 0)
			{
				sandbox.Record("cleanup_failure",
					new
					{
						errors = cleanupFailures.Select(exception => exception.ToString()).ToArray()
					});
				if (scenarioCompleted)
				{
					Assert.Fail(
						$"Live scenario cleanup failed: {string.Join(Environment.NewLine, cleanupFailures.Select(exception => exception.Message))}");
				}
			}

			if (scenarioCompleted)
			{
				sandbox.MarkPassed();
			}
		}
	}

	private static async Task AssertDisassemblyAsync(LiveSandboxSession sandbox, ILiveMcpToolClient instance)
	{
		const string allocationName = "disassembly-probe";
		JsonNode allocated = await SuccessfulCallAsync(instance, CheatEngineToolNames.MemoryAllocate,
			new Dictionary<string, object?> { ["name"] = allocationName, ["size"] = 64 });
		try
		{
			string address = allocated["address"]!.GetValue<string>();
			await SuccessfulCallAsync(instance, CheatEngineToolNames.MemoryWrite,
				new Dictionary<string, object?>
				{
					["address"] = address,
					["valueType"] = "bytes",
					["value"] = "90 C3"
				});
			JsonNode single = await SuccessfulCallAsync(instance, CheatEngineToolNames.CodeDisassemble,
				new Dictionary<string, object?> { ["address"] = address, ["count"] = 1 });
			JsonNode instruction = Assert.Single(single["instructions"]!.AsArray())!;
			Assert.Equal("nop", instruction["opcode"]!.GetValue<string>().Trim(), true);
			Assert.Equal(1, instruction["size"]!.GetValue<int>());
			Assert.Equal("90", instruction["bytes"]!.GetValue<string>());
			Assert.False(string.IsNullOrWhiteSpace(instruction["addressText"]!.GetValue<string>()));
			Assert.Equal(string.Empty, instruction["extra"]!.GetValue<string>());
			JsonNode range = await SuccessfulCallAsync(instance, CheatEngineToolNames.CodeDisassemble,
				new Dictionary<string, object?> { ["address"] = address, ["count"] = 2 });
			JsonArray instructions = range["instructions"]!.AsArray();
			Assert.Equal(2, instructions.Count);
			Assert.Equal("nop", instructions[0]!["opcode"]!.GetValue<string>().Trim(), true);
			Assert.Equal("ret", instructions[1]!["opcode"]!.GetValue<string>().Trim(), true);
			Assert.Equal("C3", instructions[1]!["bytes"]!.GetValue<string>());
			sandbox.Record("disassembly_columns", new
			{
				single = single.DeepClone(),
				range = range.DeepClone()
			});
		}
		finally
		{
			await SuccessfulCallAsync(instance, CheatEngineToolNames.MemoryFree,
				new Dictionary<string, object?> { ["name"] = allocationName });
		}
	}

	private static async Task AssertScannerIsolationAsync(LiveSandboxSession sandbox, ILiveMcpToolClient instanceA,
		ILiveMcpToolClient instanceB)
	{
		await sandbox.ScanUiAsync("A", "prepare");
		await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanFirst,
			new Dictionary<string, object?> { ["value"] = "20260927" });
		await WaitForMainScanAsync(instanceA);
		JsonNode main = await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanListResults);
		string[] visible = await sandbox.ScanUiAsync("A", "snapshot");
		sandbox.Record("main_scan_first", new
		{
			response = main.DeepClone(),
			ui = visible
		});
		Assert.Equal("main", main["scannerName"]!.GetValue<string>());
		Assert.Equal("ui", main["mode"]!.GetValue<string>());
		Assert.Equal(1L, main["count"]!.GetValue<long>());
		AssertScanContains(main, sandbox.HostA.TargetAddress, "20260927");
		Assert.Equal("1", visible[1]);
		Assert.Contains("1", visible[2], StringComparison.Ordinal);
		Assert.Equal("20260927", visible[3]);
		Assert.Equal("3", visible[4]);
		Assert.Equal("1", visible[5]);

		JsonNode independentOne = await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanFirst,
			new Dictionary<string, object?>
			{
				["scannerName"] = "independent-one",
				["valueType"] = "int32",
				["value"] = "20260927"
			});
		JsonNode independentTwo = await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanFirst,
			new Dictionary<string, object?>
			{
				["scannerName"] = "independent-two",
				["valueType"] = "int32",
				["value"] = "20260927"
			});
		Assert.Equal(visible, await sandbox.ScanUiAsync("A", "snapshot"));
		JsonNode listed = await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanListScanners);
		Assert.Equal(3, listed["scanners"]!.AsArray().Count);
		foreach ((string name, string mode, JsonNode expected) in new[]
				 {
					 ("main", "ui", main), ("independent-one", "independent", independentOne),
					 ("independent-two", "independent", independentTwo)
				 })
		{
			JsonNode scanner = Assert.Single(listed["scanners"]!.AsArray(),
				scanner => scanner!["scannerName"]!.GetValue<string>() == name)!;
			Assert.Equal(mode, scanner["mode"]!.GetValue<string>());
			Assert.Equal("ResultsReady", scanner["state"]!.GetValue<string>());
			Assert.True(scanner["resultsReady"]!.GetValue<bool>());
			Assert.Equal("int32", scanner["valueType"]!.GetValue<string>());
			Assert.Equal(expected["count"]!.GetValue<long>(), scanner["count"]!.GetValue<long>());
			JsonNode status = await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanGetStatus,
				new Dictionary<string, object?> { ["scannerName"] = name });
			Assert.True(JsonNode.DeepEquals(scanner, status));
		}

		Assert.Single((await SuccessfulCallAsync(instanceB, CheatEngineToolNames.ScanListScanners))["scanners"]!.AsArray());

		await WriteMemoryAsync(instanceA, sandbox.HostA.TargetAddress, 20260932);
		await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanNext,
			new Dictionary<string, object?> { ["scannerName"] = "independent-one", ["value"] = "20260932" });
		AssertScanContains(
			await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanListResults,
				new Dictionary<string, object?> { ["scannerName"] = "independent-one" }), sandbox.HostA.TargetAddress,
			"20260932");
		Assert.Equal(visible, await sandbox.ScanUiAsync("A", "snapshot"));
		await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanNext,
			new Dictionary<string, object?> { ["value"] = "20260932" });
		await WaitForMainScanAsync(instanceA);
		AssertScanContains(await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanListResults), sandbox.HostA.TargetAddress,
			"20260932");
		await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanReset,
			new Dictionary<string, object?> { ["scannerName"] = "independent-one" });
		Assert.Equal(1L,
			(await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanListResults))["count"]!.GetValue<long>());
		Assert.True((await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanGetStatus,
				new Dictionary<string, object?> { ["scannerName"] = "independent-two" }))["resultsReady"]!
			.GetValue<bool>());
		await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanReset,
			new Dictionary<string, object?> { ["scannerName"] = "independent-two" });

		Assert.Equal("false", (await sandbox.ScanUiAsync("A", "hide"))[7]);
		await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanReset);
		string[] resetUi = await sandbox.ScanUiAsync("A", "snapshot");
		Assert.Equal("0", resetUi[1]);
		Assert.Equal("true", resetUi[7]);
		await WriteMemoryAsync(instanceA, sandbox.HostA.TargetAddress, 20260927);
		await sandbox.ScanUiAsync("A", "manual");
		await WaitForMainScanAsync(instanceA);
		AssertScanContains(await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanListResults), sandbox.HostA.TargetAddress,
			"20260927");
		await SuccessfulCallAsync(instanceA, CheatEngineToolNames.ScanReset);
		sandbox.Record("main_and_independent_scanners",
			new
			{
				visibleRows = visible[1],
				manualScanReadable = true,
				independentScanners = 2,
				instanceIsolation = true
			});
	}

	private static void AssertScanContains(JsonNode page, string address, string value)
	{
		Assert.Contains(page["results"]!.AsArray(), row =>
			string.Equals(row!["address"]!.GetValue<string>(), address, StringComparison.OrdinalIgnoreCase)
			&& row["value"]!.GetValue<string>() == value);
	}

	private static async Task WaitForMainScanAsync(ILiveMcpToolClient client)
	{
		DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15);
		do
		{
			JsonNode status = await SuccessfulCallAsync(client, CheatEngineToolNames.ScanGetStatus);
			if (status["state"]!.GetValue<string>() != "Scanning")
			{
				Assert.Equal("ResultsReady", status["state"]!.GetValue<string>());
				return;
			}

			await Task.Delay(50, TestContext.Current.CancellationToken);
		} while (DateTimeOffset.UtcNow < deadline);

		Assert.Fail("CE's main scanner did not finish within 15 seconds.");
	}

	private static void AssertInstance(JsonArray instances, LiveSandboxHost expected)
	{
		JsonNode instance = Assert.Single(instances,
			candidate => candidate!["instanceId"]!.GetValue<string>() == expected.InstanceId)!;
		Assert.Equal($"Live Qualification {expected.Name}", instance["name"]!.GetValue<string>());
		Assert.Equal(expected.ProcessId, instance["processId"]!.GetValue<int>());
	}

	private static void AssertRuntimeLocation(JsonNode runtimeInfo, string pluginPath)
	{
		Assert.Equal(Path.GetFileName(pluginPath), runtimeInfo["runtimeFileName"]!.GetValue<string>());
	}

	private static async Task AssertEventuallyInstancesAsync(LiveMcpClient gateway, string remainingInstanceId)
	{
		DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
		do
		{
			JsonArray instances = (await GatewayCallAsync(gateway, CheatEngineToolNames.InstanceList))["instances"]!.AsArray();
			if (instances.Count == 1 && instances[0]!["instanceId"]!.GetValue<string>() == remainingInstanceId)
			{
				return;
			}

			await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		} while (DateTimeOffset.UtcNow < deadline);

		Assert.Fail("The gateway did not withdraw the stopped instance while retaining the running instance.");
	}

	private static async Task OpenTargetAsync(ILiveMcpToolClient client, LiveSandboxHost host)
	{
		JsonNode opened = await SuccessfulCallAsync(client, CheatEngineToolNames.ProcessAttach,
			new Dictionary<string, object?>
			{
				["process"] = host.TargetProcessId.ToString(CultureInfo.InvariantCulture)
			});
		Assert.Equal(host.TargetProcessId, opened["processId"]!.GetValue<int>());
	}

	private static async Task AssertSpeedAsync(ILiveMcpToolClient client, double expected)
	{
		JsonNode configured = await SuccessfulCallAsync(client, CheatEngineToolNames.SpeedhackSetSpeed,
			new Dictionary<string, object?> { ["speed"] = expected });
		Assert.Equal(expected, configured["speed"]!.GetValue<double>(), 4);
		JsonNode observed = await SuccessfulCallAsync(client, CheatEngineToolNames.SpeedhackGetState);
		Assert.Equal(expected, observed["speed"]!.GetValue<double>(), 4);
	}

	private static async Task<JsonNode> SuccessfulCallAsync(ILiveMcpToolClient client, string name,
		IReadOnlyDictionary<string, object?>? arguments = null)
	{
		JsonNode? result = await client.CallToolAsync(name, arguments)
			.WaitAsync(McpTimeout, TestContext.Current.CancellationToken);
		Assert.NotNull(result);
		return result;
	}

	private static async Task<JsonNode> GatewayCallAsync(LiveMcpClient client, string name)
	{
		JsonNode? result =
			await client.CallToolAsync(name).WaitAsync(McpTimeout, TestContext.Current.CancellationToken);
		Assert.NotNull(result);
		return result;
	}

	private static Task<JsonNode> ReadMemoryAsync(ILiveMcpToolClient client, string address)
	{
		return SuccessfulCallAsync(client, CheatEngineToolNames.MemoryRead,
			new Dictionary<string, object?> { ["address"] = address, ["valueType"] = "int32" });
	}

	private static Task<JsonNode> WriteMemoryAsync(ILiveMcpToolClient client, string address, int value)
	{
		return SuccessfulCallAsync(client, CheatEngineToolNames.MemoryWrite,
			new Dictionary<string, object?>
			{
				["address"] = address,
				["valueType"] = "int32",
				["value"] = value.ToString(CultureInfo.InvariantCulture)
			});
	}

	private static void AssertMemoryValue(JsonNode result, int expected)
	{
		Assert.Equal(expected, MemoryValue(result));
	}

	// memory_read returns target values as text.
	private static int MemoryValue(JsonNode result)
	{
		return int.Parse(result["value"]!.GetValue<string>(), NumberStyles.AllowLeadingSign,
			CultureInfo.InvariantCulture);
	}

	private static async Task AssertEventuallyMemoryValueAsync(ILiveMcpToolClient client, string address, int expected)
	{
		DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
		int observed;
		do
		{
			JsonNode result = await ReadMemoryAsync(client, address);
			observed = MemoryValue(result);
			if (observed == expected)
			{
				return;
			}

			await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		} while (DateTimeOffset.UtcNow < deadline);

		Assert.Fail($"The active memory record did not restore {expected}; last observed value was {observed}.");
	}

	private static async Task CleanupMemoryRecordAsync(ILiveMcpToolClient client, int recordId)
	{
		await SuccessfulCallAsync(client, CheatEngineToolNames.RecordSetActive,
			new Dictionary<string, object?> { ["ids"] = new[] { recordId }, ["active"] = false });
		await SuccessfulCallAsync(client, CheatEngineToolNames.RecordDelete,
			new Dictionary<string, object?> { ["ids"] = new[] { recordId } });
	}

	private static async Task RestoreSpeedAsync(ILiveMcpToolClient client)
	{
		await SuccessfulCallAsync(client, CheatEngineToolNames.SpeedhackSetSpeed,
			new Dictionary<string, object?> { ["speed"] = 1.0 });
	}
}
