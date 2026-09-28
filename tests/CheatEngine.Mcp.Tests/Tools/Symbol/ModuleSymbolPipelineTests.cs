using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Core;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tests.Tools.Modules;
using CheatEngine.Mcp.Tools.Modules;
using CheatEngine.Mcp.Tools.Symbol;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Tools.Symbol;

/// <summary>The module and symbol tools composed and called through a real MCP server and client.</summary>
public sealed class ModuleSymbolPipelineTests : IDisposable
{
	private readonly McpFilePathsTests.Scratch _scratch = new();

	public void Dispose()
	{
		_scratch.Dispose();
	}

	[Fact]
	public async Task ModuleList_Success_IsAnObjectWithHexAddressesAndNoSuccessField()
	{
		ModuleSymbolTarget target = new();
		target.AddModule("game.exe", 0x140000000, 0x5000);
		await using Activation activation = await Activation.StartAsync(target, _scratch);

		CallToolResult result = await activation.Pipeline.CallAsync(CheatEngineToolNames.ModuleList);

		Assert.NotEqual(true, result.IsError);
		JsonElement content = Assert.IsType<JsonElement>(result.StructuredContent);
		Assert.Equal("140000000", content.GetProperty("modules")[0].GetProperty("base").GetString());
		Assert.False(content.TryGetProperty("nextOffset", out _));
		Assert.False(content.TryGetProperty("success", out _));
	}

	[Fact]
	public async Task ModuleGet_UnknownModule_IsANotFoundEnvelope()
	{
		ModuleSymbolTarget target = new();
		await using Activation activation = await Activation.StartAsync(target, _scratch);

		CallToolResult result = await activation.Pipeline.CallAsync(CheatEngineToolNames.ModuleGet,
			"""{"module":"missing.dll"}""");

		ToolError error = TestMcpPipeline.AssertError(result, ToolErrorKind.NotFound);
		Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
		Assert.False(error.Retryable);
		Assert.Contains("module_list", error.Hint, StringComparison.Ordinal);
	}

