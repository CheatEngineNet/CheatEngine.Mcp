using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Qualifies bounded code-dissector state with an owned, never-executed instruction buffer.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveDissectionQualification
{
	private const string Allocation = "live-dissection-fixture";
	private const int AllocationSize = 256;
	private const int FunctionOffset = 16;
	private const int StringOffset = 64;
	private const int DissectSize = 22;
	private const string ReferencedText = "LiveDissectionFixture";

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		bool allocated = false;
		bool jobMayBeRunning = false;
		bool dissectionAttempted = false;
		bool dissectionCleared = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		string? address = null;
		string? functionAddress = null;
		string? jobId = null;
		try
		{
			await RequireEmptyDissectorAsync(instance);
			JsonNode process = await CallAsync(instance, CheatEngineToolNames.ProcessGetCurrent);
			Assert.True(process["isOpen"]!.GetValue<bool>());
			int pointerSize = process["pointerSize"]!.GetValue<int>();
			Assert.True(pointerSize is 4 or 8);

			JsonNode allocation = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = Allocation, ["size"] = AllocationSize });
			allocated = true;
			address = Text(allocation, "address");
			Fixture fixture = Fixture.Create(address, pointerSize);
			functionAddress = fixture.Function;
			JsonNode written = await CallAsync(instance, CheatEngineToolNames.MemoryWrite, new Dictionary<string, object?>
			{
				["address"] = address,
				["valueType"] = "bytes",
				["value"] = Hex(fixture.Bytes),
				["verify"] = true
			});
			Assert.True(written["verified"]!.GetValue<bool>());

			dissectionAttempted = true;
			jobMayBeRunning = true;
			JsonNode start = await CallAsync(instance, CheatEngineToolNames.CodeStartDissect, new Dictionary<string, object?>
			{
				["address"] = address,
				["size"] = DissectSize,
				["lifetimeSeconds"] = 30
			});
			jobId = Text(start, "jobId");
			Assert.True(Text(start["job"], "state") is "running" or "completed");
			JsonNode completed = await WaitForDissectionAsync(instance, jobId);
			JsonNode eventItem = Assert.Single(completed["items"]!.AsArray())!;
			Assert.Equal(("dissect", address, DissectSize),
				(Text(eventItem, "kind"), Text(eventItem, "address"), eventItem["size"]!.GetValue<int>()));
			Assert.Equal(0L, completed["dropped"]!.GetValue<long>());
			Assert.False(completed["more"]!.GetValue<bool>());
			sandbox.Record("all_tools_code_start_dissect", new
			{
				start = start.DeepClone(),
				completion = completed.DeepClone()
			});

			JsonNode references = await CallAsync(instance, CheatEngineToolNames.CodeFindReferences,
				new Dictionary<string, object?> { ["address"] = fixture.Function, ["offset"] = 0, ["limit"] = 4 });
			Assert.Equal((fixture.Function, 1, true, 0), (Text(references, "address"), references["total"]!.GetValue<int>(),
				references["exact"]!.GetValue<bool>(), references["offset"]!.GetValue<int>()));
			JsonNode reference = Assert.Single(references["references"]!.AsArray())!;
			Assert.Equal((fixture.Call, fixture.Function), (Text(reference, "fromAddress"), Text(reference, "toAddress")));
			Assert.False(string.IsNullOrWhiteSpace(Text(reference, "kind")));
			Assert.Null(references["nextOffset"]);
			sandbox.Record("all_tools_code_find_references", references.DeepClone());

			JsonNode strings = await CallAsync(instance, CheatEngineToolNames.CodeFindStrings, new Dictionary<string, object?>
			{
				["textContains"] = ReferencedText,
				["offset"] = 0,
				["limit"] = 4
			});
			Assert.Equal((1, true, 0), (strings["total"]!.GetValue<int>(), strings["exact"]!.GetValue<bool>(),
				strings["offset"]!.GetValue<int>()));
			JsonNode stringReference = Assert.Single(strings["strings"]!.AsArray())!;
			Assert.Equal((fixture.Text, ReferencedText), (Text(stringReference, "address"), Text(stringReference, "text")));
			Assert.Null(strings["nextOffset"]);
			sandbox.Record("all_tools_code_find_strings", strings.DeepClone());

			JsonNode functions = await CallAsync(instance, CheatEngineToolNames.CodeListFunctions,
				new Dictionary<string, object?> { ["offset"] = 0, ["limit"] = 4 });
			Assert.Equal((1, 0), (functions["total"]!.GetValue<int>(), functions["offset"]!.GetValue<int>()));
			Assert.Equal([fixture.Function], functions["functions"]!.AsArray().Select(item => item!.GetValue<string>()));
			Assert.Null(functions["nextOffset"]);
			sandbox.Record("all_tools_code_list_functions", functions.DeepClone());

			JsonNode function = await CallAsync(instance, CheatEngineToolNames.CodeGetFunction, new Dictionary<string, object?>
			{
				["address"] = fixture.Function,
				["jumpRange"] = 32
			});
			Assert.Equal(fixture.Function, Text(function, "address"));
			Assert.True(function["found"]!.GetValue<bool>());
			ulong fixtureStart = Convert.ToUInt64(address, 16);
			ulong estimatedStart = Convert.ToUInt64(Text(function, "startAddress"), 16);
			ulong estimatedEnd = Convert.ToUInt64(Text(function, "endAddress"), 16);
			// The heuristic may include leading NOPs after the caller's RET. Every allowed start is an instruction boundary.
			Assert.InRange(estimatedStart, fixtureStart + (pointerSize == 8 ? 13UL : 11UL), fixtureStart + FunctionOffset);
			// Preserve CE's stop boundary: it may identify the terminal RET or the byte immediately after it.
			Assert.Contains(estimatedEnd, new[] { fixtureStart + FunctionOffset + 5, fixtureStart + DissectSize });
			Assert.Equal(checked((long) (estimatedEnd - estimatedStart)), function["size"]!.GetValue<long>());
			// This fixture contains a CALL to the function, but no jump to it.
			Assert.False(function["addressIsJumpDestination"]!.GetValue<bool>());
			sandbox.Record("all_tools_code_get_function", function.DeepClone());

			JsonNode clear = await ClearDissectAsync(instance);
			Assert.True(clear["cleared"]!.GetValue<bool>());
			await AssertClearedAsync(instance, fixture.Function);
			dissectionCleared = true;
			sandbox.Record("all_tools_code_clear_dissect", clear.DeepClone());
		}
		catch (Exception exception)
		{
			failure = exception;
			throw;
		}
		finally
		{
			if (jobId is not null)
			{
				await CleanupAsync(cleanup, async () =>
				{
					await StopDissectionAsync(instance, jobId);
					jobMayBeRunning = false;
				});
			}
			else if (jobMayBeRunning)
			{
				cleanup.Add(new InvalidOperationException(
					"Dissection start did not return an identity; the owned allocation is retained for host teardown."));
			}
			if (dissectionAttempted && !dissectionCleared && !jobMayBeRunning && functionAddress is not null)
			{
				await CleanupAsync(cleanup, async () =>
				{
					JsonNode clear = await ClearDissectAsync(instance);
					Assert.True(clear["cleared"]!.GetValue<bool>());
					await AssertClearedAsync(instance, functionAddress);
					dissectionCleared = true;
				});
			}
			if (allocated && !jobMayBeRunning && (!dissectionAttempted || dissectionCleared))
			{
				await CleanupAsync(cleanup, async () =>
				{
					JsonNode freed = await CallAsync(instance, CheatEngineToolNames.MemoryFree,
						new Dictionary<string, object?> { ["name"] = Allocation });
					Assert.True(freed["release"]!["isComplete"]!.GetValue<bool>());
					LiveMcpToolResult absent = await instance.CallToolRawAsync(CheatEngineToolNames.MemoryFree,
						new Dictionary<string, object?> { ["name"] = Allocation });
					Assert.True(absent.IsError);
					Assert.Equal("not_found", Text(absent.Payload?["error"], "kind"));
				});
			}
			sandbox.Record("live_dissection_cleanup", new
			{
				allocationRetainedForHostTeardown = allocated && (jobMayBeRunning || (dissectionAttempted && !dissectionCleared)),
				failures = cleanup.Select(error => error.ToString()).ToArray()
			});
			LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
		}
	}

	private static async Task RequireEmptyDissectorAsync(LiveMcpInstanceClient instance)
	{
		JsonNode functions = await CallAsync(instance, CheatEngineToolNames.CodeListFunctions,
			new Dictionary<string, object?> { ["offset"] = 0, ["limit"] = 1 });
		Assert.Equal(0, functions["total"]!.GetValue<int>());
		Assert.Empty(functions["functions"]!.AsArray());
		JsonNode strings = await CallAsync(instance, CheatEngineToolNames.CodeFindStrings,
			new Dictionary<string, object?> { ["offset"] = 0, ["limit"] = 1 });
		Assert.Equal(0, strings["total"]!.GetValue<int>());
		Assert.Empty(strings["strings"]!.AsArray());
	}

	private static async Task<JsonNode> WaitForDissectionAsync(LiveMcpInstanceClient instance, string jobId)
	{
		for (int attempt = 0; attempt < 40; attempt++)
		{
			JsonNode page = await CallAsync(instance, CheatEngineToolNames.CodePollJob,
				new Dictionary<string, object?> { ["jobId"] = jobId, ["afterSequence"] = 0L, ["limit"] = 4 });
			string state = Text(page["job"], "state");
			if (state == "completed")
			{
				return page;
			}

			Assert.Equal("running", state);
			await Task.Delay(100, TestContext.Current.CancellationToken);
		}

		throw new TimeoutException("Code dissection did not complete within four seconds.");
	}

	private static async Task StopDissectionAsync(LiveMcpInstanceClient instance, string jobId)
	{
		for (int attempt = 0; attempt < 5; attempt++)
		{
			LiveMcpToolResult result = await instance.CallToolRawAsync(CheatEngineToolNames.RuntimeStopJob,
				new Dictionary<string, object?> { ["jobId"] = jobId });
			if (result.IsError)
			{
				JsonNode? error = result.Payload?["error"];
				Assert.Equal("partial_effect", Text(error, "kind"));
				Assert.True(error!["retryable"]!.GetValue<bool>());
				await Task.Delay(100, TestContext.Current.CancellationToken);
				continue;
			}

			Assert.Equal(jobId, Text(result.Payload, "jobId"));
			Assert.True(result.Payload!["released"]!.GetValue<bool>());
			LiveMcpToolResult absent = await instance.CallToolRawAsync(CheatEngineToolNames.CodePollJob,
				new Dictionary<string, object?> { ["jobId"] = jobId, ["afterSequence"] = 0L, ["limit"] = 1 });
			Assert.True(absent.IsError);
			Assert.Equal("not_found", Text(absent.Payload?["error"], "kind"));
			return;
		}

		throw new TimeoutException("Dissection stop remains pending after five bounded attempts; the owned allocation is retained for host teardown.");
	}

	private static async Task<JsonNode> ClearDissectAsync(LiveMcpInstanceClient instance)
	{
		for (int attempt = 0; attempt < 5; attempt++)
		{
			LiveMcpToolResult result = await instance.CallToolRawAsync(CheatEngineToolNames.CodeClearDissect);
			if (!result.IsError)
			{
				return result.Payload ?? throw new InvalidDataException("code_clear_dissect returned no payload.");
			}

			JsonNode? error = result.Payload?["error"];
			Assert.Equal("busy", Text(error, "kind"));
			await Task.Delay(100, TestContext.Current.CancellationToken);
		}

		throw new TimeoutException("The completed dissection remained busy after five bounded clear attempts.");
	}

	private static async Task AssertClearedAsync(LiveMcpInstanceClient instance, string function)
	{
		JsonNode references = await CallAsync(instance, CheatEngineToolNames.CodeFindReferences,
			new Dictionary<string, object?> { ["address"] = function, ["offset"] = 0, ["limit"] = 4 });
		Assert.Equal((0, true), (references["total"]!.GetValue<int>(), references["exact"]!.GetValue<bool>()));
		Assert.Empty(references["references"]!.AsArray());

		JsonNode strings = await CallAsync(instance, CheatEngineToolNames.CodeFindStrings,
			new Dictionary<string, object?> { ["textContains"] = ReferencedText, ["offset"] = 0, ["limit"] = 4 });
		Assert.Equal((0, true), (strings["total"]!.GetValue<int>(), strings["exact"]!.GetValue<bool>()));
		Assert.Empty(strings["strings"]!.AsArray());

		JsonNode functions = await CallAsync(instance, CheatEngineToolNames.CodeListFunctions,
			new Dictionary<string, object?> { ["offset"] = 0, ["limit"] = 4 });
		Assert.Equal(0, functions["total"]!.GetValue<int>());
		Assert.Empty(functions["functions"]!.AsArray());
	}

	private static async Task CleanupAsync(List<Exception> failures, Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception exception)
		{
			failures.Add(exception);
		}
	}

	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool,
		IReadOnlyDictionary<string, object?>? arguments = null) => await instance.CallToolAsync(tool, arguments)
		?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");

	private static string Text(JsonNode? node, string name) => node?[name]?.GetValue<string>()
		?? throw new InvalidDataException($"Expected {name}.");

	private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

	private sealed record Fixture(string Function, string Call, string Text, byte[] Bytes)
	{
		internal static Fixture Create(string address, int pointerSize)
		{
			ulong start = Convert.ToUInt64(address, 16);
			ulong function = checked(start + FunctionOffset);
			ulong text = checked(start + StringOffset);
			byte[] bytes = new byte[AllocationSize];
			Array.Fill(bytes, (byte) 0x90, 0, DissectSize);
			if (pointerSize == 8)
			{
				bytes[0] = 0x48;
				bytes[1] = 0x8D;
				bytes[2] = 0x0D;
				WriteInt32(bytes, 3, checked((int) (text - (start + 7))));
				bytes[7] = 0xE8;
				WriteInt32(bytes, 8, checked((int) (function - (start + 12))));
				bytes[12] = 0xC3;
			}
			else
			{
				bytes[0] = 0x68;
				WriteUInt32(bytes, 1, checked((uint) text));
				bytes[5] = 0xE8;
				WriteInt32(bytes, 6, checked((int) (function - (start + 10))));
				bytes[10] = 0xC3;
			}

			bytes[FunctionOffset] = 0xB8;
			WriteInt32(bytes, FunctionOffset + 1, 42);
			bytes[FunctionOffset + 5] = 0xC3;
			Encoding.ASCII.GetBytes(ReferencedText).CopyTo(bytes, StringOffset);
			bytes[StringOffset + ReferencedText.Length] = 0;
			return new Fixture(Address(function), Address(start + (pointerSize == 8 ? 7UL : 5UL)), Address(text), bytes);
		}

		private static void WriteInt32(byte[] bytes, int offset, int value)
		{
			BitConverter.GetBytes(value).CopyTo(bytes, offset);
		}

		private static void WriteUInt32(byte[] bytes, int offset, uint value)
		{
			BitConverter.GetBytes(value).CopyTo(bytes, offset);
		}

		private static string Address(ulong value) => value.ToString("X", System.Globalization.CultureInfo.InvariantCulture);
	}
}
