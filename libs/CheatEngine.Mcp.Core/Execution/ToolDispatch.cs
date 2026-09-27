using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Client;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Lua;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Core.Execution;

/// <summary>
///     The activation-scoped dispatch facade that v2 tools inject: it admits a bounded number of concurrent dispatches,
///     runs each compound operation through <see cref="ToolExecution" />, measures how long it waited for and held Cheat
///     Engine's main thread, and is the single choke point for fixed Lua.
/// </summary>
/// <remarks>
///     <para>
///         Every exception leaves as <see cref="CheatEngineToolException" />, except a caller's cancellation, which the
///         MCP
///         SDK never answers. A refusal before the body starts (busy, disabled feature, invalid Lua arguments) reports
///         <c>not_started</c>; an unexpected exception inside the body reports <c>internal</c> with <c>unknown</c> and is
///         logged (event 3004). A body that held the main thread longer than
///         <see cref="McpExecutionOptions.DispatchBudgetMilliseconds" /> is logged (event 3001), and a request cancelled
///         after its body started is logged (event 3003), because its effects stand unreported.
///     </para>
///     <para>
///         Fixed Lua runs only through <see cref="RunLua{T}" /> or, inside a <see cref="Run{T}" /> body,
///         <see cref="ExecuteLua{T}" />: each body is scanned once for sensitive Cheat Engine APIs, and the matching
///         exposure switch must be on (<see cref="LuaFeatureScan" />).
///     </para>
/// </remarks>
public sealed partial class ToolDispatch
{
	private readonly TimeSpan _budget;
	private readonly int _budgetMilliseconds;
	private readonly LuaJsonBufferPool _buffers = new();
	private readonly ILogger<ToolDispatch> _logger;
	private readonly ConditionalWeakTable<string, McpFeature[]> _luaRequirements = new();
	private readonly int _maximumConcurrency;
	private readonly DispatchStatistics _statistics;
	private readonly TimeProvider _time;
	private int _active;

