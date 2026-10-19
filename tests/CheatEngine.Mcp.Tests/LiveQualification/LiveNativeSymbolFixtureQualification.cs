using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>
/// Draft-only live helper for one fixed x64 DLL/PDB pair. It intentionally makes no target-side call after injection.
/// Integrate only with the dedicated SymbolFixture admission described beside this file.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class LiveNativeSymbolFixtureQualification
{
	private static readonly ExpectedField[] ExpectedLayout =
	[
		new("Magic", "0", "uint32"),
		// StructureTools intentionally loses PDB signed-display metadata, so the word is exposed as uint16.
		new("Delta", "4", "uint16"),
		new("Flags", "6", "uint16"),
		new("Counter", "8", "uint64")
	];

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance,
		NativeSymbolFixture artifact)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(artifact);
		artifact.VerifyBeforeHostStart();
		await AssertOwnedTargetAttachedAsync(sandbox, instance);
		await AssertGatesAsync(instance);

		// Admission is the irreversible boundary. The owning scenario must switch to target-first teardown before this call.
		LiveMcpToolResult injection = await instance.CallToolRawAsync(CheatEngineToolNames.ExecInjectLibrary,
			new Dictionary<string, object?> { ["libraryPath"] = artifact.DllPath, ["skipSymbolReloadWait"] = true });
		if (injection.IsError)
		{
			throw new InvalidOperationException("The fixed native fixture injection was refused: " + injection.ErrorText);
		}

		JsonNode injected = injection.Payload ?? throw new InvalidDataException("Native fixture injection returned no payload.");
		Assert.Equal(artifact.DllPath, Text(injected, "libraryPath"), ignoreCase: true);
		Assert.True(injected["skippedSymbolReloadWait"]!.GetValue<bool>());

		LoadedFixtureModule osModule = RequireOsModule(sandbox.HostA.TargetProcessId, artifact);
		JsonNode mcpModule = await RequireMcpModuleAsync(instance, osModule);
		await VerifyExportsAsync(instance, artifact, osModule, mcpModule);
		sandbox.Record("all_tools_exec_inject_library", new
		{
			path = artifact.DllPath,
			artifact.DllSha256,
			baseAddress = Hex(osModule.BaseAddress),
			imageSize = osModule.ImageSize,
			artifact.CodeView,
			injected = injected.DeepClone()
		});

		await VerifyDiscriminatingReloadAsync(sandbox, instance, artifact, osModule, Text(mcpModule, "name"));
		await VerifyPdbLayoutAsync(sandbox, instance, artifact, osModule);
	}

	private static async Task AssertOwnedTargetAttachedAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		JsonNode overview = await CallAsync(instance, CheatEngineToolNames.RuntimeGetOverview);
		Assert.Equal(sandbox.HostA.TargetProcessId, overview["process"]!["processId"]!.GetValue<int>());
	}

	private static async Task AssertGatesAsync(LiveMcpInstanceClient instance)
	{
		JsonNode runtime = await CallAsync(instance, CheatEngineToolNames.RuntimeGetInfo);
		JsonNode gates = runtime["gates"] ?? throw new InvalidDataException("The native fixture host did not report gates.");
		Assert.True(gates["targetCodeExecution"]!.GetValue<bool>());
		Assert.False(gates["unsafeLua"]!.GetValue<bool>());
		Assert.False(gates["autoAssembler"]!.GetValue<bool>());
		Assert.False(gates["kernelAccess"]!.GetValue<bool>());
	}

	private static LoadedFixtureModule RequireOsModule(int targetProcessId, NativeSymbolFixture artifact)
	{
		using Process target = Process.GetProcessById(targetProcessId);
		ProcessModule loaded = Assert.Single(target.Modules.Cast<ProcessModule>(), module =>
			string.Equals(Path.GetFullPath(module.FileName), artifact.DllPath, StringComparison.OrdinalIgnoreCase));
		Assert.Equal(artifact.DllSha256, Hash(loaded.FileName), ignoreCase: true);
		Assert.True(loaded.ModuleMemorySize > 0);
		return new LoadedFixtureModule((ulong) loaded.BaseAddress.ToInt64(), loaded.ModuleMemorySize, loaded.ModuleName, loaded.FileName);
	}

	private static async Task<JsonNode> RequireMcpModuleAsync(LiveMcpInstanceClient instance, LoadedFixtureModule expected)
	{
		JsonNode modules = await CallAsync(instance, CheatEngineToolNames.ModuleList,
			new Dictionary<string, object?> { ["nameContains"] = expected.Name, ["limit"] = 100, ["format"] = "detailed" });
		JsonNode actual = Assert.Single(modules["modules"]!.AsArray(), candidate =>
			string.Equals(Text(candidate!, "path"), expected.Path, StringComparison.OrdinalIgnoreCase) &&
			Address(Text(candidate!, "base")) == expected.BaseAddress)!;
		Assert.True(actual["is64Bit"]!.GetValue<bool>());
		Assert.Equal(expected.ImageSize, actual["size"]!.GetValue<long>());
		return actual;
	}

	private static async Task VerifyExportsAsync(LiveMcpInstanceClient instance, NativeSymbolFixture artifact,
		LoadedFixtureModule loaded, JsonNode mcpModule)
	{
		JsonNode exports = await CallAsync(instance, CheatEngineToolNames.ModuleListExports, new Dictionary<string, object?>
		{
			["module"] = Text(mcpModule, "name"),
			["limit"] = 100
		});
		Assert.Equal(Text(mcpModule, "name"), Text(exports, "module"));
		foreach ((string name, uint rva) in artifact.ExportRvas)
		{
			JsonNode entry = Assert.Single(exports["exports"]!.AsArray(), item => Text(item!, "name") == name)!;
			Assert.Equal(loaded.BaseAddress + rva, Address(Text(entry, "address")));
			Assert.Null(entry["forwarder"]);
		}
	}

	private static async Task VerifyDiscriminatingReloadAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance,
		NativeSymbolFixture artifact, LoadedFixtureModule loaded, string moduleName)
	{
		using CancellationTokenSource reloadDeadline = new(TimeSpan.FromSeconds(10));
		LiveMcpInstanceClient bounded = instance.WithCancellation(reloadDeadline.Token);
		JsonNode before = await FindTickAsync(bounded, moduleName);
		if (!before["symbolsLoaded"]!.GetValue<bool>() || before["symbols"]!.AsArray().Count != 0)
		{
			sandbox.Record("symbol_reload_inconclusive_auto_discovery", new
			{
				before = before.DeepClone(),
				moduleName
			});
			return; // Injection already made the export visible, or CE is still loading; reload is not discriminating.
		}

		JsonNode started = await CallAsync(bounded, CheatEngineToolNames.SymbolReload,
			new Dictionary<string, object?> { ["scope"] = "new_modules" });
		Assert.Equal("new_modules", Text(started, "scope"));
		JsonNode? final = null;
		while (!reloadDeadline.IsCancellationRequested)
		{
			JsonNode observed = await FindTickAsync(bounded, moduleName);
			if (observed["symbolsLoaded"]!.GetValue<bool>() && observed["symbols"]!.AsArray().Count != 0)
			{
				final = observed;
				break;
			}
			await Task.Delay(100, reloadDeadline.Token);
		}
		JsonNode found = final ?? throw new TimeoutException("symbol_reload did not produce a loaded owned export within ten seconds.");
		Assert.True(found["symbolsLoaded"]!.GetValue<bool>());
		JsonNode tick = Assert.Single(found["symbols"]!.AsArray(), item =>
			string.Equals(Text(item!, "name"), "CemcpFixtureTick", StringComparison.OrdinalIgnoreCase))!;
		Assert.Equal(loaded.BaseAddress + artifact.ExportRvas["CemcpFixtureTick"], Address(Text(tick, "address")));
		sandbox.Record("all_tools_symbol_reload", new
		{
			started = started.DeepClone(),
			before = before.DeepClone(),
			after = found.DeepClone()
		});
	}

	private static async Task<JsonNode> FindTickAsync(LiveMcpInstanceClient instance, string moduleName) =>
		await CallAsync(instance, CheatEngineToolNames.SymbolFind, new Dictionary<string, object?>
		{
			["nameContains"] = "CemcpFixtureTick",
			["module"] = moduleName,
			["limit"] = 20
		});

	private static async Task VerifyPdbLayoutAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance,
		NativeSymbolFixture artifact, LoadedFixtureModule loaded)
	{
		JsonNode before = await CallAsync(instance, CheatEngineToolNames.StructureGetPdbLayout,
			new Dictionary<string, object?> { ["typeName"] = "CemcpPdbFixtureLayout", ["limit"] = ExpectedLayout.Length });
		if (before["found"]!.GetValue<bool>())
		{
			sandbox.Record("symbol_add_module_inconclusive_existing_layout", new
			{
				before = before.DeepClone()
			});
			return; // The explicit load has no observable effect in this CE session.
		}
		Assert.Empty(before["elements"]!.AsArray());
		Assert.False(before["truncated"]!.GetValue<bool>());

		string baseAddress = "0x" + Hex(loaded.BaseAddress);
		JsonNode added = await CallAsync(instance, CheatEngineToolNames.SymbolAddModule, new Dictionary<string, object?>
		{
			["path"] = artifact.PdbPath,
			["baseAddress"] = baseAddress,
			["enumStructures"] = true
		});
		Assert.Equal(artifact.PdbPath, Text(added, "path"), ignoreCase: true);
		Assert.Equal(Hex(loaded.BaseAddress), Text(added, "base"));
		Assert.True(added["enumStructures"]!.GetValue<bool>());

		JsonNode layout = await CallAsync(instance, CheatEngineToolNames.StructureGetPdbLayout,
			new Dictionary<string, object?> { ["typeName"] = "CemcpPdbFixtureLayout", ["limit"] = ExpectedLayout.Length });
		Assert.Equal("CemcpPdbFixtureLayout", Text(layout, "typeName"));
		Assert.True(layout["found"]!.GetValue<bool>());
		Assert.False(layout["truncated"]!.GetValue<bool>());
		JsonArray fields = layout["elements"]!.AsArray();
		Assert.Equal(ExpectedLayout.Length, fields.Count);
		for (int index = 0; index < ExpectedLayout.Length; index++)
		{
			ExpectedField expected = ExpectedLayout[index];
			Assert.Equal((expected.Name, expected.Offset, expected.ValueType),
				(Text(fields[index], "name"), Text(fields[index], "offset"), Text(fields[index], "valueType")));
		}
		sandbox.Record("all_tools_symbol_add_module", new
		{
			added = added.DeepClone(),
			artifact.PdbSha256,
			artifact.CodeView
		});
		sandbox.Record("all_tools_structure_get_pdb_layout", layout.DeepClone());
	}

	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool,
		IReadOnlyDictionary<string, object?>? arguments = null) =>
		await instance.CallToolAsync(tool, arguments) ?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
	private static string Text(JsonNode? node, string property) => node?[property]?.GetValue<string>() ?? throw new InvalidDataException($"Missing string '{property}'.");
	private static ulong Address(string text) => ulong.Parse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text,
		NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
	private static string Hex(ulong address) => address.ToString("X", CultureInfo.InvariantCulture);
	private static string Hash(string path)
	{
		using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (input.Length is <= 0 or > 1024 * 1024)
		{
			throw new InvalidDataException("The owned mapped fixture image exceeds its reviewed bound.");
		}
		return Convert.ToHexString(SHA256.HashData(input));
	}

	private sealed record ExpectedField(string Name, string Offset, string ValueType);
	private sealed record LoadedFixtureModule(ulong BaseAddress, long ImageSize, string Name, string Path);
}

