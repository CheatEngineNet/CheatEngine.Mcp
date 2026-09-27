using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>The exposure switches: the gate, the declarative call-tool filter, the tool metadata and the Lua scan.</summary>
public sealed class FeatureGateTests
{
	[Fact]
	public async Task CallTool_DisabledFeature_RefusesBeforeBindingAndDispatch()
	{
		await using GatedActivation activation =
			await GatedActivation.StartAsync(new McpFeatureOptions { EnableKernelAccess = false });

		// Arguments that would fail binding prove the refusal comes first.
		foreach (string arguments in new[] { """{"count":2}""", """{"count":"not a number"}""", "{}" })
		{
			CallToolResult result = await activation.Pipeline.CallAsync(GatedProbeTool.Name, arguments);

			ToolError error = TestMcpPipeline.AssertError(result, ToolErrorKind.CapabilityDisabled);
			Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
			Assert.False(error.Retryable);
			Assert.Equal($"{GatedProbeTool.Name} is disabled by the Mcp:EnableKernelAccess setting.", error.Message);
			Assert.Contains("Mcp:EnableKernelAccess", error.Hint, StringComparison.Ordinal);
		}

		Assert.Equal(0, activation.Dispatcher.Calls);
	}

	[Fact]
	public async Task CallTool_EnabledFeatures_BindAndDispatchOnce()
	{
		await using GatedActivation activation = await GatedActivation.StartAsync(new McpFeatureOptions());

		CallToolResult result = await activation.Pipeline.CallAsync(GatedProbeTool.Name, """{"count":3}""");

		Assert.NotEqual(true, result.IsError);
		Assert.Equal(3, Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("count").GetInt32());
		Assert.Equal(1, activation.Dispatcher.Calls);
	}

	[Fact]
	public async Task CallTool_GatedToolWithoutAGate_IsRefusedAsNotStarted()
	{
		CheatEngineMcpPrimitiveOptions manifest = CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog,
			static builder => builder.AddToolType<StaticGatedProbeTool>()
				.AddJsonTypeInfoResolver(TestJsonContext.Default));
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync(manifest, McpPrimitiveBinding.Catalog);

		CallToolResult result = await pipeline.CallAsync(StaticGatedProbeTool.Name, """{"text":"x"}""");

