using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Execution;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Lua;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Lua;

using Microsoft.Extensions.Options;

using ToolLuaExecuteOutcome = CheatEngine.Mcp.Tools.Lua.LuaExecuteOutcome;

namespace CheatEngine.Mcp.Tests.Tools.Lua;

/// <summary>
///     Managed-adapter qualification for the two advertised Lua tools. Fixed Lua's native behavior is covered
///     separately; these cases prove the tool-to-dispatch and unsafe-adapter contracts without Cheat Engine.
/// </summary>
public sealed class LuaToolQualificationTests
{
	[Fact]
	public void FindApi_ForwardsExactPageArgumentsAndReturnsEveryKnownPageField()
	{
		StateTestHarness harness = new();
		LuaApiSearchResult expected = new(12, 10, false,
		[
			new LuaApiSearchLine(41, "getAddressSafe(address)"),
			new LuaApiSearchLine(59, "getAddressList().getMemoryRecordByDescription(name)")
		]);
		harness.Answer<LuaApiSearchResult>(source =>
		{
			Assert.Contains("local a = { n = 3, [1] = \"getAddress\", [2] = 2, [3] = 8 };", source,
				StringComparison.Ordinal);
			return expected;
		});
		LuaTools tools = new(harness.Dispatch);

		LuaApiSearchResult result = tools.FindApi("getAddress", 2, 8, TestContext.Current.CancellationToken);

		Assert.Equal((12, 10, false), (result.Total, result.NextOffset, result.Truncated));
		Assert.Equal([(41, "getAddressSafe(address)"),
			(59, "getAddressList().getMemoryRecordByDescription(name)")],
			result.Lines.Select(static line => (line.Number, line.Text)));
		(string operation, string source) = Assert.Single(harness.LuaCalls);
		Assert.Equal(CheatEngineToolNames.LuaFindApi, operation);
		Assert.Contains(LuaScripts.FindApi, source, StringComparison.Ordinal);
		Assert.Equal(1, harness.Dispatches);
	}

	[Fact]
	public void FindApi_MaximumValidUtf8Query_ReachesTheFixedLuaAdapter()
	{
		StateTestHarness harness = new();
		string query = new('\u00e9', LuaTools.MaximumQueryBytes / 2);
		harness.Answer<LuaApiSearchResult>(source =>
		{
			Assert.Contains("[1] = \"\\195\\169", source, StringComparison.Ordinal);
			return new LuaApiSearchResult(0, null, false, []);
		});
		LuaTools tools = new(harness.Dispatch);

		LuaApiSearchResult result = tools.FindApi(query, 1, 0, TestContext.Current.CancellationToken);

		Assert.Equal((0, null, false), (result.Total, result.NextOffset, result.Truncated));
		Assert.Single(harness.LuaCalls);
		Assert.Equal(1, harness.Dispatches);
	}

