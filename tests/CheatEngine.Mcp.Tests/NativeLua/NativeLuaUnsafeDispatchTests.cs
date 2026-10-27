using System.Reflection;
using System.Text.Json;

using CheatEngine.Client;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Lua;

using Microsoft.Extensions.Options;

using Xunit.Sdk;

using TestLuaExecuteOutcome = CheatEngine.Mcp.Tests.Support.LuaExecuteOutcome;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void LuaExecute_ReportsTheCallerChunkHostEffect()
	{
		using RuntimeScope scope = CreateScope();
		IUnsafeLuaClient unsafeLua = ClientTestDouble.Create<IUnsafeLuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(IUnsafeLuaClient.TryExecute), method.Name);
			LuaScript script = Assert.IsType<LuaScript>(arguments![0]);
			RunStageA(script.Source);
			arguments[1] = default(CheatEngineFailure);
			return true;
		});
		LuaTools tools = new(CreateUnsafeDispatch(CreateFixedLuaClient(static () =>
		{
		}), stopping: TestContext.Current.CancellationToken), unsafeLua);

		LuaExecuteResult completed = tools.Execute("return 7", cancellationToken: CancellationToken.None);
		LuaExecuteResult notApplied = tools.Execute("return +", cancellationToken: CancellationToken.None);
		LuaExecuteResult unknown =
			tools.Execute("error('after effects may exist')", cancellationToken: CancellationToken.None);

		Assert.Equal((true, ToolHostEffect.Completed), (completed.Ok, completed.HostEffect));
		Assert.Equal(("compile", ToolHostEffect.NotApplied), (notApplied.Phase, notApplied.HostEffect));
		Assert.Equal(("runtime", ToolHostEffect.Unknown), (unknown.Phase, unknown.HostEffect));
	}

	[Fact]
	public void RunUnsafeLua_DropsOpaqueValuesAndRunsStageBCleanupAfterStageAFailure()
	{
		using RuntimeScope scope = CreateScope();
		int unsafeCalls = 0;
		int fixedLuaCalls = 0;
		bool failAfterStageA = false;
		IUnsafeLuaClient unsafeLua = ClientTestDouble.Create<IUnsafeLuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(IUnsafeLuaClient.TryExecute), method.Name);
			LuaScript script = Assert.IsType<LuaScript>(arguments![0]);
			unsafeCalls++;
			RunStageA(script.Source);
			if (failAfterStageA)
			{
				arguments[1] = new CheatEngineFailure(CheatEngineFailureKind.LuaError, "UnsafeLua.TryExecute",
					"Simulated stage-A Client failure.", hostEffect: CheatEngineHostEffect.Started);
				return false;
			}

			arguments[1] = default(CheatEngineFailure);
			return true;
		});
		ToolDispatch dispatch = CreateUnsafeDispatch(CreateFixedLuaClient(() => fixedLuaCalls++),
			stopping: TestContext.Current.CancellationToken);

		LuaUnsafeExecution<TestLuaExecuteOutcome> copied = dispatch.RunUnsafeLua("unsafe_bridge", unsafeLua,
			"return 7, print, {visible = 'kept', hidden = print}", null,
			TestJsonContext.Default.LuaExecuteOutcome, CancellationToken.None);

		Assert.True(copied.Result.Ok, copied.Result.Error);
		JsonElement[] values = Assert.IsType<JsonElement[]>(copied.Result.ReturnValues);
		Assert.Collection(values,
			value => Assert.Equal(7, value.GetInt32()),
			value => Assert.Equal(JsonValueKind.Null, value.ValueKind),
			value =>
			{
				Assert.Equal("kept", value.GetProperty("visible").GetString());
				Assert.False(value.TryGetProperty("hidden", out _));
			});
		Assert.Equal(2, copied.DroppedOpaqueCount);
		Assert.Equal(1, unsafeCalls);
		Assert.Equal(1, fixedLuaCalls);
		Assert.Null(ReadGlobal(LuaUnsafeScriptWrapper.ResultGlobal));

		failAfterStageA = true;
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			dispatch.RunUnsafeLua("unsafe_bridge", unsafeLua, "return 'stale unless stage B clears it'", null,
				TestJsonContext.Default.LuaExecuteOutcome, CancellationToken.None));

		Assert.Equal(ToolErrorKind.HostRefused, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.Started, exception.Error.HostEffect);
		Assert.Equal("Simulated stage-A Client failure.", exception.Error.Message);
		Assert.Equal(2, unsafeCalls);
		Assert.Equal(2, fixedLuaCalls);
		Assert.Null(ReadGlobal(LuaUnsafeScriptWrapper.ResultGlobal));
	}

	[Fact]
	public void RunUnsafeLua_CallerCancelsAfterStageA_StillRunsStageBCleanup()
	{
		using RuntimeScope scope = CreateScope();
		using CancellationTokenSource caller = new();
		using CancellationTokenSource stopping = new();
		int fixedLuaCalls = 0;
		IUnsafeLuaClient unsafeLua = ClientTestDouble.Create<IUnsafeLuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(IUnsafeLuaClient.TryExecute), method.Name);
			RunStageA(Assert.IsType<LuaScript>(arguments![0]).Source);
			caller.Cancel();
			arguments[1] = default(CheatEngineFailure);
			return true;
		});
		ToolDispatch dispatch = CreateUnsafeDispatch(CreateFixedLuaClient(() => fixedLuaCalls++, token =>
			{
				Assert.Equal(stopping.Token, token);
				Assert.NotEqual(caller.Token, token);
			}), stopping: stopping.Token);

		LuaUnsafeExecution<TestLuaExecuteOutcome> copied = dispatch.RunUnsafeLua("unsafe_bridge", unsafeLua,
			"return 'cleared despite cancellation'", null, TestJsonContext.Default.LuaExecuteOutcome, caller.Token);

		Assert.True(copied.Result.Ok, copied.Result.Error);
		Assert.Equal(1, fixedLuaCalls);
		Assert.Null(ReadGlobal(LuaUnsafeScriptWrapper.ResultGlobal));
	}

	[Fact]
	public void RunUnsafeLua_DisabledCapability_RefusesBeforeEitherLuaStage()
	{
		int unsafeCalls = 0;
		int fixedLuaCalls = 0;
		IUnsafeLuaClient unsafeLua = ClientTestDouble.Create<IUnsafeLuaClient>((_, _) =>
		{
			unsafeCalls++;
			return null;
		});
		ILuaClient fixedLua = ClientTestDouble.Create<ILuaClient>((_, _) =>
		{
			fixedLuaCalls++;
			throw new XunitException("Unsafe Lua must be refused before the fixed result reader runs.");
		});
		ToolDispatch dispatch = CreateUnsafeDispatch(fixedLua, new McpFeatureOptions { EnableUnsafeLua = false },
			TestContext.Current.CancellationToken);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			dispatch.RunUnsafeLua("unsafe_bridge", unsafeLua, "return 1", null,
				TestJsonContext.Default.LuaExecuteOutcome, CancellationToken.None));

		Assert.Equal(ToolErrorKind.CapabilityDisabled, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Equal(0, unsafeCalls);
		Assert.Equal(0, fixedLuaCalls);
	}

	private static ToolDispatch CreateUnsafeDispatch(ILuaClient? lua,
		McpFeatureOptions? features = null, CancellationToken stopping = default)
	{
		ICheatEngineClient client;
		if (lua is null || !stopping.CanBeCanceled)
		{
			client = lua is null
				? ClientTestDouble.Client()
				: ClientTestDouble.Client((nameof(ICheatEngineClient.Lua), lua));
		}
		else
		{
			ICheatEngineDispatcher dispatcher = ClientTestDouble.Create<ICheatEngineDispatcher>((method, arguments) =>
			{
				if (method.Name == "get_IsMainThread")
				{
					return true;
				}

				if (method.Name.StartsWith("Invoke", StringComparison.Ordinal) && arguments?[0] is Delegate callback)
				{
					return callback.DynamicInvoke();
				}

				throw new NotSupportedException($"No dispatcher behavior was configured for {method.Name}.");
			});
			client = ClientTestDouble.Client(dispatcher, stopping, (nameof(ICheatEngineClient.Lua), lua));
		}
		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		return new ToolDispatch(client,
			new McpFeatureGate(Options.Create(features ?? new McpFeatureOptions { EnableUnsafeLua = true })),
			execution, new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
			new PluginFixedLuaExecutor(client));
	}

	private static ILuaClient CreateFixedLuaClient(Action fixedLuaExecuted, Action<CancellationToken>? inspectToken = null)
	{
		return ClientTestDouble.Create<ILuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ILuaClient.Execute), method.Name);
			CancellationToken cancellationToken = Assert.IsType<CancellationToken>(arguments![1]);
			inspectToken?.Invoke(cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			fixedLuaExecuted();
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
	}
}