		ToolError error = TestMcpPipeline.AssertError(result, ToolErrorKind.Internal);
		Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
		Assert.Contains("EnableUnsafeLua", error.Message, StringComparison.Ordinal);
		Assert.Equal(0, StaticGatedProbeTool.Calls);
	}

	[Fact]
	public void Catalog_GatedTool_ListsItsRequirementsInMetaWithoutConstruction()
	{
		CheatEngineMcpPrimitiveOptions manifest = CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog,
			static builder => builder.AddToolType<GatedProbeTool>().AddToolType<ContractProbeTool>());

		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(manifest);

		Tool gated = catalog.Tools.Single(static tool => tool.Name == GatedProbeTool.Name);
		Assert.True(JsonNode.DeepEquals(
			JsonNode.Parse(
				"""{"cheatengine/requires":["target_code_execution","kernel_access"],"cheatengine/dispatchClass":"short"}"""),
			gated.Meta));
		Assert.All(catalog.Tools.Where(static tool => tool.Name != GatedProbeTool.Name),
			static tool => Assert.False(tool.Meta?.ContainsKey(McpFeatureGate.RequiresMetaKey) ?? false));
	}

	[Fact]
	public async Task ListTools_GatedTool_PublishesTheSameMetaAsTheCatalog()
	{
		await using GatedActivation activation = await GatedActivation.StartAsync(new McpFeatureOptions());

		IList<McpClientTool> tools =
			await activation.Pipeline.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

		JsonObject? meta = Assert.Single(tools).ProtocolTool.Meta;
		Assert.Equal(["target_code_execution", "kernel_access"],
			meta![McpFeatureGate.RequiresMetaKey]!.AsArray().Select(static value => value!.GetValue<string>()));
	}

	[Fact]
	public void Resolve_GatedToolsWithoutAGate_FailsTheActivation()
	{
		ServiceCollection activation = new();
		new CheatEngineMcpBuilder(activation, CheatEngineMcpMode.Backend).AddToolType<StaticGatedProbeTool>();
		activation.AddOptions<CheatEngineMcpPrimitiveOptions>();
		using ServiceProvider root = activation.BuildServiceProvider();
		using IServiceScope scope = root.CreateScope();

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
			McpPrimitiveTargets.Resolve(scope.ServiceProvider,
				root.GetRequiredService<IOptions<CheatEngineMcpPrimitiveOptions>>().Value));

		Assert.Contains("AddExecutionServices", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Gate_Switches_AreReportedAndRequiredIndependently()
	{
		McpFeatureGate gate = new(Options.Create(new McpFeatureOptions
		{
			EnableAutoAssembler = false, EnableKernelAccess = false
		}));

		Assert.True(gate.IsEnabled(McpFeature.UnsafeLua));
		Assert.False(gate.IsEnabled(McpFeature.AutoAssembler));
		Assert.True(gate.IsEnabled(McpFeature.TargetCodeExecution));
		Assert.False(gate.IsEnabled(McpFeature.KernelAccess));
		Assert.Equal(new McpFeatureSummary(true, false, true, false), gate.Snapshot());
		gate.Require(McpFeature.UnsafeLua, "lua_execute");
		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => gate.Require(McpFeature.AutoAssembler, "asm_apply_script"));
		Assert.Equal(ToolErrorKind.CapabilityDisabled, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Equal("asm_apply_script is disabled by the Mcp:EnableAutoAssembler setting.", exception.Error.Message);
		Assert.Equal("Set Mcp:EnableAutoAssembler to true in appsettings.json, then disable and re-enable the plugin.",
			exception.Error.Hint);
	}

	[Fact]
	public void Gate_OptionsValue_IsReadOnceAtConstruction()
	{
		McpFeatureOptions options = new();
		McpFeatureGate gate = new(Options.Create(options));

		options.EnableKernelAccess = false;

		Assert.True(gate.IsEnabled(McpFeature.KernelAccess));
	}

	[Theory]
	[InlineData(McpFeature.UnsafeLua, "EnableUnsafeLua", "unsafe_lua")]
	[InlineData(McpFeature.AutoAssembler, "EnableAutoAssembler", "auto_assembler")]
	[InlineData(McpFeature.TargetCodeExecution, "EnableTargetCodeExecution", "target_code_execution")]
	[InlineData(McpFeature.KernelAccess, "EnableKernelAccess", "kernel_access")]
	public void Names_EveryFeature_HasItsSettingAndContractName(McpFeature feature, string setting, string contract)
	{
		Assert.Equal(setting, McpFeatureGate.SettingName(feature));
		Assert.Equal(contract, McpFeatureGate.ContractName(feature));
		Assert.NotNull(typeof(McpFeatureOptions).GetProperty(setting));
	}

	[Fact]
	public void LuaFeatureScan_SensitiveApis_AreFoundOncePerFeatureInOrder()
	{
		const string body = """
		                    local ok = autoAssemble(a[1]) and autoAssembleCheck(a[1])
		                    -- a comment naming loadTable still counts
		                    local r = obj.executeCodeLocalEx(a[2]) or injectDLL(a[3])
		                    dbvm_watch_reads(1); dbk_writesIgnored(2)
		                    return {ok, r}
		                    """;

		Assert.Equal([
			McpFeature.UnsafeLua, McpFeature.AutoAssembler, McpFeature.TargetCodeExecution,
			McpFeature.KernelAccess
		], LuaFeatureScan.Scan(body));
	}

	[Theory]
	[InlineData("return readBytes(a[1], a[2], true)")]
	[InlineData("local myautoAssemble, compiled, loadTables = 1, 2, 3 return {}")]
	[InlineData("return {getAddressSafe(a[1]), mcp.hex(a[2])}")]
	[InlineData("")]
	public void LuaFeatureScan_OrdinaryBodies_NeedNoFeature(string body)
	{
		Assert.Empty(LuaFeatureScan.Scan(body));
	}

	/// <summary>An activation with a gated instance tool, served through its live binding over in-memory pipes.</summary>
	private sealed class GatedActivation : IAsyncDisposable
	{
		private readonly ServiceProvider _root;
		private readonly AsyncServiceScope _scope;

		private GatedActivation(ServiceProvider root, AsyncServiceScope scope, RecordingDispatcher dispatcher,
			TestMcpPipeline pipeline)
		{
			_root = root;
			_scope = scope;
			Dispatcher = dispatcher;
			Pipeline = pipeline;
		}

		internal RecordingDispatcher Dispatcher
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

		internal static async Task<GatedActivation> StartAsync(McpFeatureOptions features)
		{
			RecordingDispatcher dispatcher = new();
			ServiceCollection activation = new();
			activation.AddSingleton(ClientTestDouble.Client(dispatcher.Dispatcher, CancellationToken.None));
			activation.AddLogging();
			activation.AddSingleton(Options.Create(features));
			new CheatEngineMcpBuilder(activation, CheatEngineMcpMode.Backend).AddExecutionServices()
				.AddToolType<GatedProbeTool>();
			activation.AddOptions<CheatEngineMcpPrimitiveOptions>();
			ServiceProvider root = activation.BuildServiceProvider(new ServiceProviderOptions
			{
				ValidateOnBuild = true, ValidateScopes = true
			});
			AsyncServiceScope scope = root.CreateAsyncScope();
			CheatEngineMcpPrimitiveOptions manifest =
				root.GetRequiredService<IOptions<CheatEngineMcpPrimitiveOptions>>().Value;
			McpPrimitiveTargets targets = McpPrimitiveTargets.Resolve(scope.ServiceProvider, manifest);
			Assert.NotNull(targets.Gate);
			TestMcpPipeline pipeline =
				await TestMcpPipeline.StartAsync(manifest, McpPrimitiveBinding.FromTargets(targets));
			return new GatedActivation(root, scope, dispatcher, pipeline);
		}
	}
}

