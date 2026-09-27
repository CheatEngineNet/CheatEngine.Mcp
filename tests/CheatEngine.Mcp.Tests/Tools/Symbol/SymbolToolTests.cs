using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Core;
using CheatEngine.Mcp.Tests.Tools.Modules;
using CheatEngine.Mcp.Tools.Symbol;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tests.Tools.Symbol;

/// <summary>
///     <c>symbol_resolve</c>, the module preference, <c>symbol_reload</c> and <c>symbol_add_module</c> over a simulated
///     target.
/// </summary>
public sealed class SymbolToolTests : IDisposable
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private readonly McpFilePathsTests.Scratch _scratch = new();

	public void Dispose()
	{
		_scratch.Dispose();
	}

	[Fact]
	public void Resolve_Expressions_ResolveInBandWithNamesSymbolsAndPerItemErrors()
	{
		ModuleSymbolTarget target = new();
		target.Addresses["kernel32.CreateFileW"] = 0x7FFB10001000;
		target.Addresses["7FF600001234"] = 0x7FF600001234;
		target.Names[0x7FFB10001000] = "kernel32.CreateFileW";
		target.Names[0x7FF600001234] = "game.exe+1234";
		target.Symbols["kernel32.CreateFileW"] = new SymbolInfo("kernel32.dll", "CreateFileW",
			new Address(0x7FFB10001000), new MemorySize(0x40));
		target.ResolveFailures["[bad]+8"] = CheatEngineFailureKind.MemoryReadFailed;

		SymbolResolveResult result = Tools(target).Resolve(["kernel32.CreateFileW", "7FF600001234", "nothing", "[bad]+8"], cancellationToken: Token);

		Assert.Equal(4, result.Items.Length);
		Assert.Equal(new SymbolResolution("kernel32.CreateFileW", "7FFB10001000", "kernel32.CreateFileW",
			new SymbolDetails("kernel32.dll", "CreateFileW", "7FFB10001000", 0x40)), result.Items[0]);
		Assert.Equal(new SymbolResolution("7FF600001234", "7FF600001234", "game.exe+1234"),
			result.Items[1]);
		Assert.Equal(ToolErrorKind.NotFound, result.Items[2].Error!.Kind);
		Assert.Null(result.Items[2].Address);
		Assert.Equal(ToolErrorKind.MemoryReadFailed, result.Items[3].Error!.Kind);
		Assert.Equal(1, target.Dispatcher.Calls);
	}

	[Fact]
	public void Resolve_SystemicFailure_FailsTheWholeCall()
	{
		ModuleSymbolTarget target = new();
		target.ResolveFailures["game.exe"] = CheatEngineFailureKind.TargetNotAttached;

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target).Resolve(["game.exe"], cancellationToken: Token));

		Assert.Equal(ToolErrorKind.NotAttached, exception.Error.Kind);
	}

	[Fact]
	public void Resolve_Shallow_ReturnsTheAddressWithoutNameOrSymbolLookups()
	{
		ModuleSymbolTarget target = new();
		target.Addresses["game.exe+10"] = 0x140000010;

		SymbolResolveResult result = Tools(target).Resolve(["game.exe+10"], true, cancellationToken: Token);

		Assert.Equal(new SymbolResolution("game.exe+10", "140000010"), Assert.Single(result.Items));
		Assert.Equal(1, target.Calls(nameof(IInspectionClient.TryResolveAddress)));
		Assert.Equal(0, target.Calls(nameof(IInspectionClient.TryResolveName)));
		Assert.Equal(0, target.Calls(nameof(IInspectionClient.TryGetSymbol)));
	}

	public static TheoryData<string[], ToolErrorKind, string> RefusedExpressions => new()
	{
		{ [], ToolErrorKind.InvalidArgument, "expressions" },
		{ [.. Enumerable.Repeat("game.exe", 257)], ToolErrorKind.LimitExceeded, "expressions" },
		{ ["game.exe", " "], ToolErrorKind.InvalidArgument, "expressions[1]" },
		{ [new string('a', 1025)], ToolErrorKind.InvalidArgument, "expressions[0]" },
		{ ["game\u0000.exe"], ToolErrorKind.InvalidArgument, "expressions[0]" }
	};

	[Theory]
	[MemberData(nameof(RefusedExpressions))]
	public void Resolve_RefusedExpressions_NeverDispatch(string[] expressions, ToolErrorKind kind, string parameter)
	{
		ModuleSymbolTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target).Resolve(expressions, cancellationToken: Token));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(parameter, exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void GetModulePreference_ReturnsTheCopiedList()
	{
		ModuleSymbolTarget target = new();
		target.LuaResults[typeof(ModulePreference)] = new ModulePreference(["game", "engine"], false);

		ModulePreference preference = Tools(target).GetModulePreference(cancellationToken: Token);

		Assert.Equal(["game", "engine"], preference.Modules);
		Assert.Contains("getModulePreference()", Assert.Single(target.LuaSources), StringComparison.Ordinal);
	}

	[Fact]
	public void SetModulePreference_Modules_ReachTheFixedScriptOnlyAsArguments()
	{
		ModuleSymbolTarget target = new();
		target.LuaResults[typeof(ModulePreference)] = new ModulePreference(["engine", "game"], false);

		ModulePreference preference = Tools(target).SetModulePreference(["engine", "game"], true, cancellationToken: Token);

		Assert.Equal(["engine", "game"], preference.Modules);
		string source = Assert.Single(target.LuaSources);
		Assert.Contains("local a = { n = 2, [1] = {\"engine\",\"game\",}, [2] = true }", source, StringComparison.Ordinal);
		Assert.EndsWith(SymbolScripts.SetModulePreference, source, StringComparison.Ordinal);
	}

	public static TheoryData<string[], ToolErrorKind> RefusedPreferences => new()
	{
		{ [], ToolErrorKind.InvalidArgument },
		{ [.. Enumerable.Range(0, 65).Select(static index => $"m{index}")], ToolErrorKind.LimitExceeded },
		{ ["game.exe"], ToolErrorKind.InvalidArgument },
		{ ["game", "GAME"], ToolErrorKind.InvalidArgument },
		{ [" "], ToolErrorKind.InvalidArgument },
		{ [new string('m', 257)], ToolErrorKind.InvalidArgument }
	};

	[Theory]
	[MemberData(nameof(RefusedPreferences))]
	public void SetModulePreference_RefusedModules_NeverDispatch(string[] modules, ToolErrorKind kind)
	{
		ModuleSymbolTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target).SetModulePreference(modules, cancellationToken: Token));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(0, target.Dispatcher.Calls);
		Assert.Empty(target.LuaSources);
	}

	[Theory]
	[InlineData(SymbolReloadScope.NewModules, null, "[1] = \"new_modules\", [2] = nil")]
	[InlineData(SymbolReloadScope.All, null, "[1] = \"all\", [2] = nil")]
	[InlineData(SymbolReloadScope.Dotnet, "Assembly-CSharp.dll", "[1] = \"dotnet\", [2] = \"Assembly-CSharp.dll\"")]
	public void Reload_Scope_StartsTheReloadAndReportsDone(SymbolReloadScope scope, string? module, string arguments)
	{
		ModuleSymbolTarget target = new();
		target.LuaResults[typeof(LuaSymbolReload)] = new LuaSymbolReload(false);

		SymbolLoadState state = Tools(target).Reload(scope, module, cancellationToken: Token);

		Assert.Equal(new SymbolLoadState(false, scope), state);
		Assert.Contains(arguments, Assert.Single(target.LuaSources), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(SymbolReloadScope.All, "game.dll")]
	[InlineData(SymbolReloadScope.Dotnet, " ")]
	[InlineData((SymbolReloadScope) 9, null)]
	public void Reload_RefusedArguments_NeverDispatch(SymbolReloadScope scope, string? module)
	{
		ModuleSymbolTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target).Reload(scope, module, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void AddModule_LocalFile_LoadsTheNormalizedPathAtTheResolvedBase()
	{
		ModuleSymbolTarget target = new();
		target.Addresses["game.exe"] = 0x140000000;
		target.LuaResults[typeof(LuaSymbolModuleLoad)] = new LuaSymbolModuleLoad(true);
		string folder = _scratch.CreateFolder("symbols");
		string pdb = Path.Combine(folder, "game.pdb");
		File.WriteAllBytes(pdb, [1, 2, 3]);

		SymbolModuleLoad load = Tools(target).AddModule(folder.Replace('\\', '/') + "/./game.pdb", "game.exe", true, cancellationToken: Token);

		Assert.Equal(new SymbolModuleLoad(pdb, "140000000", true), load);
		string source = Assert.Single(target.LuaSources);
		Assert.Contains("[2] = 0x140000000, [3] = true", source, StringComparison.Ordinal);
		Assert.EndsWith(SymbolScripts.AddModule, source, StringComparison.Ordinal);
		Assert.Equal(1, target.Dispatcher.Calls);
	}

	public static TheoryData<string> RefusedSymbolPaths => new()
	{
		"\\\\attacker\\share\\game.pdb",
		"\\\\?\\C:\\Games\\game.pdb",
		"\\\\.\\PhysicalDrive0",
		"C:\\Games\\game.pdb:stream",
		"C:\\Games\\NUL",
		"game.pdb"
	};

	[Theory]
	[MemberData(nameof(RefusedSymbolPaths))]
	public void AddModule_RefusedPath_NeverDispatches(string path)
	{
		ModuleSymbolTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target).AddModule(path, "game.exe", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, target.Dispatcher.Calls);
		Assert.Equal(0, target.TotalClientCalls);
	}

	[Fact]
	public void AddModule_ThroughAJunctionOrTheRegistry_NeverDispatches()
	{
		ModuleSymbolTarget target = new();
		string real = _scratch.CreateFolder("real");
		File.WriteAllBytes(Path.Combine(real, "game.pdb"), [1]);
		string link = _scratch.CreateJunction("link", real);
		string registry = _scratch.CreateFolder("registry");
		File.WriteAllBytes(Path.Combine(registry, "game.pdb"), [1]);

		CheatEngineToolException linked = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target).AddModule(Path.Combine(link, "game.pdb"), "game.exe", cancellationToken: Token));
		CheatEngineToolException protectedFile = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target).AddModule(Path.Combine(registry, "game.pdb"), "game.exe", cancellationToken: Token));

		Assert.Contains("reparse point", linked.Error.Message, StringComparison.Ordinal);
		Assert.Contains("protected MCP directory", protectedFile.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void AddModule_MissingFileOrUnresolvedBase_AreNotFound()
	{
		ModuleSymbolTarget target = new();
		string folder = _scratch.CreateFolder("symbols");
		File.WriteAllBytes(Path.Combine(folder, "game.pdb"), [1]);

		CheatEngineToolException missing = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target).AddModule(Path.Combine(folder, "missing.pdb"), "game.exe", cancellationToken: Token));
		CheatEngineToolException unresolved = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target).AddModule(Path.Combine(folder, "game.pdb"), "nowhere", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.NotFound, missing.Error.Kind);
		Assert.Equal(ToolErrorKind.NotFound, unresolved.Error.Kind);
		Assert.Empty(target.LuaSources);
	}

	private SymbolTools Tools(ModuleSymbolTarget target)
	{
		return new SymbolTools(target.Dispatch, new McpFilePaths(new McpFileOptions(),
			Path.Combine(_scratch.Root, "registry"), Path.Combine(_scratch.Root, "data")));
	}
}
