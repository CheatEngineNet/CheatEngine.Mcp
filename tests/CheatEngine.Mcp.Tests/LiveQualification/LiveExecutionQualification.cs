using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Bounded x64 evidence for the fixed target-execution tools.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveExecutionQualification
{
	private const string CodeAllocation = "live-execution-code";
	private const string DataAllocation = "live-execution-data";
	private const long CodeAllocationBytes = 65_536;
	private const long DataAllocationBytes = 8;
	private const string Source = """
		long long mcp_exec_remote(long long* output, long long value) { *output = value + 1000; return *output + 1; }
		long long mcp_exec_method(long long* self, long long value) { *self = value + 2000; return *self + 2; }
		""";

	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.Equal(LiveQualificationScenario.Execution, inputs.Scenario);
		Assert.Equal("x64", inputs.TargetArchitecture);
		await using LiveSandboxSession sandbox = await LiveSandboxSession.StartAsync(inputs,
			LiveSandboxOptions.Smoke with
			{
				ExecutionQualification = true,
				EnableTargetCodeExecution = true
			});
		LiveMcpClient? gateway = null;
		Exception? primaryFailure = null;
		List<Exception> cleanupFailures = [];
		try
		{
			gateway = await LiveMcpClient.ConnectGatewayAsync(sandbox.GatewayExecutablePath, sandbox.InstanceDirectory,
				TestContext.Current.CancellationToken);
			LiveMcpInstanceClient instance = gateway.Bind(sandbox.HostA.InstanceId);
			await CallAsync(instance, CheatEngineToolNames.ProcessAttach,
				new Dictionary<string, object?> { ["process"] = sandbox.HostA.TargetProcessId.ToString(CultureInfo.InvariantCulture) });
			await VerifyAsync(sandbox, instance);
		}
		catch (Exception exception)
		{
			primaryFailure = exception;
		}
		finally
		{
			if (gateway is not null)
			{
				try
				{
					await gateway.DisposeAsync();
				}
				catch (Exception exception) { cleanupFailures.Add(exception); }
			}

			await sandbox.TerminateExecutionTargetsBeforeHostCleanupAsync("execution fixture finalization", cleanupFailures);
		}

		if (primaryFailure is not null && cleanupFailures.Count == 0)
		{
			ExceptionDispatchInfo.Capture(primaryFailure).Throw();
		}
		if (primaryFailure is not null || cleanupFailures.Count != 0)
		{
			if (primaryFailure is not null)
			{
				cleanupFailures.Insert(0, primaryFailure);
			}

			throw new AggregateException(cleanupFailures);
		}

		sandbox.MarkPassed();
	}

	private static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		JsonNode runtime = await CallAsync(instance, CheatEngineToolNames.RuntimeGetInfo);
		JsonNode gates = runtime["gates"] ?? throw new InvalidDataException("Missing runtime gates.");
		Assert.True(gates["targetCodeExecution"]!.GetValue<bool>());
		Assert.False(gates["unsafeLua"]!.GetValue<bool>());
		Assert.False(gates["autoAssembler"]!.GetValue<bool>());
		Assert.False(gates["kernelAccess"]!.GetValue<bool>());
		sandbox.Record("live_execution_runtime", runtime.DeepClone());

		JsonNode local = await CallAsync(instance, CheatEngineToolNames.ExecCallLocal,
			new Dictionary<string, object?> { ["localAddress"] = "kernel32.GetProcessId", ["parameter"] = -1L });
		Assert.Matches("^[0-9A-F]+$", Text(local, "functionAddress"));
		Assert.Equal(sandbox.HostA.ProcessId.ToString(CultureInfo.InvariantCulture), Text(local, "returnValue"));
		Receipt(sandbox, CheatEngineToolNames.ExecCallLocal, local);

		await VerifyOwnedTargetCallsAsync(sandbox, instance);
	}

	private static async Task VerifyOwnedTargetCallsAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		bool codeAttempted = false;
		bool dataAttempted = false;
		bool callsProven = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			await AssertNoNamedAllocationAsync(instance, CodeAllocation);
			await AssertNoNamedAllocationAsync(instance, DataAllocation);
			codeAttempted = true;
			JsonNode code = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = CodeAllocation, ["size"] = CodeAllocationBytes, ["executable"] = true });
			string codeAddress = Text(code, "address");
			long codeBytes = code["size"]!.GetValue<long>();
			Assert.True(codeBytes >= CodeAllocationBytes);
			dataAttempted = true;
			JsonNode data = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = DataAllocation, ["size"] = DataAllocationBytes });
			string dataAddress = Text(data, "address");
			Assert.True(data["size"]!.GetValue<long>() >= DataAllocationBytes);
			await WriteInt64Async(instance, dataAddress, 0);
			Assert.Equal("0", await ReadInt64Async(instance, dataAddress));

			JsonNode compiled = await CallAsync(instance, CheatEngineToolNames.ExecCompileC, new Dictionary<string, object?>
			{
				["source"] = Source,
				["address"] = PointerExpression(codeAddress),
				["targetSelf"] = false,
				["kernelMode"] = false,
				["noDebug"] = true
			});
			Assert.False(compiled["symbolsTruncated"]!.GetValue<bool>());
			Assert.False(compiled["targetSelf"]!.GetValue<bool>());
			Assert.False(compiled["kernelMode"]!.GetValue<bool>());
			string remoteAddress = Text(RequireOwnedSymbol(compiled, "mcp_exec_remote", codeAddress, codeBytes), "address");
			string methodAddress = Text(RequireOwnedSymbol(compiled, "mcp_exec_method", codeAddress, codeBytes), "address");
			Assert.NotEqual(remoteAddress, methodAddress);

			JsonNode remote = await CallAsync(instance, CheatEngineToolNames.ExecCallRemote, new Dictionary<string, object?>
			{
				["functionAddress"] = PointerExpression(remoteAddress),
				["callingConvention"] = "stdcall",
				["timeoutMilliseconds"] = 1_000,
				["arguments"] = new object?[] { Integral(PointerExpression(dataAddress)), Integral("23") }
			});
			Assert.Equal(remoteAddress, Text(remote, "functionAddress"));
			Assert.Equal("1024", Text(remote, "returnValue"));
			Assert.Equal("1023", await ReadInt64Async(instance, dataAddress));
			Receipt(sandbox, CheatEngineToolNames.ExecCallRemote, remote);

			JsonNode method = await CallAsync(instance, CheatEngineToolNames.ExecCallMethod, new Dictionary<string, object?>
			{
				["functionAddress"] = PointerExpression(methodAddress),
				["classInstance"] = PointerExpression(dataAddress),
				["classRegister"] = 1,
				["callingConvention"] = "stdcall",
				["timeoutMilliseconds"] = 1_000,
				["arguments"] = new object?[] { Integral("29") }
			});
			Assert.Equal(methodAddress, Text(method, "functionAddress"));
			Assert.Equal("2031", Text(method, "returnValue"));
			Assert.Equal("2029", await ReadInt64Async(instance, dataAddress));
			Receipt(sandbox, CheatEngineToolNames.ExecCompileC, compiled);
			Receipt(sandbox, CheatEngineToolNames.ExecCallMethod, method);
			callsProven = true;
		}
		catch (Exception exception)
		{
			failure = exception;
		}
		finally
		{
			if (callsProven)
			{
				if (dataAttempted)
				{
					await CleanupAsync(cleanup, () => ReleaseAsync(sandbox, instance, DataAllocation, "live_execution_data_cleanup"));
				}

				if (codeAttempted)
				{
					await CleanupAsync(cleanup, () => ReleaseAsync(sandbox, instance, CodeAllocation, "live_execution_code_cleanup"));
				}
			}
		}

		ThrowQualification(failure, cleanup);
	}

	private static Dictionary<string, object?> Integral(string value) => new() { ["type"] = "integer", ["value"] = value };

	private static JsonObject RequireOwnedSymbol(JsonNode compiled, string name, string allocationAddress, long allocationBytes)
	{
		JsonNode? item = compiled["symbols"]!.AsArray().SingleOrDefault(symbol => Text(symbol!, "name") == name);
		JsonObject symbol = item?.AsObject() ?? throw new InvalidDataException($"Compiler did not return symbol '{name}'.");
		ulong start = ParseAddress(allocationAddress);
		Assert.InRange(ParseAddress(Text(symbol, "address")), start, checked(start + (ulong) allocationBytes - 1));
		return symbol;
	}

	private static async Task AssertNoNamedAllocationAsync(LiveMcpInstanceClient instance, string name)
	{
		JsonNode resources = await CallAsync(instance, CheatEngineToolNames.RuntimeListResources);
		Assert.DoesNotContain(resources["resources"]!.AsArray(), item => string.Equals(Text(item!, "name"), name, StringComparison.Ordinal));
	}

	private static async Task WriteInt64Async(LiveMcpInstanceClient instance, string address, long value)
	{
		JsonNode result = await CallAsync(instance, CheatEngineToolNames.MemoryWrite,
			new Dictionary<string, object?> { ["address"] = PointerExpression(address), ["valueType"] = "int64", ["value"] = value.ToString(CultureInfo.InvariantCulture), ["verify"] = true });
		Assert.True(result["verified"]!.GetValue<bool>());
	}

	private static async Task<string> ReadInt64Async(LiveMcpInstanceClient instance, string address) =>
		Text(await CallAsync(instance, CheatEngineToolNames.MemoryRead,
			new Dictionary<string, object?> { ["address"] = PointerExpression(address), ["valueType"] = "int64" }), "value");

	private static async Task ReleaseAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string name, string receipt)
	{
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
		LiveMcpToolResult result = await instance.WithCancellation(timeout.Token).CallToolRawAsync(CheatEngineToolNames.MemoryFree,
			new Dictionary<string, object?> { ["name"] = name });
		Assert.False(result.IsError);
		Assert.True(result.Payload!["release"]!["isComplete"]!.GetValue<bool>());
		result = await instance.WithCancellation(timeout.Token).CallToolRawAsync(CheatEngineToolNames.MemoryFree,
			new Dictionary<string, object?> { ["name"] = name });
		Assert.True(result.IsError);
		Assert.Equal("not_found", Text(result.Payload!["error"], "kind"));
		sandbox.Record(receipt, new
		{
			name,
			freed = true
		});
	}

	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool, IReadOnlyDictionary<string, object?>? arguments = null) =>
		await instance.CallToolAsync(tool, arguments) ?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");

	private static string Text(JsonNode? node, string property) => node?[property]?.GetValue<string>() ?? throw new InvalidDataException($"Missing string '{property}'.");
	internal static string PointerExpression(string address) => "0x" + ParseAddress(address).ToString("X", CultureInfo.InvariantCulture);
	private static ulong ParseAddress(string value) => ulong.Parse(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
	private static void Receipt(LiveSandboxSession sandbox, string tool, JsonNode value) => sandbox.Record("all_tools_" + tool, value.DeepClone());
	private static async Task CleanupAsync(List<Exception> cleanup, Func<Task> operation)
	{
		try
		{
			await operation();
		}
		catch (Exception exception) { cleanup.Add(exception); }
	}

	private static void ThrowQualification(Exception? failure, List<Exception> cleanup)
	{
		if (failure is null && cleanup.Count == 0)
		{
			return;
		}

		if (failure is not null && cleanup.Count == 0)
		{
			ExceptionDispatchInfo.Capture(failure).Throw();
		}

		throw failure is null ? new AggregateException(cleanup) : new AggregateException([failure, .. cleanup]);
	}
}
