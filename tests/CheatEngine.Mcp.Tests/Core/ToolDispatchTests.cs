using System.Text.Json.Serialization.Metadata;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>
///     The v2 dispatch facade: admission, exception classification, token linking, statistics and the Lua choke
///     point.
/// </summary>
public sealed class ToolDispatchTests
{
	[Fact]
	public void Run_Body_RunsOnceAndReturnsItsResult()
	{
		Harness harness = new();
		int calls = 0;

		int result = harness.Dispatch.Run("memory_read_probe", _ => ++calls, CancellationToken.None);

		Assert.Equal(1, result);
		Assert.Equal(1, calls);
		Assert.Equal(1, harness.Dispatcher.Calls);
		Assert.Equal(1, harness.Statistics.Snapshot().Count);
	}

	[Fact]
	public void Run_ArgumentExceptionAfterStart_IsInternalUnknownAndLogged()
	{
		Harness harness = new();
		ArgumentException fault = new("A codec rejected a value after the write.");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Dispatch.Run<int>("record_create_probe", _ => throw fault, CancellationToken.None));

		Assert.Equal(ToolErrorKind.Internal, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.Unknown, exception.Error.HostEffect);
		Assert.False(exception.Error.Retryable);
		Assert.Same(fault, exception.InnerException);
		Assert.DoesNotContain(fault.Message, exception.Error.Message, StringComparison.Ordinal);
		(LogLevel level, EventId eventId, _, Exception? logged) = Assert.Single(harness.Logger.Entries);
		Assert.Equal((LogLevel.Error, 3004), (level, eventId.Id));
		Assert.Null(logged);
	}

	[Fact]
	public void Run_ExceptionBeforeAdmission_IsInternalNotStartedWithoutRunningTheBody()
	{
		Harness harness = new();
		harness.Dispatcher.Admission = static _ => throw new InvalidOperationException("dispatcher unavailable");
		bool ran = false;

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Dispatch.Run("memory_read_probe", _ => ran = true, CancellationToken.None));

		Assert.False(ran);
		Assert.Equal(ToolErrorKind.Internal, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Equal(ToolExecution.NotAdmittedMessage, exception.Error.Message);
		Assert.Equal(0, harness.Statistics.Snapshot().Count);
	}

	[Fact]
	public void Run_ClientFailureInsideBody_KeepsItsKindOperationAndHostEffect()
	{
		Harness harness = new();
		CheatEngineFailure failure = new(CheatEngineFailureKind.MemoryWriteFailed, "Memory.WriteBytes",
			"The write was refused.", hostEffect: CheatEngineHostEffect.Started);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Dispatch.Run<int>("memory_write_probe", token => throw failure.ToException(token),
				CancellationToken.None));

		Assert.Equal(ToolErrorKind.MemoryWriteFailed, exception.Error.Kind);
		Assert.Equal("Memory.WriteBytes", exception.Error.Operation);
		Assert.Equal(ToolHostEffect.Started, exception.Error.HostEffect);
		Assert.Empty(harness.Logger.Entries);
	}

	[Fact]
	public void Run_ToolExceptionInsideBody_PassesUnchanged()
	{
		Harness harness = new();
		CheatEngineToolException declared = CheatEngineToolException.NotFound("The record is gone.", "List records.");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Dispatch.Run<int>("record_get_probe", _ => throw declared, CancellationToken.None));

		Assert.Same(declared, exception);
	}

	[Fact]
	public void Run_RequestAndStoppingTokens_AreLinkedForTheDispatchAndTheBody()
	{
		using CancellationTokenSource request = new();
		using CancellationTokenSource stopping = new();
		Harness harness = new(stopping: stopping.Token);
		CancellationToken observed = default;

		(bool before, bool after) byRequest = harness.Dispatch.Run("memory_read_probe", token =>
		{
			observed = token;
			bool before = token.IsCancellationRequested;
			request.Cancel();
			return (before, token.IsCancellationRequested);
		}, request.Token);
		using CancellationTokenSource secondRequest = new();
		bool byStopping = harness.Dispatch.Run("memory_read_probe", token =>
		{
			stopping.Cancel();
			return token.IsCancellationRequested;
		}, secondRequest.Token);

		Assert.True(harness.Dispatcher.Tokens.TryPeek(out CancellationToken admitted));
		Assert.Equal(admitted, observed);
		Assert.Equal((false, true), byRequest);
		Assert.True(byStopping);
		Assert.False(secondRequest.IsCancellationRequested);
	}

	[Fact]
	public void Run_UncancellableRequest_UsesTheStoppingToken()
	{
		using CancellationTokenSource stopping = new();
		Harness harness = new(stopping: stopping.Token);
		CancellationToken observed = default;

		harness.Dispatch.Run("memory_read_probe", token => observed = token, CancellationToken.None);

		Assert.Equal(stopping.Token, observed);
	}

	[Fact]
	public void Run_CancelledBeforeAdmission_RethrowsTheCancellationWithoutLogging()
	{
		using CancellationTokenSource request = new();
		request.Cancel();
		Harness harness = new();
		harness.Dispatcher.Admission = RecordingDispatcher.ObserveCancellation;
		bool ran = false;

		Assert.ThrowsAny<OperationCanceledException>(() =>
			harness.Dispatch.Run("memory_read_probe", _ => ran = true, request.Token));

		Assert.False(ran);
		Assert.Empty(harness.Logger.Entries);
		Assert.Equal(0, harness.Statistics.Snapshot().Count);
	}

	[Fact]
	public void Run_CancelledAfterTheBodyStarted_LogsAWarning()
	{
		using CancellationTokenSource request = new();
		Harness harness = new();

		int result = harness.Dispatch.Run("memory_write_probe", _ =>
		{
			request.Cancel();
			return 7;
		}, request.Token);

		Assert.Equal(7, result);
		(LogLevel level, EventId eventId, string message, _) = Assert.Single(harness.Logger.Entries);
		Assert.Equal((LogLevel.Warning, 3003), (level, eventId.Id));
		Assert.Contains("memory_write_probe", message, StringComparison.Ordinal);
	}

	[Fact]
	public void Run_ActivationStopping_IsStoppingNotStarted()
	{
		using CancellationTokenSource stopping = new();
		stopping.Cancel();
		Harness harness = new(stopping: stopping.Token);
		harness.Dispatcher.Admission = RecordingDispatcher.ObserveCancellation;

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Dispatch.Run("memory_read_probe", static _ => 1, CancellationToken.None));

		Assert.Equal(ToolErrorKind.Stopping, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
	}

	[Fact]
	public void Run_ConcurrencyLimitReached_IsBusyNotStartedWithoutDispatching()
	{
		Harness harness = new(new McpExecutionOptions { MaxConcurrentDispatches = 1 });
		CheatEngineToolException? refused = null;

		harness.Dispatch.Run("memory_read_probe", _ =>
		{
			refused = Assert.Throws<CheatEngineToolException>(() =>
				harness.Dispatch.Run("memory_read_second", static _ => 2, CancellationToken.None));
			return 1;
		}, CancellationToken.None);

		Assert.NotNull(refused);
		Assert.Equal(ToolErrorKind.Busy, refused.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, refused.Error.HostEffect);
		Assert.True(refused.Error.Retryable);
		Assert.Equal(1, harness.Dispatcher.Calls);
		Assert.Equal(1, harness.Statistics.Snapshot().Rejected);
		Assert.Equal(3, harness.Dispatch.Run("memory_read_probe", static _ => 3, CancellationToken.None));
	}

	[Fact]
	public void Run_BodyOverBudget_IsRecordedAndLoggedWithItsTimes()
	{
		Harness harness = new();
		harness.Dispatcher.Admission = _ => harness.Time.Advance(TimeSpan.FromMilliseconds(30));

		harness.Dispatch.Run("scan_first_probe", _ =>
		{
			harness.Time.Advance(TimeSpan.FromMilliseconds(250));
			return 0;
		}, CancellationToken.None);
		harness.Dispatch.Run("memory_read_probe", _ =>
		{
			harness.Time.Advance(TimeSpan.FromMilliseconds(20));
			return 0;
		}, CancellationToken.None);

		DispatchSummary summary = harness.Statistics.Snapshot();
		Assert.Equal(new DispatchSummary(100, 2, 1, 0, 250, "scan_first_probe", 30), summary);
		(LogLevel level, EventId eventId, string message, _) = Assert.Single(harness.Logger.Entries);
		Assert.Equal((LogLevel.Warning, 3001), (level, eventId.Id));
		Assert.Contains("scan_first_probe", message, StringComparison.Ordinal);
		Assert.Contains("250 ms", message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("return autoAssemble(a[1])", McpFeature.AutoAssembler, "EnableAutoAssembler")]
	[InlineData("return executeCodeEx(0, nil, a[1])", McpFeature.TargetCodeExecution, "EnableTargetCodeExecution")]
	[InlineData("return executeCode(a[1])", McpFeature.TargetCodeExecution, "EnableTargetCodeExecution")]
	[InlineData("return executeCodeLocal(a[1])", McpFeature.TargetCodeExecution, "EnableTargetCodeExecution")]
	[InlineData("return executeMethod(0, nil, a[1])", McpFeature.TargetCodeExecution, "EnableTargetCodeExecution")]
	[InlineData("return injectLibrary(a[1])", McpFeature.TargetCodeExecution, "EnableTargetCodeExecution")]
	[InlineData("return injectDotNetDLL(a[1])", McpFeature.TargetCodeExecution, "EnableTargetCodeExecution")]
	[InlineData("return compile(a[1])", McpFeature.TargetCodeExecution, "EnableTargetCodeExecution")]
	[InlineData("return compileCS(a[1])", McpFeature.TargetCodeExecution, "EnableTargetCodeExecution")]
	[InlineData("LaunchMonoDataCollector() return {}", McpFeature.TargetCodeExecution, "EnableTargetCodeExecution")]
	[InlineData("return mono_invoke_method(nil, a[1])", McpFeature.TargetCodeExecution, "EnableTargetCodeExecution")]
	[InlineData("speedhack_setSpeed(a[1]) return {}", McpFeature.TargetCodeExecution, "EnableTargetCodeExecution")]
	[InlineData("return dbk_readMSR(a[1])", McpFeature.KernelAccess, "EnableKernelAccess")]
	[InlineData("return {dbvm_getCR3()}", McpFeature.KernelAccess, "EnableKernelAccess")]
	[InlineData("local t = loadTable(a[1]) return {}", McpFeature.UnsafeLua, "EnableUnsafeLua")]
	public void RunLua_SensitiveApiWithItsSwitchOff_IsRefusedBeforeDispatch(string body, McpFeature feature,
		string setting)
	{
		Harness harness = new(features: Only(feature, false));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Dispatch.RunLua("probe_operation", body, TestJsonContext.Default.StringArray,
				CancellationToken.None, "x"));

		Assert.Equal(ToolErrorKind.CapabilityDisabled, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Contains("probe_operation", exception.Error.Message, StringComparison.Ordinal);
		Assert.Contains($"Mcp:{setting}", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(0, harness.Dispatcher.Calls);
		Assert.Equal(0, harness.LuaCalls);
	}

	[Fact]
	public void RunLua_SensitiveApiWithItsSwitchOn_IsDispatched()
	{
		Harness harness = new(features: Only(McpFeature.AutoAssembler, false));
		harness.LuaResult = new LuaJsonResult<string[]>(["ok"], null, 0);

		string[] result = harness.Dispatch.RunLua("probe_operation", "return {executeCodeEx(a[1])}",
			TestJsonContext.Default.StringArray, CancellationToken.None, "x");

		Assert.Equal(["ok"], result);
		Assert.Equal(1, harness.Dispatcher.Calls);
		Assert.Equal(1, harness.LuaCalls);
	}

	[Fact]
	public void RunLua_Script_IsBuiltBeforeDispatchWithRuntimeValuesPreludeAndArguments()
	{
		Harness harness = new(new McpExecutionOptions { DispatchBudgetMilliseconds = 250 });
		harness.LuaResult = new LuaJsonResult<string[]>([], null, 0);

		harness.Dispatch.RunLua("probe_operation", "return {}", TestJsonContext.Default.StringArray,
			CancellationToken.None, "text", 7, ulong.MaxValue);

		string[] lines = harness.LuaSource!.Split('\n');
		Assert.Equal(2, lines.Length);
		Assert.StartsWith(
			"local k = { budgetMs = 250 }; local a = { n = 3, [1] = \"text\", [2] = 7, [3] = 0xFFFFFFFFFFFFFFFF }; local mcp = {} ",
			lines[0], StringComparison.Ordinal);
		Assert.EndsWith(LuaPrelude.Source, lines[0], StringComparison.Ordinal);
		Assert.Equal("return {}", lines[1]);
		Assert.Equal("probe_operation", harness.LuaOperationName);
	}

	[Fact]
	public void RunLua_UnsupportedArgument_IsInvalidArgumentNotStarted()
	{
		Harness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Dispatch.RunLua("probe_operation", "return {}", TestJsonContext.Default.StringArray,
				CancellationToken.None, new object()));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Theory]
	[InlineData("not_found", null, ToolErrorKind.NotFound, ToolHostEffect.Unknown, false)]
	[InlineData("busy", "not_started", ToolErrorKind.Busy, ToolHostEffect.NotStarted, true)]
	[InlineData("busy", null, ToolErrorKind.Busy, ToolHostEffect.Unknown, false)]
	[InlineData("invalid_state", "started", ToolErrorKind.InvalidState, ToolHostEffect.Started, false)]
	[InlineData("memory_write_failed", "cleanup_unconfirmed", ToolErrorKind.MemoryWriteFailed,
		ToolHostEffect.CleanupUnconfirmed, false)]
	[InlineData("no_such_kind", "not_started", ToolErrorKind.Internal, ToolHostEffect.NotStarted, false)]
	[InlineData("not_found", "no_such_effect", ToolErrorKind.NotFound, ToolHostEffect.Unknown, false)]
	public void RunLua_DeclaredScriptError_BecomesTheContractError(string kind, string? hostEffect,
		ToolErrorKind expectedKind, ToolHostEffect expectedEffect, bool retryable)
	{
		Harness harness = new();
		harness.LuaResult =
			new LuaJsonResult<string[]>(null, new LuaScriptError(kind, "declared", hostEffect, "hint"), 0);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Dispatch.RunLua("probe_operation", "return {}", TestJsonContext.Default.StringArray,
				CancellationToken.None));

		Assert.Equal(expectedKind, exception.Error.Kind);
		Assert.Equal(expectedEffect, exception.Error.HostEffect);
		Assert.Equal(retryable, exception.Error.Retryable);
		Assert.Equal("probe_operation", exception.Error.Operation);
		Assert.Equal("hint", exception.Error.Hint);
		Assert.Contains("declared", exception.Error.Message, StringComparison.Ordinal);
		Assert.Empty(harness.Logger.Entries);
	}

	[Theory]
	[InlineData(true, ToolErrorKind.LimitExceeded)]
	[InlineData(false, ToolErrorKind.Internal)]
	public void RunLua_ResultCopyViolation_IsReportedAsCompleted(bool limit, ToolErrorKind kind)
	{
		Harness harness = new();
		harness.LuaFault = new LuaJsonException(limit ? LuaJsonViolation.Limit : LuaJsonViolation.Contract,
			"The copy failed.");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Dispatch.RunLua("probe_operation", "return {}", TestJsonContext.Default.StringArray,
				CancellationToken.None));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.Completed, exception.Error.HostEffect);
		Assert.Same(harness.LuaFault, exception.InnerException);
	}

	[Fact]
	public void RunLua_LuaErrorFromTheClient_IsHostRefusedWithItsEffect()
	{
		Harness harness = new();
		harness.LuaFault = new CheatEngineFailure(CheatEngineFailureKind.LuaError, "Lua.Execute", "boom",
			hostEffect: CheatEngineHostEffect.Started).ToException(CancellationToken.None);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Dispatch.RunLua("probe_operation", "error('boom')", TestJsonContext.Default.StringArray,
				CancellationToken.None));

		Assert.Equal(ToolErrorKind.HostRefused, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.Started, exception.Error.HostEffect);
	}

	[Fact]
	public void ExecuteLua_InsideRun_AppliesTheChokePointToo()
	{
		Harness harness = new(features: Only(McpFeature.KernelAccess, false));
		CheatEngineToolException? refused = null;

		harness.Dispatch.Run("kernel_read_probe", token =>
		{
			refused = Assert.Throws<CheatEngineToolException>(() =>
				harness.Dispatch.ExecuteLua("kernel_read_probe", "return {dbk_readMSR(a[1])}",
					TestJsonContext.Default.StringArray, token, 16));
			return 0;
		}, CancellationToken.None);

		Assert.Equal(ToolErrorKind.CapabilityDisabled, refused!.Error.Kind);
		Assert.Equal(0, harness.LuaCalls);
	}

	[Fact]
	public void Statistics_ConcurrentRecords_AreAllCountedWithTheirMaximum()
	{
		DispatchStatistics statistics = new(Options.Create(new McpExecutionOptions()));

		Parallel.For(1, 1001, new ParallelOptions { CancellationToken = TestContext.Current.CancellationToken },
			index => statistics.Record("op" + index, TimeSpan.FromMilliseconds(index % 7),
				TimeSpan.FromMilliseconds(index)));

		DispatchSummary summary = statistics.Snapshot();
		Assert.Equal(1000, summary.Count);
		Assert.Equal(900, summary.OverBudget);
		Assert.Equal(1000, summary.MaxExecutedMs);
		Assert.Equal("op1000", summary.MaxOperation);
		Assert.Equal(6, summary.MaxQueuedMs);
	}

	private static McpFeatureOptions Only(McpFeature feature, bool enabled)
	{
		return new McpFeatureOptions
		{
			EnableUnsafeLua = feature != McpFeature.UnsafeLua || enabled,
			EnableAutoAssembler = feature != McpFeature.AutoAssembler || enabled,
			EnableTargetCodeExecution = feature != McpFeature.TargetCodeExecution || enabled,
			EnableKernelAccess = feature != McpFeature.KernelAccess || enabled
		};
	}

	private sealed class Harness
	{
		private int _luaCalls;

		internal Harness(McpExecutionOptions? options = null, McpFeatureOptions? features = null,
			CancellationToken stopping = default)
		{
			Client = ClientTestDouble.Client(Dispatcher.Dispatcher, stopping);
			IOptions<McpExecutionOptions> execution = Options.Create(options ?? new McpExecutionOptions());
			Statistics = new DispatchStatistics(execution);
			Dispatch = new ToolDispatch(Client, new McpFeatureGate(Options.Create(features ?? new McpFeatureOptions())),
				execution, Statistics, Time, Logger, new FixedLuaExecutor(this));
		}

		internal RecordingDispatcher Dispatcher
		{
			get;
		} = new();

		internal ICheatEngineClient Client
		{
			get;
		}

		internal ManualTimeProvider Time
		{
			get;
		} = new();

		internal RecordingLogger<ToolDispatch> Logger
		{
			get;
		} = new();

		internal DispatchStatistics Statistics
		{
			get;
		}

		internal ToolDispatch Dispatch
		{
			get;
		}

		internal object? LuaResult
		{
			get;
			set;
		}

		internal Exception? LuaFault
		{
			get;
			set;
		}

		internal string? LuaOperationName
		{
			get;
			private set;
		}

		internal string? LuaSource
		{
			get;
			private set;
		}

		private sealed class FixedLuaExecutor(Harness harness) : IFixedLuaExecutor
		{
			public LuaJsonResult<T> Execute<T>(string operation, string source, JsonTypeInfo<T> resultType,
				LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque, CancellationToken cancellationToken)
			{
				Interlocked.Increment(ref harness._luaCalls);
				harness.LuaOperationName = operation;
				harness.LuaSource = source;
				return harness.LuaFault is null
					? Assert.IsType<LuaJsonResult<T>>(harness.LuaResult)
					: throw harness.LuaFault;
			}
		}

		internal int LuaCalls => Volatile.Read(ref _luaCalls);
	}
}
