using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Symbol;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Bounded live evidence for pure utility, overview, assembly and reversible symbol-preference tools.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveUtilitySymbolQualification
{
	private const string AssemblyAllocation = "live-utility-symbol-assembly";
	private static readonly string[] AssemblyInstructions = ["nop", "ret"];

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		ArgumentNullException.ThrowIfNull(instance);
		await VerifyUtilitiesAndOverviewAsync(sandbox, instance);
		await VerifyAssemblyAsync(sandbox, instance);
		await VerifyModulePreferenceAsync(sandbox, instance);
	}

	private static async Task VerifyUtilitiesAndOverviewAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		JsonNode calculated = await CallAsync(instance, CheatEngineToolNames.UtilCalculate,
			new Dictionary<string, object?> { ["expression"] = "0x10 + 5 * 3" });
		Assert.Equal(("31", "31", "1F"), (Text(calculated, "unsignedValue"), Text(calculated, "signedValue"), Text(calculated, "hex")));
		Receipt(sandbox, CheatEngineToolNames.UtilCalculate, calculated);

		JsonNode converted = await CallAsync(instance, CheatEngineToolNames.UtilConvertValue, new Dictionary<string, object?>
		{
			["value"] = "16909060",
			["sourceType"] = "int32",
			["targetType"] = "int32",
			["byteOrder"] = "little_endian",
			["pointerSize"] = 4
		});
		Assert.Equal(("int32", "int32", "little_endian", 4, "04 03 02 01", "16909060"),
			(Text(converted, "sourceType"), Text(converted, "targetType"), Text(converted, "byteOrder"),
				converted["pointerSize"]!.GetValue<int>(), Text(converted, "bytes"), Text(converted, "value")));
		Receipt(sandbox, CheatEngineToolNames.UtilConvertValue, converted);

		JsonNode overview = await CallAsync(instance, CheatEngineToolNames.RuntimeGetOverview);
		JsonNode runtime = overview["runtime"]!;
		JsonNode gates = runtime["gates"]!;
		JsonNode process = overview["process"]!;
		Assert.True(process["isOpen"]!.GetValue<bool>());
		Assert.Equal("CheatEngine.Mcp.dll", Text(runtime, "pluginFileName"));
		Assert.Equal((sandbox.HostA.TargetProcessId, sandbox.TargetArchitecture == "x86" ? 4 : 8, false, false, false, false, 0, 0),
			(process["processId"]!.GetValue<int>(), process["pointerSize"]!.GetValue<int>(), gates["unsafeLua"]!.GetValue<bool>(),
				gates["autoAssembler"]!.GetValue<bool>(), gates["targetCodeExecution"]!.GetValue<bool>(), gates["kernelAccess"]!.GetValue<bool>(),
				overview["resourceCount"]!.GetValue<int>(), overview["jobCount"]!.GetValue<int>()));
		Receipt(sandbox, CheatEngineToolNames.RuntimeGetOverview, overview);
	}

	private static async Task VerifyAssemblyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		bool allocated = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			JsonNode allocation = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = AssemblyAllocation, ["size"] = 16 });
			allocated = true;
			string address = Text(allocation, "address");
			JsonNode before = await CallAsync(instance, CheatEngineToolNames.MemoryRead,
				new Dictionary<string, object?> { ["address"] = address, ["valueType"] = "bytes", ["size"] = 16 });
			JsonNode assembled = await CallAsync(instance, CheatEngineToolNames.AsmAssemble, new Dictionary<string, object?>
			{
				["address"] = address,
				["instructions"] = AssemblyInstructions
			});
			JsonArray instructions = assembled["instructions"]!.AsArray();
			Assert.Equal(2, instructions.Count);
			Assert.Equal(("90C3", 2, address, "90", 1),
				(Text(assembled, "bytes"), assembled["size"]!.GetValue<int>(), Text(instructions[0], "address"),
					Text(instructions[0], "bytes"), instructions[0]!["size"]!.GetValue<int>()));
			Assert.Equal((Add(address, 1), "C3", 1),
				(Text(instructions[1], "address"), Text(instructions[1], "bytes"), instructions[1]!["size"]!.GetValue<int>()));
			JsonNode after = await CallAsync(instance, CheatEngineToolNames.MemoryRead,
				new Dictionary<string, object?> { ["address"] = address, ["valueType"] = "bytes", ["size"] = 16 });
			Assert.Equal(Text(before, "value"), Text(after, "value"));
			Receipt(sandbox, CheatEngineToolNames.AsmAssemble, assembled);
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			if (allocated)
			{
				await CleanupAsync(cleanup, async () =>
				{
					JsonNode freed = await CallAsync(instance, CheatEngineToolNames.MemoryFree,
						new Dictionary<string, object?> { ["name"] = AssemblyAllocation });
					Assert.Equal(AssemblyAllocation, Text(freed, "name"));
					Assert.Equal(16L, freed["size"]!.GetValue<long>());
					Assert.True(freed["release"]!["isComplete"]!.GetValue<bool>());
					LiveMcpToolResult absent = await instance.CallToolRawAsync(CheatEngineToolNames.MemoryFree,
						new Dictionary<string, object?> { ["name"] = AssemblyAllocation });
					Assert.True(absent.IsError);
					Assert.Equal("not_found", Text(absent.Payload!["error"], "kind"));
				});
			}
			sandbox.Record("live_utility_symbol_assembly_cleanup", new
			{
				freeAttempted = allocated,
				failures = cleanup.Count
			});
			ThrowCleanup(failure, cleanup);
		}
	}

	private static async Task VerifyModulePreferenceAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		string[]? original = null;
		bool mutationAttempted = false;
		bool restored = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			JsonNode initial = await CallAsync(instance, CheatEngineToolNames.SymbolGetModulePreference);
			Assert.False(initial["truncated"]!.GetValue<bool>());
			original = Strings(initial["modules"]);
			Assert.InRange(original.Length, 0, SymbolTools.MaximumPreferenceModules);
			Assert.Equal(original.Length, original.Distinct(StringComparer.OrdinalIgnoreCase).Count());
			Assert.All(original, module =>
			{
				Assert.False(string.IsNullOrWhiteSpace(module));
				Assert.InRange(module.Length, 1, SymbolTools.MaximumModuleLength);
				Assert.DoesNotContain('.', module);
				Assert.DoesNotContain(module, char.IsControl);
			});

			using Process target = Process.GetProcessById(sandbox.HostA.TargetProcessId);
			string[] loaded = target.Modules.Cast<ProcessModule>().Select(module => module.ModuleName).ToArray();
			Assert.Contains(loaded, name => string.Equals(name, "kernel32.dll", StringComparison.OrdinalIgnoreCase));
			Assert.Contains(loaded, name => string.Equals(name, "ntdll.dll", StringComparison.OrdinalIgnoreCase));
			string[] preferred = ["kernel32", "ntdll"];
			if (original.SequenceEqual(preferred, StringComparer.OrdinalIgnoreCase))
			{
				Array.Reverse(preferred);
			}
			mutationAttempted = true;
			JsonNode changed = await CallAsync(instance, CheatEngineToolNames.SymbolSetModulePreference,
				new Dictionary<string, object?> { ["modules"] = preferred, ["replace"] = true });
			Assert.False(changed["truncated"]!.GetValue<bool>());
			Assert.Equal(preferred, Strings(changed["modules"]));
			JsonNode readback = await CallAsync(instance, CheatEngineToolNames.SymbolGetModulePreference);
			Assert.False(readback["truncated"]!.GetValue<bool>());
			Assert.Equal(preferred, Strings(readback["modules"]));
			Receipt(sandbox, CheatEngineToolNames.SymbolGetModulePreference, readback);
			Receipt(sandbox, CheatEngineToolNames.SymbolSetModulePreference, changed);
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			if (mutationAttempted && original is not null)
			{
				await CleanupAsync(cleanup, async () =>
				{
					await CallAsync(instance, CheatEngineToolNames.SymbolSetModulePreference,
						new Dictionary<string, object?> { ["modules"] = original, ["replace"] = true });
					JsonNode restoredPreference = await CallAsync(instance, CheatEngineToolNames.SymbolGetModulePreference);
					Assert.False(restoredPreference["truncated"]!.GetValue<bool>());
					Assert.Equal(original, Strings(restoredPreference["modules"]));
					restored = true;
				});
			}
			if (restored)
			{
				sandbox.Record("live_utility_symbol_cleanup", new
				{
					modules = original,
					restored,
					failures = cleanup.Count
				});
			}
			ThrowCleanup(failure, cleanup);
		}
	}

	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool, IReadOnlyDictionary<string, object?>? arguments = null) =>
		await instance.CallToolAsync(tool, arguments) ?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
	private static string Text(JsonNode? node, string property) => node?[property]?.GetValue<string>() ?? throw new InvalidDataException($"Missing string '{property}'.");
	private static string[] Strings(JsonNode? node) =>
		[.. (node ?? throw new InvalidDataException("Missing module preference list.")).AsArray().Select(item => item!.GetValue<string>())];
	private static string Add(string address, int offset) =>
		(ulong.Parse(address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address[2..] : address,
			NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) + (ulong) offset).ToString("X", CultureInfo.InvariantCulture);
	private static void Receipt(LiveSandboxSession sandbox, string tool, JsonNode value) => sandbox.Record("all_tools_" + tool, value.DeepClone());
	private static async Task CleanupAsync(List<Exception> failures, Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception exception) { failures.Add(exception); }
	}
	private static void ThrowCleanup(Exception? failure, List<Exception> cleanup)
	{
		if (cleanup.Count == 0)
		{
			return;
		}
		if (failure is not null)
		{
			throw new AggregateException([failure, .. cleanup]);
		}
		throw new AggregateException(cleanup);
	}
}
