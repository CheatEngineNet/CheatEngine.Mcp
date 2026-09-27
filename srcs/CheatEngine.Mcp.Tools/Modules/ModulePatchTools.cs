using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Win32.SafeHandles;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Modules;

/// <summary>
///     The <c>module_find_patches</c> tool: compares a module's sections in memory with its file on disk, after applying
///     the file's base relocations, and reports the differing ranges (hooks, patches, cracks).
/// </summary>
/// <remarks>
///     The module file is read only after <see cref="McpFilePaths" /> accepted the path Cheat Engine reports (a local
///     fixed drive, no UNC or device path, no link), so a crafted module path cannot make the host authenticate to a
///     remote share. Memory is compared in 256 KiB dispatches, each of which first checks that the target selection is
///     unchanged.
/// </remarks>
[McpServerToolType]
public sealed class ModulePatchTools
{
	/// <summary>The largest <c>limit</c>.</summary>
	internal const int MaximumLimit = 1024;

	/// <summary>The most bytes compared by one call.</summary>
	internal const long MaximumComparedBytes = 64 * 1024 * 1024;

	/// <summary>The bytes of memory read by one dispatch.</summary>
	internal const int ChunkBytes = 256 * 1024;

	/// <summary>The largest relocation table read from the file.</summary>
	internal const int MaximumRelocationBytes = 64 * 1024 * 1024;

	private const int PageSize = 4096;

	/// <summary>How far a relocation may reach across a chunk edge: the widest relocation, minus one, rounded up.</summary>
	private const uint RelocationSpill = 8;

	private readonly ToolDispatch _dispatch;
	private readonly McpFilePaths _paths;

