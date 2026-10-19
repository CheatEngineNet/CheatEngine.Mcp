using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Gateway qualification for persisted notes and offline roadmap tools using only owned live fixtures.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveRoadmapQualification
{
	private static readonly string[] NoteTags = ["live", "roadmap"];
	private static readonly string[] NoteObservations = ["gateway round trip"];

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		ArgumentNullException.ThrowIfNull(instance);

		string root = Path.Combine(sandbox.RunDirectory, "roadmap-qualification");
		Directory.CreateDirectory(root);
		await VerifyNotesAsync(sandbox, instance, root);
		await VerifyIl2CppAsync(sandbox, instance, root);
		await VerifyStructuresAsync(sandbox, instance, root);
		await VerifyProcessPagingAsync(sandbox, instance);
	}

	internal static async Task VerifyNotesAfterRestartAsync(LiveSandboxSession sandbox,
		LiveMcpInstanceClient instance, int originalTargetProcessId)
	{
		Assert.NotEqual(originalTargetProcessId, sandbox.HostA.TargetProcessId);
		string name = NoteName(originalTargetProcessId);
		JsonNode result = await CallAsync(instance, CheatEngineToolNames.InvestigationSearchNotes,
			new Dictionary<string, object?>
			{
				["directory"] = Path.Combine(sandbox.RunDirectory, "roadmap-qualification"),
				["name"] = name,
				["limit"] = 1
			});
		Assert.Equal(1, Int(result, "total"));
		JsonNode note = Single(result, "notes");
		Assert.Equal((name, 2, "Owned live evidence revision two", "updated through the gateway"),
			(Text(note, "name"), Int(note, "revision"), Text(note, "title"), Text(note, "body")));
		Assert.Empty(note["tags"]!.AsArray());
		Assert.Empty(note["observations"]!.AsArray());
		sandbox.Record("live_roadmap_notes_restart", new
		{
			originalTargetProcessId,
			currentTargetProcessId = sandbox.HostA.TargetProcessId,
			note = note.DeepClone()
		});
	}

	private static string NoteName(int processId) => "live_roadmap_note_" + processId.ToString(CultureInfo.InvariantCulture);

	private static async Task VerifyNotesAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string root)
	{
		string name = NoteName(sandbox.HostA.TargetProcessId);
		JsonNode created = await CallAsync(instance, CheatEngineToolNames.InvestigationCreateNote, new Dictionary<string, object?>
		{
			["directory"] = root,
			["name"] = name,
			["title"] = "Owned live evidence",
			["body"] = "created through the gateway",
			["tags"] = NoteTags,
			["confidence"] = "high",
			["observations"] = NoteObservations
		});
		Assert.Equal((name, 1), (Text(created, "name"), Int(created, "revision")));
		Receipt(sandbox, CheatEngineToolNames.InvestigationCreateNote, created);

		JsonNode searched = await CallAsync(instance, CheatEngineToolNames.InvestigationSearchNotes,
			new Dictionary<string, object?> { ["directory"] = root, ["name"] = name, ["limit"] = 1 });
		JsonNode searchedNote = Single(searched, "notes");
		Assert.Equal((1, name, 1), (Int(searched, "total"), Text(searchedNote, "name"), Int(searchedNote, "revision")));
		Receipt(sandbox, CheatEngineToolNames.InvestigationSearchNotes, searched);

		const string finalTitle = "Owned live evidence revision two";
		const string finalBody = "updated through the gateway";
		JsonNode updated = await CallAsync(instance, CheatEngineToolNames.InvestigationUpdateNote, new Dictionary<string, object?>
		{
			["directory"] = root,
			["name"] = name,
			["expectedRevision"] = 1,
			["title"] = finalTitle,
			["body"] = finalBody
		});
		Assert.Equal((name, 2, finalTitle, finalBody), (Text(updated, "name"), Int(updated, "revision"), Text(updated, "title"), Text(updated, "body")));
		Receipt(sandbox, CheatEngineToolNames.InvestigationUpdateNote, updated);

		string exportPath = Path.Combine(root, name + ".export.json");
		JsonNode exported = await CallAsync(instance, CheatEngineToolNames.InvestigationExportNotes,
			new Dictionary<string, object?> { ["directory"] = root, ["path"] = exportPath, ["name"] = name });
		Assert.Equal((Path.GetFileName(exportPath), 1, "cemcp.investigation-export.v1"),
			(Text(exported, "fileName"), Int(exported, "notes"), Text(exported, "format")));
		using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(exportPath));
		JsonElement note = document.RootElement.GetProperty("notes").EnumerateArray().Single();
		Assert.Equal(("cemcp.investigation-export", 1, name, 2, finalBody),
			(document.RootElement.GetProperty("schema").GetString(), document.RootElement.GetProperty("version").GetInt32(),
				note.GetProperty("name").GetString(), note.GetProperty("revision").GetInt32(), note.GetProperty("body").GetString()));
		Receipt(sandbox, CheatEngineToolNames.InvestigationExportNotes, exported);
	}

	private static async Task VerifyIl2CppAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string root)
	{
		string modulePath = Path.Combine(root, Path.GetFileName(sandbox.TargetImagePath));
		File.Copy(sandbox.TargetImagePath, modulePath, true);
		(uint rva, string moduleName) = ReadSafeRva(modulePath);
		string dumpPath = Path.Combine(root, "owned-live.dump.cs");
		await File.WriteAllTextAsync(dumpPath, Dump(rva), new UTF8Encoding(false));
		string? dataset = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			JsonNode imported = await CallAsync(instance, CheatEngineToolNames.Il2CppImportDump,
				new Dictionary<string, object?> { ["path"] = dumpPath, ["modulePath"] = modulePath });
			dataset = Text(imported, "datasetId");
			Assert.Equal(("dump_cs_v1", 1, 1, 2), (Text(imported, "format"), Int(imported, "formatVersion"), Int(imported, "typeCount"), Int(imported, "memberCount")));
			Receipt(sandbox, CheatEngineToolNames.Il2CppImportDump, imported);

			JsonNode types = await CallAsync(instance, CheatEngineToolNames.Il2CppFindTypes,
				new Dictionary<string, object?> { ["datasetId"] = dataset, ["nameContains"] = "probe", ["limit"] = 1 });
			JsonNode type = Single(types, "types");
			Assert.Equal("Probe", Text(type, "name"));
			Receipt(sandbox, CheatEngineToolNames.Il2CppFindTypes, types);

			JsonNode members = await CallAsync(instance, CheatEngineToolNames.Il2CppFindMembers, new Dictionary<string, object?>
			{
				["datasetId"] = dataset,
				["kind"] = "method",
				["typeId"] = Text(type, "typeId"),
				["nameContains"] = "tick",
				["limit"] = 1
			});
			JsonNode method = Single(members, "members");
			Assert.Equal(("Tick", rva.ToString("X", CultureInfo.InvariantCulture)), (Text(method, "name"), Text(method, "rva")));
			Receipt(sandbox, CheatEngineToolNames.Il2CppFindMembers, members);

			JsonNode listed = await CallAsync(instance, CheatEngineToolNames.ModuleList,
				new Dictionary<string, object?> { ["nameContains"] = moduleName, ["format"] = "detailed", ["limit"] = 1000 });
			using Process target = Process.GetProcessById(sandbox.HostA.TargetProcessId);
			using ProcessModule main = target.MainModule ?? throw new InvalidOperationException("The owned target has no main module.");
			string baseAddress = ((ulong) main.BaseAddress).ToString("X", CultureInfo.InvariantCulture);
			Assert.Contains(listed["modules"]!.AsArray(), item => item is not null &&
				string.Equals(Text(item, "name"), moduleName, StringComparison.OrdinalIgnoreCase) && Text(item, "base") == baseAddress);
			JsonNode resolved = await CallAsync(instance, CheatEngineToolNames.Il2CppResolveMethod,
				new Dictionary<string, object?> { ["datasetId"] = dataset, ["methodId"] = Text(method, "memberId") });
			ulong expected = checked(ulong.Parse(baseAddress, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) + rva);
			JsonNode resolution = resolved["resolution"]!;
			Assert.Equal((sandbox.HostA.TargetProcessId, expected.ToString("X", CultureInfo.InvariantCulture)),
				(Int(resolution, "processId"), Text(resolution, "address")));
			Receipt(sandbox, CheatEngineToolNames.Il2CppResolveMethod, resolved);
		}
		catch (Exception exception)
		{
			failure = exception;
			throw;
		}
		finally
		{
			if (dataset is not null)
			{
				try
				{
					JsonNode deleted = await CallAsync(instance, CheatEngineToolNames.Il2CppDeleteDump,
						new Dictionary<string, object?> { ["datasetId"] = dataset });
					Assert.True(deleted["deleted"]!.GetValue<bool>());
					Receipt(sandbox, CheatEngineToolNames.Il2CppDeleteDump, deleted);
				}
				catch (Exception exception) { cleanup.Add(exception); }
			}
			ThrowCleanup(failure, cleanup);
		}
	}

	private static async Task VerifyStructuresAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string root)
	{
		string suffix = sandbox.HostA.TargetProcessId.ToString(CultureInfo.InvariantCulture);
		string parent = "McpLiveRoadmapA_" + suffix;
		string child = "McpLiveRoadmapB_" + suffix;
		string path = Path.Combine(root, "structures-" + suffix + ".json");
		bool parentExists = false;
		bool childExists = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			JsonNode childCreated = await CallAsync(instance, CheatEngineToolNames.StructureCreate,
				new Dictionary<string, object?> { ["name"] = child, ["elements"] = Array.Empty<object?>() });
			childExists = true;
			Assert.Equal(child, Text(childCreated, "name"));
			JsonNode parentCreated = await CallAsync(instance, CheatEngineToolNames.StructureCreate,
				new Dictionary<string, object?>
				{
					["name"] = parent,
					["elements"] = new object?[]
					{
						new Dictionary<string, object?> { ["offset"] = "0", ["name"] = "health", ["valueType"] = "int32" },
						new Dictionary<string, object?> { ["offset"] = "8", ["name"] = "child", ["valueType"] = "pointer", ["childStructure"] = child }
					}
				});
			parentExists = true;
			Assert.Equal(parent, Text(parentCreated, "name"));

			JsonNode exported = await CallAsync(instance, CheatEngineToolNames.StructureExport,
				new Dictionary<string, object?> { ["names"] = new[] { parent }, ["path"] = path });
			Assert.Equal((Path.GetFileName(path), 1, sandbox.TargetArchitecture == "x64" ? 8 : 4, 2),
				(Text(exported, "fileName"), Int(exported, "version"), Int(exported, "pointerWidth"), Int(exported, "layoutCount")));
			byte[] before = await File.ReadAllBytesAsync(path);
			Receipt(sandbox, CheatEngineToolNames.StructureExport, exported);

			await DeleteStructureAsync(instance, parent);
			parentExists = false;
			await DeleteStructureAsync(instance, child);
			childExists = false;
			JsonNode imported = await CallAsync(instance, CheatEngineToolNames.StructureImport,
				new Dictionary<string, object?> { ["path"] = path });
			parentExists = true;
			childExists = true;
			Assert.Equal((Path.GetFileName(path), 1, sandbox.TargetArchitecture == "x64" ? 8 : 4, 2),
				(Text(imported, "fileName"), Int(imported, "version"), Int(imported, "pointerWidth"), Int(imported, "layoutCount")));
			Receipt(sandbox, CheatEngineToolNames.StructureImport, imported);

			JsonNode restored = await CallAsync(instance, CheatEngineToolNames.StructureGet,
				new Dictionary<string, object?> { ["name"] = parent, ["limit"] = 8, ["format"] = "detailed" });
			JsonArray fields = restored["elements"]!.AsArray();
			Assert.Equal(2, fields.Count);
			Assert.Equal(("0", "health", "int32"), (Text(fields[0]!, "offset"), Text(fields[0]!, "name"), Text(fields[0]!, "valueType")));
			Assert.Equal(("8", "child", "pointer", child), (Text(fields[1]!, "offset"), Text(fields[1]!, "name"), Text(fields[1]!, "valueType"), Text(fields[1]!, "childStructure")));
			string afterPath = Path.Combine(root, "structures-after-" + suffix + ".json");
			await CallAsync(instance, CheatEngineToolNames.StructureExport,
				new Dictionary<string, object?> { ["names"] = new[] { parent }, ["path"] = afterPath });
			Assert.Equal(before, await File.ReadAllBytesAsync(afterPath));
		}
		catch (Exception exception)
		{
			failure = exception;
			throw;
		}
		finally
		{
			if (parentExists)
			{
				await CleanupDeleteAsync(instance, parent, cleanup);
			}

			if (childExists)
			{
				await CleanupDeleteAsync(instance, child, cleanup);
			}

			sandbox.Record("live_roadmap_cleanup", new
			{
				parentDeleteAttempted = parentExists,
				childDeleteAttempted = childExists,
				failures = cleanup.Count
			});
			ThrowCleanup(failure, cleanup);
		}
	}

	private static async Task VerifyProcessPagingAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		JsonNode first = await CallAsync(instance, CheatEngineToolNames.ProcessList,
			new Dictionary<string, object?> { ["pageSize"] = 1 });
		JsonNode firstProcess = Single(first, "processes");
		Assert.True(Int(firstProcess, "processId") > 0);
		Assert.True(Int(first, "snapshotTotal") > 1);
		string cursor = Text(first, "nextCursor");
		Assert.NotEmpty(cursor);
		Dictionary<string, object?> arguments = new()
		{
			["cursor"] = cursor
		};
		JsonNode next = await CallAsync(instance, CheatEngineToolNames.ProcessList, arguments);
		JsonNode nextProcess = Single(next, "processes");
		Assert.True(Int(nextProcess, "processId") > Int(firstProcess, "processId"));
		Assert.Equal(Int(first, "snapshotTotal"), Int(next, "snapshotTotal"));
		JsonNode replay = await CallAsync(instance, CheatEngineToolNames.ProcessList, arguments);
		Assert.True(JsonNode.DeepEquals(next, replay));
		sandbox.Record("all_tools_process_list_paging", new
		{
			first = first.DeepClone(),
			next = next.DeepClone(),
			replayMatches = true
		});
	}

	private static async Task DeleteStructureAsync(LiveMcpInstanceClient instance, string name)
	{
		JsonNode deleted = await CallAsync(instance, CheatEngineToolNames.StructureDelete,
			new Dictionary<string, object?> { ["name"] = name });
		Assert.Equal(name, Text(deleted, "name"));
	}

	private static async Task CleanupDeleteAsync(LiveMcpInstanceClient instance, string name, List<Exception> cleanup)
	{
		try
		{
			await DeleteStructureAsync(instance, name);
		}
		catch (Exception exception) { cleanup.Add(exception); }
	}

	private static (uint Rva, string ModuleName) ReadSafeRva(string path)
	{
		using FileStream stream = File.OpenRead(path);
		using PEReader reader = new(stream);
		PEHeaders headers = reader.PEHeaders;
		SectionHeader section = headers.SectionHeaders.First(section => section.VirtualSize > 16 && section.VirtualAddress > 0);
		return (checked((uint) section.VirtualAddress + 16U), Path.GetFileName(path));
	}

	private static string Dump(uint rva) => $$"""
// Image 0: Owned.Live.dll - 0
// Namespace: Owned.Live
public class Probe // TypeDefIndex: 1
{
// Fields
public int Count; // 0x10
// Methods
// RVA: 0x{{rva:X}} Offset: 0x{{rva:X}}
public void Tick() { }
}
""";

	private static void Receipt(LiveSandboxSession sandbox, string tool, JsonNode value) => sandbox.Record("all_tools_" + tool, value.DeepClone());
	private static JsonNode Single(JsonNode node, string property) => Assert.Single(node[property]!.AsArray())!;
	private static string Text(JsonNode node, string property) => node[property]!.GetValue<string>();
	private static int Int(JsonNode node, string property) => node[property]!.GetValue<int>();
	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool, IReadOnlyDictionary<string, object?>? arguments = null) => await instance.CallToolAsync(tool, arguments) ?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
	private static void ThrowCleanup(Exception? failure, List<Exception> cleanup) => LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
}
