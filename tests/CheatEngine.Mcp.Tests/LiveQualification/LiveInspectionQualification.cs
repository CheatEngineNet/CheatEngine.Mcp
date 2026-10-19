using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Exact byte and pointer fixtures for inspection tools, stored only in an owned allocation.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveInspectionQualification
{
	private const string Allocation = "live-inspection-fixture";
	private static readonly string[] Offsets = ["-10", "20"];
	private static readonly string[] Encodings = ["utf8", "utf16_le"];

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		bool allocated = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			JsonNode allocation = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = Allocation, ["size"] = 512 });
			allocated = true;
			string address = allocation["address"]!.GetValue<string>();
			int width = sandbox.TargetArchitecture == "x64" ? 8 : 4;
			byte[] bytes = new byte[512];
			PutPointer(bytes, 0, At(address, 16), width);
			PutPointer(bytes, 16, At(address, 384), width);
			PutPointer(bytes, 16 + (width * 2), At(address, 400), width);
			Encoding.UTF8.GetBytes("Alpha Fox\0beta 🦊\0").CopyTo(bytes, 64);
			Encoding.Unicode.GetBytes("λambda\0日本語\0").CopyTo(bytes, 128);
			PutPointer(bytes, 256, At(address, 288), width);
			PutPointer(bytes, 272, At(address, 304), width);
			BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(336), 20261019);
			bytes[384] = 0x90;
			bytes[385] = 0xC3;
			JsonNode written = await CallAsync(instance, CheatEngineToolNames.MemoryWrite,
				new Dictionary<string, object?>
				{
					["address"] = address,
					["valueType"] = "bytes",
					["value"] = Convert.ToHexString(bytes),
					["verify"] = true
				});
			Assert.True(written["verified"]!.GetValue<bool>());
			sandbox.Record("all_tools_memory_write", written.DeepClone());
			await VerifyStringsAsync(sandbox, instance, address);
			await VerifyVtableAsync(sandbox, instance, address, width);
			await VerifyChainsAsync(sandbox, instance, address);
			await VerifyCodeAndRegionAsync(sandbox, instance, address);
			await LivePointerPersistenceQualification.VerifyAsync(sandbox, instance, address, width);
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
				try
				{
					JsonNode freed = await CallAsync(instance, CheatEngineToolNames.MemoryFree,
						new Dictionary<string, object?> { ["name"] = Allocation });
					Assert.Equal(Allocation, freed["name"]!.GetValue<string>());
					Assert.Equal(512L, freed["size"]!.GetValue<long>());
					Assert.True(freed["release"]!["isComplete"]!.GetValue<bool>());
					LiveMcpToolResult absent = await instance.CallToolRawAsync(CheatEngineToolNames.MemoryFree,
						new Dictionary<string, object?> { ["name"] = Allocation });
					Assert.True(absent.IsError);
					Assert.Equal("not_found", absent.Payload!["error"]!["kind"]!.GetValue<string>());
					sandbox.Record("all_tools_memory_free", freed.DeepClone());
				}
				catch (Exception exception) { cleanup.Add(exception); }
			}
			sandbox.Record("live_inspection_cleanup", new
			{
				failures = cleanup.Select(error => error.ToString()).ToArray()
			});
			LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
		}
	}

	private static async Task VerifyStringsAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string address)
	{
		foreach ((int start, string encoding, string first, string second, int secondOffset, int secondBytes) in
			new[] { (64, "utf8", "Alpha Fox", "beta 🦊", 10, 9), (128, "utf16_le", "λambda", "日本語", 14, 6) })
		{
			Dictionary<string, object?> arguments = new()
			{
				["address"] = At(address, start),
				["size"] = 32,
				["encoding"] = encoding,
				["minLength"] = 2,
				["limit"] = 1
			};
			JsonNode page = await CallAsync(instance, CheatEngineToolNames.MemoryFindStrings, arguments);
			Assert.Equal(2, page["total"]!.GetValue<int>());
			Assert.Equal(1, page["nextOffset"]!.GetValue<int>());
			Assert.True(page["truncated"]!.GetValue<bool>());
			JsonNode item = Assert.Single(page["strings"]!.AsArray())!;
			Assert.Equal((At(address, start), first),
				(item["address"]!.GetValue<string>(), item["text"]!.GetValue<string>()));
			arguments["offset"] = 1;
			JsonNode next = await CallAsync(instance, CheatEngineToolNames.MemoryFindStrings, arguments);
			JsonNode last = Assert.Single(next["strings"]!.AsArray())!;
			Assert.Equal((At(address, start + secondOffset), second, secondBytes),
				(last["address"]!.GetValue<string>(), last["text"]!.GetValue<string>(), last["byteLength"]!.GetValue<int>()));
			Assert.Null(next["nextOffset"]);
			Assert.False(next["truncated"]!.GetValue<bool>());
			Assert.False(last["previewTruncated"]!.GetValue<bool>());
			arguments["offset"] = 0;
			arguments["contains"] = second;
			JsonNode filtered = await CallAsync(instance, CheatEngineToolNames.MemoryFindStrings, arguments);
			Assert.Equal(1, filtered["total"]!.GetValue<int>());
			Assert.Equal(second, Assert.Single(filtered["strings"]!.AsArray())!["text"]!.GetValue<string>());
		}
		sandbox.Record("all_tools_memory_find_strings", new
		{
			encodings = Encodings,
			pagesVerified = 4,
			filtersVerified = 2
		});
	}

	private static async Task VerifyVtableAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string address, int width)
	{
		JsonNode result = await CallAsync(instance, CheatEngineToolNames.MemoryReadVtable,
			new Dictionary<string, object?> { ["address"] = address, ["count"] = 3 });
		Assert.Equal((address, At(address, 16), width), (result["address"]!.GetValue<string>(),
			result["vtableAddress"]!.GetValue<string>(), result["pointerBytes"]!.GetValue<int>()));
		JsonArray slots = result["slots"]!.AsArray();
		Assert.Equal(3, slots.Count);
		string[] expected = [At(address, 384), "0", At(address, 400)];
		for (int index = 0; index < slots.Count; index++)
		{
			Assert.Equal((index, At(address, 16 + (index * width)), expected[index]),
				(slots[index]!["index"]!.GetValue<int>(), slots[index]!["slotAddress"]!.GetValue<string>(),
					slots[index]!["functionAddress"]!.GetValue<string>()));
			Assert.Null(slots[index]!["error"]);
		}
		JsonNode direct = await CallAsync(instance, CheatEngineToolNames.MemoryReadVtable,
			new Dictionary<string, object?> { ["address"] = At(address, 16), ["addressIsVtable"] = true, ["startIndex"] = 1, ["count"] = 2 });
		Assert.Equal(1, direct["startIndex"]!.GetValue<int>());
		Assert.Equal(2, direct["slots"]!.AsArray().Count);
		Assert.True(JsonNode.DeepEquals(slots[1], direct["slots"]![0]));
		Assert.True(JsonNode.DeepEquals(slots[2], direct["slots"]![1]));
		sandbox.Record("all_tools_memory_read_vtable", result.DeepClone());
	}

	private static async Task VerifyChainsAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string address)
	{
		JsonNode single = await CallAsync(instance, CheatEngineToolNames.PointerReadChain,
			new Dictionary<string, object?> { ["base"] = At(address, 256), ["offsets"] = Offsets, ["valueType"] = "int32" });
		Assert.Equal((At(address, 336), "20261019"),
			(single["address"]!.GetValue<string>(), single["value"]!.GetValue<string>()));
		AssertHops(single["hops"]!.AsArray(), address);
		sandbox.Record("all_tools_pointer_read_chain", single.DeepClone());
		Dictionary<string, object?> arguments = new()
		{
			["candidates"] = new object[] { new { id = "owned", @base = At(address, 256), offsets = Offsets } },
			["target"] = At(address, 336),
			["valueType"] = "int32",
			["includeHops"] = true
		};
		JsonNode batch = await CallAsync(instance, CheatEngineToolNames.PointerReadChains, arguments);
		JsonNode item = Assert.Single(batch["candidates"]!.AsArray())!;
		Assert.Equal(("resolved", "match", "read", "20261019"),
			(item["chainStatus"]!.GetValue<string>(), item["comparisonStatus"]!.GetValue<string>(),
				item["valueStatus"]!.GetValue<string>(), item["value"]!.GetValue<string>()));
		AssertHops(item["hops"]!.AsArray(), address);
		Assert.Equal(1, batch["summary"]!["processed"]!.GetValue<int>());
		Assert.Equal(1, batch["summary"]!["matches"]!.GetValue<int>());
		arguments.Remove("includeHops");
		JsonNode compact = await CallAsync(instance, CheatEngineToolNames.PointerReadChains, arguments);
		Assert.Null(Assert.Single(compact["candidates"]!.AsArray())!["hops"]);
		Assert.True(JsonNode.DeepEquals(batch["summary"], compact["summary"]));
		sandbox.Record("all_tools_pointer_read_chains", batch.DeepClone());
	}

	private static async Task VerifyCodeAndRegionAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance,
		string address)
	{
		string code = At(address, 384);
		JsonNode decoded = await CallAsync(instance, CheatEngineToolNames.CodeDecode,
			new Dictionary<string, object?> { ["address"] = code });
		Assert.Equal(1, decoded["length"]!.GetValue<int>());
		Assert.Equal(code, decoded["instruction"]!["address"]!.GetValue<string>());
		Assert.Equal("90", decoded["instruction"]!["bytes"]!.GetValue<string>());
		Assert.Equal("nop", decoded["instruction"]!["opcode"]!.GetValue<string>().Trim(), ignoreCase: true);
		sandbox.Record("all_tools_code_decode", decoded.DeepClone());
		JsonNode disassembled = await CallAsync(instance, CheatEngineToolNames.CodeDisassemble,
			new Dictionary<string, object?> { ["address"] = code, ["count"] = 2 });
		JsonArray instructions = disassembled["instructions"]!.AsArray();
		Assert.Equal(2, instructions.Count);
		for (int index = 0; index < 2; index++)
		{
			Assert.Equal(At(address, 384 + index), instructions[index]!["address"]!.GetValue<string>());
			Assert.Equal(index == 0 ? "90" : "C3", instructions[index]!["bytes"]!.GetValue<string>());
			Assert.Equal(index == 0 ? "nop" : "ret", instructions[index]!["opcode"]!.GetValue<string>().Trim(), ignoreCase: true);
			Assert.Equal(1, instructions[index]!["size"]!.GetValue<int>());
		}
		sandbox.Record("all_tools_code_disassemble", disassembled.DeepClone());
		JsonNode regions = await CallAsync(instance, CheatEngineToolNames.MemoryListRegions,
			new Dictionary<string, object?> { ["startAddress"] = code, ["endAddress"] = At(address, 385), ["limit"] = 2 });
		JsonNode region = Assert.Single(regions["regions"]!.AsArray())!;
		ulong regionBase = ulong.Parse(region["base"]!.GetValue<string>(), NumberStyles.AllowHexSpecifier,
			CultureInfo.InvariantCulture);
		ulong codeAddress = ulong.Parse(code, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
		long size = region["size"]!.GetValue<long>();
		Assert.True(size >= 2 && regionBase <= codeAddress && codeAddress - regionBase <= (ulong) size - 2);
		Assert.Equal("committed", region["state"]!.GetValue<string>());
		Assert.Equal(1, regions["total"]!.GetValue<int>());
		Assert.False(regions["truncated"]!.GetValue<bool>());
		sandbox.Record("all_tools_memory_list_regions", regions.DeepClone());
	}

	private static void AssertHops(JsonArray hops, string address)
	{
		Assert.Equal(2, hops.Count);
		foreach ((int index, int read, int pointer, string offset, int next) in new[] { (0, 256, 288, "-10", 272), (1, 272, 304, "20", 336) })
		{
			Assert.Equal((At(address, read), At(address, pointer), offset, At(address, next)),
				(hops[index]!["readAt"]!.GetValue<string>(), hops[index]!["pointer"]!.GetValue<string>(),
					hops[index]!["offset"]!.GetValue<string>(), hops[index]!["next"]!.GetValue<string>()));
		}
	}

	private static void PutPointer(byte[] bytes, int offset, string address, int width)
	{
		ulong value = ulong.Parse(address, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
		if (width == 8)
		{
			BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
		}
		else
		{
			BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), checked((uint) value));
		}
	}

	private static string At(string address, int offset) => checked(ulong.Parse(address, NumberStyles.AllowHexSpecifier,
		CultureInfo.InvariantCulture) + (uint) offset).ToString("X", CultureInfo.InvariantCulture);
	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool,
		IReadOnlyDictionary<string, object?>? arguments = null) => await instance.CallToolAsync(tool, arguments)
		?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
}
