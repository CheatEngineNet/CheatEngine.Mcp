using System.Text;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Structures;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Tools.Structures;

/// <summary><c>structure_generate_c_header</c> over a Client double: refusals, script arguments and both generators.</summary>
public sealed class StructureHeaderToolsTests
{
	private const string CheatEngineHeader = """{"names":["Player","Weapon"],"text":"typedef struct Player Player;"}""";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public static TheoryData<string[], ToolErrorKind, string> InvalidNames => new()
	{
		{ [], ToolErrorKind.InvalidArgument, "names" },
		{ [.. Enumerable.Range(0, 65).Select(static index => "S" + index)], ToolErrorKind.LimitExceeded, "names" },
		{ ["Player", " "], ToolErrorKind.InvalidArgument, "names[1]" },
		{ ["Player", "Player"], ToolErrorKind.InvalidArgument, "names[1]" },
		{ [new string('n', 257)], ToolErrorKind.LimitExceeded, "names[0]" }
	};

	[Theory]
	[MemberData(nameof(InvalidNames))]
	public void GenerateCHeader_InvalidNames_AreRefusedWithoutDispatch(string[] names, ToolErrorKind kind,
		string parameter)
	{
		StructureToolHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Headers.GenerateCHeader(names, cancellationToken: Token));

		Assert.Equal((kind, ToolHostEffect.NotStarted), (exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(parameter, exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void GenerateCHeader_UndefinedGenerator_IsInvalidArgumentWithoutDispatch()
	{
		StructureToolHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Headers.GenerateCHeader(["Player"], (StructureHeaderGenerator) 7, Token));

		Assert.Equal("generator", exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void GenerateCHeader_CheatEngineText_IsReturnedWithEveryDeclaredStructure()
	{
		StructureToolHarness harness = new()
		{
			Lua = static call => call.Runs(StructureLuaScripts.CHeader) ? CheatEngineHeader : null
		};

		StructureCHeader header = harness.Headers.GenerateCHeader(["Player"], cancellationToken: Token);

		Assert.Contains("[1] = {\"Player\",}, [2] = \"auto\", [3] = 256, [4] = 8192, [5] = 1048576, [6] = 1048576",
			Assert.Single(harness.LuaCalls).Arguments, StringComparison.Ordinal);
		Assert.Equal(("typedef struct Player Player;", StructureHeaderGenerator.CheatEngine),
			(header.Text, header.Generator));
		Assert.Equal(["Player", "Weapon"], header.Structures);
		Assert.Equal(1, harness.Dispatcher.Calls);
	}

	[Theory]
	[InlineData(StructureHeaderGenerator.CheatEngine, "cheat_engine")]
	[InlineData(StructureHeaderGenerator.Managed, "managed")]
	public void GenerateCHeader_Generator_ReachesTheScriptAsItsMode(StructureHeaderGenerator generator, string mode)
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ => CheatEngineHeader
		};

		harness.Headers.GenerateCHeader(["Player", "Enemy"], generator, Token);

		Assert.Contains($"[1] = {{\"Player\",\"Enemy\",}}, [2] = \"{mode}\",", Assert.Single(harness.LuaCalls).Arguments,
			StringComparison.Ordinal);
	}

	[Fact]
	public void GenerateCHeader_CopiedStructures_AreWrittenByTheManagedGenerator()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ =>
				"""
				{"names":["Player","Weapon"],"structures":[
				{"name":"Player","size":16,"elements":[{"offset":0,"vartype":2,"byteSize":4,"name":"health","display":"dtSignedInteger"},
				{"offset":8,"vartype":12,"byteSize":8,"name":"weapon","child":"Weapon"}]},
				{"name":"Weapon","size":4,"elements":[{"offset":0,"vartype":4,"byteSize":4,"name":"damage"}]}]}
				"""
		};

		StructureCHeader header = harness.Headers.GenerateCHeader(["Player"], cancellationToken: Token);

		Assert.Equal(StructureHeaderGenerator.Managed, header.Generator);
		Assert.Equal(["Player", "Weapon"], header.Structures);
		Assert.Contains("struct Player\n{\n\tint32_t health; // 0x0 int32\n\tuint8_t pad_4[0x4]; // 0x4 padding\n" +
						"\tWeapon *weapon; // 0x8 pointer to Weapon\n};\n", header.Text, StringComparison.Ordinal);
		Assert.Contains("\tfloat damage; // 0x0 float\n", header.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void GenerateCHeader_ManagedTextOverOneMiB_IsLimitExceeded()
	{
		StringBuilder copy = new("""{"names":["Big"],"structures":[{"name":"Big","size":8800,"elements":[""");
		string name = new('n', 250);
		for (int index = 0; index < 2200; index++)
		{
			copy.Append(index == 0 ? string.Empty : ",").Append("{\"offset\":").Append(index * 4)
				.Append(",\"vartype\":2,\"byteSize\":4,\"name\":\"").Append(name).Append(' ').Append(index).Append("\"}");
		}

		string reply = copy.Append("]}]}").ToString();
		StructureToolHarness harness = new()
		{
			Lua = _ => reply
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Headers.GenerateCHeader(["Big"], StructureHeaderGenerator.Managed, Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("names", exception.Error.Details!.Value.GetProperty("parameter").GetString());
	}

	[Theory]
	[InlineData("unsupported", ToolErrorKind.Unsupported)]
	[InlineData("not_found", ToolErrorKind.NotFound)]
	[InlineData("limit_exceeded", ToolErrorKind.LimitExceeded)]
	public void GenerateCHeader_DeclaredRefusal_IsTheContractError(string declared, ToolErrorKind kind)
	{
		StructureToolHarness harness = new()
		{
			Lua = _ => StructureToolHarness.Declared(declared)
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Headers.GenerateCHeader(["Player"], StructureHeaderGenerator.CheatEngine, Token));

		Assert.Equal((kind, ToolHostEffect.NotStarted), (exception.Error.Kind, exception.Error.HostEffect));
	}

	[Fact]
	public void GenerateCHeader_ScriptWithNeitherTextNorStructures_IsInternal()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ => """{"names":["Player"]}"""
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Headers.GenerateCHeader(["Player"], cancellationToken: Token));

		Assert.Equal(ToolErrorKind.Internal, exception.Error.Kind);
	}

	[Fact]
	public async Task Pipeline_GenerateCHeader_BindsTheGeneratorAndReturnsStructuredContent()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ => CheatEngineHeader
		};
		(ServiceProvider root, AsyncServiceScope scope, TestMcpPipeline pipeline) = await harness.ServeAsync();
		try
		{
			CallToolResult result = await pipeline.CallAsync(CheatEngineToolNames.StructureGenerateCHeader,
				"""{"names":["Player"],"generator":"cheat_engine"}""");

			Assert.NotEqual(true, result.IsError);
			JsonElement content = result.StructuredContent!.Value;
			Assert.Equal("cheat_engine", content.GetProperty("generator").GetString());
			Assert.Equal(2, content.GetProperty("structures").GetArrayLength());
			Assert.Contains("[2] = \"cheat_engine\"", Assert.Single(harness.LuaCalls).Arguments,
				StringComparison.Ordinal);

			ToolError refused = TestMcpPipeline.AssertError(
				await pipeline.CallAsync(CheatEngineToolNames.StructureGenerateCHeader, """{"names":[]}"""),
				ToolErrorKind.InvalidArgument);
			Assert.Equal("names", refused.Details!.Value.GetProperty("parameter").GetString());
			Assert.Single(harness.LuaCalls);
		}
		finally
		{
			await pipeline.DisposeAsync();
			await scope.DisposeAsync();
			await root.DisposeAsync();
		}
	}
}
