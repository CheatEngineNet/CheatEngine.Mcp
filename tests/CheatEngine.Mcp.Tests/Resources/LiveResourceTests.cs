using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Client;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Tables;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Resources;
using CheatEngine.Mcp.Resources.Live;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tests.Tools.Memory;
using CheatEngine.Mcp.Tests.Tools.Modules;
using CheatEngine.Mcp.Tests.Tools.Pointer;
using CheatEngine.Mcp.Tests.Tools.Structures;
using CheatEngine.Mcp.Tools.Asm;
using CheatEngine.Mcp.Tools.Code;
using CheatEngine.Mcp.Tools.Debugger;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.Mcp.Tools.Modules;
using CheatEngine.Mcp.Tools.Pointer;
using CheatEngine.Mcp.Tools.Processes;
using CheatEngine.Mcp.Tools.Record;
using CheatEngine.Mcp.Tools.Runtime;
using CheatEngine.Mcp.Tools.Scan;
using CheatEngine.Mcp.Tools.Speedhack;
using CheatEngine.Mcp.Tools.Structures;
using CheatEngine.Mcp.Tools.Symbol;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Resources;

/// <summary>
///     The live <c>cheatengine://instance/…</c> projections: each returns its source tool's structured result for the
///     same arguments, parses its URI variables into invalid_argument, and reports read failures as JSON-RPC errors.
/// </summary>
public sealed class LiveResourceTests
{
	private const string Instance = McpResourceUris.InstancePrefix;

	/// <summary>Every live template in catalog order, with its name and the tool it projects.</summary>
	private static readonly (string Template, string Name, string Tool)[] Projections =
	[
		("debugger", "instance_debugger", CheatEngineToolNames.DebuggerGetStatus),
		("debugger/breakpoints{?limit}", "instance_breakpoints", CheatEngineToolNames.DebuggerListBreakpoints),
		("disassembly/{address}{?count}", "instance_disassembly", CheatEngineToolNames.CodeDisassemble),
		("jobs", "instance_jobs", CheatEngineToolNames.RuntimeListJobs),
		("memory/{address}{?size}", "instance_memory", CheatEngineToolNames.MemoryRead),
		("modules/{module}", "instance_module", CheatEngineToolNames.ModuleGet),
		("modules/{module}/exports{?offset,limit}", "instance_module_exports",
			CheatEngineToolNames.ModuleListExports),
		("modules{?offset,limit}", "instance_modules", CheatEngineToolNames.ModuleList),
		("patches", "instance_patches", CheatEngineToolNames.AsmListPatches),
		("pointer-maps", "instance_pointer_maps", CheatEngineToolNames.PointerListMaps),
		("pointer-scans", "instance_pointer_scans", CheatEngineToolNames.PointerListScans),
		("pointer-scans/{scanName}/paths{?offset,limit}", "instance_pointer_paths",
			CheatEngineToolNames.PointerListPaths),
		("process", "instance_process", CheatEngineToolNames.ProcessGetCurrent),
		("records/{recordId}", "instance_record", CheatEngineToolNames.RecordGet),
		("records{?offset,limit}", "instance_records", CheatEngineToolNames.RecordList),
		("regions{?offset,limit}", "instance_regions", CheatEngineToolNames.MemoryListRegions),
		("resources", "instance_resources", CheatEngineToolNames.RuntimeListResources),
		("runtime", "instance_runtime", CheatEngineToolNames.RuntimeGetOverview),
		("scanners", "instance_scanners", CheatEngineToolNames.ScanListScanners),
		("scanners/{scannerName}", "instance_scanner", CheatEngineToolNames.ScanGetStatus),
		("speedhack", "instance_speedhack", CheatEngineToolNames.SpeedhackGetState),
		("structures/{structure}{?offset,limit}", "instance_structure", CheatEngineToolNames.StructureGet),
		("structures{?offset,limit}", "instance_structures", CheatEngineToolNames.StructureList),
		("symbols{?offset,limit}", "instance_symbols", CheatEngineToolNames.SymbolListRegistered),
		("threads", "instance_threads", CheatEngineToolNames.ProcessListThreads)
	];

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public static TheoryData<string, string, string> InvalidVariables => new()
	{
		{ "modules", "offset", "" },
		{ "modules", "offset", "abc" },
		{ "modules", "offset", "-1" },
		{ "modules", "offset", "99999999999" },
		{ "modules", "limit", "0" },
		{ "modules", "limit", "1001" },
		{ "module", "module", "" },
		{ "module", "module", "   " },
		{ "exports", "module", "" },
		{ "exports", "limit", "1001" },
		{ "regions", "offset", "1.5" },
		{ "regions", "limit", "2001" },
		{ "memory", "address", "" },
		{ "memory", "size", "0" },
		{ "memory", "size", "16385" },
		{ "memory", "size", "" },
		{ "disassembly", "address", " " },
		{ "disassembly", "count", "0" },
		{ "disassembly", "count", "1025" },
		{ "records", "limit", "1001" },
		{ "record", "recordId", "" },
		{ "record", "recordId", "abc" },
		{ "record", "recordId", "-1" },
		{ "record", "recordId", "99999999999" },
		{ "structures", "limit", "1001" },
		{ "structure", "structure", "" },
		{ "structure", "limit", "1025" },
		{ "scanner", "scannerName", "" },
		{ "symbols", "limit", "1001" },
		{ "paths", "scanName", "" },
		{ "paths", "limit", "501" },
		{ "breakpoints", "limit", "0" },
		{ "breakpoints", "limit", "1025" }
	};

