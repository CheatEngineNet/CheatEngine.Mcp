using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Builds bounded pointer snapshots and searches them without retaining CE objects.</summary>
[McpServerToolType]
public sealed class PointerScanTool(ICheatEngineClient client)
{
	private const int MaximumMaps = 4;
	private const int MaximumScans = 16;
	private const int MaximumTotalPointers = 2_000_000;
	private readonly Dictionary<string, PointerMap> _maps = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Scan> _scans = new(StringComparer.Ordinal);

	[McpServerTool(Name = "generate_pointer_map")]
	[Description(
		"Capture a bounded pointer snapshot with Client memory APIs. Maps live until deleted or plugin disable; they are not CE .scandata files. Inspect incomplete before relying on absence.")]
	public object GeneratePointerMap([Description("Unique snapshot name, 1-128 characters.")] string mapName,
		[Description("Optional first address expression to capture; requires endAddress.")]
		string? startAddress = null,
		[Description("Optional inclusive last address expression; requires startAddress.")]
		string? endAddress = null,
		[Description("Maximum attempted readable bytes, 1-67108864; default 16 MiB.")]
		int maximumBytes = 16 * 1024 * 1024,
		[Description("Maximum nonzero pointers retained, 1-1000000.")]
		int maximumPointers = 250_000,
		[Description("Pointer storage alignment: 1, 2, 4, or 8. Four also finds 4-aligned x64 pointers.")]
		int alignment = 4)
	{
		return ToolExecution.Run(client, () =>
		{
			ValidateName(mapName);
			if (_maps.ContainsKey(mapName))
			{
				return ToolExecution.Error("Delete the existing pointer map before reusing its name.");
			}

			if (_maps.Count >= MaximumMaps ||
			    maximumPointers > MaximumTotalPointers - _maps.Values.Sum(map => map.Entries.Length))
			{
				return ToolExecution.Error("Pointer map capacity exceeded: at most 4 maps and 2000000 total pointers.");
			}

			if (maximumBytes is < 1 or > 64 * 1024 * 1024 || maximumPointers is < 1 or > 1_000_000 ||
			    alignment is not (1 or 2 or 4 or 8))
			{
				return ToolExecution.Error("Invalid byte, pointer, or alignment limit.");
			}

			if (startAddress is null != endAddress is null)
			{
				return ToolExecution.Error("startAddress and endAddress must be supplied together.");
			}

			ProcessSnapshot process = client.Processes.GetCurrentProcess();
			if (!process.Bitness.IsKnown)
			{
				return ToolExecution.Error("The target pointer width is unknown.");
			}

			int width = process.Bitness.Bytes;
			ulong start = startAddress is null ? 0 : ToolExecution.Address(client, startAddress).Value;
			ulong end = endAddress is null
				? width == 4 ? uint.MaxValue : ulong.MaxValue
				: ToolExecution.Address(client, endAddress).Value;
			if (end < start || (width == 4 && end > uint.MaxValue))
			{
				return ToolExecution.Error("The capture range is reversed or exceeds the target address width.");
			}

			ImmutableArray<MemoryRegionInfo> regions =
				client.Inspection.GetMemoryRegions(new InspectionCollectionRequest(16_384));
			PointerModule[] modules = ReadModules(regions);
			List<PointerEntry> entries = [];
			ulong bytesRead = 0;
			ulong unreadableBytes = 0;
			int attempted = 0;
			bool limited = false;
			Stopwatch clock = Stopwatch.StartNew();
			foreach (MemoryRegionInfo region in regions.OrderBy(region => region.BaseAddress.Value))
			{
				// Do not touch guard pages: reading one can change the target's exception behavior.
				uint protection = (uint) region.Protection;
				if (region.State != MemoryRegionState.Committed || (protection & 0x101) != 0 ||
				    (protection & 0xEE) == 0 || region.Size.Value == 0)
				{
					continue;
				}

				ulong regionEnd = region.BaseAddress.Value > ulong.MaxValue - (region.Size.Value - 1)
					? ulong.MaxValue
					: region.BaseAddress.Value + region.Size.Value - 1;
				ulong address = Math.Max(start, region.BaseAddress.Value);
				ulong last = Math.Min(end, regionEnd);
				while (address <= last && last - address >= (ulong) width - 1)
				{
					client.Stopping.ThrowIfCancellationRequested();
					if (attempted >= maximumBytes || entries.Count >= maximumPointers ||
					    clock.Elapsed > TimeSpan.FromSeconds(5))
					{
						limited = true;
						break;
					}

					int length = (int) Math.Min(Math.Min(last - address, 65535) + 1,
						(ulong) (maximumBytes - attempted));
					if (length < width)
					{
						limited = true;
						break;
					}

					MemoryBytesReadOutcome read =
						client.Memory.ReadBytesDetailed(new MemoryBytesReadRequest(new Address(address), length));
					if (read.Failure is { Kind: not CheatEngineFailureKind.MemoryReadFailed } failure)
					{
						return ToolExecution.Failure(failure);
					}

					attempted += length;
					bytesRead += (ulong) read.ConfirmedLength;
					unreadableBytes += (ulong) (length - read.ConfirmedLength);
					PointerMap.CopyEntries(entries, address, read.Bytes.AsSpan(), width, alignment, maximumPointers);
					// Overlap just enough bytes to include an unaligned pointer straddling two successful chunks.
					ulong advance = read.IsSuccess && address + (ulong) length - 1 < last
						? (ulong) (length - width + 1)
						: (ulong) length;
					if (address > ulong.MaxValue - advance)
					{
						break;
					}

					address += advance;
				}

				if (limited)
				{
					break;
				}
			}

			ProcessSnapshot after = client.Processes.GetCurrentProcess();
			if (after != process)
			{
				return ToolExecution.Error("Target selection changed during capture; the snapshot was discarded.");
			}

			PointerMap snapshot = new(process, width, entries.ToArray(), modules,
				limited || unreadableBytes != 0 || entries.Count >= maximumPointers, bytesRead, unreadableBytes);
			_maps.Add(mapName, snapshot);
			return new
			{
				success = true,
				map = DescribeMap(mapName, snapshot),
				limited,
				rangeStart = Hex(start),
				rangeEnd = Hex(end)
			};
		});
	}

