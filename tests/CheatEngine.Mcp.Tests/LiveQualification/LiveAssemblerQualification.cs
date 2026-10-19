using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>
/// Bounded evidence for Auto Assembler, unsafe Lua, and inactive address-list script operations.
/// Every mutation is restricted to one MCP-owned allocation or record and is read back after rollback.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class LiveAssemblerQualification
{
	private const string Allocation = "live-assembler-qualification";
	private const string OriginalBytes = "90 90 90 90 90";
	private const string OriginalAllocationBytes = "90 90 90 90 90 00 00 00 00 00 00 00 00 00 00 00 90 90 90 90 90 90 90 90 90 90 90 90 90 90 90 90";
	private const string AllocationSuffixBytes = "00 00 00 00 00 00 00 00 00 00 00 90 90 90 90 90 90 90 90 90 90 90 90 90 90 90 90";
	private const string AppliedBytes = "CC CC CC CC CC";
	private const string OriginalRecordScript = "[ENABLE]\ndefine(mcp_live_record,0)\n\n[DISABLE]\ndefine(mcp_live_record,0)";
	private const string ReplacementRecordScript = "[ENABLE]\ndefine(mcp_live_record,1)\n\n[DISABLE]\ndefine(mcp_live_record,1)";
	private const string CheckScript = "[ENABLE]\ndefine(mcp_live_check,0)\n\n[DISABLE]\ndefine(mcp_live_check,0)";

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		ArgumentNullException.ThrowIfNull(instance);

		bool allocationAttempted = false;
		bool allocationAbsent = false;
		string? address = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			await VerifyLuaAsync(sandbox, instance);
			await AssertNoNamedAllocationAsync(instance, Allocation);
			allocationAttempted = true;
			JsonNode allocation = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = Allocation, ["size"] = 32 });
			address = Text(allocation, "address");
			Assert.Equal(32L, allocation["size"]!.GetValue<long>());
			await WriteBytesAsync(instance, address, OriginalAllocationBytes);
			Assert.Equal(OriginalAllocationBytes, await ReadBytesAsync(instance, address, 32));

			await VerifyCheckAndGeneratedScriptsAsync(sandbox, instance);
			await VerifyAppliedPatchAsync(sandbox, instance, address, CheatEngineToolNames.AsmApply,
				new Dictionary<string, object?> { ["script"] = ApplyScript(address), ["name"] = "live-asm-apply" }, true);
			await VerifyAppliedPatchAsync(sandbox, instance, address, CheatEngineToolNames.AsmApplyCodePatch,
				new Dictionary<string, object?>
				{
					["address"] = address,
					["expectedBytes"] = OriginalBytes,
					["replacementBytes"] = AppliedBytes,
					["name"] = "live-asm-code-patch"
				}, false);

			await VerifyRecordScriptAsync(sandbox, instance);
		}
		catch (Exception exception)
		{
			failure = exception;
			throw;
		}
		finally
		{
			using CancellationTokenSource cleanupTimeout = new(TimeSpan.FromSeconds(10));
			LiveMcpInstanceClient cleanupInstance = instance.WithCancellation(cleanupTimeout.Token);
			if (allocationAttempted && !allocationAbsent)
			{
				await CleanupAsync(cleanup, async () =>
				{
					if (address is not null)
					{
						Assert.Equal(OriginalAllocationBytes, await ReadBytesAsync(cleanupInstance, address, 32));
					}
					await ReleaseOrAssertAllocationAbsentAsync(cleanupInstance, Allocation);
					allocationAbsent = true;
					sandbox.Record("live_assembler_memory_cleanup", new
					{
						name = Allocation,
						restored = true,
						freed = true
					});
				});
			}

			if (cleanup.Count == 0)
			{
				sandbox.Record("live_assembler_cleanup", new
				{
					allocationAttempted,
					allocationAbsent
				});
			}
			ThrowQualification(failure, cleanup);
		}
	}

	private static async Task VerifyLuaAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		JsonNode result = await CallAsync(instance, CheatEngineToolNames.LuaExecute,
			new Dictionary<string, object?> { ["source"] = "return 19 + 23, 'mcp-live'", ["chunkName"] = "mcp-live-qualification" });
		Assert.True(result["ok"]!.GetValue<bool>());
		Assert.Equal("completed", Text(result, "hostEffect"));
		Assert.Equal(0, result["droppedOpaqueCount"]!.GetValue<int>());
		JsonArray values = result["returnValues"]!.AsArray();
		Assert.Equal(2, values.Count);
		Assert.Equal(42, values[0]!.GetValue<int>());
		Assert.Equal("mcp-live", values[1]!.GetValue<string>());
		Receipt(sandbox, CheatEngineToolNames.LuaExecute, result);
	}

	private static async Task VerifyCheckAndGeneratedScriptsAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		JsonNode checkedScript = await CallAsync(instance, CheatEngineToolNames.AsmCheck,
			new Dictionary<string, object?> { ["script"] = CheckScript, ["name"] = "live-asm-check" });
		Assert.True(checkedScript["accepted"]!.GetValue<bool>());
		Assert.Null(checkedScript["failedSection"]);
		Receipt(sandbox, CheatEngineToolNames.AsmCheck, checkedScript);

		string module = sandbox.TargetArchitecture == "x86" ? "dotnet.exe" : "CheatEngine.Mcp.LiveTarget.exe";
		JsonNode injection = await CallAsync(instance, CheatEngineToolNames.AsmGenerateInjection, new Dictionary<string, object?>
		{
			["module"] = module,
			["signature"] = OriginalBytes,
			["expectedBytes"] = OriginalBytes,
			["symbolName"] = "mcpLiveInjection",
			["offset"] = 0
		});
		Assert.Equal("9090909090", Text(injection, "expectedBytes"));
		Assert.Equal("mcpLiveInjection", Text(injection, "symbolName"));
		AssertGeneratedInjection(Text(injection, "script"), module);
		Receipt(sandbox, CheatEngineToolNames.AsmGenerateInjection, injection);
	}

	private static async Task VerifyAppliedPatchAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string address,
		string tool, IReadOnlyDictionary<string, object?> arguments, bool emitSharedReceipts)
	{
		string? patchId = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			JsonNode applied = await CallAsync(instance, tool, arguments);
			patchId = Text(applied, "patchId");
			Assert.True(applied["retained"]!.GetValue<bool>());
			Assert.False(applied["requiresManualRecovery"]!.GetValue<bool>());
			Assert.False(string.IsNullOrWhiteSpace(patchId));
			Assert.Equal(AppliedBytes + " " + AllocationSuffixBytes, await ReadBytesAsync(instance, address, 32));
			JsonNode listed = await CallAsync(instance, CheatEngineToolNames.AsmListPatches);
			JsonNode patch = Assert.Single(listed["patches"]!.AsArray(), value => Text(value, "patchId") == patchId)!;
			Assert.True(patch["canDisable"]!.GetValue<bool>());
			Assert.False(patch["released"]!.GetValue<bool>());
			Receipt(sandbox, tool, applied);
			if (emitSharedReceipts)
			{
				Receipt(sandbox, CheatEngineToolNames.AsmListPatches, listed);
			}
		}
		catch (Exception exception)
		{
			failure = exception;
		}
		finally
		{
			using CancellationTokenSource cleanupTimeout = new(TimeSpan.FromSeconds(10));
			LiveMcpInstanceClient cleanupInstance = instance.WithCancellation(cleanupTimeout.Token);
			if (patchId is not null)
			{
				await CleanupAsync(cleanup, async () =>
				{
					JsonNode released = await CallAsync(cleanupInstance, CheatEngineToolNames.AsmReleasePatch,
						new Dictionary<string, object?> { ["patchId"] = patchId });
					Assert.Equal((patchId, true, false, false), (Text(released, "patchId"), released["released"]!.GetValue<bool>(),
						released["retryable"]!.GetValue<bool>(), released["requiresManualRecovery"]!.GetValue<bool>()));
					Assert.Equal(OriginalAllocationBytes, await ReadBytesAsync(cleanupInstance, address, 32));
					JsonNode afterRelease = await CallAsync(cleanupInstance, CheatEngineToolNames.AsmListPatches);
					Assert.DoesNotContain(afterRelease["patches"]!.AsArray(), value => Text(value!, "patchId") == patchId);
					if (emitSharedReceipts)
					{
						Receipt(sandbox, CheatEngineToolNames.AsmReleasePatch, released);
					}
					sandbox.Record("live_assembler_patch_cleanup", new
					{
						tool,
						patchId,
						released = true,
						bytesRestored = true
					});
				});
			}
		}
		ThrowQualification(failure, cleanup);
	}

	private static async Task VerifyRecordScriptAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		int? id = null;
		bool mutationAttempted = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			JsonNode created = await CallAsync(instance, CheatEngineToolNames.RecordCreate, new Dictionary<string, object?>
			{
				["records"] = new object?[]
				{
					new Dictionary<string, object?>
					{
						["description"] = "McpLiveAssemblerRecord_" + sandbox.HostA.TargetProcessId.ToString(CultureInfo.InvariantCulture),
						["address"] = "0",
						["variableType"] = 11,
						["script"] = OriginalRecordScript
					}
				}
			});
			JsonNode record = Assert.Single(created["records"]!.AsArray())!;
			id = record["id"]!.GetValue<int>();
			Assert.False(record["active"]!.GetValue<bool>());
			Assert.Equal(OriginalRecordScript, Text(record, "script"));

			mutationAttempted = true;
			JsonNode changed = await CallAsync(instance, CheatEngineToolNames.RecordSetScript,
				new Dictionary<string, object?> { ["id"] = id.Value, ["script"] = ReplacementRecordScript });
			Assert.False(changed["record"]!["active"]!.GetValue<bool>());
			Assert.Equal(ReplacementRecordScript, Text(changed["record"], "script"));
			JsonNode reread = await CallAsync(instance, CheatEngineToolNames.RecordGet,
				new Dictionary<string, object?> { ["ids"] = new[] { id.Value } });
			Assert.Equal(ReplacementRecordScript, Text(Assert.Single(reread["records"]!.AsArray()), "script"));
			Receipt(sandbox, CheatEngineToolNames.RecordSetScript, changed);
		}
		catch (Exception exception)
		{
			failure = exception;
		}
		finally
		{
			if (id is not null && mutationAttempted)
			{
				using CancellationTokenSource restoreTimeout = new(TimeSpan.FromSeconds(10));
				LiveMcpInstanceClient restoreInstance = instance.WithCancellation(restoreTimeout.Token);
				await CleanupAsync(cleanup, async () =>
				{
					JsonNode restored = await CallAsync(restoreInstance, CheatEngineToolNames.RecordSetScript,
						new Dictionary<string, object?> { ["id"] = id.Value, ["script"] = OriginalRecordScript });
					Assert.Equal(OriginalRecordScript, Text(restored["record"], "script"));
					JsonNode reread = await CallAsync(restoreInstance, CheatEngineToolNames.RecordGet,
						new Dictionary<string, object?> { ["ids"] = new[] { id.Value } });
					Assert.Equal(OriginalRecordScript, Text(Assert.Single(reread["records"]!.AsArray()), "script"));
					sandbox.Record("live_assembler_record_script_cleanup", new
					{
						id,
						restored = true
					});
				});
			}
			if (id is not null)
			{
				using CancellationTokenSource deleteTimeout = new(TimeSpan.FromSeconds(10));
				LiveMcpInstanceClient deleteInstance = instance.WithCancellation(deleteTimeout.Token);
				await CleanupAsync(cleanup, async () =>
				{
					JsonNode deleted = await CallAsync(deleteInstance, CheatEngineToolNames.RecordDelete,
						new Dictionary<string, object?> { ["ids"] = new[] { id.Value } });
					Assert.Equal(1, deleted["deleted"]!.GetValue<int>());
					JsonNode remaining = await CallAsync(deleteInstance, CheatEngineToolNames.RecordList,
						new Dictionary<string, object?> { ["offset"] = 0, ["limit"] = 1000 });
					Assert.DoesNotContain(remaining["records"]!.AsArray(), item => item!["id"]!.GetValue<int>() == id.Value);
					sandbox.Record("live_assembler_record_delete_cleanup", new
					{
						id,
						deleted = true
					});
				});
			}
		}
		ThrowQualification(failure, cleanup);
	}

	private static async Task WriteBytesAsync(LiveMcpInstanceClient instance, string address, string value)
	{
		JsonNode written = await CallAsync(instance, CheatEngineToolNames.MemoryWrite,
			new Dictionary<string, object?> { ["address"] = address, ["valueType"] = "bytes", ["value"] = value, ["verify"] = true });
		Assert.True(written["verified"]!.GetValue<bool>());
	}

	private static async Task<string> ReadBytesAsync(LiveMcpInstanceClient instance, string address, int size)
	{
		JsonNode read = await CallAsync(instance, CheatEngineToolNames.MemoryRead,
			new Dictionary<string, object?> { ["address"] = address, ["valueType"] = "bytes", ["size"] = size });
		return Text(read, "value");
	}

	private static async Task AssertNoNamedAllocationAsync(LiveMcpInstanceClient instance, string name)
	{
		JsonNode resources = await CallAsync(instance, CheatEngineToolNames.RuntimeListResources);
		Assert.DoesNotContain(resources["resources"]!.AsArray(), item =>
			string.Equals(Text(item!, "kind"), "allocation", StringComparison.Ordinal) &&
			string.Equals(Text(item, "name"), name, StringComparison.Ordinal));
	}

	private static async Task ReleaseOrAssertAllocationAbsentAsync(LiveMcpInstanceClient instance, string name)
	{
		LiveMcpToolResult result = await instance.CallToolRawAsync(CheatEngineToolNames.MemoryFree,
			new Dictionary<string, object?> { ["name"] = name });
		if (result.IsError)
		{
			JsonNode payload = result.Payload ?? throw new InvalidDataException("Expected an error payload.");
			Assert.Equal("not_found", Text(payload["error"], "kind"));
			return;
		}

		JsonNode released = result.Payload ?? throw new InvalidDataException("Expected a release payload.");
		Assert.True(released["release"]!["isComplete"]!.GetValue<bool>());
		LiveMcpToolResult absent = await instance.CallToolRawAsync(CheatEngineToolNames.MemoryFree,
			new Dictionary<string, object?> { ["name"] = name });
		Assert.True(absent.IsError);
		JsonNode absence = absent.Payload ?? throw new InvalidDataException("Expected an absence error payload.");
		Assert.Equal("not_found", Text(absence["error"], "kind"));
	}

	internal static string ApplyScript(string address) =>
		$"[ENABLE]\nassert({address},{OriginalBytes})\n{address}:\ndb {AppliedBytes}\n\n[DISABLE]\n{address}:\ndb {OriginalBytes}";

	internal static void AssertGeneratedInjection(string script, string module)
	{
		Assert.Equal($"[ENABLE]\naobscanmodule(mcpLiveInjection,{module},{OriginalBytes})\nassert(mcpLiveInjection,{OriginalBytes})\nalloc(newmem,2048,mcpLiveInjection)\nlabel(code)\nlabel(return)\nregistersymbol(mcpLiveInjection)\n\nnewmem:\n// Add reviewed custom code above the original bytes.\ncode:\ndb {OriginalBytes}\njmp return\n\nmcpLiveInjection:\njmp newmem\nreturn:\n\n[DISABLE]\nmcpLiveInjection:\ndb {OriginalBytes}\nunregistersymbol(mcpLiveInjection)\ndealloc(newmem)", script);
	}

	private static string Text(JsonNode? node, string property) => node?[property]?.GetValue<string>() ?? throw new InvalidDataException($"Missing string '{property}'.");
	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool, IReadOnlyDictionary<string, object?>? arguments = null) =>
		await instance.CallToolAsync(tool, arguments) ?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
	private static void Receipt(LiveSandboxSession sandbox, string tool, JsonNode value) => sandbox.Record("all_tools_" + tool, value.DeepClone());
	private static async Task CleanupAsync(List<Exception> failures, Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception exception) { failures.Add(exception); }
	}
	private static void ThrowQualification(Exception? failure, List<Exception> cleanup)
	{
		if (failure is null)
		{
			if (cleanup.Count == 0)
			{
				return;
			}
			throw new AggregateException(cleanup);
		}

		if (cleanup.Count == 0)
		{
			ExceptionDispatchInfo.Capture(failure).Throw();
		}
		throw new AggregateException([failure, .. cleanup]);
	}
}