	[Fact]
	public void FindApi_UnpairedSurrogate_RefusesBeforeFixedLuaDispatch()
	{
		StateTestHarness harness = new();
		LuaTools tools = new(harness.Dispatch);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.FindApi("get\uD800Address", cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("unpaired UTF-16 surrogate", exception.Error.Message, StringComparison.Ordinal);
		Assert.Empty(harness.LuaCalls);
		Assert.Equal(0, harness.Dispatches);
	}

	[Fact]
	public void Execute_DisabledFeature_RefusesBeforeUnsafeOrFixedLua()
	{
		FixedLuaDouble fixedLua = new(new ToolLuaExecuteOutcome(true));
		int unsafeCalls = 0;
		IUnsafeLuaClient unsafeLua = CreateUnsafeLua(() => unsafeCalls++);
		LuaTools tools = new(CreateDispatch(fixedLua, enableUnsafeLua: false), unsafeLua);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.Execute("return 7", cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, unsafeCalls);
		Assert.Empty(fixedLua.Calls);
	}

	[Fact]
	public void Execute_MissingUnsafeClient_ReportsUnsupportedBeforeFixedLua()
	{
		FixedLuaDouble fixedLua = new(new ToolLuaExecuteOutcome(true));
		LuaTools tools = new(CreateDispatch(fixedLua, enableUnsafeLua: true));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.Execute("return 7", cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Empty(fixedLua.Calls);
	}

	[Fact]
	public void Execute_Success_UsesTheUnsafeAdapterAndKeepsCopiedValuesAndOpaqueCount()
	{
		JsonElement[] values =
		[
			JsonElement.Parse("7"),
			JsonElement.Parse("{\"visible\":true}")
		];
		FixedLuaDouble fixedLua = new(new ToolLuaExecuteOutcome(true, ReturnValues: values), droppedOpaqueCount: 3);
		int unsafeCalls = 0;
		LuaTools tools = new(CreateDispatch(fixedLua, enableUnsafeLua: true), CreateUnsafeLua(() => unsafeCalls++));

		LuaExecuteResult result = tools.Execute("return 7, print, { visible = true }", "=qualification",
			TestContext.Current.CancellationToken);

		Assert.Equal((true, null, null, ToolHostEffect.Completed, 3),
			(result.Ok, result.Phase, result.Error, result.HostEffect, result.DroppedOpaqueCount));
		Assert.Equal(7, result.ReturnValues![0].GetInt32());
		Assert.True(result.ReturnValues[1].GetProperty("visible").GetBoolean());
		Assert.Equal(1, unsafeCalls);
		Assert.Single(fixedLua.Calls);
		Assert.Equal(CheatEngineToolNames.LuaExecute, fixedLua.Calls[0].Operation);
	}

	[Theory]
	[InlineData("compile", ToolHostEffect.NotApplied)]
	[InlineData("runtime", ToolHostEffect.Unknown)]
	public void Execute_FailedLuaOutcome_MapsPhaseToTheAdvertisedHostEffect(string phase, ToolHostEffect effect)
	{
		FixedLuaDouble fixedLua = new(new ToolLuaExecuteOutcome(false, phase, "simulated " + phase + " failure"));
		int unsafeCalls = 0;
		LuaTools tools = new(CreateDispatch(fixedLua, enableUnsafeLua: true), CreateUnsafeLua(() => unsafeCalls++));

		LuaExecuteResult result = tools.Execute("return 7", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal((false, phase, "simulated " + phase + " failure", effect, null, 0),
			(result.Ok, result.Phase, result.Error, result.HostEffect, result.ReturnValues, result.DroppedOpaqueCount));
		Assert.Equal(1, unsafeCalls);
		Assert.Single(fixedLua.Calls);
	}

	private static ToolDispatch CreateDispatch(IFixedLuaExecutor fixedLua, bool enableUnsafeLua)
	{
		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		return new ToolDispatch(ClientTestDouble.Client(),
			new McpFeatureGate(Options.Create(new McpFeatureOptions { EnableUnsafeLua = enableUnsafeLua })), execution,
			new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>(), fixedLua);
	}

	private static IUnsafeLuaClient CreateUnsafeLua(Action invoked)
	{
		return ClientTestDouble.Create<IUnsafeLuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(IUnsafeLuaClient.TryExecute), method.Name);
			LuaScript script = Assert.IsType<LuaScript>(arguments![0]);
			Assert.Contains("return 7", script.Source, StringComparison.Ordinal);
			invoked();
			arguments[1] = default(CheatEngineFailure);
			return true;
		});
	}

	private sealed class FixedLuaDouble(object result, int droppedOpaqueCount = 0) : IFixedLuaExecutor
	{
		internal List<(string Operation, string Source)> Calls
		{
			get;
		} = [];

		public LuaJsonResult<T> Execute<T>(string operation, string source, JsonTypeInfo<T> resultType,
			LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Calls.Add((operation, source));
			Assert.Equal(LuaOpaqueValueHandling.Drop, opaque);
			return new LuaJsonResult<T>(Assert.IsType<T>(result), null, droppedOpaqueCount);
		}
	}
}