	[McpServerTool(Name = "pointer_scan")]
	[Description(
		"Find pointer chains in a captured map using nonnegative offsets. Results and traversal are bounded; module-relative roots support later rebasing.")]
	public object PointerScan([Description("Unique scan name, 1-128 characters.")] string scannerName,
		[Description("Existing pointer map name.")]
		string mapName,
		[Description("Hexadecimal destination address in the snapshot, with optional 0x prefix.")]
		string targetAddress,
		[Description("Maximum pointer dereferences, 1-8.")]
		int maximumDepth = 5,
		[Description("Largest nonnegative offset at each hop, 0-1048576.")]
		int maximumOffset = 4096,
		[Description("Only return roots inside known modules.")]
		bool moduleRootsOnly = true,
		[Description("Maximum retained paths, 1-10000.")]
		int maximumResults = 1000,
		[Description("Maximum visited candidates, 1-1000000.")]
		int maximumNodes = 100_000)
	{
		return ToolExecution.Run(client, () =>
		{
			ValidateName(scannerName);
			if (_scans.ContainsKey(scannerName) || _scans.Count >= MaximumScans)
			{
				return ToolExecution.Error(
					"Scan name already exists or the 16-scan limit was reached; reset a pointer scan first.");
			}

			if (maximumDepth is < 1 or > 8 || maximumOffset is < 0 or > 1_048_576 ||
			    maximumResults is < 1 or > 10_000 || maximumNodes is < 1 or > 1_000_000)
			{
				return ToolExecution.Error("Invalid depth, offset, result, or traversal limit.");
			}

			PointerMap map = GetMap(mapName);
			ulong target = ParseAddress(targetAddress, map.Width);
			PointerSearchResult result = map.Search(target, maximumDepth, maximumOffset, moduleRootsOnly,
				maximumResults, maximumNodes, client.Stopping);
			Scan scan = new(map.Width, result.Paths, result.Truncated || map.Incomplete);
			_scans.Add(scannerName, scan);
			return new
			{
				success = true,
				scannerName,
				count = scan.Paths.Length,
				incomplete = scan.Incomplete,
				traversalLimited = result.Truncated,
				visitedNodes = result.VisitedNodes
			};
		});
	}

