using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using System.Xml.Linq;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Reversible gateway qualification for files, tables, records, and the owned target pause state.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveFileRecordQualification
{
	private const string Allocation = "live-file-record-allocation";

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		string root = Path.Combine(sandbox.RunDirectory, "roadmap-qualification");
		string tables = Path.Combine(root, "tables");
		string dump = Path.Combine(root, "owned-record.bin");
		string table = Path.Combine(tables, "owned-records-" + sandbox.HostA.TargetProcessId.ToString(CultureInfo.InvariantCulture) + ".ct");
		Directory.CreateDirectory(tables);
		bool allocated = false;
		bool ownsEmptyTable = false;
		bool pauseAttempted = false;
		bool pauseResumeRequired = false;
		bool ownsDump = false;
		bool ownsTable = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			JsonNode initial = await CallAsync(instance, CheatEngineToolNames.RecordList,
				new Dictionary<string, object?> { ["offset"] = 0, ["limit"] = 16 });
			Assert.Equal(0, initial["total"]!.GetValue<int>());
			ownsEmptyTable = true;
			Assert.False(File.Exists(dump));
			Assert.False(File.Exists(table));
			JsonNode allocation = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = Allocation, ["size"] = 16 });
			allocated = true;
			string address = Text(allocation, "address");
			await CallAsync(instance, CheatEngineToolNames.MemoryWrite, new Dictionary<string, object?>
			{
				["address"] = address,
				["valueType"] = "bytes",
				["value"] = "0B00000016000000",
				["verify"] = true
			});
			ownsDump = true;
			await VerifyFilesAsync(sandbox, instance, dump, address);
			ownsTable = true;
			await VerifyRecordsAndTableAsync(sandbox, instance, table, address);
			pauseAttempted = true;
			pauseResumeRequired = true;
			JsonNode pause = await CallAsync(instance, CheatEngineToolNames.ProcessSetPaused,
				new Dictionary<string, object?> { ["paused"] = true });
			Assert.True(pause["paused"]!.GetValue<bool>());
			string resourceId = Text(pause, "resourceId");
			JsonNode resources = await CallAsync(instance, CheatEngineToolNames.RuntimeListResources);
			Assert.Contains(resources["resources"]!.AsArray(), resource => Text(resource!, "id") == resourceId);
			JsonNode resume = await CallAsync(instance, CheatEngineToolNames.ProcessSetPaused,
				new Dictionary<string, object?> { ["paused"] = false });
			Assert.False(resume["paused"]!.GetValue<bool>());
			JsonNode releasedResources = await CallAsync(instance, CheatEngineToolNames.RuntimeListResources);
			Assert.DoesNotContain(releasedResources["resources"]!.AsArray(), resource => Text(resource!, "id") == resourceId);
			pauseResumeRequired = false;
			Receipt(sandbox, CheatEngineToolNames.ProcessSetPaused, new JsonObject
			{
				["pause"] = pause.DeepClone(),
				["resume"] = resume.DeepClone(),
				["resourceId"] = resourceId
			});
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			if (pauseResumeRequired)
			{
				await CleanupAsync(cleanup, async () =>
			{
				JsonNode resume = await CallAsync(instance, CheatEngineToolNames.ProcessSetPaused,
					new Dictionary<string, object?> { ["paused"] = false });
				Assert.False(resume["paused"]!.GetValue<bool>());
			});
			}

			if (ownsEmptyTable)
			{
				await CleanupAsync(cleanup, async () =>
			{
				JsonNode cleared = await CallAsync(instance, CheatEngineToolNames.RecordClear);
				Assert.True(cleared["deleted"]!.GetValue<int>() >= 0);
				JsonNode empty = await CallAsync(instance, CheatEngineToolNames.RecordList,
					new Dictionary<string, object?> { ["offset"] = 0, ["limit"] = 16 });
				Assert.Equal(0, empty["total"]!.GetValue<int>());
			});
			}

			if (allocated)
			{
				await CleanupAsync(cleanup, async () =>
			{
				JsonNode freed = await CallAsync(instance, CheatEngineToolNames.MemoryFree,
					new Dictionary<string, object?> { ["name"] = Allocation });
				Assert.Equal(Allocation, Text(freed, "name"));
				Assert.True(freed["release"]!["isComplete"]!.GetValue<bool>());
				LiveMcpToolResult absent = await instance.CallToolRawAsync(CheatEngineToolNames.MemoryFree,
					new Dictionary<string, object?> { ["name"] = Allocation });
				Assert.True(absent.IsError);
				Assert.Equal("not_found", Text(absent.Payload!["error"]!, "kind"));
			});
			}

			if (ownsDump)
			{
				await CleanupAsync(cleanup, () => DeleteOwnedFileAsync(dump));
			}

			if (ownsTable)
			{
				await CleanupAsync(cleanup, () => DeleteOwnedFileAsync(table));
			}

			sandbox.Record("live_file_record_cleanup", new
			{
				pauseAttempted,
				clearAttempted = ownsEmptyTable,
				freeAttempted = allocated,
				dumpDeleted = ownsDump && !File.Exists(dump),
				tableDeleted = ownsTable && !File.Exists(table),
				failures = cleanup.Count
			});
			LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
		}
	}

	private static async Task VerifyFilesAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string dump, string address)
	{
		JsonNode dumped = await CallAsync(instance, CheatEngineToolNames.MemoryDumpToFile,
			new Dictionary<string, object?> { ["address"] = address, ["size"] = 8, ["path"] = dump });
		Assert.Equal((8, 0), (dumped["bytesWritten"]!.GetValue<int>(), dumped["zeroFilledBytes"]!.GetValue<int>()));
		Assert.Equal("0B00000016000000", Convert.ToHexString(await File.ReadAllBytesAsync(dump)));
		Receipt(sandbox, CheatEngineToolNames.MemoryDumpToFile, dumped);
		await File.WriteAllBytesAsync(dump, [0x21, 0, 0, 0, 0x2C, 0, 0, 0]);
		JsonNode loaded = await CallAsync(instance, CheatEngineToolNames.MemoryLoadFromFile,
			new Dictionary<string, object?> { ["address"] = address, ["path"] = dump });
		Assert.Equal(8L, loaded["bytesWritten"]!.GetValue<long>());
		JsonNode read = await CallAsync(instance, CheatEngineToolNames.MemoryRead,
			new Dictionary<string, object?> { ["address"] = address, ["valueType"] = "bytes", ["size"] = 8 });
		Assert.Equal("21 00 00 00 2C 00 00 00", Text(read, "value"));
		Receipt(sandbox, CheatEngineToolNames.MemoryLoadFromFile, loaded);
	}

	private static async Task VerifyRecordsAndTableAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string table, string address)
	{
		string suffix = sandbox.HostA.TargetProcessId.ToString(CultureInfo.InvariantCulture);
		JsonNode created = await CallAsync(instance, CheatEngineToolNames.RecordCreate, new Dictionary<string, object?>
		{
			["records"] = new object?[]
			{
				new Dictionary<string, object?> { ["description"] = "McpLiveRecordA_" + suffix, ["address"] = address, ["value"] = "33", ["variableType"] = 2 },
				new Dictionary<string, object?> { ["description"] = "McpLiveRecordB_" + suffix, ["address"] = address + "+4", ["value"] = "44", ["variableType"] = 2 }
			}
		});
		JsonArray records = created["records"]!.AsArray();
		JsonNode first = records[0]!, second = records[1]!;
		int firstId = first["id"]!.GetValue<int>(), secondId = second["id"]!.GetValue<int>();
		Assert.Equal(("33", "44"), (Text(first, "value"), Text(second, "value")));
		JsonNode selected = await CallAsync(instance, CheatEngineToolNames.RecordSelect, new Dictionary<string, object?> { ["id"] = secondId });
		Assert.Equal(secondId, selected["id"]!.GetValue<int>());
		Receipt(sandbox, CheatEngineToolNames.RecordSelect, selected);
		JsonNode selectedRead = await CallAsync(instance, CheatEngineToolNames.RecordGetSelected);
		Assert.True(selectedRead["selected"]!.GetValue<bool>());
		Assert.Equal(secondId, selectedRead["record"]!["id"]!.GetValue<int>());
		Receipt(sandbox, CheatEngineToolNames.RecordGetSelected, selectedRead);
		JsonNode dropdown = await CallAsync(instance, CheatEngineToolNames.RecordSetDropdown, new Dictionary<string, object?>
		{
			["id"] = firstId,
			["items"] = new object?[] { new Dictionary<string, object?> { ["value"] = "33", ["description"] = "thirty-three" } },
			["disallowManualInput"] = true,
			["descriptionOnly"] = false,
			["displayAsListItem"] = true
		});
		Assert.Equal((1, true, false, true), (dropdown["record"]!["dropdown"]!["itemCount"]!.GetValue<int>(), dropdown["record"]!["dropdown"]!["disallowManualInput"]!.GetValue<bool>(), dropdown["record"]!["dropdown"]!["descriptionOnly"]!.GetValue<bool>(), dropdown["record"]!["dropdown"]!["displayAsListItem"]!.GetValue<bool>()));
		JsonNode dropdownRead = await CallAsync(instance, CheatEngineToolNames.RecordGet,
			new Dictionary<string, object?> { ["ids"] = new[] { firstId }, ["includeDropdown"] = true });
		JsonNode persistedDropdown = Assert.Single(dropdownRead["records"]!.AsArray())!["dropdown"]!;
		JsonNode persistedItem = Assert.Single(persistedDropdown["items"]!.AsArray())!;
		Assert.Equal(("33", "thirty-three", 1, true, false, true),
			(Text(persistedItem, "value"), Text(persistedItem, "description"), persistedDropdown["itemCount"]!.GetValue<int>(),
				persistedDropdown["disallowManualInput"]!.GetValue<bool>(), persistedDropdown["descriptionOnly"]!.GetValue<bool>(), persistedDropdown["displayAsListItem"]!.GetValue<bool>()));
		Receipt(sandbox, CheatEngineToolNames.RecordSetDropdown, dropdown);
		JsonNode grouped = await CallAsync(instance, CheatEngineToolNames.RecordGroup,
			new Dictionary<string, object?> { ["description"] = "McpLiveGroup_" + suffix, ["ids"] = new[] { firstId, secondId } });
		int groupId = grouped["group"]!["id"]!.GetValue<int>();
		Assert.Equal(2, grouped["group"]!["childCount"]!.GetValue<int>());
		Assert.Equal([firstId, secondId], grouped["records"]!.AsArray().Select(record => record!["id"]!.GetValue<int>()));
		Receipt(sandbox, CheatEngineToolNames.RecordGroup, grouped);
		JsonNode moved = await CallAsync(instance, CheatEngineToolNames.RecordMove,
			new Dictionary<string, object?> { ["id"] = secondId });
		Assert.Equal(secondId, moved["record"]!["id"]!.GetValue<int>());
		JsonNode groupAfterMove = await CallAsync(instance, CheatEngineToolNames.RecordGet,
			new Dictionary<string, object?> { ["ids"] = new[] { groupId } });
		Assert.Equal(1, groupAfterMove["records"]![0]!["childCount"]!.GetValue<int>());
		JsonNode children = await CallAsync(instance, CheatEngineToolNames.RecordList,
			new Dictionary<string, object?> { ["parentId"] = groupId, ["offset"] = 0, ["limit"] = 16 });
		Assert.Equal([firstId], children["records"]!.AsArray().Select(record => record!["id"]!.GetValue<int>()));
		Receipt(sandbox, CheatEngineToolNames.RecordMove, moved);
		JsonNode saved = await CallAsync(instance, CheatEngineToolNames.TableSave, new Dictionary<string, object?> { ["path"] = table });
		Assert.False(saved["replacedExisting"]!.GetValue<bool>());
		VerifySavedTable(table, "McpLiveRecordA_" + suffix, "McpLiveRecordB_" + suffix, "McpLiveGroup_" + suffix, address);
		Receipt(sandbox, CheatEngineToolNames.TableSave, saved);
		JsonNode listed = await CallAsync(instance, CheatEngineToolNames.TableListFiles, new Dictionary<string, object?> { ["limit"] = 16 });
		Assert.Contains(listed["files"]!.AsArray(), file => string.Equals(Text(file!, "path"), table, StringComparison.OrdinalIgnoreCase));
		Receipt(sandbox, CheatEngineToolNames.TableListFiles, listed);
		JsonNode cleared = await CallAsync(instance, CheatEngineToolNames.RecordClear);
		Assert.Equal(3, cleared["deleted"]!.GetValue<int>());
		JsonNode afterClear = await CallAsync(instance, CheatEngineToolNames.RecordList,
			new Dictionary<string, object?> { ["offset"] = 0, ["limit"] = 16 });
		Assert.Equal(0, afterClear["total"]!.GetValue<int>());
		Receipt(sandbox, CheatEngineToolNames.RecordClear, cleared);
		JsonNode loaded = await CallAsync(instance, CheatEngineToolNames.TableLoad, new Dictionary<string, object?> { ["path"] = table });
		Assert.Equal((false, "xml", 3, 0), (loaded["merge"]!.GetValue<bool>(), Text(loaded["inspection"]!, "format"), loaded["inspection"]!["recordCount"]!.GetValue<int>(), loaded["inspection"]!["assemblerScriptCount"]!.GetValue<int>()));
		await VerifyLoadedTableAsync(instance, "McpLiveRecordA_" + suffix, "McpLiveRecordB_" + suffix, "McpLiveGroup_" + suffix, address);
		Receipt(sandbox, CheatEngineToolNames.TableLoad, loaded);
	}

	private static void VerifySavedTable(string table, string firstDescription, string secondDescription, string groupDescription, string address)
	{
		XDocument document = XDocument.Load(table);
		XElement[] entries = [.. document.Descendants("CheatEntry")];
		Assert.Equal(3, entries.Length);
		XElement first = FindEntry(entries, firstDescription);
		XElement second = FindEntry(entries, secondDescription);
		XElement group = FindEntry(entries, groupDescription);
		Assert.Equal(address, first.Element("Address")!.Value);
		Assert.Equal(address + "+4", second.Element("Address")!.Value);
		Assert.Equal("4 Bytes", first.Element("VariableType")!.Value);
		Assert.Contains(first, group.Descendants("CheatEntry"));
		Assert.DoesNotContain(second, group.Descendants("CheatEntry"));
		Assert.Contains(first.Descendants(), element => element.Value.Contains("33", StringComparison.Ordinal));
		Assert.Contains(first.Descendants(), element => element.Value.Contains("thirty-three", StringComparison.Ordinal));
	}

	private static async Task VerifyLoadedTableAsync(LiveMcpInstanceClient instance, string firstDescription, string secondDescription, string groupDescription, string address)
	{
		JsonNode listed = await CallAsync(instance, CheatEngineToolNames.RecordList,
			new Dictionary<string, object?> { ["offset"] = 0, ["limit"] = 16 });
		Assert.Equal(3, listed["total"]!.GetValue<int>());
		JsonArray entries = listed["records"]!.AsArray();
		JsonNode first = Assert.Single(entries, entry => Text(entry!, "description") == firstDescription)!;
		JsonNode second = Assert.Single(entries, entry => Text(entry!, "description") == secondDescription)!;
		JsonNode group = Assert.Single(entries, entry => Text(entry!, "description") == groupDescription)!;
		Assert.Equal((address, "33"), (Text(first, "address"), Text(first, "value")));
		Assert.Equal((address + "+4", "44"), (Text(second, "address"), Text(second, "value")));
		int firstId = first["id"]!.GetValue<int>();
		int groupId = group["id"]!.GetValue<int>();
		JsonNode children = await CallAsync(instance, CheatEngineToolNames.RecordList,
			new Dictionary<string, object?> { ["parentId"] = groupId, ["offset"] = 0, ["limit"] = 16 });
		Assert.Equal([firstId], children["records"]!.AsArray().Select(entry => entry!["id"]!.GetValue<int>()));
		JsonNode firstWithDropdown = await CallAsync(instance, CheatEngineToolNames.RecordGet,
			new Dictionary<string, object?> { ["ids"] = new[] { firstId }, ["includeDropdown"] = true });
		JsonNode dropdown = Assert.Single(firstWithDropdown["records"]!.AsArray())!["dropdown"]!;
		JsonNode item = Assert.Single(dropdown["items"]!.AsArray())!;
		Assert.Equal(("33", "thirty-three", true, false, true), (Text(item, "value"), Text(item, "description"),
			dropdown["disallowManualInput"]!.GetValue<bool>(), dropdown["descriptionOnly"]!.GetValue<bool>(), dropdown["displayAsListItem"]!.GetValue<bool>()));
	}

	private static XElement FindEntry(IEnumerable<XElement> entries, string description)
	{
		return Assert.Single(entries, entry => entry.Element("Description")?.Value.Trim('"') == description);
	}

	private static Task DeleteOwnedFileAsync(string path)
	{
		File.Delete(path);
		Assert.False(File.Exists(path));
		return Task.CompletedTask;
	}

	private static async Task CleanupAsync(List<Exception> errors, Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception exception) { errors.Add(exception); }
	}

	private static void Receipt(LiveSandboxSession sandbox, string tool, JsonNode result) => sandbox.Record("all_tools_" + tool, result.DeepClone());
	private static string Text(JsonNode node, string property) => node[property]!.GetValue<string>();
	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool, IReadOnlyDictionary<string, object?>? arguments = null) => await instance.CallToolAsync(tool, arguments) ?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
}
