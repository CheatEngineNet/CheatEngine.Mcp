using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Api;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed unsafe partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void Execute_Prelude_FormatsHexBytesAndNumbersAndExposesTheBudget()
	{
		using RuntimeScope scope = CreateScope();
		const string body = """
		                    return {hex = mcp.hex(a[1]), negative = mcp.hex(-1), nilHex = mcp.hex(nil) == nil,
		                    	bytes = mcp.bytes({0x48, 0x8B, 5}), prefix = mcp.bytes({1, 2, 3}, 2), text = mcp.bytes('A\0'),
		                    	notANumber = mcp.num(0/0), infinity = mcp.num(1/0), negativeInfinity = mcp.num(-1/0),
		                    	finite = mcp.num(0.5), integer = mcp.num(7), budget = k.budgetMs, expired = mcp.expired()}
		                    """;

		LuaPreludeProbe probe = PluginLuaToolRuntime.Execute(CreateJsonLuaClient(), "prelude_probe", body,
			TestJsonContext.Default.LuaPreludeProbe, CancellationToken.None, 0xFFFF800000001000UL);

		Assert.Equal(new LuaPreludeProbe("FFFF800000001000", "FFFFFFFFFFFFFFFF", true, "48 8B 05", "01 02", "41 00",
			"NaN", "Infinity", "-Infinity", 0.5, 7, LuaToolRuntime.DefaultBudgetMilliseconds, false), probe);
	}

	[Fact]
	public void Execute_Prelude_ExpiresAfterTheBudgetAcrossTheTickWrap()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("tick = 4294967290; getTickCount = function() return tick end");
		const string body = """
		                    local seen = {mcp.expired()}
		                    tick = 50
		                    seen[2] = mcp.expired()
		                    tick = 4294967290 + k.budgetMs - 1
		                    seen[3] = mcp.expired()
		                    tick = (4294967290 + k.budgetMs) % 4294967296
		                    seen[4] = mcp.expired()
		                    return seen
		                    """;

		bool[] seen = PluginLuaToolRuntime.Execute(CreateJsonLuaClient(), "expiry_probe", body,
			TestJsonContext.Default.BooleanArray, CancellationToken.None);

		Assert.Equal([false, false, false, true], seen);
		InstallStubs("getTickCount = nil");
	}

	[Fact]
	public void Execute_Arguments_ReachTheFixedBodyOnlyThroughA()
	{
		using RuntimeScope scope = CreateScope();
		const string body = """
		                    return {count = a.n, text = a[1], address = a[2], flag = a[3], items = a[4]}
		                    """;

		LuaArgumentProbe probe = PluginLuaToolRuntime.Execute(CreateJsonLuaClient(), "argument_probe", body,
			TestJsonContext.Default.LuaArgumentProbe, CancellationToken.None, "\"]] ) error('x') --☃",
			ulong.MaxValue, true, new[] { "x", "y" });

		Assert.Equal(4, probe.Count);
		Assert.Equal("\"]] ) error('x') --☃", probe.Text);
		Assert.Equal(-1, probe.Address);
		Assert.True(probe.Flag);
		Assert.Equal(["x", "y"], probe.Items);
	}

	[Theory]
	[InlineData("return mcp.err('not_found', 'gone', nil, 'List first.')", ToolErrorKind.NotFound,
		ToolHostEffect.Unknown, "List first.")]
	[InlineData("return mcp.err('busy', 'wait', 'not_started')", ToolErrorKind.Busy, ToolHostEffect.NotStarted, null)]
	[InlineData("return mcp.err('memory_write_failed', 'half written', 'started')", ToolErrorKind.MemoryWriteFailed,
		ToolHostEffect.Started, null)]
	[InlineData("return mcp.err('bogus_kind', 'odd')", ToolErrorKind.Internal, ToolHostEffect.Unknown,
		CheatEngineToolException.InternalHint)]
	public void Execute_DeclaredError_BecomesTheContractError(string body, ToolErrorKind kind, ToolHostEffect effect,
		string? hint)
	{
		using RuntimeScope scope = CreateScope();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			PluginLuaToolRuntime.Execute(CreateJsonLuaClient(), "declared_probe", body,
				TestJsonContext.Default.StringArray,
				CancellationToken.None));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(effect, exception.Error.HostEffect);
		Assert.Equal(hint, exception.Error.Hint);
		Assert.Equal("declared_probe", exception.Error.Operation);
		Assert.Equal(ToolFailureMapping.IsRetryable(kind, effect), exception.Error.Retryable);
	}

	[Theory]
	[InlineData("return {1, x = 2}", ToolErrorKind.Internal)]
	[InlineData("return {0/0}", ToolErrorKind.Internal)]
	[InlineData("return {print}", ToolErrorKind.Internal)]
	[InlineData("return {1, 2}", ToolErrorKind.Internal)]
	[InlineData("return nil", ToolErrorKind.Internal)]
	[InlineData("return {n = 65536}", ToolErrorKind.LimitExceeded)]
	public void Execute_ResultBreakingTheCopyContract_IsReportedAsCompleted(string body, ToolErrorKind kind)
	{
		using RuntimeScope scope = CreateScope();
		int top = LuaApi.lua_gettop(s_state);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			PluginLuaToolRuntime.Execute(CreateJsonLuaClient(), "copy_probe", body, TestJsonContext.Default.StringArray,
				CancellationToken.None));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.Completed, exception.Error.HostEffect);
		Assert.IsType<LuaJsonException>(exception.InnerException);
		Assert.Equal(top, LuaApi.lua_gettop(s_state));
	}

	[Theory]
	[InlineData("[", CheatEngineHostEffect.NotStarted)]
	[InlineData("error('runtime failure')", CheatEngineHostEffect.Started)]
	public void Execute_LuaFailure_IsAClientFailureWithItsHostEffect(string body, CheatEngineHostEffect effect)
	{
		using RuntimeScope scope = CreateScope();

		CheatEngineClientException exception = Assert.ThrowsAny<CheatEngineClientException>(() =>
			PluginLuaToolRuntime.Execute(CreateJsonLuaClient(), "failure_probe", body,
				TestJsonContext.Default.StringArray,
				CancellationToken.None));

		Assert.Equal(CheatEngineFailureKind.LuaError, exception.Failure.Kind);
		Assert.Equal(effect, exception.Failure.HostEffect);
	}

	[Fact]
	public void RunLua_NativeScript_ReportsBodyLinesOffsetByTheSingleLinePrelude()
	{
		using RuntimeScope scope = CreateScope();
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		string[] result = dispatch.RunLua("line_probe", "return {mcp.hex(a[1]), tostring(k.budgetMs)}",
			TestJsonContext.Default.StringArray, CancellationToken.None, 255);
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			dispatch.RunLua("line_probe", "local x = 1\nerror('second line')", TestJsonContext.Default.StringArray,
				CancellationToken.None));

		Assert.Equal(["FF", "100"], result);
		Assert.Equal(ToolErrorKind.HostRefused, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.Started, exception.Error.HostEffect);
		Assert.Contains("line_probe:3: second line", exception.Error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void RunLua_DisabledSwitch_NeverReachesLua()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("dbvmCalls = 0; dbvm_getCR3 = function() dbvmCalls = dbvmCalls + 1 return 1 end");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions { EnableKernelAccess = false });

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			dispatch.RunLua("kernel_get_probe", "return {tostring(dbvm_getCR3())}",
				TestJsonContext.Default.StringArray, CancellationToken.None));

		Assert.Equal(ToolErrorKind.CapabilityDisabled, exception.Error.Kind);
		Assert.Equal(0L, ReadGlobal("dbvmCalls"));
	}

	private static ToolDispatch CreateNativeDispatch(McpFeatureOptions features)
	{
		IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
		ICheatEngineClient client = CreateJsonLuaClient();
		return new ToolDispatch(client, new McpFeatureGate(Options.Create(features)), options,
			new DispatchStatistics(options), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
			new PluginFixedLuaExecutor(client));
	}

	/// <summary>A Client double whose inline dispatcher and Lua facade run any typed operation on the test state.</summary>
	private static ICheatEngineClient CreateJsonLuaClient()
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
		return ClientTestDouble.Client((nameof(ICheatEngineClient.Lua), lua));
	}
}
