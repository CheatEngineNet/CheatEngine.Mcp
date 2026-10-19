using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Read-only qualification for a verified signature at the owned target image's PE entry point.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveSignatureQualification
{
	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		ArgumentNullException.ThrowIfNull(instance);

		using Process target = Process.GetProcessById(sandbox.HostA.TargetProcessId);
		using ProcessModule executable = target.MainModule ?? throw new InvalidOperationException("The owned target has no main module.");
		string path = Path.GetFullPath(executable.FileName);
		ulong moduleBase = (ulong) executable.BaseAddress;
		(uint entryRva, uint imageSize) = ReadImageInfo(path);
		ulong expectedAddress = checked(moduleBase + entryRva);
		string moduleName = executable.ModuleName;

		JsonNode modules = await CallAsync(instance, CheatEngineToolNames.ModuleList,
			new Dictionary<string, object?> { ["nameContains"] = moduleName, ["format"] = "detailed", ["limit"] = 1000 });
		JsonNode module = Assert.Single(modules["modules"]!.AsArray(), item =>
			item is not null && string.Equals(Text(item, "name"), moduleName, StringComparison.OrdinalIgnoreCase) &&
			Address(Text(item, "base")) == moduleBase && string.Equals(Text(item, "path"), path, StringComparison.OrdinalIgnoreCase))!;
		Assert.Equal((long) imageSize, module["size"]!.GetValue<long>());
		Assert.Equal((long) imageSize, executable.ModuleMemorySize);
		Assert.True(entryRva < imageSize);
		Assert.InRange(imageSize, 1U, 64U * 1024 * 1024);

		string address = Hex(expectedAddress);
		JsonNode signature = await CallAsync(instance, CheatEngineToolNames.AobGenerateSignature, new Dictionary<string, object?>
		{
			["address"] = address,
			["module"] = moduleName,
			["verify"] = true
		});
		Assert.Equal(address, Text(signature, "address"));
		Assert.Equal(moduleName, Text(signature, "module"), ignoreCase: true);
		Assert.Equal("cheat_engine", Text(signature, "generator"));
		Assert.Equal((true, true, 1),
			(signature["unique"]!.GetValue<bool>(), signature["verified"]!.GetValue<bool>(), signature["matchCount"]!.GetValue<int>()));
		string pattern = Text(signature, "pattern");
		string patternStart = Text(signature, "patternStart");
		int offset = signature["offset"]!.GetValue<int>();
		int length = signature["length"]!.GetValue<int>();
		string[] tokens = ParsePattern(pattern);
		Assert.Equal(length, tokens.Length);
		Assert.InRange(offset, 0, length - 1);
		Assert.Equal(expectedAddress, checked(Address(patternStart) + (ulong) offset));

		JsonNode memory = await CallAsync(instance, CheatEngineToolNames.MemoryRead,
			new Dictionary<string, object?> { ["address"] = patternStart, ["valueType"] = "bytes", ["size"] = length });
		AssertPatternMatchesMemory(tokens, Text(memory, "value"));

		JsonNode found = await CallAsync(instance, CheatEngineToolNames.AobFind, new Dictionary<string, object?>
		{
			["patterns"] = new[] { pattern },
			["module"] = moduleName,
			["limit"] = 2,
			["includeMapped"] = false
		});
		JsonNode result = Assert.Single(found["results"]!.AsArray())!;
		Assert.Equal((pattern, 1, true, true, "bounded_scan", true),
			(Text(result, "pattern"), result["count"]!.GetValue<int>(), result["exact"]!.GetValue<bool>(),
				result["unique"]!.GetValue<bool>(), Text(result, "scope"), result["targetVerified"]!.GetValue<bool>()));
		Assert.Equal(patternStart, Assert.Single(result["matches"]!.AsArray())!.GetValue<string>());
		sandbox.Record("all_tools_aob_generate_signature", new
		{
			module = moduleName,
			path,
			moduleBase = Hex(moduleBase),
			entryRva = entryRva.ToString("X", CultureInfo.InvariantCulture),
			address,
			signature = signature.DeepClone(),
			patternBytesVerifiedAgainstMemory = true,
			boundedModuleRescanMatched = true
		});
	}

	private static (uint EntryPointRva, uint ImageSize) ReadImageInfo(string path)
	{
		using FileStream image = File.OpenRead(path);
		using PEReader pe = new(image);
		PEHeader header = pe.PEHeaders.PEHeader ?? throw new InvalidDataException("Target image does not have a PE header.");
		uint rva = checked((uint) header.AddressOfEntryPoint);
		int imageSize = header.SizeOfImage;
		Assert.NotEqual(0U, rva);
		Assert.True(imageSize > 0);
		return (rva, checked((uint) imageSize));
	}

	private static string[] ParsePattern(string pattern)
	{
		string[] tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		Assert.InRange(tokens.Length, 1, 64);
		Assert.Contains(tokens, static token => token != "??");
		Assert.All(tokens, token => Assert.True(token == "??" ||
			byte.TryParse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _),
			$"Invalid AOB token '{token}'."));
		return tokens;
	}

	private static void AssertPatternMatchesMemory(string[] pattern, string memoryBytes)
	{
		string[] bytes = memoryBytes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		Assert.Equal(pattern.Length, bytes.Length);
		for (int index = 0; index < pattern.Length; index++)
		{
			Assert.True(byte.TryParse(bytes[index], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte value));
			if (pattern[index] != "??")
			{
				Assert.Equal(byte.Parse(pattern[index], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture), value);
			}
		}
	}

	private static ulong Address(string value) => ulong.Parse(
		value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.AsSpan(2) : value.AsSpan(),
		NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
	private static string Hex(ulong value) => value.ToString("X", CultureInfo.InvariantCulture);
	private static string Text(JsonNode node, string property) => node[property]!.GetValue<string>();
	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool,
		IReadOnlyDictionary<string, object?> arguments) => await instance.CallToolAsync(tool, arguments)
		?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
}