	/// <summary>Creates the tool; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="paths">The activation's host-file policy.</param>
	public ModulePatchTools(ToolDispatch dispatch, McpFilePaths paths)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(paths);
		_dispatch = dispatch;
		_paths = paths;
	}

	/// <summary>Finds the ranges where a module's code in memory differs from its file.</summary>
	/// <param name="module">The module name, or an address inside it.</param>
	/// <param name="includeNonExecutable">Whether to compare data sections too.</param>
	/// <param name="limit">The most ranges to report.</param>
	/// <param name="cancellationToken">The request's token.</param>
	/// <returns>The differing ranges.</returns>
	[McpServerTool(Name = CheatEngineToolNames.ModuleFindPatches, Title = "Find module patches", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Compare one module's executable sections in memory with its file on disk, after applying the file's base relocations, and report each differing range with its address, Cheat Engine name, file bytes and memory bytes: hooks, code patches and MCP or Cheat Engine injections. Read-only, in 256 KiB chunks; at most 64 MiB per call. The module's file must be on a local fixed drive (UNC, device and linked paths are refused) and must be the build that was loaded. The import address table is skipped; includeNonExecutable also compares data sections, which a running program changes on its own.")]
	public PatchScanResult FindPatches(
		[Description("The module name, such as game.exe, or an address expression inside it.")]
		string module,
		[Description("Also compare non-executable sections; they change at run time, so expect many differences.")]
		bool includeNonExecutable = false,
		[Description("The most differing ranges to report, 1 to 1024.")]
		int limit = 256,
		CancellationToken cancellationToken = default)
	{
		string wanted = ModuleLocator.RequireModule(module);
		if (limit < 1)
		{
			throw CheatEngineToolException.InvalidArgument("limit", "must be at least 1.");
		}

		if (limit > MaximumLimit)
		{
			throw CheatEngineToolException.LimitExceeded("limit", $"must be at most {MaximumLimit}.");
		}

		const string operation = CheatEngineToolNames.ModuleFindPatches;
		ICheatEngineClient client = _dispatch.Client;
		ScanTarget target = _dispatch.Run(operation, token =>
		{
			ProcessSnapshot process = client.Processes.GetCurrentProcess(token);
			return new ScanTarget(process.Id, process.SelectionEpoch, ModuleLocator.Find(client, wanted, token));
		}, cancellationToken);
		ModuleInfo found = target.Module;
		using HeldFile held = OpenModuleFile(found, operation);
		using SafeFileHandle file = File.OpenHandle(held.FullPath);
		PeHeaders fileHeaders = ParseFileHeaders(found, file, held.Length);

		byte[] memoryHeaderBytes = _dispatch.Run(operation, token =>
		{
			EnsureSameTarget(client, target, token);
			return ModuleLocator.ReadHeaderBytes(client, found, token);
		}, cancellationToken);
		PeHeaders memoryHeaders = ModuleExportTools.ParseOrRefuse(found, memoryHeaderBytes);
		EnsureSameBuild(found, fileHeaders, memoryHeaders);

		ulong baseAddress = found.BaseAddress.ToUInt64();
		ulong delta = unchecked(baseAddress - fileHeaders.ImageBase);
		PeRelocations relocations = delta == 0 ? PeRelocations.None : ReadRelocations(found, file, fileHeaders);
		PeDataDirectory importAddressTable = fileHeaders.Directories[PeHeaders.ImportAddressTableDirectory];
		Func<uint, bool>? excluded = importAddressTable.IsPresent ? importAddressTable.Contains : null;
		ulong mappedSize = ModuleLocator.MappedSize(found, memoryHeaders.SizeOfImage);

		PatchCollector collector = new(limit);
		long compared = 0;
		long examined = 0;
		long unreadable = 0;
		int applied = 0;
		bool truncated = false;
		foreach (PeSection section in fileHeaders.Sections.OrderBy(static section => section.VirtualAddress))
		{
			if (truncated)
			{
				break;
			}

			if (!includeNonExecutable && !section.IsExecutable)
			{
				continue;
			}

			ulong available = Math.Min(section.FileBackedSize,
				(ulong) Math.Max(0, held.Length - section.PointerToRawData));
			ulong sectionEnd = Math.Min(section.VirtualAddress + available, mappedSize);
			for (ulong start = section.VirtualAddress; start < sectionEnd; start += ChunkBytes)
			{
				uint chunkStart = (uint) start;
				uint chunkEnd = (uint) Math.Min(sectionEnd, start + ChunkBytes);
				if (examined + (chunkEnd - chunkStart) > MaximumComparedBytes)
				{
					truncated = true;
					break;
				}

				examined += chunkEnd - chunkStart;

				List<MemorySegment> segments = _dispatch.Run(operation, token =>
				{
					EnsureSameTarget(client, target, token);
					return ReadSegments(client, baseAddress + chunkStart, (int) (chunkEnd - chunkStart), token);
				}, cancellationToken);
				uint windowStart = Math.Max(section.VirtualAddress, chunkStart - Math.Min(chunkStart, RelocationSpill));
				uint windowEnd = (uint) Math.Min(sectionEnd, (ulong) chunkEnd + RelocationSpill);
				byte[] window = ReadFile(file, section.PointerToRawData + (long) (windowStart - section.VirtualAddress),
					(int) (windowEnd - windowStart));
				applied += PeImageReader.ApplyRelocations(window, windowStart, relocations, delta, chunkStart,
					chunkEnd);
				uint next = chunkStart;
				foreach (MemorySegment segment in segments)
				{
					uint rva = chunkStart + segment.Offset;
					unreadable += rva - next;
					int comparedSegment = collector.Compare(rva, section.Name,
						window.AsSpan((int) (rva - windowStart), segment.Bytes.Length), segment.Bytes, excluded);
					compared += comparedSegment;
					next = rva + (uint) segment.Bytes.Length;
				}

				unreadable += chunkEnd - next;
				if (next != chunkEnd)
				{
					collector.Break();
				}

				if (collector.IsFull)
				{
					truncated = true;
					break;
				}
			}

			collector.Break();
		}

		ModulePatch[] patches = Name(client, target, found, collector.Patches, cancellationToken);
		return new PatchScanResult(found.Name, held.FullPath, compared, unreadable, applied, truncated, patches);
	}

	private HeldFile OpenModuleFile(ModuleInfo module, string operation)
	{
		if (string.IsNullOrWhiteSpace(module.PathToFile))
		{
			throw CheatEngineToolException.InvalidState($"Cheat Engine reports no file for {module.Name}.",
				"Only modules loaded from a file can be compared.");
		}

		try
		{
			// Only the handle is kept: it pins the checked file, and the sections are read through a second handle.
			return _paths.OpenRead(module.PathToFile, operation, 0, "module");
		}
		catch (CheatEngineToolException exception) when (exception.Error.Kind is ToolErrorKind.InvalidArgument)
		{
			throw new CheatEngineToolException(
				exception.Error with
				{
					Kind = ToolErrorKind.InvalidState,
					Message = $"The file of {module.Name} is refused: {exception.Error.Message}"
				}, exception);
		}
	}

	private static PeHeaders ParseFileHeaders(ModuleInfo module, SafeFileHandle file, long length)
	{
		try
		{
			return PeImageReader.ParseHeaders(ReadFile(file, 0,
				(int) Math.Min(length, PeImageReader.MaximumHeaderBytes)));
		}
		catch (InvalidDataException exception)
		{
			throw CheatEngineToolException.InvalidState(
				$"The file of {module.Name} is not a PE image this tool can read: {exception.Message}");
		}
	}

	private static void EnsureSameBuild(ModuleInfo module, PeHeaders file, PeHeaders memory)
	{
		bool same = file.TimeDateStamp == memory.TimeDateStamp && file.SizeOfImage == memory.SizeOfImage &&
					file.Sections.Length == memory.Sections.Length &&
					file.Sections.Select(static section => section.VirtualAddress)
						.SequenceEqual(memory.Sections.Select(static section => section.VirtualAddress));
		if (!same)
		{
			throw CheatEngineToolException.InvalidState(
				$"The file of {module.Name} on disk is not the build that was loaded: its time stamp, size or sections differ.",
				"Compare against the file that was loaded, or restart the target after an update.");
		}
	}

	private static PeRelocations ReadRelocations(ModuleInfo module, SafeFileHandle file, PeHeaders headers)
	{
		PeDataDirectory directory = headers.Directories[PeHeaders.BaseRelocationDirectory];
		if (!directory.IsPresent)
		{
			return PeRelocations.None;
		}

		if (directory.Size > MaximumRelocationBytes ||
			!PeImageReader.TryMapToFile(headers, directory.Rva, directory.Size, out long offset))
		{
			throw CheatEngineToolException.InvalidState(
				$"The relocation table of {module.Name} is missing from its file or larger than 64 MiB.");
		}

		try
		{
			return PeImageReader.ParseRelocations(ReadFile(file, offset, (int) directory.Size));
		}
		catch (InvalidDataException exception)
		{
			throw CheatEngineToolException.InvalidState(
				$"The relocation table of {module.Name} is malformed: {exception.Message}");
		}
	}

	private static byte[] ReadFile(SafeFileHandle file, long offset, int length)
	{
		byte[] buffer = new byte[length];
		int read = 0;
		while (read < length)
		{
			int count = RandomAccess.Read(file, buffer.AsSpan(read), offset + read);
			if (count == 0)
			{
				throw new InvalidDataException("The module file ended before the data its headers describe.");
			}

			read += count;
		}

		return buffer;
	}

	private static void EnsureSameTarget(ICheatEngineClient client, ScanTarget target, CancellationToken token)
	{
		ProcessSnapshot current = client.Processes.GetCurrentProcess(token);
		if (current.Id != target.ProcessId || current.SelectionEpoch != target.Epoch)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.TargetChanged,
				"The attached process changed during the comparison.", CheatEngineToolNames.ModuleFindPatches,
				ToolHostEffect.Completed, false, "Re-attach the process, then repeat the comparison."));
		}
	}

	/// <summary>Reads a range, skipping each unreadable 4 KiB page. Inside a dispatch.</summary>
	private static List<MemorySegment> ReadSegments(ICheatEngineClient client, ulong address, int length,
		CancellationToken token)
	{
		List<MemorySegment> segments = [];
		ulong current = address;
		ulong stop = address + (ulong) length;
		while (current < stop)
		{
			MemoryBytesReadOutcome outcome = client.Memory.ReadBytesDetailed(
				new MemoryBytesReadRequest(new Address(current), (int) (stop - current)), token);
			if (outcome.ConfirmedLength > 0)
			{
				segments.Add(new MemorySegment((uint) (current - address), [.. outcome.Bytes]));
			}

			if (outcome.IsSuccess)
			{
				break;
			}

			CheatEngineFailure failure = outcome.Failure!.Value;
			if (failure.Kind is not CheatEngineFailureKind.MemoryReadFailed)
			{
				throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
			}

			ulong failed = current + (ulong) outcome.ConfirmedLength;
			current = (failed + PageSize) & ~(ulong) (PageSize - 1);
		}

		return segments;
	}

	private ModulePatch[] Name(ICheatEngineClient client, ScanTarget target, ModuleInfo module,
		IReadOnlyList<CollectedPatch> patches, CancellationToken cancellationToken)
	{
		if (patches.Count == 0)
		{
			return [];
		}

		ulong baseAddress = module.BaseAddress.ToUInt64();
		return _dispatch.Run(CheatEngineToolNames.ModuleFindPatches, token =>
		{
			EnsureSameTarget(client, target, token);
			return patches.Select(patch =>
			{
				Address address = new(baseAddress + patch.Rva);
				string? symbol = client.Inspection.TryResolveName(address, out string? name,
					out CheatEngineFailure _, token)
					? name
					: null;
				return new ModulePatch(HexFormat.Address(address), patch.Section, patch.Length,
					HexFormat.Bytes(patch.FileBytes), HexFormat.Bytes(patch.MemoryBytes), symbol);
			}).ToArray();
		}, cancellationToken);
	}

	private sealed record ScanTarget(TargetProcessId ProcessId, long Epoch, ModuleInfo Module);

	private sealed record MemorySegment(uint Offset, byte[] Bytes);
}