	[Fact]
	public async Task SymbolResolve_TooManyExpressions_IsALimitEnvelopeWithoutDispatch()
	{
		ModuleSymbolTarget target = new();
		await using Activation activation = await Activation.StartAsync(target, _scratch);
		string expressions = JsonSerializer.Serialize(Enumerable.Repeat("game.exe", 257).ToArray(),
			TestJsonContext.Default.StringArray);

		CallToolResult result = await activation.Pipeline.CallAsync(CheatEngineToolNames.SymbolResolve,
			$$"""{"expressions":{{expressions}}}""");

		ToolError error = TestMcpPipeline.AssertError(result, ToolErrorKind.LimitExceeded);
		Assert.Equal("expressions", error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public async Task SymbolAddModule_UncPath_IsAnInvalidArgumentEnvelopeWithoutDispatch()
	{
		ModuleSymbolTarget target = new();
		await using Activation activation = await Activation.StartAsync(target, _scratch);

		CallToolResult result = await activation.Pipeline.CallAsync(CheatEngineToolNames.SymbolAddModule,
			"""{"path":"\\\\attacker\\share\\game.pdb","baseAddress":"game.exe"}""");

		ToolError error = TestMcpPipeline.AssertError(result, ToolErrorKind.InvalidArgument);
		Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
		Assert.DoesNotContain("attacker", error.Message, StringComparison.Ordinal);
		Assert.Equal(0, target.Dispatcher.Calls);
		Assert.Equal(0, target.TotalClientCalls);
	}

	[Fact]
	public async Task SymbolFind_Success_IsAPagedObjectFromAReadOnlyHostScanTool()
	{
		ModuleSymbolTarget target = new();
		target.LuaResults[typeof(LuaSymbolFind)] = new LuaSymbolFind(3, false, true,
			[new SymbolMatch("game.GetHealth", "140001000", "game", 64)]);
		await using Activation activation = await Activation.StartAsync(target, _scratch);

		CallToolResult result = await activation.Pipeline.CallAsync(CheatEngineToolNames.SymbolFind,
			"""{"nameContains":"health","limit":1}""");
		IList<McpClientTool> tools =
			await activation.Pipeline.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

		Assert.NotEqual(true, result.IsError);
		JsonElement content = Assert.IsType<JsonElement>(result.StructuredContent);
		Assert.Equal((3, 1), (content.GetProperty("total").GetInt32(), content.GetProperty("nextOffset").GetInt32()));
		JsonElement match = content.GetProperty("symbols")[0];
		Assert.Equal(("game.GetHealth", "140001000", 64), (match.GetProperty("name").GetString(),
			match.GetProperty("address").GetString(), match.GetProperty("size").GetInt32()));
		Assert.False(match.TryGetProperty("registered", out _));
		Assert.False(content.TryGetProperty("success", out _));
		Tool find = tools.Single(static tool => tool.Name == CheatEngineToolNames.SymbolFind).ProtocolTool;
		Assert.Equal(McpDispatchClass.HostScan, find.Meta?[McpDispatchClass.MetaKey]?.GetValue<string>());
		Assert.Equal((true, false, true, false), (find.Annotations!.ReadOnlyHint, find.Annotations.DestructiveHint,
			find.Annotations.IdempotentHint, find.Annotations.OpenWorldHint));
	}

	[Fact]
	public async Task SymbolFind_OneCharacter_IsAnInvalidArgumentEnvelopeWithoutDispatch()
	{
		ModuleSymbolTarget target = new();
		await using Activation activation = await Activation.StartAsync(target, _scratch);

		CallToolResult result = await activation.Pipeline.CallAsync(CheatEngineToolNames.SymbolFind,
			"""{"nameContains":"h"}""");

		ToolError error = TestMcpPipeline.AssertError(result, ToolErrorKind.InvalidArgument);
		Assert.Equal("nameContains", error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, target.Dispatcher.Calls);
		Assert.Equal(0, target.TotalClientCalls);
	}

	/// <summary>The module and symbol tools composed in backend mode over a simulated target, served over MCP.</summary>
	private sealed class Activation : IAsyncDisposable
	{
		private readonly ServiceProvider _root;
		private readonly AsyncServiceScope _scope;

		private Activation(ServiceProvider root, AsyncServiceScope scope, TestMcpPipeline pipeline)
		{
			_root = root;
			_scope = scope;
			Pipeline = pipeline;
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

		internal static async Task<Activation> StartAsync(ModuleSymbolTarget target,
			McpFilePathsTests.Scratch scratch)
		{
			ServiceCollection services = new();
			services.AddSingleton(target.Client);
			services.AddLogging();
			services.AddScoped<IFixedLuaExecutor, PluginFixedLuaExecutor>();
			services.AddSingleton(new McpFilePaths(new McpFileOptions(), Path.Combine(scratch.Root, "registry"),
				Path.Combine(scratch.Root, "data")));
			new CheatEngineMcpBuilder(services, CheatEngineMcpMode.Backend).AddExecutionServices().AddModuleTools()
				.AddSymbolTools();
			services.AddOptions<CheatEngineMcpPrimitiveOptions>();
			ServiceProvider root = services.BuildServiceProvider(new ServiceProviderOptions
			{
				ValidateOnBuild = true,
				ValidateScopes = true
			});
			AsyncServiceScope scope = root.CreateAsyncScope();
			CheatEngineMcpPrimitiveOptions manifest =
				root.GetRequiredService<IOptions<CheatEngineMcpPrimitiveOptions>>().Value;
			McpPrimitiveTargets targets = McpPrimitiveTargets.Resolve(scope.ServiceProvider, manifest);
			Assert.IsType<SymbolRegistrationTools>(targets.Get(typeof(SymbolRegistrationTools)));
			Assert.IsType<ModulePatchTools>(targets.Get(typeof(ModulePatchTools)));
			TestMcpPipeline pipeline =
				await TestMcpPipeline.StartAsync(manifest, McpPrimitiveBinding.FromTargets(targets));
			return new Activation(root, scope, pipeline);
		}
	}
}
