using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Bounded, reversible live checks for the safe memory-analysis workflows on the disposable A target.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveWorkflowQualification
{
	private const string Source = "live-workflow-source";
	private const string Destination = "live-workflow-destination";
	private static readonly string[] InitialBatchValues = ["1", "-2", "3"];
	private static readonly string[] ScanTransitions = ["unknown", "changed", "increased", "decreased", "reset", "between"];
	private static readonly string[] HealthValues = ["7", "7"];
	private static readonly string[] FlagValues = ["100", "200"];

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpClient gateway)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		ArgumentNullException.ThrowIfNull(gateway);
		LiveMcpInstanceClient instance = gateway.Bind(sandbox.HostA.InstanceId);
		await VerifyGatesAsync(sandbox, instance);
		await VerifyMemoryAsync(sandbox, instance);
		await VerifyScannerAsync(sandbox, instance);
		await VerifyAobAsync(sandbox, instance);
		await VerifyControlFlowAsync(sandbox, instance);
		await VerifyModulesAsync(sandbox, instance);
		await VerifyStructuresAsync(sandbox, instance);
	}

	internal static async Task VerifyGatesAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		JsonNode runtime = await CallAsync(instance, CheatEngineToolNames.RuntimeGetInfo);
		JsonNode? gates = runtime["gates"];
		Assert.NotNull(gates);
		Assert.False(gates!["unsafeLua"]!.GetValue<bool>());
		Assert.False(gates["autoAssembler"]!.GetValue<bool>());
		Assert.False(gates["targetCodeExecution"]!.GetValue<bool>());
		Assert.False(gates["kernelAccess"]!.GetValue<bool>());
		sandbox.Record("live_workflow_gates", new
		{
			unsafeLua = false,
			autoAssembler = false,
			targetCodeExecution = false,
			kernelAccess = false
		});
	}

	private static async Task VerifyMemoryAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		string snapshot = "live-workflow-snapshot";
		bool sourceAllocated = false, destinationAllocated = false, snapshotCreated = false;
		JsonNode? previousProtection = null;
		string? destinationAddress = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			JsonNode source = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = Source, ["size"] = 128 });
			sourceAllocated = true;
			JsonNode destination = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = Destination, ["size"] = 128 });
			destinationAllocated = true;
			string src = Text(source, "address"), dst = Text(destination, "address");
			destinationAddress = dst;
			Assert.Equal(128L, source["size"]!.GetValue<long>());
			Assert.False(source["executable"]!.GetValue<bool>());
			Assert.Equal(128L, destination["size"]!.GetValue<long>());

			JsonNode batch = await CallAsync(instance, CheatEngineToolNames.MemoryWriteBatch, new Dictionary<string, object?>
			{
				["items"] = new object?[]
				{
					new Dictionary<string, object?> { ["address"] = At(src, 0), ["valueType"] = "int32", ["value"] = "1" },
					new Dictionary<string, object?> { ["address"] = At(src, 4), ["valueType"] = "int32", ["value"] = "-2" },
					new Dictionary<string, object?> { ["address"] = At(src, 8), ["valueType"] = "int32", ["value"] = "3" }
				},
				["verify"] = true
			});
			Assert.Equal(3, batch["written"]!.GetValue<int>());
			Assert.True(batch["verified"]!.GetValue<bool>());
			Assert.Null(batch["mismatched"]);
			JsonNode values = await CallAsync(instance, CheatEngineToolNames.MemoryRead,
				new Dictionary<string, object?> { ["address"] = src, ["valueType"] = "int32", ["count"] = 3 });
			Assert.Equal(InitialBatchValues, Strings(values["values"]));

			await WriteAsync(instance, At(src, 16), "int32", "-1");
			await WriteAsync(instance, At(src, 20), "uint32", "4294967295");
			await WriteAsync(instance, At(src, 24), "uint32", "287454020", "big_endian");
			await WriteAsync(instance, At(src, 32), "string", "MCP", nullTerminate: true);
			await WriteAsync(instance, At(src, 40), "wstring", "CE", nullTerminate: true);
			await WriteAsync(instance, At(src, 56), "uint64", "18446744073709551615");
			await WriteAsync(instance, At(src, 64), "int64", "-9223372036854775808");
			Assert.Equal("-1", await ReadAsync(instance, At(src, 16), "int32"));
			Assert.Equal("4294967295", await ReadAsync(instance, At(src, 20), "uint32"));
			Assert.Equal("11 22 33 44", await ReadAsync(instance, At(src, 24), "bytes", size: 4));
			Assert.Equal("287454020", await ReadAsync(instance, At(src, 24), "uint32", byteOrder: "big_endian"));
			Assert.Equal("MCP", await ReadAsync(instance, At(src, 32), "string", length: 4));
			Assert.Equal("CE", await ReadAsync(instance, At(src, 40), "wstring", length: 3));
			Assert.Equal("18446744073709551615", await ReadAsync(instance, At(src, 56), "uint64"));
			Assert.Equal("-9223372036854775808", await ReadAsync(instance, At(src, 64), "int64"));

			JsonNode readBatch = await CallAsync(instance, CheatEngineToolNames.MemoryReadBatch, new Dictionary<string, object?>
			{
				["items"] = new object?[]
				{
					new Dictionary<string, object?> { ["address"] = src, ["valueType"] = "int32" },
					new Dictionary<string, object?> { ["address"] = sandbox.HostA.UnreadableRootAddress, ["valueType"] = "int32" },
					new Dictionary<string, object?> { ["address"] = At(src, 32), ["valueType"] = "string", ["length"] = 4 }
				}
			});
			Assert.Equal(1, readBatch["failed"]!.GetValue<int>());
			JsonArray readItems = readBatch["items"]!.AsArray();
			Assert.Equal("1", Text(readItems[0], "value"));
			Assert.Equal("memory_read_failed", Text(readItems[1]!["error"], "kind"));
			Assert.Equal("MCP", Text(readItems[2], "value"));

			await WriteAsync(instance, At(src, 48), "int32", "123");
			LiveMcpToolResult partial = await instance.CallToolRawAsync(CheatEngineToolNames.MemoryWriteBatch,
				new Dictionary<string, object?>
				{
					["items"] = new object?[]
					{
						new Dictionary<string, object?> { ["address"] = At(src, 48), ["valueType"] = "int32", ["value"] = "456" },
						new Dictionary<string, object?> { ["address"] = sandbox.HostA.UnreadableRootAddress, ["valueType"] = "int32", ["value"] = "789" }
					},
					["verify"] = false
				});
			Assert.True(partial.IsError);
			JsonNode partialPayload = Payload(partial);
			Assert.Equal("partial_effect", Text(partialPayload["error"], "kind"));
			Assert.Equal("started", Text(partialPayload["error"], "hostEffect"));
			Assert.False(partialPayload["error"]!["retryable"]!.GetValue<bool>());
			Assert.Equal(1, partialPayload["error"]!["details"]!["completed"]!.GetValue<int>());
			Assert.Equal(1, partialPayload["error"]!["details"]!["failedIndex"]!.GetValue<int>());
			Assert.Equal("partial", Text(partialPayload["error"]!["details"], "effectState"));
			Assert.Equal("456", await ReadAsync(instance, At(src, 48), "int32"));
			await WriteAsync(instance, At(src, 48), "int32", "123");

			const string known = "00 11 22 33 44 55 66 77 88 99 AA BB CC DD EE FF";
			await WriteAsync(instance, src, "bytes", known);
			JsonNode hash = await CallAsync(instance, CheatEngineToolNames.MemoryHash,
				new Dictionary<string, object?> { ["address"] = src, ["size"] = 16, ["algorithm"] = "sha256" });
			Assert.Equal(Convert.ToHexString(SHA256.HashData(Convert.FromHexString(known.Replace(" ", string.Empty))))
				.ToLowerInvariant(), Text(hash, "hash"));
			JsonNode copy = await CallAsync(instance, CheatEngineToolNames.MemoryCopy,
				new Dictionary<string, object?> { ["source"] = src, ["destination"] = dst, ["size"] = 16 });
			Assert.Equal(src, Text(copy, "source"));
			Assert.Equal(dst, Text(copy, "destination"));
			JsonNode compare = await CallAsync(instance, CheatEngineToolNames.MemoryCompare,
				new Dictionary<string, object?> { ["addressA"] = src, ["addressB"] = dst, ["size"] = 16, ["maxDifferences"] = 4 });
			Assert.True(compare["equal"]!.GetValue<bool>());
			Assert.False(compare["truncated"]!.GetValue<bool>());
			Assert.Empty(compare["differences"]!.AsArray());

			JsonNode snapshotResult = await CallAsync(instance, CheatEngineToolNames.MemoryCreateSnapshot,
				new Dictionary<string, object?> { ["name"] = snapshot, ["address"] = dst, ["size"] = 16 });
			snapshotCreated = true;
			Assert.Equal(0, snapshotResult["unreadableBytes"]!.GetValue<int>());
			Assert.Empty(snapshotResult["unreadable"]!.AsArray());
			await WriteAsync(instance, dst, "int32", "7");
			JsonNode diff = await CallAsync(instance, CheatEngineToolNames.MemoryCompareSnapshot,
				new Dictionary<string, object?> { ["name"] = snapshot, ["valueType"] = "int32", ["alignment"] = 4, ["change"] = "changed", ["offset"] = 0, ["limit"] = 10 });
			Assert.Equal((4, 0, 1), (diff["compared"]!.GetValue<int>(), diff["skipped"]!.GetValue<int>(), diff["total"]!.GetValue<int>()));
			Assert.Equal(dst, Text(Assert.Single(diff["changes"]!.AsArray()), "address"));

			JsonNode protection = await CallAsync(instance, CheatEngineToolNames.MemorySetProtection,
				new Dictionary<string, object?> { ["address"] = dst, ["size"] = 16, ["read"] = true, ["write"] = false, ["execute"] = false });
			previousProtection = protection["previous"]!.DeepClone();
			Assert.True(protection["current"]!["read"]!.GetValue<bool>());
			Assert.False(protection["current"]!["write"]!.GetValue<bool>());
			Assert.False(protection["current"]!["execute"]!.GetValue<bool>());
			string refusedPath = Path.Combine(sandbox.InstanceDirectory, "forbidden-workflow-dump.bin");
			LiveMcpToolResult refused = await instance.CallToolRawAsync(CheatEngineToolNames.MemoryDumpToFile,
				new Dictionary<string, object?> { ["address"] = src, ["size"] = 16, ["path"] = refusedPath, ["overwrite"] = true });
			Assert.True(refused.IsError);
			JsonNode refusal = Payload(refused);
			Assert.Equal("invalid_argument", Text(refusal["error"], "kind"));
			Assert.Equal("not_started", Text(refusal["error"], "hostEffect"));
			Assert.False(File.Exists(refusedPath));
			sandbox.Record("live_workflow_memory", new
			{
				source = src,
				destination = dst,
				hash = Text(hash, "hash"),
				integerRoundTrips = true,
				batchPartial = true,
				snapshot = snapshotResult.DeepClone(),
				filePolicyRefusal = true
			});
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			if (previousProtection is not null && destinationAddress is not null)
			{
				await CleanupAsync(cleanup, () => CallAsync(instance, CheatEngineToolNames.MemorySetProtection,
					new Dictionary<string, object?>
					{
						["address"] = destinationAddress,
						["size"] = 16,
						["read"] = previousProtection["read"]!.GetValue<bool>(),
						["write"] = previousProtection["write"]!.GetValue<bool>(),
						["execute"] = previousProtection["execute"]!.GetValue<bool>()
					}));
			}
			if (snapshotCreated)
			{
				await CleanupAsync(cleanup, () => CallAsync(instance, CheatEngineToolNames.MemoryDeleteSnapshot, new Dictionary<string, object?> { ["name"] = snapshot }));
			}

			if (destinationAllocated)
			{
				await CleanupAsync(cleanup, () => CallAsync(instance, CheatEngineToolNames.MemoryFree, new Dictionary<string, object?> { ["name"] = Destination }));
			}

			if (sourceAllocated)
			{
				await CleanupAsync(cleanup, () => CallAsync(instance, CheatEngineToolNames.MemoryFree, new Dictionary<string, object?> { ["name"] = Source }));
			}

			sandbox.Record("live_workflow_memory_cleanup", new
			{
				protectionRestoreAttempted = previousProtection is not null,
				snapshotDeleteAttempted = snapshotCreated,
				destinationFreeAttempted = destinationAllocated,
				sourceFreeAttempted = sourceAllocated,
				failures = cleanup.Count
			});
			ThrowCleanup(failure, cleanup);
		}
	}

	private static async Task VerifyScannerAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		JsonNode settings = await CallAsync(instance, CheatEngineToolNames.ScanGetStatus,
			new Dictionary<string, object?> { ["scannerName"] = "main" });
		Assert.True(settings["settings"]!["memPrivate"]!.GetValue<bool>(),
			"Named scan qualification requires the existing CE MEM_PRIVATE preference to be enabled.");
		const string allocation = "live-workflow-scanner";
		const string scanner = "live-workflow-scanner-session";
		bool allocated = false, created = false;
		List<Exception> cleanup = [];
		Exception? failure = null;
		try
		{
			JsonNode result = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate, new Dictionary<string, object?> { ["name"] = allocation, ["size"] = 64 });
			allocated = true;
			string address = Text(result, "address");
			await WriteAsync(instance, address, "int32", "10");
			created = true;
			await CallAsync(instance, CheatEngineToolNames.ScanFirst, ScanArguments(scanner, address, "unknown"));
			await WriteAsync(instance, address, "int32", "20");
			await CallAsync(instance, CheatEngineToolNames.ScanNext, new Dictionary<string, object?> { ["scannerName"] = scanner, ["comparison"] = "changed" });
			await AssertScanAsync(instance, scanner, address, "20");
			await WriteAsync(instance, address, "int32", "30");
			await CallAsync(instance, CheatEngineToolNames.ScanNext, new Dictionary<string, object?> { ["scannerName"] = scanner, ["comparison"] = "increased" });
			await AssertScanAsync(instance, scanner, address, "30");
			await WriteAsync(instance, address, "int32", "25");
			await CallAsync(instance, CheatEngineToolNames.ScanNext, new Dictionary<string, object?> { ["scannerName"] = scanner, ["comparison"] = "decreased" });
			await AssertScanAsync(instance, scanner, address, "25");
			JsonNode reset = await CallAsync(instance, CheatEngineToolNames.ScanReset, new Dictionary<string, object?> { ["scannerName"] = scanner });
			Assert.False(reset["removed"]!.GetValue<bool>());
			Dictionary<string, object?> between = ScanArguments(scanner, address, "between");
			between["value"] = "20";
			between["upperValue"] = "30";
			await CallAsync(instance, CheatEngineToolNames.ScanFirst, between);
			await AssertScanAsync(instance, scanner, address, "25");
			sandbox.Record("live_workflow_scanner", new
			{
				address,
				transitions = ScanTransitions
			});
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			if (created)
			{
				await CleanupAsync(cleanup, async () =>
				{
					LiveMcpToolResult deleted = await instance.CallToolRawAsync(CheatEngineToolNames.ScanDelete,
						new Dictionary<string, object?> { ["scannerName"] = scanner });
					if (deleted.IsError)
					{
						Assert.Equal("not_found", Text(Payload(deleted)["error"], "kind"));
					}
				});
			}

			if (allocated)
			{
				await CleanupAsync(cleanup, () => CallAsync(instance, CheatEngineToolNames.MemoryFree, new Dictionary<string, object?> { ["name"] = allocation }));
			}

			sandbox.Record("live_workflow_scanner_cleanup", new
			{
				deleteAttempted = created,
				freeAttempted = allocated,
				failures = cleanup.Count
			});
			ThrowCleanup(failure, cleanup);
		}
	}

	private static async Task VerifyAobAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		const string allocation = "live-workflow-aob";
		bool allocated = false;
		List<Exception> cleanup = [];
		Exception? failure = null;
		try
		{
			JsonNode result = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate, new Dictionary<string, object?> { ["name"] = allocation, ["size"] = 64 });
			allocated = true;
			string address = Text(result, "address");
			await WriteAsync(instance, At(address, 16), "bytes", "D1 2B 7E 93 44 A8 19 C6");
			await WriteAsync(instance, At(address, 32), "int32", "16909060");
			Dictionary<string, object?> args = ScanBounds(address);
			args["patterns"] = new[] { "D1 2B 7E 93 44 A8 19 C6", "D1 2B ?? 93 44 A8 19 C6" };
			sandbox.Record("live_workflow_aob_step", new
			{
				step = "before_aob_find"
			});
			JsonNode found = await CallAsync(instance, CheatEngineToolNames.AobFind, args);
			Assert.Equal(2, found["results"]!.AsArray().Count);
			Assert.Equal("D1 2B 7E 93 44 A8 19 C6", Text(found["results"]![0], "pattern"));
			Assert.Equal("D1 2B ?? 93 44 A8 19 C6", Text(found["results"]![1], "pattern"));
			foreach (JsonNode? match in found["results"]!.AsArray())
			{
				AssertAob(match!, At(address, 16));
			}

			Dictionary<string, object?> value = ScanBounds(address);
			value["valueType"] = "int32";
			value["value"] = "16909060";
			sandbox.Record("live_workflow_aob_step", new
			{
				step = "before_aob_find_value"
			});
			JsonNode foundValue = await CallAsync(instance, CheatEngineToolNames.AobFindValue, value);
			Assert.Equal("04 03 02 01", Text(foundValue["result"], "pattern"));
			AssertAob(foundValue["result"]!, At(address, 32));
			sandbox.Record("live_workflow_aob", new
			{
				address,
				exactAndWildcard = found.DeepClone(),
				value = foundValue.DeepClone()
			});
		}
		catch (Exception exception) { failure = exception; throw; }
		finally { if (allocated) { await CleanupAsync(cleanup, () => CallAsync(instance, CheatEngineToolNames.MemoryFree, new Dictionary<string, object?> { ["name"] = allocation })); } sandbox.Record("live_workflow_aob_cleanup", new { freeAttempted = allocated, failures = cleanup.Count }); ThrowCleanup(failure, cleanup); }
	}

	private static async Task VerifyControlFlowAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		const string allocation = "live-workflow-cfg";
		bool allocated = false;
		List<Exception> cleanup = [];
		Exception? failure = null;
		try
		{
			JsonNode allocationResult = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate, new Dictionary<string, object?> { ["name"] = allocation, ["size"] = 64 });
			allocated = true;
			string address = Text(allocationResult, "address");
			await WriteAsync(instance, address, "bytes", "31 C0 85 C0 74 06 B8 01 00 00 00 C3 B8 02 00 00 00 C3");
			JsonNode graph = await CallAsync(instance, CheatEngineToolNames.CodeGetFunctionGraph, new Dictionary<string, object?> { ["address"] = address, ["maxInstructions"] = 16, ["maxBytes"] = 18, ["includeInstructions"] = true });
			Assert.Equal((address, address, At(address, 18)), (Text(graph, "entry"), Text(graph, "start"), Text(graph, "end")));
			Assert.False(graph["truncated"]!.GetValue<bool>());
			Assert.Equal(7, graph["instructionCount"]!.GetValue<int>());
			Assert.Empty(graph["calls"]!.AsArray());
			JsonArray blocks = graph["blocks"]!.AsArray();
			Assert.Equal(3, blocks.Count);
			Assert.Equal("conditional", Text(blocks[0], "terminator"));
			Assert.Equal(new[] { At(address, 12), At(address, 6) }, Strings(blocks[0]!["successors"]));
			Assert.Equal("return", Text(blocks[1], "terminator"));
			Assert.Equal("return", Text(blocks[2], "terminator"));
			Assert.Equal(7, blocks.Sum(block => block!["instructionCount"]!.GetValue<int>()));
			JsonNode truncated = await CallAsync(instance, CheatEngineToolNames.CodeGetFunctionGraph, new Dictionary<string, object?> { ["address"] = address, ["maxInstructions"] = 1, ["maxBytes"] = 18, ["includeInstructions"] = false });
			Assert.True(truncated["truncated"]!.GetValue<bool>());
			Assert.Equal(1, truncated["instructionCount"]!.GetValue<int>());
			Assert.Equal("limit", Text(Assert.Single(truncated["blocks"]!.AsArray()), "terminator"));
			Assert.Null(Assert.Single(truncated["blocks"]!.AsArray())!["instructions"]);
			sandbox.Record("live_workflow_cfg", new
			{
				address,
				graph = graph.DeepClone(),
				truncated = truncated.DeepClone(),
				executed = false
			});
		}
		catch (Exception exception) { failure = exception; throw; }
		finally { if (allocated) { await CleanupAsync(cleanup, () => CallAsync(instance, CheatEngineToolNames.MemoryFree, new Dictionary<string, object?> { ["name"] = allocation })); } sandbox.Record("live_workflow_cfg_cleanup", new { freeAttempted = allocated, failures = cleanup.Count }); ThrowCleanup(failure, cleanup); }
	}

	private static async Task VerifyModulesAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		JsonNode listed = await CallAsync(instance, CheatEngineToolNames.ModuleList, new Dictionary<string, object?> { ["format"] = "detailed", ["limit"] = 1000 });
		string expected = sandbox.TargetArchitecture == "x86" ? "dotnet.exe" : "CheatEngine.Mcp.LiveTarget.exe";
		JsonNode module = Assert.Single(listed["modules"]!.AsArray(), item => string.Equals(Text(item, "name"), expected, StringComparison.OrdinalIgnoreCase))!;
		string name = Text(module, "name"), @base = Text(module, "base");
		Assert.True(Parse(@base) > 0);
		Assert.True(module["size"]!.GetValue<long>() > 0);
		Assert.Equal(sandbox.TargetArchitecture == "x64", module["is64Bit"]!.GetValue<bool>());
		JsonNode details = await CallAsync(instance, CheatEngineToolNames.ModuleGet, new Dictionary<string, object?> { ["module"] = name });
		Assert.Equal(@base, Text(details, "base"));
		Assert.NotEmpty(details["sections"]!.AsArray());
		Assert.Contains(details["sections"]!.AsArray(), section => section!["executable"]?.GetValue<bool>() == true);
		Assert.Equal(sandbox.TargetArchitecture, Text(details["pe"], "machine"));
		JsonNode imports = await CallAsync(instance, CheatEngineToolNames.ModuleListImports, new Dictionary<string, object?> { ["module"] = name, ["includeDelayLoaded"] = true, ["offset"] = 0, ["limit"] = 1000 });
		Assert.True(imports["total"]!.GetValue<int>() > 0);
		foreach (JsonNode? import in imports["imports"]!.AsArray())
		{
			Assert.False(string.IsNullOrWhiteSpace(Text(import, "dll")));
			_ = Parse(Text(import, "slotAddress"));
			_ = Parse(Text(import, "value"));
		}
		JsonNode symbols = await CallAsync(instance, CheatEngineToolNames.SymbolResolve, new Dictionary<string, object?> { ["expressions"] = new[] { name, name + "+0" }, ["shallow"] = true });
		Assert.Equal(2, symbols["items"]!.AsArray().Count);
		foreach (JsonNode? item in symbols["items"]!.AsArray())
		{
			Assert.Null(item!["error"]);
			Assert.Equal(@base, Text(item, "address"));
		}
		JsonNode patches = await CallAsync(instance, CheatEngineToolNames.ModuleFindPatches, new Dictionary<string, object?> { ["module"] = name, ["includeNonExecutable"] = false, ["limit"] = 1024 });
		Assert.Equal(name, Text(patches, "module"));
		Assert.True(patches["comparedBytes"]!.GetValue<long>() > 0);
		Assert.Equal(0L, patches["unreadableBytes"]!.GetValue<long>());
		Assert.False(patches["truncated"]!.GetValue<bool>());
		sandbox.Record("live_workflow_modules", new
		{
			expected,
			selected = module.DeepClone(),
			details = details.DeepClone(),
			imports = imports.DeepClone(),
			symbols = symbols.DeepClone(),
			patches = patches.DeepClone()
		});
	}

	private static async Task VerifyStructuresAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		const string first = "live-workflow-structure-a", second = "live-workflow-structure-b";
		string name = "McpLiveWorkflow_" + sandbox.HostA.TargetProcessId.ToString(CultureInfo.InvariantCulture);
		bool a = false, b = false, structure = false;
		List<Exception> cleanup = [];
		Exception? failure = null;
		try
		{
			JsonNode firstAllocation = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate, new Dictionary<string, object?> { ["name"] = first, ["size"] = 16 });
			a = true;
			JsonNode secondAllocation = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate, new Dictionary<string, object?> { ["name"] = second, ["size"] = 16 });
			b = true;
			string addressA = Text(firstAllocation, "address"), addressB = Text(secondAllocation, "address");
			foreach ((string address, string health, string flags) in new[] { (addressA, "7", "100"), (addressB, "7", "200") })
			{
				await WriteAsync(instance, address, "int32", health);
				await WriteAsync(instance, At(address, 4), "uint16", flags);
			}
			JsonNode created = await CallAsync(instance, CheatEngineToolNames.StructureCreate, new Dictionary<string, object?> { ["name"] = name, ["elements"] = new object?[] { new Dictionary<string, object?> { ["offset"] = "0", ["name"] = "health", ["valueType"] = "int32" }, new Dictionary<string, object?> { ["offset"] = "4", ["name"] = "flags", ["valueType"] = "uint16" } } });
			structure = true;
			Assert.Equal(2, created["elementCount"]!.GetValue<int>());
			Assert.True(created["size"]!.GetValue<int>() >= 6);
			JsonNode read = await CallAsync(instance, CheatEngineToolNames.StructureRead, new Dictionary<string, object?> { ["name"] = name, ["addresses"] = new[] { addressA, addressB }, ["offset"] = 0, ["limit"] = 8, ["format"] = "detailed" });
			JsonArray rows = read["elements"]!.AsArray();
			Assert.Equal(HealthValues, Strings(rows[0]!["values"]));
			Assert.Equal(FlagValues, Strings(rows[1]!["values"]));
			JsonNode compared = await CallAsync(instance, CheatEngineToolNames.StructureCompare, new Dictionary<string, object?> { ["groupA"] = new[] { addressA }, ["groupB"] = new[] { addressB }, ["structureName"] = name, ["granularity"] = 4, ["interpretAs"] = "auto", ["mode"] = "discriminate", ["offset"] = 0, ["limit"] = 8 });
			JsonNode row = Assert.Single(compared["rows"]!.AsArray())!;
			Assert.Equal(("4", "flags", "discriminator"), (Text(row, "offset"), Text(row, "name"), Text(row, "classification")));
			JsonNode written = await CallAsync(instance, CheatEngineToolNames.StructureWriteElement, new Dictionary<string, object?> { ["name"] = name, ["address"] = addressA, ["index"] = 0, ["value"] = "9" });
			Assert.Equal("9", Text(written, "value"));
			Assert.Equal("9", await ReadAsync(instance, addressA, "int32"));
			JsonNode header = await CallAsync(instance, CheatEngineToolNames.StructureGenerateCHeader, new Dictionary<string, object?> { ["names"] = new[] { name }, ["generator"] = "managed" });
			Assert.Equal("managed", Text(header, "generator"));
			Assert.Contains(name, Text(header, "text"), StringComparison.Ordinal);
			Assert.Contains("int32_t health", Text(header, "text"), StringComparison.Ordinal);
			Assert.Contains("uint16_t flags", Text(header, "text"), StringComparison.Ordinal);
			sandbox.Record("live_workflow_structures", new
			{
				name,
				read = read.DeepClone(),
				comparison = compared.DeepClone(),
				header = header.DeepClone()
			});
		}
		catch (Exception exception) { failure = exception; throw; }
		finally { if (structure) { await CleanupAsync(cleanup, () => CallAsync(instance, CheatEngineToolNames.StructureDelete, new Dictionary<string, object?> { ["name"] = name })); } if (b) { await CleanupAsync(cleanup, () => CallAsync(instance, CheatEngineToolNames.MemoryFree, new Dictionary<string, object?> { ["name"] = second })); } if (a) { await CleanupAsync(cleanup, () => CallAsync(instance, CheatEngineToolNames.MemoryFree, new Dictionary<string, object?> { ["name"] = first })); } sandbox.Record("live_workflow_structures_cleanup", new { deleteAttempted = structure, freeBAttempted = b, freeAAttempted = a, failures = cleanup.Count }); ThrowCleanup(failure, cleanup); }
	}

	/// <summary>Reuses selected bounded workflows in a fresh diagnostic session; this does not qualify the full matrix.</summary>
	internal static async Task VerifyDispatchDiagnosticAsync(LiveSandboxSession sandbox, LiveMcpClient gateway,
		LiveDispatchDiagnosticCase @case)
	{
		LiveMcpInstanceClient instance = gateway.Bind(sandbox.HostA.InstanceId);
		await VerifyGatesAsync(sandbox, instance);
		if (@case is LiveDispatchDiagnosticCase.MemoryNamedScanThenAob or LiveDispatchDiagnosticCase.ResourcePreludeMemoryNamedScanThenAob)
		{
			sandbox.Record("dispatch_diagnostic_step", new
			{
				@case = @case.ToString(),
				step = "before_memory"
			});
			await VerifyMemoryAsync(sandbox, instance);
		}
		if (@case is LiveDispatchDiagnosticCase.NamedScanThenAob or LiveDispatchDiagnosticCase.MemoryNamedScanThenAob
			or LiveDispatchDiagnosticCase.ResourcePreludeMemoryNamedScanThenAob)
		{
			sandbox.Record("dispatch_diagnostic_step", new
			{
				@case = @case.ToString(),
				step = "before_named_scan"
			});
			await VerifyScannerAsync(sandbox, instance);
		}
		sandbox.Record("dispatch_diagnostic_step", new
		{
			@case = @case.ToString(),
			step = "before_aob"
		});
		await VerifyAobAsync(sandbox, instance);
		sandbox.Record("dispatch_diagnostic_step", new
		{
			@case = @case.ToString(),
			step = "before_post_aob_allocation"
		});
		await VerifyPostAobAllocationAsync(sandbox, instance);
		sandbox.Record("dispatch_diagnostic_result", new
		{
			@case = @case.ToString(),
			aobToolCalls = 2,
			nativeScans = 3,
			postAobAllocationFreed = true,
			stableQualification = false
		});
	}

	private static async Task VerifyPostAobAllocationAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		const string allocation = "live-dispatch-post-aob";
		bool allocated = false;
		bool freed = false;
		List<Exception> cleanup = [];
		Exception? failure = null;
		try
		{
			JsonNode result = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = allocation, ["size"] = 64, ["executable"] = false });
			allocated = true;
			sandbox.Record("dispatch_diagnostic_post_aob_allocation", new
			{
				result = result.DeepClone()
			});
			Assert.Equal(allocation, Text(result, "name"));
			Assert.NotEqual(0UL, Parse(Text(result, "address")));
			Assert.False(result["executable"]!.GetValue<bool>());
		}
		catch (Exception exception)
		{
			failure = exception;
			throw;
		}
		finally
		{
			if (allocated)
			{
				await CleanupAsync(cleanup, async () =>
				{
					JsonNode result = await CallAsync(instance, CheatEngineToolNames.MemoryFree,
						new Dictionary<string, object?> { ["name"] = allocation });
					freed = true;
					sandbox.Record("dispatch_diagnostic_post_aob_free", new
					{
						result = result.DeepClone()
					});
				});
			}
			sandbox.Record("dispatch_diagnostic_post_aob_cleanup", new
			{
				allocated,
				freeAttempted = allocated,
				freed,
				failures = cleanup.Count
			});
			ThrowCleanup(failure, cleanup);
		}
	}

	private static Dictionary<string, object?> ScanArguments(string name, string address, string comparison) => new()
	{
		["scannerName"] = name,
		["valueType"] = "int32",
		["comparison"] = comparison,
		["startAddress"] = address,
		["endAddress"] = At(address, 64),
		["writable"] = "required",
		["executable"] = "excluded",
		["copyOnWrite"] = "any",
		["alignment"] = 4,
		["includeMapped"] = false
	};

	private static Dictionary<string, object?> ScanBounds(string address) => new()
	{
		["startAddress"] = address,
		["endAddress"] = At(address, 63),
		["writable"] = "required",
		["executable"] = "excluded",
		["copyOnWrite"] = "excluded",
		["alignment"] = 4,
		["limit"] = 2,
		["includeMapped"] = false
	};

	private static async Task AssertScanAsync(LiveMcpInstanceClient instance, string name, string address, string value)
	{
		JsonNode status = await CallAsync(instance, CheatEngineToolNames.ScanGetStatus, new Dictionary<string, object?> { ["scannerName"] = name });
		Assert.Equal((name, "independent", "ResultsReady", true, 1L, "int32"), (Text(status, "scannerName"), Text(status, "mode"), Text(status, "state"), status["resultsReady"]!.GetValue<bool>(), status["count"]!.GetValue<long>(), Text(status, "valueType")));
		JsonNode results = await CallAsync(instance, CheatEngineToolNames.ScanListResults, new Dictionary<string, object?> { ["scannerName"] = name, ["startIndex"] = 0, ["maximumResults"] = 8 });
		JsonNode item = Assert.Single(results["results"]!.AsArray())!;
		Assert.Equal((address, value), (Text(item, "address"), Text(item, "value")));
		Assert.False(results["hasMore"]!.GetValue<bool>());
	}

	private static void AssertAob(JsonNode result, string address)
	{
		Assert.Equal(1, result["count"]!.GetValue<int>());
		Assert.True(result["exact"]!.GetValue<bool>());
		Assert.True(result["unique"]!.GetValue<bool>());
		Assert.Equal("bounded_scan", Text(result, "scope"));
		Assert.True(result["targetVerified"]!.GetValue<bool>());
		Assert.Equal(address, Assert.Single(Strings(result["matches"])));
	}

	private static async Task WriteAsync(LiveMcpInstanceClient client, string address, string valueType, string value, string? byteOrder = null, bool nullTerminate = false)
	{
		Dictionary<string, object?> arguments = new()
		{
			["address"] = address,
			["valueType"] = valueType,
			["value"] = value,
			["verify"] = true
		};
		if (byteOrder is not null)
		{
			arguments["byteOrder"] = byteOrder;
		}

		if (nullTerminate)
		{
			arguments["nullTerminate"] = true;
		}

		JsonNode result = await CallAsync(client, CheatEngineToolNames.MemoryWrite, arguments);
		Assert.True(result["verified"]!.GetValue<bool>());
	}

	private static async Task<string> ReadAsync(LiveMcpInstanceClient client, string address, string valueType, int? size = null, int? length = null, string? byteOrder = null)
	{
		Dictionary<string, object?> arguments = new()
		{
			["address"] = address,
			["valueType"] = valueType
		};
		if (size is not null)
		{
			arguments["size"] = size;
		}

		if (length is not null)
		{
			arguments["length"] = length;
		}

		if (byteOrder is not null)
		{
			arguments["byteOrder"] = byteOrder;
		}

		return Text(await CallAsync(client, CheatEngineToolNames.MemoryRead, arguments), "value");
	}

	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient client, string tool, IReadOnlyDictionary<string, object?>? arguments = null) => await client.CallToolAsync(tool, arguments) ?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
	private static JsonNode Payload(LiveMcpToolResult result) => result.Payload ?? throw new InvalidDataException("Expected error returned no payload.");
	private static string Text(JsonNode? node, string property) => node?[property]?.GetValue<string>() ?? throw new InvalidDataException($"Missing string '{property}'.");
	private static string[] Strings(JsonNode? node) => node is null ? [] : node.AsArray().Select(item => item!.GetValue<string>()).ToArray();
	private static ulong Parse(string value) => ulong.Parse(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
	private static string At(string address, int offset) => checked(Parse(address) + (ulong) offset).ToString("X", CultureInfo.InvariantCulture);
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

		if (failure is null)
		{
			throw new AggregateException("Live workflow cleanup failed.", cleanup);
		}

		throw new AggregateException("Live workflow failed and cleanup also failed.", [failure, .. cleanup]);
	}
}
