using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Known-value orientation checks on the owned fixture before any workflow changes its buffers.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveAdditionalReadQualification
{
	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpClient gateway)
	{
		LiveMcpInstanceClient instance = gateway.Bind(sandbox.HostA.InstanceId);
		JsonNode current = await CallAsync(instance, CheatEngineToolNames.ProcessGetCurrent);
		Assert.True(current["isOpen"]!.GetValue<bool>());
		Assert.Equal(sandbox.HostA.TargetProcessId, current["processId"]!.GetValue<int>());
		int pointerSize = current["pointerSize"]!.GetValue<int>();
		Assert.Equal(sandbox.TargetArchitecture == "x64" ? 8 : 4, pointerSize);
		// The target is selected before plugin activation, so the initial Client selection epoch may be zero.
		long selectionEpoch = current["selectionEpoch"]!.GetValue<long>();
		Assert.True(selectionEpoch >= 0);
		sandbox.Record("all_tools_process_get_current", current.DeepClone());

		JsonNode processes = await CallAsync(instance, CheatEngineToolNames.ProcessList,
			new Dictionary<string, object?>
			{
				["maximumResults"] = 4096,
				["nameContains"] = sandbox.TargetArchitecture == "x64" ? "CheatEngine.Mcp.LiveTarget" : "dotnet"
			});
		Assert.Contains(processes["processes"]!.AsArray(), process =>
			process!["processId"]!.GetValue<int>() == sandbox.HostA.TargetProcessId);
		Assert.False(processes["truncated"]!.GetValue<bool>());
		sandbox.Record("all_tools_process_list", new
		{
			targetFound = true
		});

		JsonNode threads = await CallAsync(instance, CheatEngineToolNames.ProcessListThreads);
		Assert.NotEmpty(threads["threads"]!.AsArray());
		Assert.All(threads["threads"]!.AsArray(), thread => Assert.True(thread!.GetValue<long>() > 0));
		Assert.False(threads["truncated"]!.GetValue<bool>());
		sandbox.Record("all_tools_process_list_threads", threads.DeepClone());

		// Set the captured value so failure cannot leave a different target interpretation behind.
		JsonNode pointer = await CallAsync(instance, CheatEngineToolNames.ProcessSetPointerSize,
			new Dictionary<string, object?> { ["pointerSize"] = pointerSize });
		Assert.Equal(pointerSize, pointer["pointerSize"]!.GetValue<int>());
		JsonNode after = await CallAsync(instance, CheatEngineToolNames.ProcessGetCurrent);
		Assert.Equal(pointerSize, after["pointerSize"]!.GetValue<int>());
		Assert.Equal(sandbox.HostA.TargetProcessId, after["processId"]!.GetValue<int>());
		Assert.Equal(selectionEpoch, after["selectionEpoch"]!.GetValue<long>());
		sandbox.Record("all_tools_process_set_pointer_size", new
		{
			pointerSize
		});

		string address = Parse(sandbox.HostA.TargetAddress).ToString("X", CultureInfo.InvariantCulture);
		JsonNode info = await CallAsync(instance, CheatEngineToolNames.MemoryGetAddressInfo,
			new Dictionary<string, object?>
			{
				["addresses"] = new[] { sandbox.HostA.TargetAddress },
				["includeRtti"] = false,
				["includePointerValue"] = true
			});
		JsonNode item = Assert.Single(info["items"]!.AsArray())!;
		Assert.Null(item["error"]);
		Assert.Equal(address, item["address"]!.GetValue<string>());
		JsonNode region = item["region"]!;
		Assert.Equal("committed", region["state"]!.GetValue<string>());
		Assert.Equal("private", region["type"]!.GetValue<string>());
		Assert.True(region["size"]!.GetValue<long>() > 0);
		Assert.True(region["protection"]!["read"]!.GetValue<bool>());
		Assert.True(region["protection"]!["write"]!.GetValue<bool>());
		Assert.NotNull(item["pointerValue"]);
		sandbox.Record("all_tools_memory_get_address_info", info.DeepClone());

		JsonNode samples = await CallAsync(instance, CheatEngineToolNames.MemoryReadSamples,
			new Dictionary<string, object?>
			{
				["addresses"] = new[] { sandbox.HostA.TargetAddress },
				["valueType"] = "int32",
				["intervalMs"] = 25,
				["durationMs"] = 100
			});
		int ticks = samples["ticks"]!.GetValue<int>();
		Assert.True(ticks > 0);
		Assert.True(samples["elapsedMs"]!.GetValue<int>() >= 0);
		Assert.Null(samples["missedTicks"]);
		JsonNode series = Assert.Single(samples["series"]!.AsArray())!;
		Assert.Equal(address, series["address"]!.GetValue<string>());
		Assert.Equal(ticks, series["samples"]!.GetValue<int>());
		Assert.Equal(1, series["distinctCount"]!.GetValue<int>());
		Assert.Equal("20260926", series["first"]!.GetValue<string>());
		Assert.Equal("20260926", series["last"]!.GetValue<string>());
		Assert.False(series["truncated"]!.GetValue<bool>());
		Assert.Null(series["error"]);
		Assert.Single(series["changes"]!.AsArray());
		Assert.Equal("20260926", series["changes"]![0]!["value"]!.GetValue<string>());
		sandbox.Record("all_tools_memory_read_samples", samples.DeepClone());

		JsonNode api = await CallAsync(instance, CheatEngineToolNames.LuaFindApi,
			new Dictionary<string, object?> { ["query"] = "getAddressSafe", ["limit"] = 100, ["offset"] = 0 });
		Assert.True(api["total"]!.GetValue<int>() > 0);
		Assert.False(api["truncated"]!.GetValue<bool>());
		Assert.NotEmpty(api["lines"]!.AsArray());
		Assert.All(api["lines"]!.AsArray(), line =>
		{
			Assert.True(line!["number"]!.GetValue<int>() > 0);
			Assert.Contains("getAddressSafe", line["text"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
		});
		sandbox.Record("all_tools_lua_find_api", api.DeepClone());

		JsonNode decoded = await CallAsync(instance, CheatEngineToolNames.CodeDisassembleBytes,
			new Dictionary<string, object?> { ["hexadecimalBytes"] = "90" });
		Assert.Equal("0", decoded["origin"]!.GetValue<string>());
		Assert.Contains("nop", decoded["text"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
		JsonNode returned = await CallAsync(instance, CheatEngineToolNames.CodeDisassembleBytes,
			new Dictionary<string, object?> { ["hexadecimalBytes"] = "C3" });
		Assert.Equal("0", returned["origin"]!.GetValue<string>());
		Assert.Contains("ret", returned["text"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
		sandbox.Record("all_tools_code_disassemble_bytes", new
		{
			nop = decoded.DeepClone(),
			ret = returned.DeepClone()
		});
	}

	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool,
		IReadOnlyDictionary<string, object?>? arguments = null) => await instance.CallToolAsync(tool, arguments)
		?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");

	private static ulong Parse(string address) => ulong.Parse(
		address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address[2..] : address,
		NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
}