	[McpServerTool(Name = "rescan_pointer_scan")]
	[Description(
		"Filter existing paths against a new map or live Client pointer-chain reads. Module roots rebase; absolute roots remain absolute. Unresolved paths are retained and counted; interrupted scans keep original results.")]
	public object RescanPointerScan([Description("Existing pointer scan name.")] string scannerName,
		[Description("New hexadecimal destination address, with optional 0x prefix.")]
		string targetAddress,
		[Description("New snapshot name, or omit to read the currently selected process.")]
		string? mapName = null)
	{
		return ToolExecution.Run(client, () =>
		{
			Scan scan = GetScan(scannerName);
			ulong target = ParseAddress(targetAddress, scan.Width);
			PointerMap? map = mapName is null ? null : GetMap(mapName);
			ProcessSnapshot? process = map is null ? client.Processes.GetCurrentProcess() : null;
			if ((map?.Width ?? process!.Value.Bitness.Bytes) != scan.Width)
			{
				return ToolExecution.Error("The rescan pointer width differs from the original scan.");
			}

			PointerModule[] modules = map?.Modules ??
			                          ReadModules(
				                          client.Inspection.GetMemoryRegions(new InspectionCollectionRequest(16_384)));
			List<PointerPath> retained = [];
			int unresolved = 0;
			Stopwatch clock = Stopwatch.StartNew();
			foreach (PointerPath path in scan.Paths)
			{
				client.Stopping.ThrowIfCancellationRequested();
				if (clock.Elapsed > TimeSpan.FromSeconds(5))
				{
					return ToolExecution.Error(
						"Rescan exceeded five seconds; the original results were retained. Use a new pointer map to rescan offline.");
				}

				bool resolved;
				ulong destination;
				if (map is not null)
				{
					resolved = map.TryResolve(path, out destination);
				}
				else if (PointerMap.TryRebase(path, modules, out ulong address))
				{
					resolved = client.Memory.TryResolvePointerChain(
						new PointerChainRequest(new Address(address), path.Offsets), out Address value,
						out CheatEngineFailure failure);
					if (!resolved &&
					    failure.Kind is not (CheatEngineFailureKind.MemoryReadFailed
						    or CheatEngineFailureKind.NotFound))
					{
						return ToolExecution.Failure(failure);
					}

					destination = value.Value;
				}
				else
				{
					resolved = false;
					destination = 0;
				}

				if (!resolved)
				{
					unresolved++;
					// A missing snapshot entry or unreadable live hop is not proof that this chain is wrong.
					retained.Add(path with { Verification = "unresolved" });
				}
				else if (destination == target)
				{
					retained.Add(path with { Verification = map is null ? "liveMatch" : "snapshotMatch" });
				}
			}

			if (process is not null && process.Value != client.Processes.GetCurrentProcess())
			{
				return ToolExecution.Error(
					"Target selection changed during rescan; the original results were retained.");
			}

			Scan updated = new(scan.Width, retained.ToArray(),
				scan.Incomplete || map?.Incomplete == true || unresolved != 0);
			_scans[scannerName] = updated;
			return new
			{
				success = true,
				scannerName,
				count = updated.Paths.Length,
				verifiedMatches = updated.Paths.Length - unresolved,
				removed = scan.Paths.Length - updated.Paths.Length,
				unresolved,
				incomplete = updated.Incomplete
			};
		});
	}