	[Fact]
	public void Catalog_EveryLiveResource_NamesItsSourceToolWithAssistantAnnotations()
	{
		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(CheatEngineMcpComposition.CreateManifest(
			CheatEngineMcpMode.Catalog, static builder => builder.AddResources()));

		Assert.Equal(Projections.Select(static projection => Instance + projection.Template),
			catalog.InstanceResources.Select(static resource => resource.Template.UriTemplate));
		foreach (((string template, string name, string tool), McpCatalogResource resource) in
				 Projections.Zip(catalog.InstanceResources))
		{
			Assert.Equal(name, resource.Template.Name);
			Assert.Equal(McpResourceUris.JsonMimeType, resource.Template.MimeType);
			Assert.Equal(tool, resource.Template.Meta![McpSourceToolAttribute.MetaKey]!.GetValue<string>());
			Assert.Contains(tool, resource.Template.Description, StringComparison.Ordinal);
			Assert.Equal([Role.Assistant], resource.Template.Annotations!.Audience);
			Assert.Equal(0.3f, resource.Template.Annotations.Priority);
			Assert.Equal(template.Contains('{', StringComparison.Ordinal), resource.IsTemplated);
		}
	}

	[Fact]
	public void BackendCatalog_EverySourceTool_IsServedReadOnlyClosedWorldUngatedAndShort()
	{
		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(TestComposition.BackendManifest);

		foreach ((string _, string _, string name) in Projections)
		{
			Tool tool = Assert.Single(catalog.Tools, candidate => candidate.Name == name);
			Assert.True(tool.Annotations!.ReadOnlyHint, name);
			Assert.False(tool.Annotations.OpenWorldHint, name);
			Assert.Equal(McpDispatchClass.Short, tool.Meta![McpDispatchClass.MetaKey]!.GetValue<string>());
			Assert.False(tool.Meta.ContainsKey(McpFeatureGate.RequiresMetaKey), name);
		}
	}

