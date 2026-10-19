// This proves generated hook source without applying or executing it.
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
internal static class LiveApiHookQualification
{
	private const string Allocation = "live-api-hook-qualification";
	private const string Bytes = "90 90 90 90 90 90 90 90 90 90 90 90 90 90 90 90 C3 90 90 90 90 90 90 90 90 90 90 90 90 90 90 90";

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		bool attempted = false;
		string? address = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			await AssertNoNamedAllocationAsync(instance, Allocation);
			attempted = true;
			JsonNode allocation = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = Allocation, ["size"] = 32, ["executable"] = true });
			address = Text(allocation, "address");
			await WriteBytesAsync(instance, address, Bytes);
			Assert.Equal(Bytes, await ReadBytesAsync(instance, address, 32));

			string destination = Add(address, 16);
			JsonNode decoded = await CallAsync(instance, CheatEngineToolNames.CodeDisassemble,
				new Dictionary<string, object?> { ["address"] = address, ["count"] = 5 });
			JsonArray instructions = decoded["instructions"]!.AsArray();
			Assert.Equal(5, instructions.Count);
			Assert.All(instructions, instruction => Assert.Equal(("nop", "90", 1),
				(Text(instruction!["opcode"], "").Trim(), Text(instruction, "bytes"), instruction["size"]!.GetValue<int>())));
			Assert.Equal(Enumerable.Range(0, 5).Select(offset => Add(address, offset)),
				instructions.Select(instruction => Text(instruction, "address")));

			JsonNode generated = await CallAsync(instance, CheatEngineToolNames.AsmGenerateApiHook,
				new Dictionary<string, object?>
				{
					["address"] = address,
					["jumpTarget"] = destination,
					["extension"] = "McpLiveApiHook",
					["targetSelf"] = false
				});
			string script = Text(generated, "script");
			AssertHookContract(script, address, destination, sandbox.TargetArchitecture == "x64");
			JsonNode checkedScript = await CallAsync(instance, CheatEngineToolNames.AsmCheck,
				new Dictionary<string, object?> { ["script"] = script, ["name"] = "live-api-hook-generated" });
			Assert.True(checkedScript["accepted"]!.GetValue<bool>());
			Assert.Null(checkedScript["failedSection"]);
			Assert.Equal(Bytes, await ReadBytesAsync(instance, address, 32));
			sandbox.Record("all_tools_asm_generate_api_hook", new
			{
				address,
				destination,
				firstFiveInstructions = instructions.Select(item => item!.DeepClone()).ToArray(),
				generated = generated.DeepClone(),
				asmCheckAccepted = true,
				bytesUnchanged = true
			});
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			if (attempted)
			{
				if (address is not null)
				{
					try
					{
						using CancellationTokenSource readDeadline = new(TimeSpan.FromSeconds(10));
						Assert.Equal(Bytes, await ReadBytesAsync(instance.WithCancellation(readDeadline.Token), address, 32));
					}
					catch (Exception exception) { cleanup.Add(exception); }
				}
				try
				{
					using CancellationTokenSource releaseDeadline = new(TimeSpan.FromSeconds(10));
					await ReleaseOrAssertAbsentAsync(instance.WithCancellation(releaseDeadline.Token), Allocation);
				}
				catch (Exception exception) { cleanup.Add(exception); }
				if (cleanup.Count == 0)
				{
					sandbox.Record("live_api_hook_cleanup", new
					{
						freed = true,
						bytesUnchanged = address is not null
					});
				}
			}
			LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
		}
	}

	internal static void AssertHookContract(string script, string source, string destination, bool x64)
	{
		Assert.Equal(ExpectedTemplate(source, destination, x64), NormalizeLines(script));
	}

	internal static string[] ExpectedTemplate(string source, string destination, bool x64)
	{
		source = source.ToLowerInvariant();
		destination = destination.ToLowerInvariant();
		const string suffix = "mcpliveapihook";
		string original = "originalcall" + suffix;
		string returnHere = "returnhere" + suffix;
		if (!x64)
		{
			return
			[
				"[enable]", $"alloc({original},1024)", $"label({returnHere})", original + ":",
				"nop", "nop", "nop", "nop", "nop", $"jmp {returnHere}", source + ":", $"jmp {destination}",
				returnHere + ":", "[disable]", source + ":", "db 90 90 90 90 90", $"dealloc({original})"
			];
		}

		string trampoline = "jumptrampoline" + suffix;
		string trampolineAddress = trampoline + "address";
		return
		[
			"[enable]", $"alloc({original},1024,{source})", $"alloc({trampoline},64,{source});",
			$"label({trampolineAddress})", $"label({returnHere})", original + ":", "nop", "nop", "nop", "nop", "nop",
			$"jmp {returnHere}", trampoline + ":", $"jmp [{trampolineAddress}]", trampolineAddress + ":", $"dq {destination}",
			source + ":", $"jmp {trampoline}", returnHere + ":", "[disable]", source + ":", "db 90 90 90 90 90",
			$"dealloc({original})", $"dealloc({trampoline})"
		];
	}

	internal static string[] NormalizeLines(string script)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(script);
		return [.. script.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
			.Select(static line => line.Split("//", StringSplitOptions.None)[0].Trim())
			.Where(static line => line.Length > 0)
			.Select(static line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant())];
	}

	private static async Task AssertNoNamedAllocationAsync(LiveMcpInstanceClient instance, string name)
	{
		JsonNode resources = await CallAsync(instance, CheatEngineToolNames.RuntimeListResources);
		Assert.DoesNotContain(resources["resources"]!.AsArray(), item =>
			string.Equals(Text(item!, "name"), name, StringComparison.Ordinal));
	}

	private static async Task ReleaseOrAssertAbsentAsync(LiveMcpInstanceClient instance, string name)
	{
		LiveMcpToolResult released = await instance.CallToolRawAsync(CheatEngineToolNames.MemoryFree,
			new Dictionary<string, object?> { ["name"] = name });
		if (!released.IsError)
		{
			Assert.True(released.Payload!["release"]!["isComplete"]!.GetValue<bool>());
			released = await instance.CallToolRawAsync(CheatEngineToolNames.MemoryFree,
				new Dictionary<string, object?> { ["name"] = name });
		}
		Assert.True(released.IsError);
		Assert.Equal("not_found", Text(released.Payload!["error"], "kind"));
	}

	private static async Task WriteBytesAsync(LiveMcpInstanceClient instance, string address, string value)
	{
		JsonNode result = await CallAsync(instance, CheatEngineToolNames.MemoryWrite,
			new Dictionary<string, object?> { ["address"] = address, ["valueType"] = "bytes", ["value"] = value, ["verify"] = true });
		Assert.True(result["verified"]!.GetValue<bool>());
	}

	private static async Task<string> ReadBytesAsync(LiveMcpInstanceClient instance, string address, int size) =>
		Text(await CallAsync(instance, CheatEngineToolNames.MemoryRead,
			new Dictionary<string, object?> { ["address"] = address, ["valueType"] = "bytes", ["size"] = size }), "value");
	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool, IReadOnlyDictionary<string, object?>? arguments = null) =>
		await instance.CallToolAsync(tool, arguments) ?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
	private static string Text(JsonNode? node, string property) => string.IsNullOrEmpty(property) ? node!.GetValue<string>() :
		node?[property]?.GetValue<string>() ?? throw new InvalidDataException($"Missing string '{property}'.");
	private static string Add(string address, int offset) =>
		(ulong.Parse(address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address[2..] : address,
			NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) + (uint) offset).ToString("X", CultureInfo.InvariantCulture);
}