	[McpServerTool(Name = "get_pointer_scan_results")]
	[Description(
		"Page stored pointer chains. Offsets are in dereference order; CE's address-list offset order is reversed.")]
	public object GetPointerScanResults([Description("Existing pointer scan name.")] string scannerName,
		[Description("Zero-based first result.")]
		int startIndex = 0,
		[Description("Maximum copied paths, 1-1024.")]
		int maximumResults = 100)
	{
		return ToolExecution.Run(client, () =>
		{
			if (startIndex < 0 || maximumResults is < 1 or > 1024)
			{
				return ToolExecution.Error("startIndex must be nonnegative and maximumResults must be 1-1024.");
			}

			Scan scan = GetScan(scannerName);
			object[] results = scan.Paths.Skip(startIndex).Take(maximumResults).Select(path => (object) new
			{
				baseAddress = Hex(path.BaseAddress),
				module = path.Module,
				moduleOffset = Hex(path.ModuleOffset),
				offsets = path.Offsets,
				addressListOffsets = path.Offsets.Reverse().ToArray(),
				verification = path.Verification
			}).ToArray();
			return new
			{
				success = true,
				count = scan.Paths.Length,
				results,
				nextStartIndex = (long) startIndex + results.Length,
				hasMore = (long) startIndex + results.Length < scan.Paths.Length,
				incomplete = scan.Incomplete
			};
		});
	}

	[McpServerTool(Name = "list_pointer_maps")]
	[Description("List bounded MCP-owned snapshots and their capture completeness.")]
	public object ListPointerMaps()
	{
		return ToolExecution.Run(client,
			() => new { success = true, maps = _maps.Select(pair => DescribeMap(pair.Key, pair.Value)).ToArray() });
	}

	[McpServerTool(Name = "delete_pointer_map")]
	[Description("Delete an MCP pointer snapshot. Existing copied scan paths remain usable.")]
	public object DeletePointerMap([Description("Existing snapshot name.")] string mapName)
	{
		return ToolExecution.Run(client, () =>
			_maps.Remove(mapName)
				? new { success = true, mapName }
				: ToolExecution.Error("No pointer map exists with that name."));
	}

	[McpServerTool(Name = "reset_pointer_scan")]
	[Description("Release an MCP-owned pointer result list.")]
	public object ResetPointerScan([Description("Existing scan name.")] string scannerName)
	{
		return ToolExecution.Run(client, () =>
			_scans.Remove(scannerName)
				? new { success = true, scannerName }
				: ToolExecution.Error("No pointer scan exists with that name."));
	}

	private PointerModule[] ReadModules(ImmutableArray<MemoryRegionInfo> regions)
	{
		return client.Inspection.GetModules(new InspectionCollectionRequest(4096))
			.Select(module => new PointerModule(module.Name, module.BaseAddress.Value, module.ImageSize?.Value ??
				regions
					.Where(region =>
						region.AllocationBase == module.BaseAddress &&
						region.BaseAddress.Value >= module.BaseAddress.Value &&
						region.BaseAddress.Value - module.BaseAddress.Value <= ulong.MaxValue - region.Size.Value)
					.Select(region => region.BaseAddress.Value - module.BaseAddress.Value + region.Size.Value)
					.DefaultIfEmpty().Max()))
			.Where(module => module.Size != 0).ToArray();
	}

	private PointerMap GetMap(string name)
	{
		return _maps.TryGetValue(name, out PointerMap? map)
			? map
			: throw new ArgumentException("No pointer map exists with that name.");
	}

	private Scan GetScan(string name)
	{
		return _scans.TryGetValue(name, out Scan? scan)
			? scan
			: throw new ArgumentException("No pointer scan exists with that name.");
	}

	private static void ValidateName(string name)
	{
		if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
		{
			throw new ArgumentException("Names must contain 1-128 characters.");
		}
	}

	private static ulong ParseAddress(string text, int width)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(text);
		ReadOnlySpan<char> digits = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
			? text.AsSpan(2)
			: text.AsSpan();
		if (!ulong.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value) ||
		    (width == 4 && value > uint.MaxValue))
		{
			throw new ArgumentException("targetAddress must be hexadecimal and fit the target pointer width.");
		}

		return value;
	}

	private static string Hex(ulong address)
	{
		return $"0x{address:X}";
	}

	private static object DescribeMap(string name, PointerMap map)
	{
		return new
		{
			name,
			processId = map.Process.Id.Value,
			pointerSize = map.Width,
			pointers = map.Entries.Length,
			bytesRead = map.BytesRead,
			unreadableBytes = map.UnreadableBytes,
			incomplete = map.Incomplete
		};
	}

	private sealed record Scan(int Width, PointerPath[] Paths, bool Incomplete);
}