	/// <summary>Creates the activation's dispatch facade.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="features">The activation's exposure switches.</param>
	/// <param name="options">The execution limits; their value is read once.</param>
	/// <param name="statistics">The activation's dispatch statistics.</param>
	/// <param name="time">The clock used to measure dispatches.</param>
	/// <param name="logger">The dispatch log.</param>
	public ToolDispatch(ICheatEngineClient client, McpFeatureGate features, IOptions<McpExecutionOptions> options,
		DispatchStatistics statistics, TimeProvider time, ILogger<ToolDispatch> logger)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(features);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(statistics);
		ArgumentNullException.ThrowIfNull(time);
		ArgumentNullException.ThrowIfNull(logger);
		Client = client;
		Features = features;
		_budgetMilliseconds = options.Value.DispatchBudgetMilliseconds;
		_budget = TimeSpan.FromMilliseconds(_budgetMilliseconds);
		_maximumConcurrency = options.Value.MaxConcurrentDispatches;
		_statistics = statistics;
		_time = time;
		_logger = logger;
	}

	/// <summary>The activation's Client, for calls inside a <see cref="Run{T}" /> body.</summary>
	public ICheatEngineClient Client
	{
		get;
	}

	/// <summary>The activation's exposure switches, for requirements that depend on an argument.</summary>
	public McpFeatureGate Features
	{
		get;
	}

	/// <summary>
	///     Dispatches one compound operation to Cheat Engine's main thread; see
	///     <see cref="ToolExecution.Run{T}(ICheatEngineClient, Func{CancellationToken, T}, CancellationToken)" />.
	/// </summary>
	/// <typeparam name="T">The body's result.</typeparam>
	/// <param name="operation">The operation name used in statistics and logs, such as the tool name.</param>
	/// <param name="body">The complete operation; it receives the request token linked with the activation's stopping token.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <returns>The body's result.</returns>
	/// <exception cref="CheatEngineToolException">
	///     The operation failed, or was refused as <c>busy</c> because
	///     <see cref="McpExecutionOptions.MaxConcurrentDispatches" />
	///     dispatches are already admitted.
	/// </exception>
	/// <exception cref="OperationCanceledException">The caller cancelled the request.</exception>
	public T Run<T>(string operation, Func<CancellationToken, T> body, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ArgumentNullException.ThrowIfNull(body);
		if (Interlocked.Increment(ref _active) > _maximumConcurrency)
		{
			Interlocked.Decrement(ref _active);
			_statistics.RecordRejected();
			throw CheatEngineToolException.Busy(
				$"{_maximumConcurrency} Cheat Engine dispatches are already running; {operation} was not started.",
				"Repeat the call after the running calls finish.");
		}

		DispatchProbe probe = new(_time);
		long invoked = _time.GetTimestamp();
		try
		{
			return ToolExecution.Run(Client, body, probe, cancellationToken);
		}
		catch (CheatEngineToolException exception) when (exception.Error.Kind is ToolErrorKind.Internal &&
		                                                 exception.InnerException is { } fault)
		{
			LogUnexpectedFault(_logger, operation, fault);
			throw;
		}
		finally
		{
			Interlocked.Decrement(ref _active);
			if (probe.Entered)
			{
				Measure(operation, invoked, probe);
				if (cancellationToken.IsCancellationRequested)
				{
					// The SDK never answers a cancelled request, whether the body completed or failed: its effects stand.
					LogCancelledAfterStart(_logger, operation);
				}
			}
		}
	}

	/// <summary>
	///     Dispatches one fixed Lua body and deserializes its bounded result. The body is checked against the exposure
	///     switches and the script is built before dispatch, off Cheat Engine's main thread.
	/// </summary>
	/// <typeparam name="T">The result type.</typeparam>
	/// <param name="operation">The operation name used in statistics, logs, the Lua chunk name and errors.</param>
	/// <param name="body">
	///     The fixed, implementation-owned body; caller data is passed only through
	///     <paramref name="arguments" />.
	/// </param>
	/// <param name="resultType">The source-generated metadata of <typeparamref name="T" />.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <param name="arguments">Values encoded into the <c>a</c> table.</param>
	/// <returns>The deserialized result.</returns>
	/// <exception cref="CheatEngineToolException">
	///     The body needs a disabled switch (<c>capability_disabled</c>), an argument cannot be encoded
	///     (<c>invalid_argument</c>), the script declared a failure, or the dispatch failed.
	/// </exception>
	/// <exception cref="OperationCanceledException">The caller cancelled the request.</exception>
	public T RunLua<T>(string operation, string body, JsonTypeInfo<T> resultType, CancellationToken cancellationToken,
		params ReadOnlySpan<object?> arguments)
	{
		string source = PrepareLua(operation, body, resultType, arguments);
		return Run(operation,
			token => LuaToolRuntime.ExecuteSource(Client, operation, source, resultType, _buffers, token),
			cancellationToken);
	}

	/// <summary>
	///     Runs one fixed Lua body inside the current <see cref="Run{T}" /> body, on Cheat Engine's main thread, for
	///     compound operations that combine Client calls and Lua in one dispatch. The body is checked against the exposure
	///     switches like <see cref="RunLua{T}" />.
	/// </summary>
	/// <typeparam name="T">The result type.</typeparam>
	/// <param name="operation">The operation name used in the Lua chunk name and errors.</param>
	/// <param name="body">
	///     The fixed, implementation-owned body; caller data is passed only through
	///     <paramref name="arguments" />.
	/// </param>
	/// <param name="resultType">The source-generated metadata of <typeparamref name="T" />.</param>
	/// <param name="cancellationToken">The token the enclosing body received, or the activation's stopping token.</param>
	/// <param name="arguments">Values encoded into the <c>a</c> table.</param>
	/// <returns>The deserialized result.</returns>
	/// <exception cref="CheatEngineToolException">The body needs a disabled switch or the script declared a failure.</exception>
	public T ExecuteLua<T>(string operation, string body, JsonTypeInfo<T> resultType,
		CancellationToken cancellationToken, params ReadOnlySpan<object?> arguments)
	{
		string source = PrepareLua(operation, body, resultType, arguments);
		return LuaToolRuntime.ExecuteSource(Client, operation, source, resultType, _buffers, cancellationToken);
	}

	private string PrepareLua<T>(string operation, string body, JsonTypeInfo<T> resultType,
		ReadOnlySpan<object?> arguments)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ArgumentNullException.ThrowIfNull(body);
		ArgumentNullException.ThrowIfNull(resultType);
		foreach (McpFeature feature in _luaRequirements.GetValue(body, LuaFeatureScan.Scan))
		{
			Features.Require(feature, operation);
		}

		try
		{
			return LuaToolRuntime.BuildSource(body, _budgetMilliseconds, arguments);
		}
		catch (ArgumentException exception)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.InvalidArgument, exception.Message,
				operation, ToolHostEffect.NotStarted, false), exception);
		}
	}

	private void Measure(string operation, long invoked, DispatchProbe probe)
	{
		TimeSpan queued = _time.GetElapsedTime(invoked, probe.Started);
		TimeSpan executed = _time.GetElapsedTime(probe.Started, probe.Finished);
		if (_statistics.Record(operation, queued, executed))
		{
			LogOverBudget(_logger, operation, (long) executed.TotalMilliseconds, (long) _budget.TotalMilliseconds);
		}
	}

	[LoggerMessage(EventId = 3001, Level = LogLevel.Warning,
		Message =
			"Dispatch {Operation} held Cheat Engine's main thread for {ElapsedMs} ms (budget {BudgetMs} ms).")]
	private static partial void LogOverBudget(ILogger logger, string operation, long elapsedMs, long budgetMs);

	[LoggerMessage(EventId = 3003, Level = LogLevel.Warning,
		Message =
			"Dispatch {Operation} was cancelled by its caller after its Cheat Engine work started; its effects stand unreported.")]
	private static partial void LogCancelledAfterStart(ILogger logger, string operation);

	[LoggerMessage(EventId = 3004, Level = LogLevel.Error,
		Message = "Dispatch {Operation} failed with an unexpected exception; reported as internal.")]
	private static partial void LogUnexpectedFault(ILogger logger, string operation, Exception exception);
}
