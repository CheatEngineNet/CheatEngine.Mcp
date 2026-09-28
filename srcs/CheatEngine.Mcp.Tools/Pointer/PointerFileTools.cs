using System.ComponentModel;
using System.Text;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>Exchanges bounded pointer maps and path scans with local host files.</summary>
[McpServerToolType]
public sealed class PointerFileTools
{
	private readonly McpFilePaths _files;
	private readonly PointerStore _store;

	/// <summary>Creates the file tools without touching the target or filesystem.</summary>
	public PointerFileTools(McpFilePaths files, PointerStore store)
	{
		_files = files ?? throw new ArgumentNullException(nameof(files));
		_store = store ?? throw new ArgumentNullException(nameof(store));
	}

	/// <summary>Saves a ready map in CE's native pointer-map format.</summary>
	[McpServerTool(Name = CheatEngineToolNames.PointerSaveMap, Title = "Save pointer map", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Exports a ready pointer map as a native CE version-1 .scandata file. The destination must be a local file below Mcp:Files:AllowedRoots. A temporary file is committed atomically; overwrite must be explicit. Native maps cannot represent capture completeness, and empty or very large maps cannot be exported.")]
	public PointerMapFileResult SaveMap(
		[Description("The retained pointer map.")] string mapName,
		[Description("Absolute local .scandata path in an existing allowed write directory.")] string path,
		[Description("Replace an existing file atomically.")] bool overwrite = false,
		CancellationToken cancellationToken = default)
	{
		PointerMapSlot slot = _store.GetMap(PointerSupport.Name(mapName, "mapName"));
		PointerMap map = slot.GetUsableMap();
		string full = CheckExtension(_files.RequireWrite(path, CheatEngineToolNames.PointerSaveMap), ".scandata");
		using McpFileWrite write = _files.BeginWrite(full, CheatEngineToolNames.PointerSaveMap, overwrite);
		try
		{
			NativePointerMapFile.Save(write.Stream, map, cancellationToken);
		}
		catch (Exception exception) when (exception is InvalidDataException or EncoderFallbackException)
		{
			throw CheatEngineToolException.InvalidState($"The map cannot be represented as CE .scandata: {exception.Message}");
		}

		cancellationToken.ThrowIfCancellationRequested();
		write.Commit();
		return new PointerMapFileResult(write.FullPath, "CE.scandata.v1", slot.Describe());
	}

	/// <summary>Loads a native CE pointer map for offline searches or rescans.</summary>
	[McpServerTool(Name = CheatEngineToolNames.PointerLoadMap, Title = "Load pointer map", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Imports a local CE version-1 .scandata map without attaching to its old target. The compressed file is limited to 64 MiB and at most one million pointer addresses; a malformed or oversized file leaves the store unchanged. Native capture completeness is unknown. Search it offline, then rescan paths against current memory.")]
	public PointerMapFileResult LoadMap(
		[Description("A new map name, 1 to 128 characters.")] string mapName,
		[Description("Absolute local CE .scandata path.")] string path,
		[Description("Maximum pointer addresses accepted, 1 to 1000000.")] int maximumPointers = NativePointerMapFile.MaximumEntries,
		CancellationToken cancellationToken = default)
	{
		string name = PointerSupport.Name(mapName, "mapName");
		PointerSupport.Range(maximumPointers, 1, NativePointerMapFile.MaximumEntries, "maximumPointers");
		string full = CheckExtension(_files.RequireRead(path, CheatEngineToolNames.PointerLoadMap), ".scandata");
		using HeldFile file = _files.OpenRead(full, CheatEngineToolNames.PointerLoadMap, 0);
		RequireLength(file, NativePointerMapFile.MaximumFileBytes);
		using PinnedPointerFileStream input = new(file);
		PointerMap map;
		try
		{
			map = NativePointerMapFile.Load(input, maximumPointers, cancellationToken);
		}
		catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or DecoderFallbackException)
		{
			throw CheatEngineToolException.InvalidArgument("path", $"is not a supported CE pointer map: {exception.Message}");
		}

		cancellationToken.ThrowIfCancellationRequested();
		PointerMapSlot slot = _store.ReserveMap(name, map.Entries.Length);
		try
		{
			slot.LoadImportedMap(map);
			return new PointerMapFileResult(file.FullPath, "CE.scandata.v1", slot.Describe());
		}
		catch
		{
			_store.RemoveMap(slot);
			throw;
		}
	}

	/// <summary>Saves the current paths and context as versioned MCP JSON.</summary>
	[McpServerTool(Name = CheatEngineToolNames.PointerSaveScan, Title = "Save pointer scan", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Saves a completed pointer scan as MCP pointer-scan JSON v2, preserving signed offsets, root modules, map name and target. The destination must be below Mcp:Files:AllowedRoots. The file is committed atomically; overwrite must be explicit. This is not a CE .ptr file or a paused native scan queue.")]
	public PointerScanFileResult SaveScan(
		[Description("The retained pointer scan.")] string scanName,
		[Description("Absolute local .json path in an existing allowed write directory.")] string path,
		[Description("Replace an existing file atomically.")] bool overwrite = false,
		CancellationToken cancellationToken = default)
	{
		PointerScanSlot slot = _store.GetScan(PointerSupport.Name(scanName, "scanName"));
		PointerScanSnapshot snapshot = slot.SnapshotForSave();
		string full = CheckExtension(_files.RequireWrite(path, CheatEngineToolNames.PointerSaveScan), ".json");
		using McpFileWrite write = _files.BeginWrite(full, CheatEngineToolNames.PointerSaveScan, overwrite);
		try
		{
			PointerScanFile.Save(write.Stream, snapshot, cancellationToken);
		}
		catch (InvalidDataException exception)
		{
			throw CheatEngineToolException.InvalidState($"The scan cannot be saved: {exception.Message}");
		}

		cancellationToken.ThrowIfCancellationRequested();
		write.Commit();
		return new PointerScanFileResult(write.FullPath, "MCP.pointer-scan.v2", slot.Describe());
	}

	/// <summary>Restores a saved scan without trusting its paths as current matches.</summary>
	[McpServerTool(Name = CheatEngineToolNames.PointerLoadScan, Title = "Load pointer scan", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Loads bounded MCP pointer-scan JSON v1 or v2 under a new name. All restored paths start unresolved, even if they previously matched; rescan against a new map or current target before relying on them. Parsing errors leave the store unchanged.")]
	public PointerScanFileResult LoadScan(
		[Description("A new scan name, 1 to 128 characters.")] string scanName,
		[Description("Absolute local MCP pointer-scan .json path.")] string path,
		CancellationToken cancellationToken = default)
	{
		string name = PointerSupport.Name(scanName, "scanName");
		string full = CheckExtension(_files.RequireRead(path, CheatEngineToolNames.PointerLoadScan), ".json");
		using HeldFile file = _files.OpenRead(full, CheatEngineToolNames.PointerLoadScan, 0);
		RequireLength(file, PointerScanFile.MaximumFileBytes);
		using PinnedPointerFileStream input = new(file);
		PointerScanSnapshot snapshot;
		try
		{
			snapshot = PointerScanFile.Load(input, cancellationToken);
		}
		catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or JsonException or ArgumentException)
		{
			throw CheatEngineToolException.InvalidArgument("path", $"is not a supported pointer scan: {exception.Message}");
		}

		cancellationToken.ThrowIfCancellationRequested();
		PointerScanSlot slot = _store.ReserveScan(name, snapshot.MapName, snapshot.Target, snapshot.Width,
			snapshot.Paths.Length);
		try
		{
			slot.LoadImportedSnapshot(snapshot);
			return new PointerScanFileResult(file.FullPath, "MCP.pointer-scan", slot.Describe());
		}
		catch
		{
			_store.RemoveScan(slot);
			throw;
		}
	}

	private static string CheckExtension(string full, string expected)
	{
		if (!string.Equals(Path.GetExtension(full), expected, StringComparison.OrdinalIgnoreCase))
		{
			throw CheatEngineToolException.InvalidArgument("path", $"must end in {expected}.");
		}

		return full;
	}

	private static void RequireLength(HeldFile file, int maximum)
	{
		if (file.Length is <= 0 || file.Length > maximum)
		{
			throw CheatEngineToolException.LimitExceeded("path", $"file length must be 1 to {maximum} bytes.");
		}
	}
}