/// <summary>A v2 instance tool behind two switches that dispatches through the activation's facade.</summary>
[McpServerToolType]
public sealed class GatedProbeTool
{
	internal const string Name = CheatEngineToolNames.KernelReadPhysical;

	private readonly ToolDispatch _dispatch;

	public GatedProbeTool(ToolDispatch dispatch)
	{
		_dispatch = dispatch;
	}

	[McpServerTool(Name = Name, Title = "Read a gated probe", ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Echoes a count through one dispatch behind the kernel access switch.")]
	[RequiresFeature(McpFeature.KernelAccess)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	public ContractProbeResult Read([Description("The count to echo.")] int count,
		CancellationToken cancellationToken)
	{
		return _dispatch.Run(Name, _ => new ContractProbeResult("gated", [], false, count, null), cancellationToken);
	}
}

/// <summary>A static gated tool, callable through the catalog binding, which has no gate.</summary>
[McpServerToolType]
public sealed class StaticGatedProbeTool
{
	internal const string Name = CheatEngineToolNames.LuaExecute;
	private static int calls;

	internal static int Calls => Volatile.Read(ref calls);

	[McpServerTool(Name = Name, Title = "Execute a gated probe", ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Echoes its text behind the unsafe Lua switch.")]
	[RequiresFeature(McpFeature.UnsafeLua)]
	public static ContractProbeResult Execute([Description("Text echoed back.")] string text)
	{
		Interlocked.Increment(ref calls);
		return new ContractProbeResult(text, [], false, 1, null);
	}
}
