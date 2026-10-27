using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Processes;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Core;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Structures;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools.Structures;

/// <summary>Direct production adapters over a fixed-Lua and process client double.</summary>
public sealed class StructureInterchangeToolsTests : IDisposable
{
	private readonly McpFileScratch _scratch = new();
	public void Dispose() => _scratch.Dispose();

	[Fact]
	public void Export_ProductionAdapter_WritesNormalizedV1ClosureAndExactLuaBounds()
	{
		string folder = _scratch.CreateFolder("files");
		string path = Path.Combine(folder, "closure.json");
		InterchangeHarness harness = new(8)
		{
			Lua = _ => """{"layouts":[{"name":"Root","internal":false,"fields":[{"index":0,"offset":0,"name":"child","vartype":12,"display":null,"byteSize":8,"childStructure":"Child","childStructureStart":4,"customType":null,"bitStart":null,"bitSize":null,"nested":false}]},{"name":"Child","internal":true,"fields":[]}]}"""
		};
		StructureInterchangeResult result = new StructureInterchangeTools(harness.Dispatch, Files(folder)).Export(["Root"], path, cancellationToken: TestContext.Current.CancellationToken);
		Assert.Equal(("closure.json", 1, 8, 2), (result.FileName, result.Version, result.PointerWidth, result.LayoutCount));
		Assert.Equal(["Root"], result.Names);
		Assert.Contains("[2] = 128, [3] = 4096", Assert.Single(harness.LuaCalls).Arguments, StringComparison.Ordinal);
		using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(path));
		Assert.Equal(1, saved.RootElement.GetProperty("version").GetInt32());
		Assert.Equal(8, saved.RootElement.GetProperty("pointerWidth").GetInt32());
		Assert.Equal("Child", saved.RootElement.GetProperty("layouts")[0].GetProperty("fields")[0].GetProperty("childStructure").GetString());
	}

	[Fact]
	public void Import_ProductionAdapter_ValidFilePassesEncodedNestedReferencesToOneLuaMutation()
	{
		string folder = _scratch.CreateFolder("files");
		string path = Path.Combine(folder, "input.json");
		File.WriteAllText(path, """{"version":1,"pointerWidth":8,"roots":["A"],"layouts":[{"name":"A","internal":false,"fields":[{"offset":"0","name":"b","valueType":"Pointer","display":null,"byteSize":null,"childStructure":"B","childStructureStart":"4","customType":null,"bitStart":null,"bitSize":null}]},{"name":"B","internal":true,"fields":[]}]}""");
		InterchangeHarness harness = new(8)
		{
			Lua = _ => """{"names":["A","B"],"layoutCount":2}"""
		};
		StructureInterchangeResult result = new StructureInterchangeTools(harness.Dispatch, Files(folder)).Import(path, TestContext.Current.CancellationToken);
		Assert.Equal(("input.json", 1, 8, 2), (result.FileName, result.Version, result.PointerWidth, result.LayoutCount));
		Assert.Equal(["A"], result.Names);
		Assert.Contains("local a = { n = 1, [1] = {{\"A\",false,{{0,\"b\",12,nil,nil,2,4,},},},{\"B\",true,{},},} };",
			Assert.Single(harness.LuaCalls).Arguments, StringComparison.Ordinal);
		Assert.Equal(1, harness.Dispatcher.Calls);
	}

	[Theory]
	[InlineData(600, true)]
	[InlineData(700, false)]
	public void Export_UnicodeArgumentBudget_OnlyPublishesRestorableFiles(int fieldCount, bool fits)
	{
		string folder = _scratch.CreateFolder("files");
		string path = Path.Combine(folder, "bounded.json");
		File.WriteAllText(path, "existing destination");
		StructureInterchangeLuaField[] fields = [.. Enumerable.Range(0, fieldCount).Select(index =>
			new StructureInterchangeLuaField(index, index * 4, new string('界', 256), 2, "dtUnsignedInteger", 4,
				null, null, null, null, null, false))];
		StructureInterchangeLuaExport copy = new([new("A", false, fields), new("B", false, fields)]);
		string reply = JsonSerializer.Serialize(copy, StructureInterchangeJsonContext.Default.StructureInterchangeLuaExport);
		InterchangeHarness harness = new(8)
		{
			Lua = _ => reply
		};
		StructureInterchangeTools tools = new(harness.Dispatch, Files(folder));

		if (fits)
		{
			StructureInterchangeResult result = tools.Export(["A", "B"], path, overwrite: true, TestContext.Current.CancellationToken);
			Assert.Equal(2, result.LayoutCount);
			using JsonDocument saved = JsonDocument.Parse(File.ReadAllBytes(path));
			Assert.Equal(fieldCount, saved.RootElement.GetProperty("layouts")[0].GetProperty("fields").GetArrayLength());
		}
		else
		{
			CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
				tools.Export(["A", "B"], path, overwrite: true, TestContext.Current.CancellationToken));
			Assert.Equal(ToolErrorKind.InvalidArgument, error.Error.Kind);
			Assert.Contains("Lua arguments exceed", error.Error.Message, StringComparison.Ordinal);
			Assert.Equal("existing destination", File.ReadAllText(path));
		}
		Assert.Single(harness.LuaCalls);
		Assert.Single(Directory.EnumerateFiles(folder));
	}

	[Fact]
	public void Import_PointerWidthMismatch_IsRefusedBeforeLuaMutation()
	{
		string folder = _scratch.CreateFolder("files");
		string path = Path.Combine(folder, "x86.json");
		File.WriteAllText(path, Valid(4));
		InterchangeHarness harness = new(8);
		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() => new StructureInterchangeTools(harness.Dispatch, Files(folder)).Import(path, TestContext.Current.CancellationToken));
		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted), (error.Error.Kind, error.Error.HostEffect));
		Assert.Empty(harness.LuaCalls);
	}

	[Fact]
	public void Export_TargetSwitchDuringLua_DiscardsSnapshotBeforeFilePublication()
	{
		string folder = _scratch.CreateFolder("files");
		string path = Path.Combine(folder, "discard.json");
		InterchangeHarness harness = new(8)
		{
			Lua = _ => """{"layouts":[{"name":"A","internal":false,"fields":[]}]}""",
			OnLua = static h => h.Epoch++
		};
		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() => new StructureInterchangeTools(harness.Dispatch, Files(folder)).Export(["A"], path, cancellationToken: TestContext.Current.CancellationToken));
		Assert.Equal((ToolErrorKind.TargetChanged, ToolHostEffect.Completed), (error.Error.Kind, error.Error.HostEffect));
		Assert.False(File.Exists(path));
	}

	[Fact]
	public void Import_TargetSwitchAfterNativeMutation_ReportsCompletedEffect()
	{
		string folder = _scratch.CreateFolder("files");
		string path = Path.Combine(folder, "input.json");
		File.WriteAllText(path, Valid(8));
		InterchangeHarness harness = new(8)
		{
			Lua = _ => """{"names":["A"],"layoutCount":1}""",
			OnLua = static h => h.Epoch++
		};
		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() => new StructureInterchangeTools(harness.Dispatch, Files(folder)).Import(path, TestContext.Current.CancellationToken));
		Assert.Equal((ToolErrorKind.TargetChanged, ToolHostEffect.Completed), (error.Error.Kind, error.Error.HostEffect));
		Assert.Single(harness.LuaCalls);
	}

	[Fact]
	public void Export_PreCancelled_DoesNotOpenDestinationOrDispatchLua()
	{
		string folder = _scratch.CreateFolder("files");
		string path = Path.Combine(folder, "empty.json");
		using CancellationTokenSource cancelled = new();
		cancelled.Cancel();
		InterchangeHarness harness = new(8);
		Assert.Throws<OperationCanceledException>(() => new StructureInterchangeTools(harness.Dispatch, Files(folder)).Export(["A"], path, cancellationToken: cancelled.Token));
		Assert.Empty(harness.LuaCalls);
		Assert.False(File.Exists(path));
	}

	[Fact]
	public void Import_InvalidJson_IsRefusedBeforeLuaMutation()
	{
		string folder = _scratch.CreateFolder("files");
		string path = Path.Combine(folder, "bad.json");
		File.WriteAllText(path, "{not json}");
		InterchangeHarness harness = new(8);
		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() => new StructureInterchangeTools(harness.Dispatch, Files(folder)).Import(path, TestContext.Current.CancellationToken));
		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted), (error.Error.Kind, error.Error.HostEffect));
		Assert.Empty(harness.LuaCalls);
	}

	private static string Valid(int width) => $"{{\"version\":1,\"pointerWidth\":{width},\"roots\":[\"A\"],\"layouts\":[{{\"name\":\"A\",\"internal\":false,\"fields\":[]}}]}}";
	private McpFilePaths Files(params string[] roots) => new(new McpFileOptions { AllowedRoots = roots }, _scratch.CreateFolder("registry"), _scratch.CreateFolder("data"));

	private sealed class InterchangeHarness
	{
		internal InterchangeHarness(int pointerWidth)
		{
			ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, arguments) =>
			{
				Assert.Equal(nameof(ILuaClient.Execute), method.Name);
				object operation = arguments![0]!;
				Type type = operation.GetType();
				string source = (string) type.GetProperty("Source")!.GetValue(operation)!;
				JsonTypeInfo resultType = (JsonTypeInfo) type.GetProperty("ResultType")!.GetValue(operation)!;
				StructureLuaCall call = new((string) type.GetProperty("Operation")!.GetValue(operation)!, source);
				LuaCalls.Add(call);
				OnLua?.Invoke(this);
				object reply = Lua?.Invoke(call) ?? throw new InvalidOperationException("No Lua reply.");
				Type shape = typeof(LuaJsonResult<>).MakeGenericType(resultType.Type);
				return Activator.CreateInstance(shape, JsonSerializer.Deserialize((string) reply, resultType), null, 0);
			});
			PointerSize bitness = pointerWidth == 4 ? PointerSize.Bit32 : PointerSize.Bit64;
			IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, _) => method.Name == nameof(IProcessClient.GetCurrentProcess)
				? new ProcessSnapshot(new TargetProcessId(7), "owned.exe", null, TargetBackend.LocalProcess, pointerWidth == 4 ? CheatEngineArchitecture.X86 : CheatEngineArchitecture.X64, bitness, pointerWidth, null, Epoch)
				: throw new InvalidOperationException($"Unexpected process call {method.Name}."));
			Client = ClientTestDouble.Client(Dispatcher.Dispatcher, CancellationToken.None, (nameof(ICheatEngineClient.Lua), lua), (nameof(ICheatEngineClient.Processes), processes));
			IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
			Dispatch = new ToolDispatch(Client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), options, new DispatchStatistics(options), TimeProvider.System, new RecordingLogger<ToolDispatch>(), new PluginFixedLuaExecutor(Client));
		}
		internal RecordingDispatcher Dispatcher { get; } = new();
		internal ICheatEngineClient Client
		{
			get;
		}
		internal ToolDispatch Dispatch
		{
			get;
		}
		internal Func<StructureLuaCall, object>? Lua
		{
			get; set;
		}
		internal Action<InterchangeHarness>? OnLua
		{
			get; set;
		}
		internal long Epoch { get; set; } = 3;
		internal List<StructureLuaCall> LuaCalls { get; } = [];
	}
}