	[Theory]
	[MemberData(nameof(InvalidVariables))]
	public void Read_InvalidVariable_IsInvalidArgumentBeforeAnyDispatch(string resource, string parameter,
		string value)
	{
		ModuleSymbolTarget target = new();
		using LiveSet live = new(target);

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => live.Read(resource, parameter, value));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(parameter, exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, target.Dispatcher.Calls);
		Assert.Equal(0, target.TotalClientCalls);
	}

	[Fact]
	public void Runtime_ProjectsTheOverview()
	{
		RuntimeTarget target = new();
		RuntimeTools tools = new(target.Dispatch, TestRuntime.Info, new TargetResources(), target.Jobs);

		ReadResourceResult result = new RuntimeLiveResources(tools).Runtime(Token);

		AssertProjects(result, Instance + "runtime", tools.GetOverview(Token),
			RuntimeJsonContext.Default.RuntimeOverviewResult);
		Assert.Contains("\"isOpen\":true", Text(result), StringComparison.Ordinal);
	}

	[Fact]
	public void ResourcesAndJobs_ProjectTheRetainedState()
	{
		StateTestHarness harness = new(onMainThread: false, withLedger: false);
		RuntimeTools tools = new(harness.Dispatch, TestRuntime.Info, harness.Resources, harness.Jobs);
		harness.Jobs.StartManaged<long>("probe", TimeSpan.FromSeconds(60), 4,
			static async (_, cancellationToken) => await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
		RuntimeLiveResources live = new(tools);

		ReadResourceResult resources = live.Resources(Token);
		ReadResourceResult jobs = live.Jobs(Token);

		AssertProjects(resources, Instance + "resources", tools.ListResources(Token),
			RuntimeJsonContext.Default.RuntimeResourceList);
		AssertProjects(jobs, Instance + "jobs", tools.ListJobs(Token), RuntimeJsonContext.Default.RuntimeJobList);
		Assert.Contains("\"kind\":\"probe\"", Text(jobs), StringComparison.Ordinal);
		harness.Jobs.Dispose();
	}

	[Fact]
	public void ProcessAndThreads_ProjectTheSelectedProcess()
	{
		RuntimeTarget target = new();
		ModuleSymbolTarget threads = new();
		threads.LuaResults[typeof(ProcessThreadListResult)] = new ProcessThreadListResult([11, 12], false);
		ProcessTools current = new(target.Dispatch, new TargetResources(), new TargetTransitionGuards([]), Files());
		ProcessTools listing = new(threads.Dispatch, new TargetResources(), new TargetTransitionGuards([]), Files());

		ReadResourceResult process = new ProcessLiveResources(current).Process(Token);
		ReadResourceResult list = new ProcessLiveResources(listing).Threads(Token);

		AssertProjects(process, Instance + "process", current.GetCurrent(Token),
			ProcessJsonContext.Default.ProcessCurrentResult);
		AssertProjects(list, Instance + "threads", listing.ListThreads(Token),
			ProcessJsonContext.Default.ProcessThreadListResult);
		Assert.Equal("""{"threads":[11,12],"truncated":false}""", Text(list));
	}

	[Fact]
	public void Modules_BareAndPagedReads_ProjectTheModuleList()
	{
		ModuleSymbolTarget target = new();
		target.AddModule("game.exe", 0x140000000, 0x5000);
		target.AddModule("mono.dll", 0x180000000, 0x2000);
		target.AddModule("user32.dll", 0x7FF800000000, 0x1000);
		ModuleTools tools = new(target.Dispatch);
		ModuleLiveResources live = new(tools, new ModuleExportTools(target.Dispatch), target.Dispatch);

		ReadResourceResult bare = live.Modules(cancellationToken: Token);
		ReadResourceResult page = live.Modules("1", "1", Token);
		ReadResourceResult limited = live.Modules(limit: "2", cancellationToken: Token);

		AssertProjects(bare, Instance + "modules", tools.List(cancellationToken: Token),
			ModuleJsonContext.Default.ModuleList);
		AssertProjects(page, Instance + "modules?offset=1&limit=1", tools.List(offset: 1, limit: 1,
			cancellationToken: Token), ModuleJsonContext.Default.ModuleList);
		AssertProjects(limited, Instance + "modules?limit=2", tools.List(limit: 2, cancellationToken: Token),
			ModuleJsonContext.Default.ModuleList);
		Assert.Contains("\"name\":\"mono.dll\"", Text(page), StringComparison.Ordinal);
		Assert.DoesNotContain("game.exe", Text(page), StringComparison.Ordinal);
	}

	[Fact]
	public void ModuleAndExports_ProjectOneModuleByNameOrExpression()
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
		target.Addresses["sample.dll+4010"] = SampleModule.LoadedBase + 0x4010;
		ModuleTools modules = new(target.Dispatch);
		ModuleExportTools exports = new(target.Dispatch);
		ModuleLiveResources live = new(modules, exports, target.Dispatch);

		ReadResourceResult module = live.Module("sample.dll+4010", Token);
		ReadResourceResult all = live.Exports("SAMPLE.dll", cancellationToken: Token);
		ReadResourceResult page = live.Exports("sample.dll", "1", "2", Token);

		AssertProjects(module, Instance + "modules/sample.dll%2B4010", modules.Get("sample.dll+4010", Token),
			ModuleJsonContext.Default.ModuleDetails);
		AssertProjects(all, Instance + "modules/SAMPLE.dll/exports",
			exports.ListExports("SAMPLE.dll", cancellationToken: Token), ModuleJsonContext.Default.ExportList);
		AssertProjects(page, Instance + "modules/sample.dll/exports?offset=1&limit=2",
			exports.ListExports("sample.dll", null, 1, 2, Token), ModuleJsonContext.Default.ExportList);
		Assert.Equal(2, JsonNode.Parse(Text(page))!["exports"]!.AsArray().Count);
	}

	[Fact]
	public void Module_Missing_IsNotFound()
	{
		ToolDispatch dispatch = ModuleToolTests.LoadedSample().Dispatch;
		ModuleLiveResources live = new(new ModuleTools(dispatch), new ModuleExportTools(dispatch), dispatch);

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => live.Module("missing.dll", Token));

		Assert.Equal(ToolErrorKind.NotFound, exception.Error.Kind);
	}

	[Fact]
	public void Regions_BareReadIsASmallerPageThanTheTool_AndQueryPages()
	{
		TargetDouble target = new()
		{
			Inspection = static (method, _) => method.Name switch
			{
				nameof(IInspectionClient.GetModules) => ImmutableArray<ModuleInfo>.Empty,
				nameof(IInspectionClient.GetMemoryRegions) =>
					ImmutableArray.CreateRange(Enumerable.Range(0, 150).Select(Region)),
				_ => throw new InvalidOperationException($"Unexpected inspection call {method.Name}.")
			}
		};
		MemoryInfoTools tools = new(target.Dispatch);
		MemoryLiveResources live = new(tools, new MemoryReadTools(target.Dispatch));

		ReadResourceResult bare = live.Regions(cancellationToken: Token);
		ReadResourceResult page = live.Regions("148", "5", Token);

		AssertProjects(bare, Instance + "regions", tools.ListRegions(limit: 100, cancellationToken: Token),
			MemoryJsonContext.Default.RegionList);
		AssertProjects(page, Instance + "regions?offset=148&limit=5",
			tools.ListRegions(offset: 148, limit: 5, cancellationToken: Token), MemoryJsonContext.Default.RegionList);
		JsonNode first = JsonNode.Parse(Text(bare))!;
		Assert.Equal((150, 100, 100), (first["total"]!.GetValue<int>(), first["nextOffset"]!.GetValue<int>(),
			first["regions"]!.AsArray().Count));
		Assert.Equal(2, JsonNode.Parse(Text(page))!["regions"]!.AsArray().Count);
	}

	[Fact]
	public void Memory_ReadsBytesWith256ByDefaultOrTheRequestedSize()
	{
		TargetDouble target = new();
		target.Symbols["game.exe+10"] = 0x401010;
		List<int> lengths = [];
		target.Memory = (method, arguments) =>
		{
			Assert.Equal(nameof(IMemoryClient.ReadBytes), method.Name);
			int length = ((MemoryBytesReadRequest) arguments[0]!).Length;
			lengths.Add(length);
			return ImmutableArray.CreateRange(Enumerable.Range(0, length).Select(static index => (byte) index));
		};
		MemoryReadTools reads = new(target.Dispatch);
		MemoryLiveResources live = new(new MemoryInfoTools(target.Dispatch), reads);

		ReadResourceResult bare = live.Memory("game.exe+10", cancellationToken: Token);
		ReadResourceResult sized = live.Memory("401000", "4", Token);

		AssertProjects(bare, Instance + "memory/game.exe%2B10",
			reads.Read("game.exe+10", McpValueType.Bytes, size: 256, cancellationToken: Token),
			MemoryJsonContext.Default.MemoryReadResult);
		AssertProjects(sized, Instance + "memory/401000?size=4",
			reads.Read("401000", McpValueType.Bytes, size: 4, cancellationToken: Token),
			MemoryJsonContext.Default.MemoryReadResult);
		Assert.Equal([256, 4, 256, 4], lengths);
		Assert.Contains("\"value\":\"00 01 02 03\"", Text(sized), StringComparison.Ordinal);
	}

	[Fact]
	public void Disassembly_Decodes20InstructionsByDefaultOrTheRequestedCount()
	{
		DisassemblyTarget target = new();
		CodeTools code = new(target.Dispatch, target.Jobs);
		CodeLiveResources live = new(code);

		ReadResourceResult bare = live.Disassembly("game.exe+1C0", cancellationToken: Token);
		ReadResourceResult three = live.Disassembly("401000", "3", Token);

		AssertProjects(bare, Instance + "disassembly/game.exe%2B1C0", code.Disassemble("game.exe+1C0",
			cancellationToken: Token), CodeJsonContext.Default.CodeDisassembly);
		AssertProjects(three, Instance + "disassembly/401000?count=3", code.Disassemble("401000", 3,
			cancellationToken: Token), CodeJsonContext.Default.CodeDisassembly);
		Assert.Equal(20, JsonNode.Parse(Text(bare))!["instructions"]!.AsArray().Count);
		Assert.Equal(3, JsonNode.Parse(Text(three))!["instructions"]!.AsArray().Count);
		target.Jobs.Dispose();
	}

	[Fact]
	public void RecordsAndRecord_ProjectTheAddressList()
	{
		RecordTarget target = new();
		RecordReadTools tools = new(target.Dispatch);
		RecordLiveResources live = new(tools);

		ReadResourceResult bare = live.Records(cancellationToken: Token);
		ReadResourceResult page = live.Records("1", "1", Token);
		ReadResourceResult record = live.Record("12", Token);

		AssertProjects(bare, Instance + "records", tools.List(0, 100, cancellationToken: Token), RecordJsonContext.Default.RecordPage);
		AssertProjects(page, Instance + "records?offset=1&limit=1", tools.List(1, 1, cancellationToken: Token),
			RecordJsonContext.Default.RecordPage);
		AssertProjects(record, Instance + "records/12", tools.Get([12], Token),
			RecordJsonContext.Default.RecordGetResult);
		Assert.Contains("\"description\":\"Ammo\"", Text(record), StringComparison.Ordinal);
		Assert.Equal(ToolErrorKind.NotFound,
			Assert.Throws<CheatEngineToolException>(() => live.Record("99", Token)).Error.Kind);
	}

	[Fact]
	public void StructuresAndStructure_ProjectStructureDissect()
	{
		StructureToolHarness harness = new()
		{
			Lua = static call => call.Runs(StructureLuaScripts.List)
				? """{"structures":[{"name":"Player","size":16,"elementCount":2}],"total":1,"truncated":false}"""
				: StructureToolHarness.Definition("Player", 2,
					StructureToolHarness.Element(0, 0, "health", 2, "dtSignedInteger", 4),
					StructureToolHarness.Element(1, 4, "armor", 2, "dtSignedInteger", 4))
		};
		StructureTools tools = harness.Structures;
		StructureLiveResources live = new(tools);

		ReadResourceResult list = live.Structures(cancellationToken: Token);
		ReadResourceResult paged = live.Structures("0", "5", Token);
		ReadResourceResult one = live.Structure("Player", cancellationToken: Token);
		ReadResourceResult elements = live.Structure("My Player", "1", "1", Token);

		AssertProjects(list, Instance + "structures", tools.List(cancellationToken: Token),
			StructuresJsonContext.Default.StructurePage);
		AssertProjects(paged, Instance + "structures?offset=0&limit=5", tools.List(null, 0, 5, Token),
			StructuresJsonContext.Default.StructurePage);
		AssertProjects(one, Instance + "structures/Player", tools.Get("Player", cancellationToken: Token),
			StructuresJsonContext.Default.StructureDefinition);
		AssertProjects(elements, Instance + "structures/My%20Player?offset=1&limit=1",
			tools.Get("My Player", 1, 1, cancellationToken: Token), StructuresJsonContext.Default.StructureDefinition);
		Assert.Contains("My Player", harness.LuaCalls[^1].Arguments, StringComparison.Ordinal);
	}

	[Fact]
	public void ScannersAndScanner_ProjectTheScanState()
	{
		ModuleSymbolTarget target = new();
		target.LuaResults[typeof(ScanUiStatus)] =
			new ScanUiStatus("main", "ui", "ResultsReady", false, true, 3, "int32", ModuleSymbolTarget.ProcessId);
		TargetResources resources = new();
		using ScanTools scans = new(target.Dispatch, resources);
		ScanLiveResources live = new(scans, resources);

		ReadResourceResult all = live.Scanners(Token);
		ReadResourceResult main = live.Scanner("main", Token);

		AssertProjects(all, Instance + "scanners", scans.ListScanners(Token), ScanJsonContext.Default.ScanScannerList);
		AssertProjects(main, Instance + "scanners/main", scans.GetStatus("main", Token),
			ScanJsonContext.Default.ScanStatusResult);
		Assert.Contains("\"state\":\"ResultsReady\"", Text(main), StringComparison.Ordinal);
		Assert.Equal(ToolErrorKind.NotFound,
			Assert.Throws<CheatEngineToolException>(() => live.Scanner("my scan", Token)).Error.Kind);
	}

	[Fact]
	public void Patches_ProjectTheRetainedPatchesWithoutADispatch()
	{
		ModuleSymbolTarget target = new();
		AsmTools asm = new(target.Dispatch, new TargetResources());

		ReadResourceResult result = new AsmLiveResources(asm).Patches();

		AssertProjects(result, Instance + "patches", asm.ListPatches(), AsmJsonContext.Default.AsmPatchList);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void Symbols_BareAndPagedReads_ProjectTheRegisteredSymbols()
	{
		ModuleSymbolTarget target = new();
		target.LuaResults[typeof(LuaRegisteredSymbols)] = new LuaRegisteredSymbols(
		[
			new LuaRegisteredSymbol("alpha", "140000010"), new LuaRegisteredSymbol("beta", "140000020"),
			new LuaRegisteredSymbol("gamma", "140000030")
		], 3, false);
		SymbolRegistrationTools symbols = new(target.Dispatch, new TargetResources(), new SymbolRegistrations());
		SymbolLiveResources live = new(symbols);

		ReadResourceResult bare = live.Symbols(cancellationToken: Token);
		ReadResourceResult page = live.Symbols("2", "1", Token);

		AssertProjects(bare, Instance + "symbols", symbols.ListRegistered(cancellationToken: Token),
			SymbolJsonContext.Default.RegisteredSymbolList);
		AssertProjects(page, Instance + "symbols?offset=2&limit=1", symbols.ListRegistered(null, 2, 1, Token),
			SymbolJsonContext.Default.RegisteredSymbolList);
		Assert.Contains("\"name\":\"gamma\"", Text(page), StringComparison.Ordinal);
	}

	[Fact]
	public async Task PointerMapsScansAndPaths_ProjectTheStoredResultsWithoutADispatch()
	{
		await using PointerFixture fixture = new();
		fixture.Maps.CreateMap("run1", cancellationToken: Token);
		fixture.WaitForMap("run1");
		fixture.Scans.FindPaths("hp", "run1", "21020", maxOffset: 0x40, allowNegativeOffsets: true,
			cancellationToken: Token);
		fixture.WaitForScan("hp");
		PointerLiveResources live = new(fixture.Maps, fixture.Scans);
		int dispatches = fixture.Dispatches;

		ReadResourceResult maps = live.Maps();
		ReadResourceResult scans = live.Scans();
		ReadResourceResult paths = live.Paths("hp");
		ReadResourceResult second = live.Paths("hp", "1", "1");

		AssertProjects(maps, Instance + "pointer-maps", fixture.Maps.ListMaps(),
			PointerJsonContext.Default.PointerMapList);
		AssertProjects(scans, Instance + "pointer-scans", fixture.Scans.ListScans(),
			PointerJsonContext.Default.PointerScanList);
		AssertProjects(paths, Instance + "pointer-scans/hp/paths", fixture.Scans.ListPaths("hp"),
			PointerJsonContext.Default.PointerPathPage);
		AssertProjects(second, Instance + "pointer-scans/hp/paths?offset=1&limit=1",
			fixture.Scans.ListPaths("hp", 1, 1), PointerJsonContext.Default.PointerPathPage);
		Assert.Equal(2, JsonNode.Parse(Text(paths))!["total"]!.GetValue<int>());
		Assert.Equal(dispatches, fixture.Dispatches);
		Assert.Equal(ToolErrorKind.NotFound,
			Assert.Throws<CheatEngineToolException>(() => live.Paths("nothing")).Error.Kind);
	}

	[Fact]
	public void DebuggerAndBreakpoints_ProjectTheDebuggerState()
	{
		ModuleSymbolTarget target = new();
		target.LuaResults[typeof(DebuggerStatus)] = new DebuggerStatus(true, true, true, false, false, false);
		target.LuaResults[typeof(DebuggerBreakpointPage)] =
			new DebuggerBreakpointPage([new DebuggerBreakpoint("401000", false)], 1, false);
		TargetResources resources = new();
		using JobRegistry jobs = Jobs(target.Dispatch, resources);
		DebuggerTools debugger = new(target.Dispatch, jobs, resources);
		DebuggerLiveResources live = new(debugger);

		ReadResourceResult status = live.Debugger(Token);
		ReadResourceResult bare = live.Breakpoints(cancellationToken: Token);
		ReadResourceResult limited = live.Breakpoints("8", Token);

		AssertProjects(status, Instance + "debugger", debugger.GetStatus(Token),
			DebuggerJsonContext.Default.DebuggerStatus);
		AssertProjects(bare, Instance + "debugger/breakpoints", debugger.ListBreakpoints(256, Token),
			DebuggerJsonContext.Default.DebuggerBreakpointPage);
		AssertProjects(limited, Instance + "debugger/breakpoints?limit=8", debugger.ListBreakpoints(8, Token),
			DebuggerJsonContext.Default.DebuggerBreakpointPage);
		Assert.Contains("[2] = 256", target.LuaSources[1], StringComparison.Ordinal);
		Assert.Contains("[2] = 8", target.LuaSources[2], StringComparison.Ordinal);
	}

	[Fact]
	public void Speedhack_ProjectsTheConfiguredSpeed()
	{
		ModuleSymbolTarget target = new();
		target.LuaResults[typeof(LuaSpeedhackState)] = new LuaSpeedhackState(2.5, true);
		SpeedhackTools speedhack = new(target.Dispatch, new TargetResources());

		ReadResourceResult result = new SpeedhackLiveResources(speedhack).Speedhack(Token);

		AssertProjects(result, Instance + "speedhack", speedhack.GetState(Token),
			SpeedhackJsonContext.Default.SpeedhackState);
		Assert.Contains("2.5", Text(result), StringComparison.Ordinal);
	}

	[Fact]
	public async Task ReadResource_BareListOverTheWire_EqualsTheToolsStructuredContent()
	{
		await using ModuleActivation activation = await ModuleActivation.StartAsync();

		ReadResourceResult read = await activation.Pipeline.Client.ReadResourceAsync(Instance + "modules",
			cancellationToken: Token);
		CallToolResult call = await activation.Pipeline.CallAsync(CheatEngineToolNames.ModuleList);
		IList<McpClientResource> concrete =
			await activation.Pipeline.Client.ListResourcesAsync(cancellationToken: Token);
		IList<McpClientResourceTemplate> templates =
			await activation.Pipeline.Client.ListResourceTemplatesAsync(cancellationToken: Token);

		TextResourceContents contents = Assert.IsType<TextResourceContents>(Assert.Single(read.Contents));
		Assert.True(JsonNode.DeepEquals(JsonNode.Parse(contents.Text),
			JsonNode.Parse(Assert.IsType<JsonElement>(call.StructuredContent).GetRawText())));
		Assert.Empty(concrete);
		Assert.Equal(
		[
			Instance + "modules/{module}", Instance + "modules/{module}/exports{?offset,limit}",
			Instance + "modules{?offset,limit}"
		], templates.Select(static template => template.UriTemplate).Order(StringComparer.Ordinal));
	}

	[Fact]
	public async Task ReadResource_PercentEncodedPathAndQuery_ReachTheToolDecoded()
	{
		await using ModuleActivation activation = await ModuleActivation.StartAsync();
		activation.Target.Addresses["sample.dll+4010"] = SampleModule.LoadedBase + 0x4010;

		ReadResourceResult module = await activation.Pipeline.Client.ReadResourceAsync(
			Instance + "modules/sample.dll%2B4010", cancellationToken: Token);
		ReadResourceResult exports = await activation.Pipeline.Client.ReadResourceAsync(
			Instance + "modules/sample.dll/exports?offset=3&limit=1", cancellationToken: Token);

		TextResourceContents details = Assert.IsType<TextResourceContents>(Assert.Single(module.Contents));
		Assert.Equal(Instance + "modules/sample.dll%2B4010", details.Uri);
		Assert.Equal("sample.dll", JsonNode.Parse(details.Text)!["name"]!.GetValue<string>());
		TextResourceContents page = Assert.IsType<TextResourceContents>(Assert.Single(exports.Contents));
		Assert.Equal(Instance + "modules/sample.dll/exports?offset=3&limit=1", page.Uri);
		Assert.Equal("Forwarded", JsonNode.Parse(page.Text)!["exports"]![0]!["name"]!.GetValue<string>());
		Assert.Equal(TimeSpan.Zero, exports.TimeToLive);
		Assert.Equal(CacheScope.Private, exports.CacheScope);
	}

	[Theory]
	[InlineData("modules?limit=abc")]
	[InlineData("modules?offset=&limit=1")]
	[InlineData("modules?limit=99999999999")]
	[InlineData("modules?limit=1001")]
	[InlineData("modules/")]
	public async Task ReadResource_InvalidVariable_IsInvalidParamsWithTheContractData(string path)
	{
		await using ModuleActivation activation = await ModuleActivation.StartAsync();

		McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
			await activation.Pipeline.Client.ReadResourceAsync(Instance + path, cancellationToken: Token));

		Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
		Assert.Equal("invalid_argument", exception.Data["kind"]);
		Assert.Equal("not_started", exception.Data["hostEffect"]);
		Assert.Equal(false, exception.Data["retryable"]);
		Assert.Equal(0, activation.Target.Dispatcher.Calls);
	}

	[Theory]
	[InlineData("2025-06-18", McpErrorCode.ResourceNotFound)]
	[InlineData("2026-07-28", McpErrorCode.InvalidParams)]
	public async Task ReadResource_MissingModule_IsNotFoundForTheNegotiatedVersion(string version, McpErrorCode code)
	{
		await using ModuleActivation activation = await ModuleActivation.StartAsync(version);

		McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
			await activation.Pipeline.Client.ReadResourceAsync(Instance + "modules/missing.dll",
				cancellationToken: Token));

		Assert.Equal(code, exception.ErrorCode);
		Assert.Equal("not_found", exception.Data["kind"]);
		Assert.Contains(CheatEngineToolNames.ModuleList, Assert.IsType<string>(exception.Data["hint"]),
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("modules?limit=1&offset=0")]
	[InlineData("modules?other=1")]
	[InlineData("modules/sample.dll/imports")]
	public async Task ReadResource_QueryOutOfOrderOrUnknownPath_IsAnUnknownResource(string path)
	{
		await using ModuleActivation activation = await ModuleActivation.StartAsync();

		McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
			await activation.Pipeline.Client.ReadResourceAsync(Instance + path, cancellationToken: Token));

		Assert.Equal(McpErrorCode.ResourceNotFound, exception.ErrorCode);
		Assert.Equal(0, activation.Target.Dispatcher.Calls);
	}

	private static void AssertProjects<T>(ReadResourceResult result, string uri, T expected, JsonTypeInfo<T> type)
	{
		TextResourceContents contents = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
		Assert.Equal(uri, contents.Uri);
		Assert.Equal(McpResourceUris.JsonMimeType, contents.MimeType);
		Assert.Equal(JsonSerializer.Serialize(expected, type), contents.Text);
		Assert.Equal(TimeSpan.Zero, result.TimeToLive);
		Assert.Equal(CacheScope.Private, result.CacheScope);
	}

	private static string Text(ReadResourceResult result)
	{
		return Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text;
	}

	private static McpFilePaths Files()
	{
		string temporary = Path.GetTempPath();
		return new McpFilePaths(new McpFileOptions(), Path.Combine(temporary, "ce-mcp-test-registry"),
			Path.Combine(temporary, "ce-mcp-test-data"));
	}

	private static JobRegistry Jobs(ToolDispatch dispatch, TargetResources resources)
	{
		return new JobRegistry(dispatch, resources, Options.Create(new McpExecutionOptions()), TimeProvider.System);
	}

	private static MemoryRegionInfo Region(int index)
	{
		Address start = new(0x400000UL + ((ulong) index * 0x1000));
		return new MemoryRegionInfo(start, start, MemoryProtection.ReadWrite, new MemorySize(0x1000),
			MemoryRegionState.Committed, MemoryProtection.ReadWrite, MemoryRegionType.Private, null);
	}

	/// <summary>Every live resource over one module target, to exercise the variable parsing of each.</summary>
	private sealed class LiveSet : IDisposable
	{
		private readonly JobRegistry _jobs;
		private readonly Dictionary<string, Func<string, string, ReadResourceResult>> _reads;
		private readonly ScanTools _scans;

		internal LiveSet(ModuleSymbolTarget target)
		{
			ToolDispatch dispatch = target.Dispatch;
			TargetResources resources = new();
			_jobs = Jobs(dispatch, resources);
			_scans = new ScanTools(dispatch, resources);
			PointerStore store = new();
			ModuleLiveResources modules = new(new ModuleTools(dispatch), new ModuleExportTools(dispatch), dispatch);
			MemoryLiveResources memory = new(new MemoryInfoTools(dispatch), new MemoryReadTools(dispatch));
			CodeLiveResources code = new(new CodeTools(dispatch, _jobs));
			RecordLiveResources records = new(new RecordReadTools(dispatch));
			StructureLiveResources structures = new(new StructureTools(dispatch));
			ScanLiveResources scans = new(_scans, resources);
			SymbolLiveResources symbols =
				new(new SymbolRegistrationTools(dispatch, resources, new SymbolRegistrations()));
			PointerLiveResources pointers = new(
				new PointerMapTools(dispatch, _jobs, store, Options.Create(new McpExecutionOptions()),
					TimeProvider.System), new PointerScanTools(dispatch, _jobs, store, TimeProvider.System));
			DebuggerLiveResources debugger = new(new DebuggerTools(dispatch, _jobs, resources));
			CancellationToken token = Token;
			_reads = new Dictionary<string, Func<string, string, ReadResourceResult>>(StringComparer.Ordinal)
			{
				["modules"] = (name, value) => name == "offset"
					? modules.Modules(value, null, token)
					: modules.Modules(null, value, token),
				["module"] = (_, value) => modules.Module(value, token),
				["exports"] = (name, value) => name == "module"
					? modules.Exports(value, cancellationToken: token)
					: modules.Exports("game.exe", limit: value, cancellationToken: token),
				["regions"] = (name, value) => name == "offset"
					? memory.Regions(value, null, token)
					: memory.Regions(null, value, token),
				["memory"] = (name, value) => name == "address"
					? memory.Memory(value, cancellationToken: token)
					: memory.Memory("401000", value, token),
				["disassembly"] = (name, value) => name == "address"
					? code.Disassembly(value, cancellationToken: token)
					: code.Disassembly("401000", value, token),
				["records"] = (_, value) => records.Records(limit: value, cancellationToken: token),
				["record"] = (_, value) => records.Record(value, token),
				["structures"] = (_, value) => structures.Structures(limit: value, cancellationToken: token),
				["structure"] = (name, value) => name == "structure"
					? structures.Structure(value, cancellationToken: token)
					: structures.Structure("Player", limit: value, cancellationToken: token),
				["scanner"] = (_, value) => scans.Scanner(value, token),
				["symbols"] = (_, value) => symbols.Symbols(limit: value, cancellationToken: token),
				["paths"] = (name, value) => name == "scanName"
					? pointers.Paths(value)
					: pointers.Paths("hp", limit: value),
				["breakpoints"] = (_, value) => debugger.Breakpoints(value, token)
			};
		}

		public void Dispose()
		{
			_scans.Dispose();
			_jobs.Dispose();
		}

		internal ReadResourceResult Read(string resource, string parameter, string value)
		{
			return _reads[resource](parameter, value);
		}
	}

	/// <summary>A Client double for the runtime and current-process tools: one attached x64 process.</summary>
	private sealed class RuntimeTarget
	{
		internal RuntimeTarget()
		{
			CheatEngineVersion version = new(7, 7, 0, 0);
			CheatEngineRuntimeSnapshot snapshot = new(7,
				new CheatEngineRuntimeVersionInfo(version, version, new Version(1, 0), new Version(2, 0), "2.0.0",
					true),
				new CheatEngineRuntimePlatformInfo(default, CheatEngineArchitecture.X64, PointerSize.Bit64,
					TargetBackend.LocalProcess, CheatEngineArchitecture.X64, PointerSize.Bit64, default, false, 8),
				CheatEngine.Client.Runtime.ClientCapabilities.Empty);
			ICheatEngineRuntime runtime = ClientTestDouble.Create<ICheatEngineRuntime>((method, _) =>
				method.Name == nameof(ICheatEngineRuntime.GetSnapshot)
					? snapshot
					: throw new NotSupportedException($"Unexpected runtime call {method.Name}."));
			IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, arguments) =>
			{
				Assert.Equal(nameof(IProcessClient.TryGetCurrentProcess), method.Name);
				arguments![0] = new ProcessSnapshot(new TargetProcessId(42), "game.exe", null,
					TargetBackend.LocalProcess, CheatEngineArchitecture.X64, PointerSize.Bit64, 8, null, 3);
				arguments[1] = default(CheatEngineFailure);
				return true;
			});
			ICheatEngineClient client = ClientTestDouble.Client(new RecordingDispatcher().Dispatcher,
				CancellationToken.None, (nameof(ICheatEngineClient.Runtime), runtime),
				(nameof(ICheatEngineClient.Processes), processes));
			Dispatch = ModuleSymbolTarget.CreateDispatch(client);
			Jobs = LiveResourceTests.Jobs(Dispatch, new TargetResources());
		}

		internal ToolDispatch Dispatch
		{
			get;
		}

		internal JobRegistry Jobs
		{
			get;
		}
	}

	/// <summary>A Client double for disassembly: every address resolves, and every instruction is a one-byte nop.</summary>
	private sealed class DisassemblyTarget
	{
		internal DisassemblyTarget()
		{
			Address current = new(0x401000);
			IInspectionClient inspection = ClientTestDouble.Create<IInspectionClient>(static (method, arguments) =>
			{
				Assert.Equal(nameof(IInspectionClient.TryResolveAddress), method.Name);
				arguments![2] = new Address(0x401000);
				arguments[3] = default(CheatEngineFailure);
				return true;
			});
			IAssemblyClient assembly = ClientTestDouble.Create<IAssemblyClient>((method, arguments) =>
			{
				Assert.Equal(nameof(IAssemblyClient.Disassemble), method.Name);
				current = (Address) arguments![0]!;
				return new AssemblyInstructionSnapshot(current, 1, string.Empty, "90", "nop", [0x90]);
			});
			ICheatEngineClient client = ClientTestDouble.Client(new RecordingDispatcher().Dispatcher,
				CancellationToken.None, (nameof(ICheatEngineClient.Inspection), inspection),
				(nameof(ICheatEngineClient.Assembly), assembly));
			StateTestHarness lua = new();
			lua.Answer<CodeLuaDisassemblyColumns>(_ => new CodeLuaDisassemblyColumns(HexFormat.Address(current), "nop", string.Empty));
			IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
			Dispatch = new ToolDispatch(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), execution,
				new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>(), lua.FixedLua);
			Jobs = LiveResourceTests.Jobs(Dispatch, new TargetResources());
		}

		internal ToolDispatch Dispatch
		{
			get;
		}

		internal JobRegistry Jobs
		{
			get;
		}
	}

	/// <summary>A Client double for the address list: three top-level records with ids 11 to 13.</summary>
	private sealed class RecordTarget
	{
		private static readonly MemoryRecordSnapshot[] Records =
		[
			Snapshot(11, 0, "Health"), Snapshot(12, 1, "Ammo"), Snapshot(13, 2, "Gold")
		];

		internal RecordTarget()
		{
			ITableClient tables = ClientTestDouble.Create<ITableClient>(static (method, arguments) =>
				method.Name switch
				{
					nameof(ITableClient.GetRecordCount) => Records.Length,
					nameof(ITableClient.GetRecordAt) => Records[(int) arguments![0]!],
					nameof(ITableClient.GetRecord) => Find((MemoryRecordId) arguments![0]!),
					_ => throw new NotSupportedException($"Unexpected table call {method.Name}.")
				});
			ICheatEngineClient client = ClientTestDouble.Client(new RecordingDispatcher().Dispatcher,
				CancellationToken.None, (nameof(ICheatEngineClient.Tables), tables));
			Dispatch = ModuleSymbolTarget.CreateDispatch(client);
		}

		internal ToolDispatch Dispatch
		{
			get;
		}

		private static MemoryRecordSnapshot Find(MemoryRecordId id)
		{
			foreach (MemoryRecordSnapshot record in Records)
			{
				if (record.Id == id)
				{
					return record;
				}
			}

			throw new CheatEngineFailure(CheatEngineFailureKind.NotFound, "Tables.GetRecord",
				"No record has that id.", hostEffect: CheatEngineHostEffect.NotStarted).ToException();
		}

		private static MemoryRecordSnapshot Snapshot(int id, int index, string description)
		{
			return new MemoryRecordSnapshot(new MemoryRecordId(id), index,
				new MemoryRecordContentSnapshot(description, "game.exe+10", "0", VariableType.Dword),
				new MemoryRecordStateSnapshot(new Address(0x401000)));
		}
	}

	/// <summary>A backend activation serving the module tools and their live resources over a sample module.</summary>
	private sealed class ModuleActivation : IAsyncDisposable
	{
		private readonly ServiceProvider _root;
		private readonly AsyncServiceScope _scope;

		private ModuleActivation(ModuleSymbolTarget target, ServiceProvider root, AsyncServiceScope scope,
			TestMcpPipeline pipeline)
		{
			Target = target;
			_root = root;
			_scope = scope;
			Pipeline = pipeline;
		}

		internal ModuleSymbolTarget Target
		{
			get;
		}

		internal TestMcpPipeline Pipeline
		{
			get;
		}

		public async ValueTask DisposeAsync()
		{
			await Pipeline.DisposeAsync();
			await _scope.DisposeAsync();
			await _root.DisposeAsync();
		}

		internal static async Task<ModuleActivation> StartAsync(
			string protocolVersion = TestMcpPipeline.DefaultProtocolVersion)
		{
			ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
			ServiceCollection activation = new();
			activation.AddSingleton(target.Client);
			activation.AddLogging();
			new CheatEngineMcpBuilder(activation, CheatEngineMcpMode.Backend).AddExecutionServices()
				.AddJsonTypeInfoResolver(ModuleJsonContext.Default).AddToolType<ModuleTools>()
				.AddToolType<ModuleExportTools>().AddResourceType<ModuleLiveResources>();
			activation.AddOptions<CheatEngineMcpPrimitiveOptions>();
			ServiceProvider root = activation.BuildServiceProvider(new ServiceProviderOptions
			{
				ValidateOnBuild = true,
				ValidateScopes = true
			});
			AsyncServiceScope scope = root.CreateAsyncScope();
			CheatEngineMcpPrimitiveOptions manifest =
				root.GetRequiredService<IOptions<CheatEngineMcpPrimitiveOptions>>().Value;
			McpPrimitiveTargets targets = McpPrimitiveTargets.Resolve(scope.ServiceProvider, manifest);
			TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync(manifest,
				McpPrimitiveBinding.FromTargets(targets), protocolVersion);
			return new ModuleActivation(target, root, scope, pipeline);
		}
	}
}
