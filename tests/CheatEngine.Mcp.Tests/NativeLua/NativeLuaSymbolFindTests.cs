using System.Collections.Immutable;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tests.Tools.Modules;
using CheatEngine.Mcp.Tools.Symbol;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     The fixed <c>symbol_find</c> body, run against a stubbed main symbol list, registered symbols and registered
///     symbol lists.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private const string SymbolFindStubs = """
	                                       done = false
	                                       copies = 0
	                                       lookups = 0
	                                       symbols = {
	                                           ['kernel32.CreateFileW'] = 0x7FFB10001000,
	                                           ['KERNEL32.createfilea'] = 0x7FFB10001100,
	                                           ['game.PlayerHealth'] = 0x140001000,
	                                           ['game.a%b.c'] = 0x140001100,
	                                           ['game.operator()'] = 0x140001200,
	                                           ['engine.HealthBar'] = 0x7FF700001000,
	                                           ['ntoskrnl.KeBugCheckEx'] = 0xFFFFF80000001000
	                                       }
	                                       infos = {
	                                           ['kernel32.createfilew'] = {modulename = 'kernel32', searchkey = 'kernel32.CreateFileW', address = 0x7FFB10001000, symbolsize = 32},
	                                           ['kernel32.createfilea'] = {modulename = 'kernel32', searchkey = 'kernel32.CreateFileA', address = 0x7FFB10009000, symbolsize = 8},
	                                           ['game.playerhealth'] = {modulename = 'game', searchkey = 'game.PlayerHealth', address = 0x140001000, symbolsize = 0},
	                                           ['engine.healthbar'] = {modulename = 'engine', searchkey = 'engine.HealthBar', address = 0x7FF700001000, symbolsize = 16}
	                                       }
	                                       registered = {
	                                           {symbolname = 'playerHealthPtr', address = 0x20000000, allocsize = 8, donotsave = true},
	                                           {symbolname = 'game.PlayerHealth', address = 0x140001000}
	                                       }
	                                       mainList = {
	                                           getSymbolList = function() copies = copies + 1 return symbols end,
	                                           getSymbolFromString = function(name) lookups = lookups + 1 return infos[string.lower(name)] end
	                                       }
	                                       getMainSymbolList = function() return mainList end
	                                       enumRegisteredSymbols = function() return registered end
	                                       symbolsDoneLoading = function() return done end
	                                       lists = {}
	                                       enumRegisteredSymbolLists = function() return lists end
	                                       """;

	/// <summary>
	///     Two registered symbol lists, as <c>{$C}</c> Auto Assembler code and <c>createSymbolList(t, name)</c> register
	///     them: <c>hpFilter</c> of the C code, and one symbol that repeats the main list's and one that repeats a
	///     registered symbol, both at the same address.
	/// </summary>
	private const string SymbolListStubs = """
	                                       listCopies = 0
	                                       ccode = {
	                                           getSymbolList = function()
	                                               listCopies = listCopies + 1
	                                               return {hpFilter = 0x30000000, ['engine.HealthBar'] = 0x7FF700001000}
	                                           end,
	                                           getSymbolFromString = function(name)
	                                               if name ~= 'hpFilter' then return nil end
	                                               return {modulename = 'ccode', address = 0x30000000, symbolsize = 64}
	                                           end
	                                       }
	                                       luaList = {
	                                           getSymbolList = function()
	                                               listCopies = listCopies + 1
	                                               return {playerHealthPtr = 0x20000000, healthScale = 0x30000100}
	                                           end,
	                                           getSymbolFromString = function() return nil end
	                                       }
	                                       lists = {ccode, luaList}
	                                       """;

	[Fact]
	public void SymbolFind_Stubbed_MatchesAsciiCaseInsensitivelyAndCompletesOnlyMatchingRecords()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SymbolFindStubs);

		SymbolFindResult result = SymbolFindTools().Find("CREATEFILE", cancellationToken: Token);

		Assert.Equal((2, false, false, (int?) null), (result.Total, result.Truncated, result.SymbolsLoaded,
			result.NextOffset));
		// The first record's lookup names another address (a duplicate name), so its module and size stay unknown.
		Assert.Equal(
		[
			new SymbolMatch("KERNEL32.createfilea", "7FFB10001100"),
			new SymbolMatch("kernel32.CreateFileW", "7FFB10001000", "kernel32", 32)
		], result.Symbols);
		Assert.Equal((1L, 2L), (ReadGlobal("copies"), ReadGlobal("lookups")));
	}

	[Theory]
	[InlineData("a%b.", "game.a%b.c")]
	[InlineData("()", "game.operator()")]
	[InlineData("E.h", "engine.HealthBar")]
	[InlineData("%a", null)]
	[InlineData("[a-z]", null)]
	[InlineData("^game", null)]
	[InlineData("x-", null)]
	public void SymbolFind_Stubbed_TreatsPatternCharactersLiterally(string text, string? expected)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SymbolFindStubs);

		SymbolFindResult result = SymbolFindTools().Find(text, cancellationToken: Token);

		Assert.Equal(expected is null ? [] : [expected], result.Symbols.Select(static symbol => symbol.Name));
	}

	[Fact]
	public void SymbolFind_Stubbed_IncludesRegisteredSymbolsOnceAndModuleKeepsItsRange()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SymbolFindStubs);
		ModuleInfo game = new("game.exe", new Address(0x140000000), new MemorySize(0x2000), true,
			"C:\\Games\\Game\\game.exe");

		SymbolFindResult all = SymbolFindTools(game).Find("health", cancellationToken: Token);
		SymbolFindResult inGame = SymbolFindTools(game).Find("health", "game.exe", cancellationToken: Token);

		// The registered game.PlayerHealth names the same address as the listed one, so it is reported once.
		Assert.Equal(
		[
			new SymbolMatch("engine.HealthBar", "7FF700001000", "engine", 16),
			new SymbolMatch("game.PlayerHealth", "140001000", Registered: true),
			new SymbolMatch("playerHealthPtr", "20000000", Size: 8, Registered: true)
		], all.Symbols);
		Assert.Equal(3, all.Total);
		Assert.Equal([new SymbolMatch("game.PlayerHealth", "140001000", Registered: true)], inGame.Symbols);
		Assert.Equal(1, inGame.Total);
	}

	[Fact]
	public void SymbolFind_Stubbed_RangeTestIsUnsignedForHighAddresses()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SymbolFindStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		LuaSymbolFind kernel = SymbolFindRun(dispatch, "bugcheck", 0xFFFFF80000000000UL, 0x100000UL);
		LuaSymbolFind below = SymbolFindRun(dispatch, "bugcheck", 0x140000000UL, 0x2000UL);
		LuaSymbolFind wrapped = SymbolFindRun(dispatch, "health", 0xFFFFF80000000000UL, 0x100000UL);

		Assert.Equal([new SymbolMatch("ntoskrnl.KeBugCheckEx", "FFFFF80000001000")], kernel.Symbols);
		Assert.Empty(below.Symbols);
		Assert.Empty(wrapped.Symbols);
	}

	[Fact]
	public void SymbolFind_Stubbed_StopsAtTheCapAndSkipsTheCopyWhenRegisteredSymbolsFillIt()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SymbolFindStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		LuaSymbolFind exact = SymbolFindRun(dispatch, "health", cap: 3);
		LuaSymbolFind capped = SymbolFindRun(dispatch, "health", cap: 2);
		InstallStubs("copies = 0");
		LuaSymbolFind registeredOnly = SymbolFindRun(dispatch, "health", cap: 1);

		Assert.Equal((3, false), (exact.Total, exact.Truncated));
		Assert.Equal((2, true, 2), (capped.Total, capped.Truncated, capped.Symbols.Length));
		// Registered symbols are collected first, so a capped search always keeps them.
		Assert.Contains(capped.Symbols, static symbol => symbol.Name == "playerHealthPtr");
		Assert.Equal((1, true), (registeredOnly.Total, registeredOnly.Truncated));
		Assert.Equal(0L, ReadGlobal("copies"));
	}

	[Fact]
	public void SymbolFind_Stubbed_SortsByLowercaseNameThenNameThenAddressBeforePaging()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SymbolFindStubs);
		InstallStubs("""
		             symbols = {['game.alpha'] = 0x20, ['game.Alpha'] = 0x10, ['game.alpha2'] = 0x30, ['game.ALPHA'] = 0x05}
		             registered = {{symbolname = 'game.Alpha', address = 0x08}}
		             infos = {}
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		LuaSymbolFind all = SymbolFindRun(dispatch, "alpha");
		InstallStubs("lookups = 0");
		LuaSymbolFind page = SymbolFindRun(dispatch, "alpha", first: 1, limit: 3);

		Assert.Equal(["game.ALPHA:5", "game.Alpha:8", "game.Alpha:10", "game.alpha:20", "game.alpha2:30"],
			all.Symbols.Select(static symbol => symbol.Name + ":" + symbol.Address));
		Assert.Equal(5, page.Total);
		Assert.Equal(["game.Alpha:8:True", "game.Alpha:10:", "game.alpha:20:"],
			page.Symbols.Select(static symbol => symbol.Name + ":" + symbol.Address + ":" + symbol.Registered));
		// Only the page's listed rows are completed, one lookup each.
		Assert.Equal(2L, ReadGlobal("lookups"));
	}

	[Fact]
	public void SymbolFind_Stubbed_ReadsTheLoadingFlagBeforeTheCopy()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SymbolFindStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		InstallStubs("mainList.getSymbolList = function() done = true return symbols end");
		LuaSymbolFind loading = SymbolFindRun(dispatch, "health");
		LuaSymbolFind loaded = SymbolFindRun(dispatch, "health");

		Assert.False(loading.SymbolsLoaded);
		Assert.True(loaded.SymbolsLoaded);
	}

	[Fact]
	public void SymbolFind_MissingLists_AreRefusedButMissingRegisteredSymbolsAreNot()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SymbolFindStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		InstallStubs("enumRegisteredSymbols = function() return nil end");
		LuaSymbolFind unregistered = SymbolFindRun(dispatch, "health");
		InstallStubs("mainList.getSymbolList = function() return nil end");
		CheatEngineToolException noCopy = Assert.Throws<CheatEngineToolException>(() =>
			SymbolFindRun(dispatch, "health"));
		InstallStubs("getMainSymbolList = function() return nil end");
		CheatEngineToolException noList = Assert.Throws<CheatEngineToolException>(() =>
			SymbolFindRun(dispatch, "health"));

		Assert.Equal(["engine.HealthBar", "game.PlayerHealth"],
			unregistered.Symbols.Select(static symbol => symbol.Name));
		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Completed),
			(noCopy.Error.Kind, noCopy.Error.HostEffect));
		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Completed),
			(noList.Error.Kind, noList.Error.HostEffect));
	}

	[Fact]
	public void SymbolFind_RegisteredSymbolLists_AreSearchedLikeCheatEngineResolvesAndCompletedFromTheirList()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SymbolFindStubs);
		InstallStubs(SymbolListStubs);

		SymbolFindResult filter = SymbolFindTools().Find("hpfilter", cancellationToken: Token);
		SymbolFindResult health = SymbolFindTools().Find("health", cancellationToken: Token);

		Assert.Equal([new SymbolMatch("hpFilter", "30000000", "ccode", 64, true)], filter.Symbols);
		Assert.Equal(1, filter.Total);
		// A name and address found in a registered symbol or an earlier list is reported once, from the first source.
		Assert.Equal(
		[
			new SymbolMatch("engine.HealthBar", "7FF700001000", Registered: true),
			new SymbolMatch("game.PlayerHealth", "140001000", Registered: true),
			new SymbolMatch("healthScale", "30000100", Registered: true),
			new SymbolMatch("playerHealthPtr", "20000000", Size: 8, Registered: true)
		], health.Symbols);
		Assert.Equal((4L, 2L), (ReadGlobal("listCopies"), ReadGlobal("copies")));
	}

	[Fact]
	public void SymbolFind_RegisteredSymbolLists_ComeBeforeTheMainListAtTheCap()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SymbolFindStubs);
		InstallStubs(SymbolListStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		LuaSymbolFind capped = SymbolFindRun(dispatch, "health", cap: 3);

		// Two registered symbols and one list symbol fill the cap, so the main list is never copied.
		Assert.Equal((3, true), (capped.Total, capped.Truncated));
		Assert.All(capped.Symbols, static symbol => Assert.True(symbol.Registered));
		Assert.Equal(0L, ReadGlobal("copies"));
	}

	[Fact]
	public void SymbolFind_MissingRegisteredSymbolLists_AreSkipped()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SymbolFindStubs);
		InstallStubs(SymbolListStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		InstallStubs("ccode.getSymbolList = function() return nil end");
		LuaSymbolFind emptyList = SymbolFindRun(dispatch, "health");
		InstallStubs("enumRegisteredSymbolLists = function() return nil end");
		LuaSymbolFind noLists = SymbolFindRun(dispatch, "health");

		Assert.Equal(["engine.HealthBar", "game.PlayerHealth", "healthScale", "playerHealthPtr"],
			emptyList.Symbols.Select(static symbol => symbol.Name));
		Assert.Null(emptyList.Symbols[0].Registered);
		Assert.Equal(["engine.HealthBar", "game.PlayerHealth", "playerHealthPtr"],
			noLists.Symbols.Select(static symbol => symbol.Name));
	}

	[Fact]
	public void SymbolFind_Il2CppMethodListStillEnumerated_ReportsSymbolsNotLoadedAndReadsTheFlagBeforeTheCopy()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SymbolFindStubs);
		InstallStubs("done = true");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		LuaSymbolFind noList = SymbolFindRun(dispatch, "health");
		// As monoscript.lua keeps it: FullyLoaded stays false until its enumeration thread is done, here mid-copy.
		InstallStubs("""
		             monoSymbolList = {FullyLoaded = false}
		             mainList.getSymbolList = function() monoSymbolList.FullyLoaded = true return symbols end
		             """);
		LuaSymbolFind enumerating = SymbolFindRun(dispatch, "health");
		LuaSymbolFind enumerated = SymbolFindRun(dispatch, "health");
		InstallStubs("monoSymbolList = setmetatable({}, {__index = function() error('no such property') end})");
		LuaSymbolFind unreadable = SymbolFindRun(dispatch, "health");
		InstallStubs("monoSymbolList = {FullyLoaded = true}; done = false");
		LuaSymbolFind loading = SymbolFindRun(dispatch, "health");

		Assert.Equal((true, false, true, true, false), (noList.SymbolsLoaded, enumerating.SymbolsLoaded,
			enumerated.SymbolsLoaded, unreadable.SymbolsLoaded, loading.SymbolsLoaded));
		Assert.Equal(noList.Symbols, enumerating.Symbols);
	}

	[Fact]
	public void SymbolFind_PlaceholderSizeOfARegisteredSymbolList_IsOmittedButAMainListSizeIsKept()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SymbolFindStubs);
		// monoscript.lua, the TCC library and createSymbolList(t, name) add their symbols with the size 1.
		InstallStubs("""
		             il2cpp = {
		                 getSymbolList = function()
		                     return {['Game.Player.get_Health'] = 0x180001000, ['Game.Player.set_Health'] = 0x180001100}
		                 end,
		                 getSymbolFromString = function(name)
		                     if name == 'Game.Player.get_Health' then
		                         return {modulename = '', address = 0x180001000, symbolsize = 1}
		                     end
		                     return {modulename = '', address = 0x180001100, symbolsize = 2}
		                 end
		             }
		             lists = {il2cpp}
		             registered = {}
		             symbols = {['game.PlayerHealth'] = 0x140001000}
		             infos = {['game.playerhealth'] = {modulename = 'game', address = 0x140001000, symbolsize = 1}}
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		LuaSymbolFind result = SymbolFindRun(dispatch, "health");

		Assert.Equal(
		[
			new SymbolMatch("Game.Player.get_Health", "180001000", Registered: true),
			new SymbolMatch("Game.Player.set_Health", "180001100", Size: 2, Registered: true),
			new SymbolMatch("game.PlayerHealth", "140001000", "game", 1)
		], result.Symbols);
	}

	/// <summary>Runs the fixed body directly with the tool's argument order.</summary>
	private static LuaSymbolFind SymbolFindRun(ToolDispatch dispatch, string text, ulong? moduleBase = null,
		ulong? moduleSize = null, int cap = SymbolScripts.MaximumFoundSymbols, int first = 0, int limit = 100)
	{
		return dispatch.RunLua(CheatEngineToolNames.SymbolFind, SymbolScripts.Find,
			SymbolLuaJsonContext.Default.LuaSymbolFind, CancellationToken.None, text, cap, first, limit, moduleBase,
			moduleSize);
	}

	/// <summary>The tools over an attached process with the given modules and the native Lua state.</summary>
	private static SymbolTools SymbolFindTools(params ModuleInfo[] modules)
	{
		IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, _) =>
		{
			Assert.Equal(nameof(IProcessClient.GetCurrentProcess), method.Name);
			return new ProcessSnapshot(new TargetProcessId(ModuleSymbolTarget.ProcessId), "game.exe", null,
				TargetBackend.LocalProcess, CheatEngineArchitecture.X64, PointerSize.Bit64, 8, null, 1);
		});
		IInspectionClient inspection = ClientTestDouble.Create<IInspectionClient>((method, arguments) =>
		{
			Assert.Equal(nameof(IInspectionClient.TryGetModules), method.Name);
			arguments![2] = modules.ToImmutableArray();
			arguments[3] = default(CheatEngineFailure);
			return true;
		});
		ICheatEngineClient client = ModuleSymbolClient(inspection, processes);
		return ModuleSymbolTools(ModuleSymbolTarget.CreateDispatch(client));
	}
}
