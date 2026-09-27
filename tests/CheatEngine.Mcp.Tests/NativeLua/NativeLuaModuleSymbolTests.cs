using System.Reflection;
using System.Text;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Core;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tests.Tools.Modules;
using CheatEngine.Mcp.Tools.Symbol;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>The fixed Lua bodies of the <c>symbol_*</c> tools, compiled and run against CE-API stubs.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private const string ModulePreferenceStubs = """
	                                             preference = {'game', 'engine', 'kernel32'}
	                                             setCalls = 0
	                                             getModulePreference = function() return preference end
	                                             setModulePreference = function(value)
	                                                 setCalls = setCalls + 1
	                                                 if type(value) == 'table' then preference = value else table.insert(preference, 1, value) end
	                                             end
	                                             """;

	private const string ReloadStubs = """
	                                   loads = 0; reinitialize = nil; dotnet = nil; done = false
	                                   loadNewSymbols = function() loads = loads + 1 end
	                                   reinitializeSymbolhandler = function(...) reinitialize = table.pack(...) end
	                                   reinitializeDotNetSymbolhandler = function(...) dotnet = table.pack(...) end
	                                   symbolsDoneLoading = function() return done end
	                                   """;

	public static TheoryData<string, string> ModuleSymbolScripts => new()
	{
		{ nameof(SymbolScripts.ListRegistered), SymbolScripts.ListRegistered },
		{ nameof(SymbolScripts.GetModulePreference), SymbolScripts.GetModulePreference },
		{ nameof(SymbolScripts.SetModulePreference), SymbolScripts.SetModulePreference },
		{ nameof(SymbolScripts.Reload), SymbolScripts.Reload },
		{ nameof(SymbolScripts.AddModule), SymbolScripts.AddModule },
		{ nameof(SymbolScripts.EnableSources), SymbolScripts.EnableSources }
	};

	[Theory]
	[MemberData(nameof(ModuleSymbolScripts))]
	public void ModuleSymbolScript_WithRepresentativeArguments_CompilesAndNeverLoadsCode(string name, string body)
	{
		using RuntimeScope scope = CreateScope();
		object?[] arguments = name switch
		{
			nameof(SymbolScripts.ListRegistered) => [SymbolScripts.MaximumRegisteredSymbols],
			nameof(SymbolScripts.SetModulePreference) => [new[] { "game", "engine" }, false],
			nameof(SymbolScripts.Reload) => ["dotnet", "Assembly-CSharp.dll"],
			nameof(SymbolScripts.AddModule) => ["C:\\Games\\game.pdb", 0x140000000UL, true],
			nameof(SymbolScripts.EnableSources) => [true, false],
			_ => []
		};

		LuaFixedScriptAssert.NeverLoadsCode(body);
		Assert.Empty(LuaFeatureScan.Scan(body));
		ModuleSymbolAssertCompiles(name, LuaToolRuntime.BuildSource(body, 100, arguments));
	}

	[Fact]
	public void ModuleSymbolListRegistered_Stubbed_CopiesEveryFieldIntoTheRecord()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             enumRegisteredSymbols = function()
		                 return {
		                     {symbolname = 'playerBase', address = 0x140000010},
		                     {symbolname = 'alloc1', address = -4096, allocsize = 4096, processid = 42, donotsave = true}
		                 }
		             end
		             """);
		SymbolRegistrationTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources(),
			new SymbolRegistrations());

		RegisteredSymbolList list = tools.ListRegistered(cancellationToken: Token);

		Assert.Equal((2, false), (list.Total, list.Truncated));
		Assert.Equal(new RegisteredSymbolEntry("playerBase", false, "140000010"),
			list.Symbols[0]);
		Assert.Equal(new RegisteredSymbolEntry("alloc1", false, "FFFFFFFFFFFFF000", true, 4096, 42),
			list.Symbols[1]);
	}

	[Fact]
	public void ModuleSymbolListRegistered_OverTheCopyBound_IsTruncatedAndANonTableIsRefused()
	{
		using RuntimeScope scope = CreateScope();
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		InstallStubs("""
		             enumRegisteredSymbols = function()
		                 return {{symbolname = 'a', address = 1}, {symbolname = 'b', address = 2}, {symbolname = 'c', address = 3}}
		             end
		             """);

		LuaRegisteredSymbols copied = dispatch.RunLua("symbol_list_registered", SymbolScripts.ListRegistered,
			SymbolLuaJsonContext.Default.LuaRegisteredSymbols, CancellationToken.None, 2);
		InstallStubs("enumRegisteredSymbols = function() return nil end");
		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			dispatch.RunLua("symbol_list_registered", SymbolScripts.ListRegistered,
				SymbolLuaJsonContext.Default.LuaRegisteredSymbols, CancellationToken.None, 2));

		Assert.Equal((3, true), (copied.Count, copied.Truncated));
		Assert.Equal(["a", "b"], copied.Symbols.Select(static symbol => symbol.Name));
		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Completed),
			(refused.Error.Kind, refused.Error.HostEffect));
	}

	[Fact]
	public void ModuleSymbolGetModulePreference_Stubbed_CopiesAtMost1024Names()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(ModulePreferenceStubs);
		SymbolTools tools = ModuleSymbolTools(CreateNativeDispatch(new McpFeatureOptions()));

		ModulePreference current = tools.GetModulePreference(Token);
		InstallStubs("preference = nil");
		ModulePreference missing = tools.GetModulePreference(Token);
		InstallStubs("preference = {}; for i = 1, 1500 do preference[i] = 'm' .. i end");
		ModulePreference large = tools.GetModulePreference(Token);

		Assert.Equal(["game", "engine", "kernel32"], current.Modules);
		Assert.Empty(missing.Modules);
		Assert.Equal((1024, true, "m1024"), (large.Modules.Length, large.Truncated, large.Modules[^1]));
	}

	[Fact]
	public void ModuleSymbolSetModulePreference_Stubbed_MovesModulesFirstOrReplacesAndReadsBack()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(ModulePreferenceStubs);
		SymbolTools tools = ModuleSymbolTools(CreateNativeDispatch(new McpFeatureOptions()));

		ModulePreference moved = tools.SetModulePreference(["ENGINE", "fresh"], cancellationToken: Token);
		ModulePreference single = tools.SetModulePreference(["game"], cancellationToken: Token);
		ModulePreference replaced = tools.SetModulePreference(["solo"], true, Token);

		Assert.Equal(["ENGINE", "fresh", "game", "kernel32"], moved.Modules);
		Assert.Equal(["game", "ENGINE", "fresh", "kernel32"], single.Modules);
		Assert.Equal(["solo"], replaced.Modules);
		Assert.Equal(3L, ReadGlobal("setCalls"));
	}

	[Fact]
	public void ModuleSymbolSetModulePreference_InvalidOrOversizedHostList_RefusesBeforeMutation()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(ModulePreferenceStubs);
		SymbolTools tools = ModuleSymbolTools(CreateNativeDispatch(new McpFeatureOptions()));

		InstallStubs("preference = nil");
		CheatEngineToolException missing = Assert.Throws<CheatEngineToolException>(() =>
			tools.SetModulePreference(["game"], cancellationToken: Token));
		InstallStubs("preference = {}; for i = 1, 1025 do preference[i] = 'm' .. i end");
		CheatEngineToolException oversized = Assert.Throws<CheatEngineToolException>(() =>
			tools.SetModulePreference(["game"], cancellationToken: Token));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.NotStarted),
			(missing.Error.Kind, missing.Error.HostEffect));
		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted),
			(oversized.Error.Kind, oversized.Error.HostEffect));
		Assert.Equal(0L, ReadGlobal("setCalls"));
	}

	[Fact]
	public void ModuleSymbolReload_EachScope_StartsItsReloadWithoutWaiting()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(ReloadStubs);
		SymbolTools tools = ModuleSymbolTools(CreateNativeDispatch(new McpFeatureOptions()));

		SymbolLoadState newModules = tools.Reload(cancellationToken: Token);
		SymbolLoadState all = tools.Reload(SymbolReloadScope.All, cancellationToken: Token);
		SymbolLoadState dotnet = tools.Reload(SymbolReloadScope.Dotnet, cancellationToken: Token);
		object? dotnetArguments = ModuleSymbolRead("dotnet.n");
		InstallStubs("done = true");
		SymbolLoadState module = tools.Reload(SymbolReloadScope.Dotnet, "Assembly-CSharp.dll", Token);

		Assert.Equal(new SymbolLoadState(false, SymbolReloadScope.NewModules), newModules);
		Assert.Equal(1L, ReadGlobal("loads"));
		Assert.Equal(new SymbolLoadState(false, SymbolReloadScope.All), all);
		Assert.Equal((1L, false), (ModuleSymbolRead("reinitialize.n"), ModuleSymbolRead("reinitialize[1]")));
		Assert.Equal(SymbolReloadScope.Dotnet, dotnet.Scope);
		Assert.Equal(0L, dotnetArguments);
		Assert.Equal(new SymbolLoadState(true, SymbolReloadScope.Dotnet), module);
		Assert.Equal("Assembly-CSharp.dll", ModuleSymbolRead("dotnet[1]"));
	}

	[Fact]
	public void ModuleSymbolReload_UnknownScope_IsADeclaredInvalidArgumentBeforeAnyReload()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(ReloadStubs);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			CreateNativeDispatch(new McpFeatureOptions()).RunLua("symbol_reload", SymbolScripts.Reload,
				SymbolLuaJsonContext.Default.LuaSymbolReload, CancellationToken.None, "bogus", null));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0L, ReadGlobal("loads"));
	}

	[Fact]
	public void ModuleSymbolAddModule_Stubbed_PassesThePathBaseAndFlagAndReportsFailures()
	{
		using RuntimeScope scope = CreateScope();
		using McpFilePathsTests.Scratch scratch = new();
		string folder = scratch.CreateFolder("symbols");
		string pdb = Path.Combine(folder, "game.pdb");
		File.WriteAllBytes(pdb, [1]);
		InstallStubs("""
		             result = nil
		             symbolHandlerAddModule = function(path, base, structures)
		                 added = {path = path, base = base, structures = structures}
		                 return result
		             end
		             """);
		IInspectionClient inspection = ClientTestDouble.Create<IInspectionClient>((method, arguments) =>
		{
			Assert.Equal(nameof(IInspectionClient.TryResolveAddress), method.Name);
			arguments![2] = new Address(0x7FF600000000);
			arguments[3] = default(CheatEngineFailure);
			return true;
		});
		ICheatEngineClient client = ModuleSymbolClient(inspection);
		SymbolTools tools = new(ModuleSymbolTarget.CreateDispatch(client), new McpFilePaths(new McpFileOptions(),
			Path.Combine(scratch.Root, "registry"), Path.Combine(scratch.Root, "data")));

		SymbolModuleLoad load = tools.AddModule(pdb, "game.exe", true, Token);
		InstallStubs("result = false");
		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			tools.AddModule(pdb, "game.exe", cancellationToken: Token));
		InstallStubs("symbolHandlerAddModule = function() error('bad pdb') end");
		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() =>
			tools.AddModule(pdb, "game.exe", cancellationToken: Token));

		Assert.Equal(new SymbolModuleLoad(pdb, "7FF600000000", true), load);
		Assert.Equal((pdb, 0x7FF600000000L), (ModuleSymbolRead("added.path"), ModuleSymbolRead("added.base")));
		Assert.Equal(ToolErrorKind.HostRefused, refused.Error.Kind);
		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Started), (failed.Error.Kind, failed.Error.HostEffect));
		Assert.Contains("bad pdb", failed.Error.Message, StringComparison.Ordinal);
	}

	private static SymbolTools ModuleSymbolTools(ToolDispatch dispatch)
	{
		string root = Path.Combine(Path.GetTempPath(), "CheatEngine.Mcp.NativeLua");
		return new SymbolTools(dispatch, new McpFilePaths(new McpFileOptions(), Path.Combine(root, "registry"),
			Path.Combine(root, "data")));
	}

	/// <summary>Loads a script without running it, and fails with Lua's message when it does not compile.</summary>
	private static void ModuleSymbolAssertCompiles(string name, string source)
	{
		LuaAdmissionStatus admission = LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation acquired);
		Assert.Equal(LuaAdmissionStatus.Admitted, admission);
		using LuaRuntimeOperation operation = acquired;
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		LuaStatus status = state.TryLoad(Encoding.UTF8.GetBytes(source),
			Encoding.UTF8.GetBytes("=CheatEngine.Mcp/" + name));
		Assert.True(status.IsOk, status.IsOk ? null : LuaError.FromStack(state, status).Message);
	}

	/// <summary>Reads a Lua expression over the stubs' globals, such as <c>added.path</c>.</summary>
	private static object? ModuleSymbolRead(string expression)
	{
		PluginLuaToolRuntime.LuaToolOperation operation = new("read_expression", "return " + expression);
		Assert.True(operation.TryExecute(ActiveContext.Instance, out object? result, out CheatEngineFailure failure),
			failure.Message);
		return result;
	}

	/// <summary>A Client double whose Lua facade runs typed operations on the test state and whose inspection is given.</summary>
	private static ICheatEngineClient ModuleSymbolClient(IInspectionClient inspection)
	{
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ILuaClient.Execute), method.Name);
			Type resultType = method.GetGenericArguments()[1];
			MethodInfo tryExecute = typeof(ILuaOperation<>).MakeGenericType(resultType)
				.GetMethod(nameof(ILuaOperation<>.TryExecute))!;
			object?[] call = [ActiveContext.Instance, null, null];
			if ((bool) tryExecute.Invoke(arguments![0], call)!)
			{
				return call[1];
			}

			((CheatEngineFailure) call[2]!).Throw();
			return null;
		});
		return ClientTestDouble.Client((nameof(ICheatEngineClient.Lua), lua),
			(nameof(ICheatEngineClient.Inspection), inspection));
	}
}
