using System.Text.Json;

using CheatEngine.Client.Inspection;
using CheatEngine.Client.Processes;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Core;
using CheatEngine.Mcp.Tests.Tools.Modules;
using CheatEngine.Mcp.Tools.Symbol;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tests.Tools.Symbol;

/// <summary><c>symbol_find</c> over a simulated target whose fixed Lua result is canned.</summary>
public sealed class SymbolFindToolTests : IDisposable
{
	private readonly McpFilePathsTests.Scratch _scratch = new();
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public static TheoryData<string?, string?, int, int, ToolErrorKind, string> RefusedArguments => new()
	{
		{ null, null, 0, 100, ToolErrorKind.InvalidArgument, "nameContains" },
		{ "h", null, 0, 100, ToolErrorKind.InvalidArgument, "nameContains" },
		{ new string('h', 129), null, 0, 100, ToolErrorKind.InvalidArgument, "nameContains" },
		{ "  ", null, 0, 100, ToolErrorKind.InvalidArgument, "nameContains" },
		{ "he\u0001th", null, 0, 100, ToolErrorKind.InvalidArgument, "nameContains" },
		{ "health", "", 0, 100, ToolErrorKind.InvalidArgument, "module" },
		{ "health", new string('m', 257), 0, 100, ToolErrorKind.InvalidArgument, "module" },
		{ "health", "game\u0000.exe", 0, 100, ToolErrorKind.InvalidArgument, "module" },
		{ "health", null, -1, 100, ToolErrorKind.InvalidArgument, "offset" },
		{ "health", null, 0, 0, ToolErrorKind.InvalidArgument, "limit" },
		{ "health", null, 0, 501, ToolErrorKind.LimitExceeded, "limit" }
	};

	public void Dispose()
	{
		_scratch.Dispose();
	}

	[Fact]
	public void Find_WithoutModule_ChecksTheProcessAndPassesTheTextOnlyAsAnArgument()
	{
		ModuleSymbolTarget target = new();
		SymbolMatch[] page =
		[
			new("game.GetHealth", "140001000", "game", 0x40),
			new("playerHealth", "20000010", Registered: true)
		];
		target.LuaResults[typeof(LuaSymbolFind)] = new LuaSymbolFind(2, false, true, page);

		SymbolFindResult result = Tools(target).Find("Health\"]..os.exit()--", cancellationToken: Token);

		Assert.Equal((2, false, true, (int?) null), (result.Total, result.Truncated, result.SymbolsLoaded,
			result.NextOffset));
		Assert.Equal(page, result.Symbols);
		string source = Assert.Single(target.LuaSources);
		Assert.Contains(
			"local a = { n = 6, [1] = \"Health\\034]..os.exit()--\", [2] = 10000, [3] = 0, [4] = 100, [5] = nil, " +
			"[6] = nil }", source, StringComparison.Ordinal);
		Assert.EndsWith(SymbolScripts.Find, source, StringComparison.Ordinal);
		Assert.Equal(1, target.Calls(nameof(IProcessClient.GetCurrentProcess)));
		Assert.Equal(0, target.Calls(nameof(IInspectionClient.TryGetModules)));
		Assert.Equal(1, target.Dispatcher.Calls);
	}

	[Theory]
	[InlineData("GAME.EXE")]
	[InlineData("game.exe+10")]
	public void Find_WithModule_PassesTheModuleRangeInTheSameDispatch(string module)
	{
		ModuleSymbolTarget target = new();
		target.AddModule("kernel32.dll", 0x7FFB10000000, 0x80000);
		target.AddModule("game.exe", 0x140000000, 0x2000);
		target.Addresses["game.exe+10"] = 0x140000010;
		target.LuaResults[typeof(LuaSymbolFind)] = new LuaSymbolFind(0, false, false, []);

		SymbolFindResult result = Tools(target).Find("health", module, 5, 20, Token);

		Assert.Equal((0, false, false, (int?) null), (result.Total, result.Truncated, result.SymbolsLoaded,
			result.NextOffset));
		Assert.Empty(result.Symbols);
		Assert.Contains("[1] = \"health\", [2] = 10000, [3] = 5, [4] = 20, [5] = 0x140000000, [6] = 0x2000 }",
			Assert.Single(target.LuaSources), StringComparison.Ordinal);
		Assert.Equal(1, target.Dispatcher.Calls);
	}

