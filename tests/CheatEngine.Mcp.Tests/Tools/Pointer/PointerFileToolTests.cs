using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Core;
using CheatEngine.Mcp.Tools.Pointer;

namespace CheatEngine.Mcp.Tests.Tools.Pointer;

/// <summary>File policy, atomic publication and activation-independent pointer persistence.</summary>
public sealed class PointerFileToolTests : IDisposable
{
	private readonly McpFilePathsTests.Scratch _scratch = new();

	[Fact]
	public void SaveAndLoadMap_UsesAllowedRootAndReportsUnknownImportCompleteness()
	{
		string directory = _scratch.CreateFolder("maps");
		string path = Path.Combine(directory, "capture.scandata");
		using PointerStore captured = new();
		PointerMapSlot slot = captured.ReserveMap("before", 1);
		slot.Prepare(42, 8, 8);
		slot.Complete(new PointerMap(42, 8, [new PointerEntry(0x1010, 0x2000)],
			[new PointerModule("game.exe", 0x1000, 0x200)], false, 8, 0));
		PointerFileTools save = new(Files(directory), captured);

		PointerMapFileResult saved = save.SaveMap("before", path,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(path, saved.FilePath);
		Assert.Equal(PointerCaptureCompleteness.Complete, saved.Map.CaptureCompleteness);
		Assert.True(File.Exists(path));
		using PointerStore reopened = new();
		PointerMapFileResult loaded = new PointerFileTools(Files(), reopened).LoadMap("after", path,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.Equal("CE.scandata.v1", loaded.Format);
		Assert.Null(loaded.Map.ProcessId);
		Assert.Null(loaded.Map.JobId);
		JsonElement json = JsonSerializer.SerializeToElement(loaded, PointerJsonContext.Default.PointerMapFileResult);
		Assert.False(json.GetProperty("map").TryGetProperty("jobId", out _));
		Assert.True(loaded.Map.Incomplete);
		Assert.Equal(PointerCaptureCompleteness.Unknown, loaded.Map.CaptureCompleteness);
		PointerMap map = reopened.GetMap("after").GetUsableMap();
		Assert.Equal(new PointerEntry(0x1010, 0x2000), Assert.Single(map.Entries));
		Assert.Equal(new PointerStaticRoot(0, 0x10), map.GetStaticRoot(0x1010));
	}

	[Fact]
	public void SaveAndLoadScan_PreservesContextButInvalidatesOldMatches()
	{
		string directory = _scratch.CreateFolder("scans");
		string path = Path.Combine(directory, "paths.json");
		using PointerStore captured = new();
		PointerScanSlot scan = captured.ReserveScan("before", "first-map", 0x3000, 8, 1);
		scan.End(PointerJobState.Ready,
			new PointerSearchResult([new PointerPath(0x1010, "game.exe", 0x10, [-0x20, 0x40])], false, 2, false),
			false, null);
		PointerScanFileResult saved = new PointerFileTools(Files(directory), captured).SaveScan("before", path,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.Equal("MCP.pointer-scan.v2", saved.Format);

		using PointerStore reopened = new();
		PointerScanFileResult loaded = new PointerFileTools(Files(), reopened).LoadScan("after", path,
			TestContext.Current.CancellationToken);
		Assert.Equal(("first-map", "3000", 1, true),
			(loaded.Scan.MapName, loaded.Scan.Target, loaded.Scan.Count, loaded.Scan.Incomplete));
		Assert.Null(loaded.Scan.JobId);
		JsonElement json = JsonSerializer.SerializeToElement(loaded, PointerJsonContext.Default.PointerScanFileResult);
		Assert.False(json.GetProperty("scan").TryGetProperty("jobId", out _));
		PointerPath restored = Assert.Single(reopened.GetScan("after").GetUsablePaths(out bool incomplete));
		Assert.True(incomplete);
		Assert.Equal([-0x20L, 0x40], restored.Offsets);
		Assert.Equal(PointerVerification.Unresolved, restored.Verification);
	}

	[Fact]
	public void SaveMap_OutsideConfiguredRoot_RefusesWithoutCreatingAFile()
	{
		string directory = _scratch.CreateFolder("outside");
		string path = Path.Combine(directory, "capture.scandata");
		using PointerStore store = ReadyMap();

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
			new PointerFileTools(Files(), store).SaveMap("map", path,
				cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Error.Kind);
		Assert.False(File.Exists(path));
	}

	[Fact]
	public void SaveMap_ExistingFile_RequiresOverwriteAndPreservesOldBytesOnFailure()
	{
		string directory = _scratch.CreateFolder("overwrite");
		string path = Path.Combine(directory, "capture.scandata");
		File.WriteAllBytes(path, [1, 2, 3]);
		using PointerStore store = ReadyMap();
		PointerFileTools tools = new(Files(directory), store);

		Assert.Throws<CheatEngineToolException>(() => tools.SaveMap("map", path,
			cancellationToken: TestContext.Current.CancellationToken));
		Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
		tools.SaveMap("map", path, overwrite: true, cancellationToken: TestContext.Current.CancellationToken);
		Assert.NotEqual([1, 2, 3], File.ReadAllBytes(path));
	}

	[Fact]
	public void LoadMap_MalformedFile_LeavesStoreEmpty()
	{
		string directory = _scratch.CreateFolder("invalid");
		string path = Path.Combine(directory, "bad.scandata");
		File.WriteAllBytes(path, [1, 2, 3]);
		using PointerStore store = new();

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
			new PointerFileTools(Files(), store).LoadMap("bad", path,
				cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Error.Kind);
		Assert.Empty(store.ListMaps());
	}

	public void Dispose() => _scratch.Dispose();

	private McpFilePaths Files(params string[] allowedRoots)
	{
		string registry = _scratch.CreateFolder("registry");
		string data = _scratch.CreateFolder("data");
		return new McpFilePaths(new McpFileOptions { AllowedRoots = allowedRoots }, registry, data);
	}

	private static PointerStore ReadyMap()
	{
		PointerStore store = new();
		PointerMapSlot slot = store.ReserveMap("map", 1);
		slot.Prepare(42, 8, 8);
		slot.Complete(new PointerMap(42, 8, [new PointerEntry(0x1010, 0x2000)],
			[new PointerModule("game.exe", 0x1000, 0x200)], false, 8, 0));
		return store;
	}
}
