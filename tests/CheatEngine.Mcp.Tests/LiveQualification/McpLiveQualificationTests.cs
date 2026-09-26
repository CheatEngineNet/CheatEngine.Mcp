using System.Runtime.Versioning;
using System.Text.Json.Nodes;

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
		await using LiveMcpClient gateway = await LiveMcpClient.ConnectGatewayAsync(sandbox.GatewayExecutablePath, sandbox.InstanceDirectory,
			TestContext.Current.CancellationToken)
			.WaitAsync(McpTimeout, TestContext.Current.CancellationToken);
		int? recordId = null;
		bool speedhackAttempted = false;
		bool scenarioCompleted = false;
		string step = "gateway tool discovery";
		LiveMcpInstanceClient? instanceA = null;
		try
		{
			string[] expectedToolNames = ToolContractTests.GetToolNames().Append("list_instances").Order(StringComparer.Ordinal).ToArray();
			string[] actualToolNames = (await gateway.ListToolsAsync().AsTask().WaitAsync(McpTimeout, TestContext.Current.CancellationToken))
				.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray();
			Assert.Equal(expectedToolNames, actualToolNames);
			sandbox.Record("gateway_tool_discovery", new
			{
				count = actualToolNames.Length
			});

			step = "gateway instance discovery";
			JsonArray instances = (await GatewayCallAsync(gateway, "list_instances"))["instances"]!.AsArray();
			Assert.Equal(2, instances.Count);
			AssertInstance(instances, sandbox.HostA);
			AssertInstance(instances, sandbox.HostB);
			Assert.NotEqual(sandbox.HostA.InstanceId, sandbox.HostB.InstanceId);
			Assert.NotEqual(new Uri(sandbox.HostA.Endpoint).Port, new Uri(sandbox.HostB.Endpoint).Port);
			sandbox.Record("gateway_instances", new
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

			step = "instance A plugin version";
			JsonNode pluginVersionA = await SuccessfulCallAsync(instanceA, "get_plugin_version");
			Assert.Equal(sandbox.HostA.PluginPath, pluginVersionA["location"]!.GetValue<string>());
			AssertRuntimeLocation(pluginVersionA, sandbox.BundleCacheDirectory);

			step = "instance B plugin version";
			JsonNode pluginVersionB = await SuccessfulCallAsync(instanceB, "get_plugin_version");
			Assert.Equal(sandbox.HostB.PluginPath, pluginVersionB["location"]!.GetValue<string>());
			AssertRuntimeLocation(pluginVersionB, sandbox.BundleCacheDirectory);

			step = "instance runtime information";
			JsonNode runtimeA = await SuccessfulCallAsync(instanceA, "get_runtime_info");
			JsonNode runtimeB = await SuccessfulCallAsync(instanceB, "get_runtime_info");
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
			JsonNode addressListB = await SuccessfulCallAsync(instanceB, "get_address_list");
			Assert.Empty(addressListB["records"]!.AsArray());
			JsonNode speedB = await SuccessfulCallAsync(instanceB, "get_speedhack_speed");
			Assert.Equal(1.0, speedB["result"]!["speed"]!.GetValue<double>(), precision: 4);

			step = "instance A address-list creation";
			JsonNode created = await SuccessfulCallAsync(instanceA, "add_memory_record", new Dictionary<string, object?>
			{
				["description"] = "CheatEngine.Mcp live target A",
				["address"] = sandbox.HostA.TargetAddress,
				["value"] = "20260928",
				["variableType"] = "Dword"
			});
			recordId = created["record"]!["id"]!.GetValue<int>();
			Assert.Equal("CheatEngine.Mcp live target A", created["record"]!["description"]!.GetValue<string>());

			step = "instance A address-list freeze";
			await SuccessfulCallAsync(instanceA, "update_memory_record", new Dictionary<string, object?> { ["id"] = recordId, ["value"] = "20260929" });
			await SuccessfulCallAsync(instanceA, "set_memory_record_active", new Dictionary<string, object?> { ["id"] = recordId, ["active"] = true });
			await WriteMemoryAsync(instanceA, sandbox.HostA.TargetAddress, 20260930);
			await AssertEventuallyMemoryValueAsync(instanceA, sandbox.HostA.TargetAddress, 20260929);
			AssertMemoryValue(await ReadMemoryAsync(instanceB, sandbox.HostB.TargetAddress), 20260926);
			Assert.Empty((await SuccessfulCallAsync(instanceB, "get_address_list"))["records"]!.AsArray());

			step = "instance A speedhack";
			speedhackAttempted = true;
			await AssertSpeedAsync(instanceA, 0.5);
			Assert.Equal(1.0, (await SuccessfulCallAsync(instanceB, "get_speedhack_speed"))["result"]!["speed"]!.GetValue<double>(), precision: 4);
			await AssertSpeedAsync(instanceA, 2.0);
			await AssertSpeedAsync(instanceA, 1.0);
			Assert.Equal(1.0, (await SuccessfulCallAsync(instanceB, "get_speedhack_speed"))["result"]!["speed"]!.GetValue<double>(), precision: 4);

			step = "instance A address-list unfreeze and deletion";
			int existingRecordId = recordId ?? throw new InvalidOperationException("The active memory record was not retained.");
			await SuccessfulCallAsync(instanceA, "set_memory_record_active", new Dictionary<string, object?> { ["id"] = existingRecordId, ["active"] = false });
			await WriteMemoryAsync(instanceA, sandbox.HostA.TargetAddress, 20260931);
			await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
			AssertMemoryValue(await ReadMemoryAsync(instanceA, sandbox.HostA.TargetAddress), 20260931);
			await SuccessfulCallAsync(instanceA, "delete_memory_record", new Dictionary<string, object?> { ["id"] = existingRecordId });
			Assert.DoesNotContain((await SuccessfulCallAsync(instanceA, "get_address_list"))["records"]!.AsArray(), entry => entry!["id"]!.GetValue<int>() == existingRecordId);
			recordId = null;

			step = "instance B shutdown";
			await sandbox.StopHostAsync("B");
			await AssertEventuallyInstancesAsync(gateway, sandbox.HostA.InstanceId);
			await Assert.ThrowsAsync<InvalidOperationException>(async () => await instanceB.CallToolAsync("get_plugin_version"));
			AssertMemoryValue(await ReadMemoryAsync(instanceA, sandbox.HostA.TargetAddress), 20260931);
			sandbox.Record("instance_b_unavailable_instance_a_healthy", new
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
				catch (Exception exception) { cleanupFailures.Add(exception); }
			}
			if (instanceA is not null && speedhackAttempted)
			{
				try
				{
					await RestoreSpeedAsync(instanceA);
				}
				catch (Exception exception) { cleanupFailures.Add(exception); }
			}
			if (cleanupFailures.Count > 0)
			{
				sandbox.Record("cleanup_failure", new
				{
					errors = cleanupFailures.Select(exception => exception.ToString()).ToArray()
				});
				if (scenarioCompleted)
				{
					Assert.Fail($"Live scenario cleanup failed: {string.Join(Environment.NewLine, cleanupFailures.Select(exception => exception.Message))}");
				}
			}
			if (scenarioCompleted)
			{
				sandbox.MarkPassed();
			}
		}
	}

	private static void AssertInstance(JsonArray instances, LiveSandboxHost expected)
	{
		JsonNode instance = Assert.Single(instances, candidate => candidate!["instanceId"]!.GetValue<string>() == expected.InstanceId)!;
		Assert.Equal($"Live Qualification {expected.Name}", instance["name"]!.GetValue<string>());
		Assert.Equal(expected.ProcessId, instance["processId"]!.GetValue<int>());
	}

	private static void AssertRuntimeLocation(JsonNode pluginVersion, string bundleCacheDirectory)
	{
		string runtimeLocation = pluginVersion["runtimeLocation"]!.GetValue<string>();
		Assert.True(LiveQualificationOptIn.IsSameOrBelow(runtimeLocation, bundleCacheDirectory));
		Assert.Equal("CheatEngine.Mcp.Runtime.dll", Path.GetFileName(runtimeLocation));
	}

	private static async Task AssertEventuallyInstancesAsync(LiveMcpClient gateway, string remainingInstanceId)
	{
		DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
		do
		{
			JsonArray instances = (await GatewayCallAsync(gateway, "list_instances"))["instances"]!.AsArray();
			if (instances.Count == 1 && instances[0]!["instanceId"]!.GetValue<string>() == remainingInstanceId)
			{
				return;
			}
			await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		}
		while (DateTimeOffset.UtcNow < deadline);
		Assert.Fail("The gateway did not withdraw the stopped instance while retaining the running instance.");
	}

	private static async Task OpenTargetAsync(ILiveMcpToolClient client, LiveSandboxHost host)
	{
		JsonNode opened = await SuccessfulCallAsync(client, "open_process", new Dictionary<string, object?>
		{
			["process"] = host.TargetProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)
		});
		Assert.Equal(host.TargetProcessId, opened["processId"]!.GetValue<int>());
	}

	private static async Task AssertSpeedAsync(ILiveMcpToolClient client, double expected)
	{
		JsonNode configured = await SuccessfulCallAsync(client, "set_speedhack_speed", new Dictionary<string, object?> { ["speed"] = expected });
		Assert.Equal(expected, configured["result"]!["speed"]!.GetValue<double>(), precision: 4);
		JsonNode observed = await SuccessfulCallAsync(client, "get_speedhack_speed");
		Assert.Equal(expected, observed["result"]!["speed"]!.GetValue<double>(), precision: 4);
	}

	private static async Task<JsonNode> SuccessfulCallAsync(ILiveMcpToolClient client, string name, IReadOnlyDictionary<string, object?>? arguments = null)
	{
		JsonNode? result = await client.CallToolAsync(name, arguments).WaitAsync(McpTimeout, TestContext.Current.CancellationToken);
		Assert.NotNull(result);
		Assert.True(result["success"]?.GetValue<bool>() == true, $"Tool '{name}' failed: {result.ToJsonString()}");
		return result;
	}

	private static async Task<JsonNode> GatewayCallAsync(LiveMcpClient client, string name)
	{
		JsonNode? result = await client.CallToolAsync(name).WaitAsync(McpTimeout, TestContext.Current.CancellationToken);
		Assert.NotNull(result);
		return result;
	}

	private static Task<JsonNode> ReadMemoryAsync(ILiveMcpToolClient client, string address) => SuccessfulCallAsync(client, "read_memory", new Dictionary<string, object?>
	{
		["address"] = address,
		["dataType"] = "int32"
	});

	private static Task<JsonNode> WriteMemoryAsync(ILiveMcpToolClient client, string address, int value) => SuccessfulCallAsync(client, "write_memory", new Dictionary<string, object?>
	{
		["address"] = address,
		["dataType"] = "int32",
		["value"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture)
	});

	private static void AssertMemoryValue(JsonNode result, int expected) => Assert.Equal(expected, result["value"]!.GetValue<int>());

	private static async Task AssertEventuallyMemoryValueAsync(ILiveMcpToolClient client, string address, int expected)
	{
		DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
		int observed;
		do
		{
			JsonNode result = await ReadMemoryAsync(client, address);
			observed = result["value"]!.GetValue<int>();
			if (observed == expected)
			{
				return;
			}
			await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		}
		while (DateTimeOffset.UtcNow < deadline);
		Assert.Fail($"The active memory record did not restore {expected}; last observed value was {observed}.");
	}

	private static async Task CleanupMemoryRecordAsync(ILiveMcpToolClient client, int recordId)
	{
		await SuccessfulCallAsync(client, "set_memory_record_active", new Dictionary<string, object?> { ["id"] = recordId, ["active"] = false });
		await SuccessfulCallAsync(client, "delete_memory_record", new Dictionary<string, object?> { ["id"] = recordId });
	}

	private static async Task RestoreSpeedAsync(ILiveMcpToolClient client)
	{
		await SuccessfulCallAsync(client, "set_speedhack_speed", new Dictionary<string, object?> { ["speed"] = 1.0 });
	}
}