	[Fact]
	public void Find_UnknownModule_IsNotFoundBeforeTheSearch()
	{
		ModuleSymbolTarget target = new();
		target.AddModule("game.exe", 0x140000000, 0x2000);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target).Find("health", "engine.dll", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.NotFound, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("module_list", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Empty(target.LuaSources);
	}

	[Fact]
	public void Find_ModuleWithoutSize_IsUnsupportedBeforeTheSearch()
	{
		ModuleSymbolTarget target = new();
		target.Modules.Add(new ModuleInfo("game.exe", new Address(0x140000000), null, true,
			"C:\\Games\\Game\\game.exe"));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target).Find("health", "game.exe", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.Unsupported, exception.Error.Kind);
		Assert.Contains("game.exe", exception.Error.Message, StringComparison.Ordinal);
		Assert.Empty(target.LuaSources);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("game.exe")]
	public void Find_NotAttached_IsNotAttachedBeforeTheSearch(string? module)
	{
		ModuleSymbolTarget target = new()
		{
			Attached = false
		};
		target.AddModule("game.exe", 0x140000000, 0x2000);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target).Find("health", module, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.NotAttached, exception.Error.Kind);
		Assert.Empty(target.LuaSources);
	}

	[Theory]
	[InlineData(0, 3, 250, 3)]
	[InlineData(247, 3, 250, null)]
	[InlineData(300, 0, 250, null)]
	public void Find_Page_ReportsTheNextOffsetOnlyBeforeTheLastCollectedMatch(int offset, int returned, int total,
		int? next)
	{
		ModuleSymbolTarget target = new();
		SymbolMatch[] page =
		[
			.. Enumerable.Range(0, returned).Select(static index => new SymbolMatch($"game.f{index}", "140001000"))
		];
		target.LuaResults[typeof(LuaSymbolFind)] = new LuaSymbolFind(total, true, true, page);

		SymbolFindResult result = Tools(target).Find("game", offset: offset, limit: 3, cancellationToken: Token);

		Assert.Equal((total, true, returned), (result.Total, result.Truncated, result.Symbols.Length));
		Assert.Equal(next is null ? null : offset + next, result.NextOffset);
	}

	[Theory]
	[MemberData(nameof(RefusedArguments))]
	public void Find_RefusedArguments_NeverDispatch(string? nameContains, string? module, int offset, int limit,
		ToolErrorKind kind, string parameter)
	{
		ModuleSymbolTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target).Find(nameContains!, module, offset, limit, Token));

		Assert.Equal((kind, ToolHostEffect.NotStarted), (exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(parameter, exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, target.Dispatcher.Calls);
		Assert.Equal(0, target.TotalClientCalls);
	}

	[Fact]
	public void Find_BoundaryArguments_AreAccepted()
	{
		ModuleSymbolTarget target = new();
		target.LuaResults[typeof(LuaSymbolFind)] = new LuaSymbolFind(0, false, true, []);

		_ = Tools(target).Find("hp", limit: 1, cancellationToken: Token);
		_ = Tools(target).Find(new string('h', 128), limit: 500, cancellationToken: Token);

		Assert.Equal(2, target.LuaSources.Count);
	}

	[Fact]
	public void Find_Result_SerializesWithoutOmittedMembers()
	{
		SymbolFindResult result = new(3, false, true,
		[
			new SymbolMatch("game.Health", "140001000"),
			new SymbolMatch("kernel32.GetTickCount", "7FFB10001000", "kernel32", 16),
			new SymbolMatch("hpAlloc", "20000000", Size: 4096, Registered: true)
		], 2);

		string json = JsonSerializer.Serialize(result, SymbolJsonContext.Default.SymbolFindResult);

		Assert.Equal(
			"{\"total\":3,\"truncated\":false,\"symbolsLoaded\":true,\"symbols\":[" +
			"{\"name\":\"game.Health\",\"address\":\"140001000\"}," +
			"{\"name\":\"kernel32.GetTickCount\",\"address\":\"7FFB10001000\",\"module\":\"kernel32\",\"size\":16}," +
			"{\"name\":\"hpAlloc\",\"address\":\"20000000\",\"size\":4096,\"registered\":true}]," +
			"\"nextOffset\":2}", json);
	}

	private SymbolTools Tools(ModuleSymbolTarget target)
	{
		return new SymbolTools(target.Dispatch, new McpFilePaths(new McpFileOptions(),
			Path.Combine(_scratch.Root, "registry"), Path.Combine(_scratch.Root, "data")));
	}
}