/// <summary>Closed-world artifact facts loaded from the run-owned build manifest before CE starts.</summary>
[SupportedOSPlatform("windows")]
internal sealed record NativeSymbolFixture(string SourcePath, string DllPath, string PdbPath, string SourceSha256,
	string DllSha256, string PdbSha256, long DllLength, long PdbLength, NativeSymbolCodeView CodeView,
	IReadOnlyDictionary<string, uint> ExportRvas)
{
	private const long MaximumSourceBytes = 64 * 1024;
	private const long MaximumDllBytes = 1024 * 1024;
	private const long MaximumPdbBytes = 16 * 1024 * 1024;
	private static readonly string[] RequiredExports = ["CemcpFixtureLayoutSize", "CemcpFixtureTick"];

	internal void VerifyBeforeHostStart()
	{
		if (!Path.IsPathFullyQualified(SourcePath) || !Path.IsPathFullyQualified(DllPath) || !Path.IsPathFullyQualified(PdbPath) ||
			!File.Exists(SourcePath) || !File.Exists(DllPath) || !File.Exists(PdbPath))
		{
			throw new InvalidDataException("The run-owned native symbol fixture paths or hashes changed before CE startup.");
		}
		long sourceLength = new FileInfo(SourcePath).Length;
		if (sourceLength is <= 0 or > MaximumSourceBytes || new FileInfo(DllPath).Length != DllLength ||
			new FileInfo(PdbPath).Length != PdbLength || DllLength is <= 0 or > MaximumDllBytes || PdbLength is <= 0 or > MaximumPdbBytes ||
			!ExportRvas.Keys.Order(StringComparer.Ordinal).SequenceEqual(RequiredExports.Order(StringComparer.Ordinal), StringComparer.Ordinal))
		{
			throw new InvalidDataException("The native fixture exceeded a reviewed bound or has a different export set.");
		}
		if (HashBounded(SourcePath, sourceLength, MaximumSourceBytes) != SourceSha256 ||
			HashBounded(DllPath, DllLength, MaximumDllBytes) != DllSha256 ||
			HashBounded(PdbPath, PdbLength, MaximumPdbBytes) != PdbSha256)
		{
			throw new InvalidDataException("The run-owned native symbol fixture bytes changed before CE startup.");
		}
		using FileStream image = File.OpenRead(DllPath);
		using PEReader pe = new(image);
		if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64)
		{
			throw new InvalidDataException("The native symbol fixture is not an AMD64 PE image.");
		}
		CodeViewDebugDirectoryData codeView = pe.ReadDebugDirectory().Select(entry => new { entry, entry.Type })
			.Where(value => value.Type == DebugDirectoryEntryType.CodeView).Select(value => pe.ReadCodeViewDebugDirectoryData(value.entry)).Single();
		NativeSymbolCodeView pdbIdentity = ReadPdbIdentity(PdbPath, PdbLength);
		if (codeView.Guid != CodeView.Guid || codeView.Age != CodeView.Age || pdbIdentity != CodeView ||
			!string.Equals(Path.GetFileName(codeView.Path), Path.GetFileName(PdbPath), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The DLL CodeView record does not identify the reviewed PDB artifact.");
		}
		foreach ((string name, uint rva) in ExportRvas)
		{
			if (string.IsNullOrWhiteSpace(name) || rva == 0)
			{
				throw new InvalidDataException("The independently parsed PE export oracle is incomplete.");
			}
		}
	}
	internal static NativeSymbolCodeView ReadCodeView(string path)
	{
		RequireBoundedFile(path, MaximumDllBytes);
		using FileStream image = File.OpenRead(path);
		using PEReader pe = new(image);
		CodeViewDebugDirectoryData value = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView)
			.Select(pe.ReadCodeViewDebugDirectoryData).Single();
		return new NativeSymbolCodeView(value.Guid, value.Age);
	}
	internal static IReadOnlyDictionary<string, uint> ReadRequiredExportRvas(string path, IReadOnlyCollection<string> required)
	{
		RequireBoundedFile(path, MaximumDllBytes);
		using FileStream image = File.OpenRead(path);
		using PEReader pe = new(image);
		DirectoryEntry directory = pe.PEHeaders.PEHeader?.ExportTableDirectory ?? throw new InvalidDataException("The fixture has no PE header.");
		BlobReader header = pe.GetSectionData(directory.RelativeVirtualAddress).GetReader(0, 40);
		header.Offset = 16;
		_ = header.ReadUInt32();
		uint functions = header.ReadUInt32();
		uint names = header.ReadUInt32();
		uint functionRva = header.ReadUInt32();
		uint nameRva = header.ReadUInt32();
		uint ordinalRva = header.ReadUInt32();
		ValidateExactExportCounts(functions, names, required.Count);
		BlobReader functionTable = pe.GetSectionData(checked((int) functionRva)).GetReader(0, checked((int) functions * 4));
		BlobReader nameTable = pe.GetSectionData(checked((int) nameRva)).GetReader(0, checked((int) names * 4));
		BlobReader ordinalTable = pe.GetSectionData(checked((int) ordinalRva)).GetReader(0, checked((int) names * 2));
		Dictionary<string, uint> found = new(StringComparer.Ordinal);
		HashSet<ushort> ordinals = [];
		for (int index = 0; index < names; index++)
		{
			string name = ReadAscii(pe, nameTable.ReadUInt32());
			ushort ordinal = ordinalTable.ReadUInt16();
			if (ordinal >= functions || !ordinals.Add(ordinal))
			{
				throw new InvalidDataException("The fixture export ordinals are invalid.");
			}

			functionTable.Offset = ordinal * 4;
			uint rva = functionTable.ReadUInt32();
			if (rva == 0 || (rva >= directory.RelativeVirtualAddress && rva < directory.RelativeVirtualAddress + directory.Size))
			{
				throw new InvalidDataException("The fixture has a null or forwarded export.");
			}

			found.Add(name, rva);
		}
		if (!found.Keys.Order(StringComparer.Ordinal).SequenceEqual(required.Order(StringComparer.Ordinal), StringComparer.Ordinal))
		{
			throw new InvalidDataException("The fixture PE exports differ from the reviewed set.");
		}

		return found;
	}
	private static string ReadAscii(PEReader pe, uint rva)
	{
		BlobReader reader = pe.GetSectionData(checked((int) rva)).GetReader();
		List<byte> bytes = [];
		while (reader.RemainingBytes > 0 && bytes.Count < 256)
		{
			byte value = reader.ReadByte();
			if (value == 0)
			{
				return Encoding.ASCII.GetString([.. bytes]);
			}

			bytes.Add(value);
		}
		throw new InvalidDataException("The fixture export name is invalid.");
	}
	internal static void ValidateExactExportCounts(uint functions, uint names, int requiredCount)
	{
		if (requiredCount is <= 0 or > 64 || names != requiredCount || functions != requiredCount)
		{
			throw new InvalidDataException("The fixture export count differs from the reviewed exact set.");
		}
	}
	private static string HashBounded(string path, long expectedLength, long maximumLength)
	{
		using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (input.Length != expectedLength || input.Length < 0 || input.Length > maximumLength)
		{
			throw new InvalidDataException("The fixture changed while it was being checked.");
		}
		using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		byte[] buffer = new byte[81920];
		int read;
		while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
		{
			hash.AppendData(buffer, 0, read);
		}
		return Convert.ToHexString(hash.GetHashAndReset());
	}
	internal static void RequireBoundedFile(string path, long maximumLength)
	{
		if (!File.Exists(path))
		{
			throw new InvalidDataException("The fixture file is absent or exceeds its reviewed bound.");
		}
		long length = new FileInfo(path).Length;
		if (length <= 0 || length > maximumLength)
		{
			throw new InvalidDataException("The fixture file is absent or exceeds its reviewed bound.");
		}
	}

	internal static NativeSymbolCodeView ReadPdbIdentity(string path, long length)
	{
		ReadOnlySpan<byte> magic = "Microsoft C/C++ MSF 7.00\r\n\u001ADS\0\0\0"u8;
		using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		byte[] header = ReadAt(input, 0, 56, length);
		if (!header.AsSpan(0, magic.Length).SequenceEqual(magic))
		{
			throw new InvalidDataException("The owned PDB is not an MSF 7.0 file.");
		}
		int blockSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(32, 4));
		int blockCount = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(40, 4));
		int directoryBytes = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(44, 4));
		int blockMapAddress = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(52, 4));
		if (blockSize is < 512 or > 65536 || (blockSize & (blockSize - 1)) != 0 || blockCount <= 0 || directoryBytes is < 8 or > 1024 * 1024 ||
			blockMapAddress <= 0 || (long) blockCount * blockSize > length)
		{
			throw new InvalidDataException("The owned PDB superblock is out of bounds.");
		}
		int directoryBlockCount = checked((directoryBytes + blockSize - 1) / blockSize);
		if (checked(directoryBlockCount * 4) > blockSize)
		{
			throw new InvalidDataException("The owned PDB directory map exceeds the reviewed single-block fixture bound.");
		}
		byte[] map = ReadAt(input, checked((long) blockMapAddress * blockSize), checked(directoryBlockCount * 4), length);
		int[] directoryBlocks = Enumerable.Range(0, directoryBlockCount).Select(index => BinaryPrimitives.ReadInt32LittleEndian(map.AsSpan(index * 4, 4))).ToArray();
		byte[] directory = ReadBlocks(input, directoryBlocks, directoryBytes, blockSize, blockCount, length);
		int cursor = 0;
		int streamCount = ReadInt32(directory, ref cursor);
		if (streamCount is < 2 or > 1024)
		{
			throw new InvalidDataException("The owned PDB has no bounded information stream.");
		}
		int[] sizes = Enumerable.Range(0, streamCount).Select(_ => ReadInt32(directory, ref cursor)).ToArray();
		if (sizes[1] < 28)
		{
			throw new InvalidDataException("The owned PDB information stream is too small.");
		}
		for (int index = 0; index < streamCount; index++)
		{
			int size = sizes[index];
			if (size is < -1 or > (int) MaximumPdbBytes)
			{
				throw new InvalidDataException("The owned PDB has an invalid or oversized stream.");
			}

			int count = size <= 0 ? 0 : checked((size + blockSize - 1) / blockSize);
			if (cursor + (count * 4) > directory.Length)
			{
				throw new InvalidDataException("The owned PDB stream directory is truncated.");
			}

			int[] blocks = Enumerable.Range(0, count).Select(offset => ReadInt32(directory, ref cursor)).ToArray();
			if (index != 1)
			{
				continue;
			}

			byte[] info = ReadBlocks(input, blocks, 28, blockSize, blockCount, length);
			return new NativeSymbolCodeView(new Guid(info.AsSpan(12, 16)), BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(8, 4)));
		}
		throw new InvalidDataException("The owned PDB information stream was absent.");
	}

	private static byte[] ReadBlocks(FileStream input, int[] blocks, int bytes, int blockSize, int blockCount, long length)
	{
		byte[] data = new byte[bytes];
		int offset = 0;
		foreach (int block in blocks)
		{
			if (block < 0 || block >= blockCount)
			{
				throw new InvalidDataException("The owned PDB block index is out of range.");
			}

			int count = Math.Min(blockSize, bytes - offset);
			ReadAt(input, checked((long) block * blockSize), count, length).CopyTo(data, offset);
			offset += count;
		}
		return offset == bytes ? data : throw new InvalidDataException("The owned PDB block sequence is too short.");
	}
	private static byte[] ReadAt(FileStream input, long offset, int count, long length)
	{
		if (offset < 0 || count < 0 || offset > length - count)
		{
			throw new InvalidDataException("The owned PDB read exceeds its bounds.");
		}

		input.Position = offset;
		byte[] data = new byte[count];
		int read = 0;
		while (read < count)
		{
			int next = input.Read(data, read, count - read);
			if (next == 0)
			{
				throw new EndOfStreamException();
			}

			read += next;
		}
		return data;
	}
	private static int ReadInt32(byte[] data, ref int cursor)
	{
		if (cursor > data.Length - 4)
		{
			throw new InvalidDataException("The owned PDB directory is truncated.");
		}

		int value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(cursor, 4));
		cursor += 4;
		return value;
	}
}
internal sealed record NativeSymbolCodeView(Guid Guid, int Age);



