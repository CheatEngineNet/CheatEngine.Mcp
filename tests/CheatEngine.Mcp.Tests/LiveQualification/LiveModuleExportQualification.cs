using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Compares mapped exports with the owned target's exact module file, independently of the product parser.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveModuleExportQualification
{
	private const int PageSize = 8;

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		using Process target = Process.GetProcessById(sandbox.HostA.TargetProcessId);
		ProcessModule expectedModule = Assert.Single(target.Modules.Cast<ProcessModule>(), module =>
			string.Equals(module.ModuleName, "kernel32.dll", StringComparison.OrdinalIgnoreCase));
		string path = expectedModule.FileName;
		ulong baseAddress = (ulong) expectedModule.BaseAddress;
		JsonNode modules = await CallAsync(instance, CheatEngineToolNames.ModuleList,
			new Dictionary<string, object?> { ["nameContains"] = "kernel32.dll", ["limit"] = 100, ["format"] = "detailed" });
		JsonNode module = Assert.Single(modules["modules"]!.AsArray()
			.Where(node => string.Equals(Text(node!, "name"), "kernel32.dll", StringComparison.OrdinalIgnoreCase))
			.DistinctBy(node => (Address(Text(node!, "base")), Text(node!, "path").ToUpperInvariant())))!;
		Assert.Equal(baseAddress, Address(Text(module, "base")));
		Assert.Equal(path, Text(module, "path"), ignoreCase: true);
		bool is64Bit = sandbox.TargetArchitecture == "x64";
		Assert.Equal(is64Bit, module["is64Bit"]!.GetValue<bool>());
		FileExport[] oracle = ReadNamedExports(path, is64Bit ? Machine.Amd64 : Machine.I386);
		FileExport[] expected = [.. oracle.Where(export => export.Name.Contains("get", StringComparison.OrdinalIgnoreCase))];
		Assert.True(expected.Length > PageSize * 2, "The fixture module must contain two full filtered export pages.");
		string name = Text(module, "name");
		JsonNode first = await VerifyPageAsync(instance, name, baseAddress, "get", expected, 0);
		JsonNode next = await VerifyPageAsync(instance, name, baseAddress, "get", expected, PageSize);
		JsonNode replay = await VerifyPageAsync(instance, name, baseAddress, "get", expected, 0);
		Assert.True(JsonNode.DeepEquals(first, replay));
		// Exercise both address and forwarder shapes with expectations read from the file.
		foreach (FileExport selected in new[]
		{
			oracle.First(export => export.Forwarder is null),
			oracle.First(export => export.Forwarder is not null)
		})
		{
			FileExport[] selectedMatches = [.. oracle.Where(export => export.Name.Contains(selected.Name, StringComparison.OrdinalIgnoreCase))];
			int offset = Array.IndexOf(selectedMatches, selected) / PageSize * PageSize;
			await VerifyPageAsync(instance, name, baseAddress, selected.Name, selectedMatches, offset);
		}

		sandbox.Record("all_tools_module_list_exports", new
		{
			module = name,
			path,
			baseAddress = baseAddress.ToString("X", CultureInfo.InvariantCulture),
			first = first.DeepClone(),
			next = next.DeepClone(),
			fileOracleMatched = true,
			replayMatches = true,
			directAndForwardedExportsVerified = true
		});
	}

	private static async Task<JsonNode> VerifyPageAsync(LiveMcpInstanceClient instance, string module,
		ulong baseAddress, string filter, FileExport[] expected, int offset)
	{
		JsonNode result = await CallAsync(instance, CheatEngineToolNames.ModuleListExports,
			new Dictionary<string, object?> { ["module"] = module, ["nameContains"] = filter, ["offset"] = offset, ["limit"] = PageSize });
		Assert.Equal(module, Text(result, "module"));
		Assert.Equal(expected.Length, result["total"]!.GetValue<int>());
		FileExport[] page = [.. expected.Skip(offset).Take(PageSize)];
		JsonArray actual = result["exports"]!.AsArray();
		Assert.Equal(page.Length, actual.Count);
		for (int index = 0; index < page.Length; index++)
		{
			FileExport entry = page[index];
			JsonNode observed = actual[index]!;
			Assert.Equal(entry.Ordinal, observed["ordinal"]!.GetValue<uint>());
			Assert.Equal(entry.Name, Text(observed, "name"));
			Assert.Equal(entry.Forwarder, observed["forwarder"]?.GetValue<string>());
			if (entry.Forwarder is null)
			{
				Assert.Equal(checked(baseAddress + entry.Rva), Address(Text(observed, "address")));
			}
			else
			{
				Assert.Null(observed["address"]);
			}
		}

		int? next = offset + page.Length < expected.Length ? offset + page.Length : null;
		Assert.Equal(next, result["nextOffset"]?.GetValue<int>());
		return result;
	}

	private static FileExport[] ReadNamedExports(string path, Machine expectedMachine)
	{
		using FileStream file = File.OpenRead(path);
		Assert.InRange(file.Length, 1, 32 * 1024 * 1024);
		using PEReader pe = new(file);
		Assert.Equal(expectedMachine, pe.PEHeaders.CoffHeader.Machine);
		DirectoryEntry directory = pe.PEHeaders.PEHeader!.ExportTableDirectory;
		Assert.True(directory.RelativeVirtualAddress > 0);
		Assert.InRange(directory.Size, 40, 8 * 1024 * 1024);
		BlobReader table = pe.GetSectionData(directory.RelativeVirtualAddress).GetReader(0, 40);
		table.Offset = 16;
		uint ordinalBase = table.ReadUInt32();
		uint functionCount = table.ReadUInt32();
		uint nameCount = table.ReadUInt32();
		uint functionRva = table.ReadUInt32();
		uint nameRva = table.ReadUInt32();
		uint ordinalRva = table.ReadUInt32();
		Assert.InRange(functionCount, 1U, 65536U);
		Assert.InRange(nameCount, 1U, 65536U);
		BlobReader functions = pe.GetSectionData(checked((int) functionRva)).GetReader(0, checked((int) functionCount * 4));
		BlobReader names = pe.GetSectionData(checked((int) nameRva)).GetReader(0, checked((int) nameCount * 4));
		BlobReader ordinals = pe.GetSectionData(checked((int) ordinalRva)).GetReader(0, checked((int) nameCount * 2));
		List<FileExport> exports = [];
		for (int index = 0; index < nameCount; index++)
		{
			string name = ReadAscii(pe, names.ReadUInt32());
			ushort function = ordinals.ReadUInt16();
			Assert.True(function < functionCount);
			functions.Offset = function * 4;
			uint rva = functions.ReadUInt32();
			Assert.NotEqual(0U, rva);
			bool forwarded = rva >= directory.RelativeVirtualAddress &&
				(ulong) rva < (ulong) directory.RelativeVirtualAddress + (ulong) directory.Size;
			exports.Add(new FileExport(checked(ordinalBase + function), name, rva, forwarded ? ReadAscii(pe, rva) : null));
		}

		return [.. exports.OrderBy(export => export.Ordinal).ThenBy(export => export.Name, StringComparer.Ordinal)];
	}

	private static string ReadAscii(PEReader pe, uint rva)
	{
		PEMemoryBlock block = pe.GetSectionData(checked((int) rva));
		BlobReader reader = block.GetReader(0, Math.Min(block.Length, 4096));
		List<byte> bytes = [];
		while (reader.RemainingBytes > 0)
		{
			byte value = reader.ReadByte();
			if (value == 0)
			{
				return Encoding.ASCII.GetString([.. bytes]);
			}

			Assert.InRange(value, (byte) 1, (byte) 127);
			bytes.Add(value);
		}

		throw new InvalidDataException("An export name or forwarder exceeded the fixture's bounded string length.");
	}

	private sealed record FileExport(uint Ordinal, string Name, uint Rva, string? Forwarder);

	private static ulong Address(string value) => ulong.Parse(
		value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.AsSpan(2) : value.AsSpan(),
		NumberStyles.HexNumber, CultureInfo.InvariantCulture);
	private static string Text(JsonNode node, string property) => node[property]!.GetValue<string>();
	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool,
		IReadOnlyDictionary<string, object?> arguments) => await instance.CallToolAsync(tool, arguments)
		?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
}
