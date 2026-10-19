using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Bounded, reversible success evidence for additional ungated tools.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveAdditionalToolQualification
{
	private const string ScanAllocation = "live-additional-tools-scan";
	private const string ScanName = "live-additional-tools-scanner";
	private const string StructureAllocation = "live-additional-tools-structure";
	private const string RuntimeReleaseAllocation = "live-additional-tools-runtime-release";
	private const string StructureBytes = "04 03 02 01 08 07 06 05 0C 0B 0A 09 10 0F 0E 0D";
	private static readonly Dictionary<string, int> FixedPrimitiveWidths = new(StringComparer.Ordinal)
	{
		["int8"] = 1,
		["uint8"] = 1,
		["int16"] = 2,
		["uint16"] = 2,
		["int32"] = 4,
		["uint32"] = 4,
		["float"] = 4,
		["int64"] = 8,
		["uint64"] = 8,
		["double"] = 8
	};

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		ArgumentNullException.ThrowIfNull(instance);
		await VerifyOwnedResourceReleaseAsync(sandbox, instance);
		await VerifyNamedScannerStopAsync(sandbox, instance);
		await VerifyStructureAutoguessAsync(sandbox, instance);
	}

	private static async Task VerifyOwnedResourceReleaseAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		bool allocationAttempted = false;
		bool cleanupVerified = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			JsonNode before = await CallAsync(instance, CheatEngineToolNames.RuntimeListResources);
			Assert.Empty(before["resources"]!.AsArray());
			await AssertNoResourceNamedAsync(instance, RuntimeReleaseAllocation);
			allocationAttempted = true;
			JsonNode allocation = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = RuntimeReleaseAllocation, ["size"] = 16, ["executable"] = false });
			Assert.Equal((RuntimeReleaseAllocation, 16L, false),
				(Text(allocation, "name"), allocation["size"]!.GetValue<long>(), allocation["executable"]!.GetValue<bool>()));
			string resourceId = Text(allocation, "resourceId");
			string address = Text(allocation, "address");
			JsonNode listed = await CallAsync(instance, CheatEngineToolNames.RuntimeListResources);
			JsonNode resource = Assert.Single(listed["resources"]!.AsArray())!;
			Assert.Equal((resourceId, "allocation", RuntimeReleaseAllocation, address, 16L, sandbox.HostA.TargetProcessId, false),
				(Text(resource, "id"), Text(resource, "kind"), Text(resource, "name"), Text(resource, "address"),
					resource["size"]!.GetValue<long>(), resource["processId"]!.GetValue<int>(), resource["orphaned"]!.GetValue<bool>()));

			JsonNode released = await CallAsync(instance, CheatEngineToolNames.RuntimeReleaseResources,
				new Dictionary<string, object?> { ["includeOrphans"] = false });
			Assert.Empty(released["acknowledged"]!.AsArray());
			JsonNode releasedResource = Assert.Single(released["release"]!["released"]!.AsArray())!;
			Assert.Equal(resourceId, Text(releasedResource["resource"], "id"));
			Assert.True(releasedResource["release"]!["isComplete"]!.GetValue<bool>());
			Assert.Null(released["release"]!["failed"]);
			Assert.Empty(released["release"]!["remaining"]!.AsArray());
			await AssertAllocationAbsentAsync(instance, RuntimeReleaseAllocation);
			JsonNode after = await CallAsync(instance, CheatEngineToolNames.RuntimeListResources);
			Assert.Empty(after["resources"]!.AsArray());
			Receipt(sandbox, CheatEngineToolNames.RuntimeReleaseResources, released);
			cleanupVerified = true;
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			if (allocationAttempted && !cleanupVerified)
			{
				await CleanupAsync(cleanup, instance, async cleanupInstance =>
				{
					await ReleaseOrAssertAllocationAbsentAsync(cleanupInstance, RuntimeReleaseAllocation);
					JsonNode resources = await CallAsync(cleanupInstance, CheatEngineToolNames.RuntimeListResources);
					Assert.Empty(resources["resources"]!.AsArray());
					cleanupVerified = true;
				});
			}
			if (cleanupVerified)
			{
				sandbox.Record("all_tools_runtime_release_resources_cleanup", new
				{
					allocationAttempted,
					cleanupVerified
				});
			}
			ThrowCleanup(failure, cleanup);
		}
	}

	private static async Task VerifyNamedScannerStopAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		bool allocationAttempted = false;
		bool scannerAttempted = false;
		bool scannerAbsent = false;
		bool allocationAbsent = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			await AssertNoResourceNamedAsync(instance, ScanAllocation);
			JsonNode initialScanners = await CallAsync(instance, CheatEngineToolNames.ScanListScanners);
			Assert.DoesNotContain(initialScanners["scanners"]!.AsArray(), item =>
				string.Equals(Text(item!, "scannerName"), ScanName, StringComparison.Ordinal));
			allocationAttempted = true;
			JsonNode allocation = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = ScanAllocation, ["size"] = 64 });
			string address = Text(allocation, "address");
			await WriteAsync(instance, address, "10");
			scannerAttempted = true;
			JsonNode first = await CallAsync(instance, CheatEngineToolNames.ScanFirst, ScanArguments(address));
			Assert.Equal((ScanName, "independent", "ResultsReady", true, 1L, "int32"),
				(Text(first, "scannerName"), Text(first, "mode"), Text(first, "state"),
					first["resultsReady"]!.GetValue<bool>(), first["count"]!.GetValue<long>(), Text(first, "valueType")));

			JsonNode stopped = await CallAsync(instance, CheatEngineToolNames.ScanStop,
				new Dictionary<string, object?> { ["scannerName"] = ScanName });
			Assert.Equal((ScanName, true, true),
				(Text(stopped, "scannerName"), stopped["stopRequested"]!.GetValue<bool>(), stopped["removed"]!.GetValue<bool>()));
			JsonNode scanners = await CallAsync(instance, CheatEngineToolNames.ScanListScanners);
			Assert.DoesNotContain(scanners["scanners"]!.AsArray(), item =>
				string.Equals(Text(item!, "scannerName"), ScanName, StringComparison.Ordinal));
			Receipt(sandbox, CheatEngineToolNames.ScanStop, stopped);
			scannerAbsent = true;
			scannerAttempted = false;
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			if (scannerAttempted)
			{
				await CleanupAsync(cleanup, instance, async cleanupInstance =>
				{
					LiveMcpToolResult deleted = await cleanupInstance.CallToolRawAsync(CheatEngineToolNames.ScanDelete,
						new Dictionary<string, object?> { ["scannerName"] = ScanName });
					if (deleted.IsError)
					{
						Assert.Equal("not_found", Text(Payload(deleted)["error"], "kind"));
					}
					JsonNode scanners = await CallAsync(cleanupInstance, CheatEngineToolNames.ScanListScanners);
					Assert.DoesNotContain(scanners["scanners"]!.AsArray(), item =>
						string.Equals(Text(item!, "scannerName"), ScanName, StringComparison.Ordinal));
					scannerAbsent = true;
				});
			}
			if (allocationAttempted)
			{
				await CleanupAsync(cleanup, instance, async cleanupInstance =>
				{
					await ReleaseOrAssertAllocationAbsentAsync(cleanupInstance, ScanAllocation);
					allocationAbsent = true;
				});
			}
			if (scannerAbsent && allocationAbsent)
			{
				sandbox.Record("all_tools_scan_stop_cleanup", new
				{
					scannerAbsent,
					allocationAbsent
				});
			}
			ThrowCleanup(failure, cleanup);
		}
	}

	private static async Task VerifyStructureAutoguessAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		string name = "McpLiveAdditionalGuess_" + sandbox.HostA.TargetProcessId.ToString(CultureInfo.InvariantCulture);
		bool allocationAttempted = false;
		bool structureMutationAttempted = false;
		bool structureDeleted = false;
		bool allocationAbsent = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			JsonNode existing = await CallAsync(instance, CheatEngineToolNames.StructureList,
				new Dictionary<string, object?> { ["nameContains"] = name, ["offset"] = 0, ["limit"] = 16 });
			Assert.DoesNotContain(existing["structures"]!.AsArray(), item =>
				string.Equals(Text(item!, "name"), name, StringComparison.Ordinal));
			await AssertNoResourceNamedAsync(instance, StructureAllocation);
			allocationAttempted = true;
			JsonNode allocation = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = StructureAllocation, ["size"] = 64 });
			string address = Text(allocation, "address");
			JsonNode written = await CallAsync(instance, CheatEngineToolNames.MemoryWrite, new Dictionary<string, object?>
			{
				["address"] = address,
				["valueType"] = "bytes",
				["value"] = StructureBytes,
				["verify"] = true
			});
			Assert.True(written["verified"]!.GetValue<bool>());
			structureMutationAttempted = true;
			JsonNode guessed = await CallAsync(instance, CheatEngineToolNames.StructureAutoguess, new Dictionary<string, object?>
			{
				["name"] = name,
				["address"] = address,
				["offset"] = "0",
				["size"] = 16,
				["createIfMissing"] = true
			});
			Assert.Equal(name, Text(guessed, "name"));
			Assert.True(guessed["size"]!.GetValue<int>() > 0);
			Assert.True(guessed["elementCount"]!.GetValue<int>() > 0);
			JsonNode definition = await CallAsync(instance, CheatEngineToolNames.StructureGet,
				new Dictionary<string, object?> { ["name"] = name, ["offset"] = 0, ["limit"] = 128, ["format"] = "detailed" });
			Assert.Equal((Text(guessed, "name"), guessed["size"]!.GetValue<int>(), guessed["elementCount"]!.GetValue<int>()),
				(Text(definition, "name"), definition["size"]!.GetValue<int>(), definition["elementCount"]!.GetValue<int>()));
			AssertAutoguessCoverage(definition, 16);
			JsonNode bytes = await CallAsync(instance, CheatEngineToolNames.MemoryRead,
				new Dictionary<string, object?> { ["address"] = address, ["valueType"] = "bytes", ["size"] = 16 });
			Assert.Equal(StructureBytes, Text(bytes, "value"));
			Receipt(sandbox, CheatEngineToolNames.StructureAutoguess, guessed);
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			if (structureMutationAttempted)
			{
				await CleanupAsync(cleanup, instance, async cleanupInstance =>
				{
					await DeleteOrAssertStructureAbsentAsync(cleanupInstance, name);
					structureDeleted = true;
				});
			}
			if (allocationAttempted)
			{
				await CleanupAsync(cleanup, instance, async cleanupInstance =>
				{
					await ReleaseOrAssertAllocationAbsentAsync(cleanupInstance, StructureAllocation);
					allocationAbsent = true;
				});
			}
			if (structureDeleted && allocationAbsent)
			{
				sandbox.Record("all_tools_structure_autoguess_cleanup", new
				{
					structureDeleted,
					allocationAbsent
				});
			}
			ThrowCleanup(failure, cleanup);
		}
	}

	private static Dictionary<string, object?> ScanArguments(string address) => new()
	{
		["scannerName"] = ScanName,
		["valueType"] = "int32",
		["comparison"] = "exact",
		["value"] = "10",
		["startAddress"] = address,
		["endAddress"] = Add(address, 64),
		["writable"] = "required",
		["executable"] = "excluded",
		["copyOnWrite"] = "any",
		["alignment"] = 4,
		["includeMapped"] = false
	};

	private static async Task WriteAsync(LiveMcpInstanceClient instance, string address, string value)
	{
		JsonNode written = await CallAsync(instance, CheatEngineToolNames.MemoryWrite, new Dictionary<string, object?>
		{
			["address"] = address,
			["valueType"] = "int32",
			["value"] = value,
			["verify"] = true
		});
		Assert.True(written["verified"]!.GetValue<bool>());
	}

	private static void AssertAutoguessCoverage(JsonNode definition, int expectedSize)
	{
		Assert.Equal(expectedSize, definition["size"]!.GetValue<int>());
		int total = definition["total"]!.GetValue<int>();
		JsonArray elements = definition["elements"]!.AsArray();
		Assert.Equal(total, definition["elementCount"]!.GetValue<int>());
		Assert.Equal(total, elements.Count);
		Assert.Null(definition["nextOffset"]);
		Assert.NotEmpty(elements);
		int nextOffset = 0;
		foreach (JsonNode? element in elements)
		{
			Assert.NotNull(element);
			JsonNode value = element!;
			int offset = checked((int) ulong.Parse(Text(value, "offset"), NumberStyles.AllowHexSpecifier,
				CultureInfo.InvariantCulture));
			Assert.Equal(nextOffset, offset);
			string valueType = Text(value, "valueType");
			Assert.True(FixedPrimitiveWidths.TryGetValue(valueType, out int width),
				$"Auto-guess returned a non-fixed type '{valueType}' for the fixed byte oracle.");
			Assert.Equal(width, value["byteSize"]!.GetValue<int>());
			nextOffset = checked(nextOffset + width);
			Assert.InRange(nextOffset, 1, expectedSize);
		}
		Assert.Equal(expectedSize, nextOffset);
	}

	private static async Task AssertAllocationAbsentAsync(LiveMcpInstanceClient instance, string name)
	{
		LiveMcpToolResult absent = await instance.CallToolRawAsync(CheatEngineToolNames.MemoryFree,
			new Dictionary<string, object?> { ["name"] = name });
		Assert.True(absent.IsError);
		Assert.Equal("not_found", Text(Payload(absent)["error"], "kind"));
	}

	private static async Task ReleaseOrAssertAllocationAbsentAsync(LiveMcpInstanceClient instance, string name)
	{
		LiveMcpToolResult released = await instance.CallToolRawAsync(CheatEngineToolNames.MemoryFree,
			new Dictionary<string, object?> { ["name"] = name });
		if (released.IsError)
		{
			Assert.Equal("not_found", Text(Payload(released)["error"], "kind"));
		}
		else
		{
			Assert.True(Payload(released)["release"]!["isComplete"]!.GetValue<bool>());
		}
		await AssertAllocationAbsentAsync(instance, name);
	}

	private static async Task DeleteOrAssertStructureAbsentAsync(LiveMcpInstanceClient instance, string name)
	{
		LiveMcpToolResult deleted = await instance.CallToolRawAsync(CheatEngineToolNames.StructureDelete,
			new Dictionary<string, object?> { ["name"] = name });
		if (deleted.IsError)
		{
			Assert.Equal("not_found", Text(Payload(deleted)["error"], "kind"));
		}
		else
		{
			Assert.Equal(name, Text(Payload(deleted), "name"));
		}
		LiveMcpToolResult absent = await instance.CallToolRawAsync(CheatEngineToolNames.StructureGet,
			new Dictionary<string, object?> { ["name"] = name, ["offset"] = 0, ["limit"] = 1 });
		Assert.True(absent.IsError);
		Assert.Equal("not_found", Text(Payload(absent)["error"], "kind"));
	}

	private static async Task AssertNoResourceNamedAsync(LiveMcpInstanceClient instance, string name)
	{
		JsonNode resources = await CallAsync(instance, CheatEngineToolNames.RuntimeListResources);
		Assert.DoesNotContain(resources["resources"]!.AsArray(), item =>
			string.Equals(item?["name"]?.GetValue<string>(), name, StringComparison.Ordinal));
	}

	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool,
		IReadOnlyDictionary<string, object?>? arguments = null) => await instance.CallToolAsync(tool, arguments)
		?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
	private static JsonNode Payload(LiveMcpToolResult result) => result.Payload ?? throw new InvalidDataException("Expected error returned no payload.");
	private static string Text(JsonNode? node, string property) => node?[property]?.GetValue<string>()
		?? throw new InvalidDataException($"Missing string '{property}'.");
	private static string Add(string address, int offset) =>
		(ulong.Parse(address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address[2..] : address,
			NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) + (ulong) offset).ToString("X", CultureInfo.InvariantCulture);
	private static void Receipt(LiveSandboxSession sandbox, string tool, JsonNode value) => sandbox.Record("all_tools_" + tool, value.DeepClone());
	private static async Task CleanupAsync(List<Exception> failures, LiveMcpInstanceClient instance,
		Func<LiveMcpInstanceClient, Task> action)
	{
		try
		{
			using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
			await action(instance.WithCancellation(timeout.Token));
		}
		catch (Exception exception) { failures.Add(exception); }
	}
	private static void ThrowCleanup(Exception? failure, List<Exception> cleanup)
	{
		if (cleanup.Count == 0)
		{
			return;
		}
		throw failure is null ? new AggregateException(cleanup) : new AggregateException([failure, .. cleanup]);
	}
}
