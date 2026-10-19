using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Bounded pointer-map and path persistence qualification over the inspection fixture's owned allocation.</summary>
[SupportedOSPlatform("windows")]
internal static class LivePointerPersistenceQualification
{
	private const string Map = "live-pointer-map";
	private const string LoadedMap = "live-pointer-map-loaded";
	private const string Scan = "live-pointer-scan";
	private const string LoadedScan = "live-pointer-scan-loaded";

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string address,
		int width)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		ArgumentNullException.ThrowIfNull(instance);
		bool map = false, loadedMap = false, scan = false, loadedScan = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		string persistenceDirectory = Path.Combine(sandbox.RunDirectory, "pointer-qualification");
		Directory.CreateDirectory(persistenceDirectory);
		string mapPath = Path.Combine(persistenceDirectory, "live-pointer-map.scandata");
		string scanPath = Path.Combine(persistenceDirectory, "live-pointer-scan.json");
		try
		{
			JsonNode started = await CallAsync(instance, CheatEngineToolNames.PointerCreateMap, new Dictionary<string, object?>
			{
				["mapName"] = Map,
				["startAddress"] = At(address, 256),
				["endAddress"] = At(address, 272 + width - 1),
				["writableOnly"] = true,
				["alignment"] = width,
				["maxBytes"] = 16 + width,
				["maxPointers"] = 4,
				["lifetimeSeconds"] = 30
			});
			map = true;
			Assert.Equal(Map, Text(started, "mapName"));
			JsonNode readyMap = await WaitForMapAsync(instance, Map);
			Assert.Equal("ready", Text(readyMap, "state"));
			Assert.Equal((16 + width, 2, 0L, false), (readyMap["bytesRead"]!.GetValue<long>(),
				readyMap["pointers"]!.GetValue<int>(), readyMap["unreadableBytes"]!.GetValue<long>(),
				readyMap["incomplete"]!.GetValue<bool>()));
			Receipt(sandbox, CheatEngineToolNames.PointerCreateMap, new
			{
				name = Map,
				bytes = 16 + width,
				state = "ready"
			});

			JsonNode maps = await CallAsync(instance, CheatEngineToolNames.PointerListMaps);
			Assert.Contains(maps["maps"]!.AsArray(), item => Text(item, "mapName") == Map && Text(item, "state") == "ready");
			Receipt(sandbox, CheatEngineToolNames.PointerListMaps, maps.DeepClone());

			JsonNode references = await CallAsync(instance, CheatEngineToolNames.PointerFindReferences, new Dictionary<string, object?>
			{
				["target"] = At(address, 288),
				["mapName"] = Map,
				["maxOffset"] = 0,
				["limit"] = 8
			});
			Assert.Equal("map", Text(references, "source"));
			Assert.Contains(references["references"]!.AsArray(), item => Text(item, "address") == At(address, 256));
			Receipt(sandbox, CheatEngineToolNames.PointerFindReferences, references.DeepClone());

			JsonNode savedMap = await CallAsync(instance, CheatEngineToolNames.PointerSaveMap,
				new Dictionary<string, object?> { ["mapName"] = Map, ["path"] = mapPath });
			Assert.Equal(Path.GetFileName(mapPath), Path.GetFileName(Text(savedMap, "filePath")));
			Assert.True(File.Exists(mapPath));
			Receipt(sandbox, CheatEngineToolNames.PointerSaveMap, savedMap.DeepClone());

			JsonNode loaded = await CallAsync(instance, CheatEngineToolNames.PointerLoadMap,
				new Dictionary<string, object?> { ["mapName"] = LoadedMap, ["path"] = mapPath, ["maximumPointers"] = 64 });
			loadedMap = true;
			Assert.Equal(LoadedMap, Text(loaded["map"], "mapName"));
			Assert.Equal(width, loaded["map"]!["pointerSize"]!.GetValue<int>());
			Receipt(sandbox, CheatEngineToolNames.PointerLoadMap, loaded.DeepClone());

			JsonNode scanStarted = await CallAsync(instance, CheatEngineToolNames.PointerFindPaths, new Dictionary<string, object?>
			{
				["scanName"] = Scan,
				["mapName"] = Map,
				["target"] = At(address, 336),
				["maxDepth"] = 2,
				["maxOffset"] = 32,
				["allowNegativeOffsets"] = true,
				["staticRootsOnly"] = false,
				["maxResults"] = 8,
				["maxNodes"] = 64,
				["lifetimeSeconds"] = 30
			});
			scan = true;
			Assert.Equal(Scan, Text(scanStarted, "scanName"));
			JsonNode readyScan = await WaitForScanAsync(instance, Scan);
			Assert.Equal("ready", Text(readyScan, "state"));
			Assert.Equal(2, readyScan["count"]!.GetValue<int>());
			Receipt(sandbox, CheatEngineToolNames.PointerFindPaths, new
			{
				name = Scan,
				state = "ready",
				paths = 2
			});

			JsonNode paths = await CallAsync(instance, CheatEngineToolNames.PointerListPaths,
				new Dictionary<string, object?> { ["scanName"] = Scan, ["limit"] = 8 });
			JsonArray foundPaths = paths["paths"]!.AsArray();
			Assert.Equal(2, foundPaths.Count);
			AssertPath(foundPaths[0], At(address, 272), ["20"]);
			AssertPath(foundPaths[1], At(address, 256), ["-10", "20"]);
			Receipt(sandbox, CheatEngineToolNames.PointerListPaths, paths.DeepClone());

			JsonNode rescanned = await CallAsync(instance, CheatEngineToolNames.PointerRescanPaths,
				new Dictionary<string, object?> { ["scanName"] = Scan, ["target"] = At(address, 336), ["mapName"] = LoadedMap });
			Assert.Equal((2, 2, 0), (rescanned["count"]!.GetValue<int>(), rescanned["verified"]!.GetValue<int>(), rescanned["removed"]!.GetValue<int>()));
			Receipt(sandbox, CheatEngineToolNames.PointerRescanPaths, rescanned.DeepClone());

			JsonNode scans = await CallAsync(instance, CheatEngineToolNames.PointerListScans);
			Assert.Contains(scans["scans"]!.AsArray(), item => Text(item, "scanName") == Scan && Text(item, "state") == "ready");
			Receipt(sandbox, CheatEngineToolNames.PointerListScans, scans.DeepClone());

			JsonNode savedScan = await CallAsync(instance, CheatEngineToolNames.PointerSaveScan,
				new Dictionary<string, object?> { ["scanName"] = Scan, ["path"] = scanPath });
			Assert.True(File.Exists(scanPath));
			Receipt(sandbox, CheatEngineToolNames.PointerSaveScan, savedScan.DeepClone());
			JsonNode importedScan = await CallAsync(instance, CheatEngineToolNames.PointerLoadScan,
				new Dictionary<string, object?> { ["scanName"] = LoadedScan, ["path"] = scanPath });
			loadedScan = true;
			Assert.Equal(LoadedScan, Text(importedScan["scan"], "scanName"));
			JsonNode loadedPaths = await CallAsync(instance, CheatEngineToolNames.PointerListPaths,
				new Dictionary<string, object?> { ["scanName"] = LoadedScan, ["limit"] = 8 });
			JsonArray restoredPaths = loadedPaths["paths"]!.AsArray();
			Assert.Equal(2, restoredPaths.Count);
			AssertPath(restoredPaths[0], At(address, 272), ["20"]);
			AssertPath(restoredPaths[1], At(address, 256), ["-10", "20"]);
			Receipt(sandbox, CheatEngineToolNames.PointerLoadScan, importedScan.DeepClone());
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			if (loadedScan)
			{
				await CleanupAsync(cleanup, () => DeleteScanAsync(instance, sandbox, LoadedScan, false));
			}

			if (scan)
			{
				await CleanupAsync(cleanup, () => DeleteScanAsync(instance, sandbox, Scan, true));
			}

			if (loadedMap)
			{
				await CleanupAsync(cleanup, () => DeleteMapAsync(instance, sandbox, LoadedMap, false));
			}

			if (map)
			{
				await CleanupAsync(cleanup, () => DeleteMapAsync(instance, sandbox, Map, true));
			}

			sandbox.Record("live_pointer_persistence_cleanup", new
			{
				failures = cleanup.Select(error => error.ToString()).ToArray()
			});
			LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
		}
	}

	private static async Task<JsonNode> WaitForMapAsync(LiveMcpInstanceClient instance, string name)
	{
		for (int attempt = 0; attempt != 40; attempt++)
		{
			JsonNode page = await CallAsync(instance, CheatEngineToolNames.PointerListMaps);
			JsonNode? map = Assert.Single(page["maps"]!.AsArray(), item => Text(item, "mapName") == name);
			Assert.NotNull(map);
			if (Text(map, "state") == "ready")
			{
				return map;
			}

			Assert.Equal("running", Text(map, "state"));
			await Task.Delay(100, TestContext.Current.CancellationToken);
		}
		throw new TimeoutException("pointer map did not become ready within 4 seconds.");
	}

	private static async Task<JsonNode> WaitForScanAsync(LiveMcpInstanceClient instance, string name)
	{
		for (int attempt = 0; attempt != 40; attempt++)
		{
			JsonNode page = await CallAsync(instance, CheatEngineToolNames.PointerListScans);
			JsonNode? scan = Assert.Single(page["scans"]!.AsArray(), item => Text(item, "scanName") == name);
			Assert.NotNull(scan);
			if (Text(scan, "state") == "ready")
			{
				return scan;
			}

			Assert.Equal("running", Text(scan, "state"));
			await Task.Delay(100, TestContext.Current.CancellationToken);
		}
		throw new TimeoutException("pointer scan did not become ready within 4 seconds.");
	}

	private static async Task DeleteMapAsync(LiveMcpInstanceClient instance, LiveSandboxSession sandbox, string name,
		bool receipt)
	{
		JsonNode deleted = await CallAsync(instance, CheatEngineToolNames.PointerDeleteMap, new Dictionary<string, object?> { ["mapName"] = name });
		Assert.Equal(name, Text(deleted, "name"));
		JsonNode listed = await CallAsync(instance, CheatEngineToolNames.PointerListMaps);
		Assert.DoesNotContain(listed["maps"]!.AsArray(), item => Text(item, "mapName") == name);
		if (receipt)
		{
			Receipt(sandbox, CheatEngineToolNames.PointerDeleteMap, deleted.DeepClone());
		}
	}

	private static async Task DeleteScanAsync(LiveMcpInstanceClient instance, LiveSandboxSession sandbox, string name,
		bool receipt)
	{
		JsonNode deleted = await CallAsync(instance, CheatEngineToolNames.PointerDeleteScan, new Dictionary<string, object?> { ["scanName"] = name });
		Assert.Equal(name, Text(deleted, "name"));
		JsonNode listed = await CallAsync(instance, CheatEngineToolNames.PointerListScans);
		Assert.DoesNotContain(listed["scans"]!.AsArray(), item => Text(item, "scanName") == name);
		if (receipt)
		{
			Receipt(sandbox, CheatEngineToolNames.PointerDeleteScan, deleted.DeepClone());
		}
	}

	private static async Task CleanupAsync(List<Exception> failures, Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception exception) { failures.Add(exception); }
	}
	private static void Receipt(LiveSandboxSession sandbox, string tool, object result) => sandbox.Record("all_tools_" + tool, result);
	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool, IReadOnlyDictionary<string, object?>? arguments = null) => await instance.CallToolAsync(tool, arguments) ?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
	private static string Text(JsonNode? node, string name) => node?[name]?.GetValue<string>() ?? throw new InvalidDataException($"Expected {name}.");
	private static void AssertPath(JsonNode? path, string expectedBase, string[] expectedOffsets)
	{
		Assert.NotNull(path);
		Assert.Equal(expectedBase, Text(path, "base"));
		Assert.Equal(expectedOffsets, path!["offsets"]!.AsArray().Select(item => item!.GetValue<string>()));
	}
	private static string At(string address, int offset) => checked(ulong.Parse(address, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) + (uint) offset).ToString("X", CultureInfo.InvariantCulture);
}
