using System.Collections.Concurrent;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Client;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Lua;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;

using Microsoft.Extensions.Options;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Support;

/// <summary>
///     One activation's state ledger, target resources and job registry over a Client double: an inline dispatcher that
///     counts dispatches and a Lua facade whose answers are canned per result type, so the managed logic runs without
///     Cheat Engine. The fixed scripts themselves are proven by the NativeLua tests.
/// </summary>
internal sealed class StateTestHarness
{
	private readonly ConcurrentDictionary<Type, Func<string, object>> _answers = new();
	private int _dispatches;

	internal StateTestHarness(McpExecutionOptions? options = null, bool onMainThread = true, bool withLedger = true)
	{
		ICheatEngineDispatcher dispatcher = ClientTestDouble.Create<ICheatEngineDispatcher>((method, arguments) =>
		{
			if (method.Name == "get_IsMainThread")
			{
				return onMainThread;
			}

			if (method.Name.StartsWith("Invoke", StringComparison.Ordinal) && arguments?[0] is Delegate callback)
			{
				if (arguments.Length > 1 && arguments[1] is CancellationToken cancellationToken)
				{
					cancellationToken.ThrowIfCancellationRequested();
				}

				Interlocked.Increment(ref _dispatches);
				return callback.DynamicInvoke();
			}

			throw new NotSupportedException($"No dispatcher behavior was configured for {method.Name}.");
		});
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ILuaClient.Execute), method.Name);
			return Respond(arguments![0]!);
		});
		Client = ClientTestDouble.Client(dispatcher, Stopping.Token, (nameof(ICheatEngineClient.Lua), lua));
		IOptions<McpExecutionOptions> execution = Options.Create(options ?? new McpExecutionOptions());
		Dispatch = new ToolDispatch(Client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), execution,
			new DispatchStatistics(execution), Time, new RecordingLogger<ToolDispatch>(), new FixedLuaExecutor(this));
		Ledger = withLedger ? new McpStateLedger(Dispatch, Time) : null;
		Resources = new TargetResources(Ledger, Time);
		Jobs = new JobRegistry(Dispatch, Resources, execution, Time);
		// An empty root unless a test says otherwise.
		Answer(static _ => new LuaStateSnapshot([], 0, false));
		Answer(static _ => new LuaStateRelease([], null, []));
	}

	internal CancellationTokenSource Stopping
	{
		get;
	} = new();

	internal ManualClock Time
	{
		get;
	} = new();

	internal ICheatEngineClient Client
	{
		get;
	}

	internal ToolDispatch Dispatch
	{
		get;
	}

	internal McpStateLedger? Ledger
	{
		get;
	}

	internal TargetResources Resources
	{
		get;
	}

	internal JobRegistry Jobs
	{
		get;
	}

	/// <summary>The Lua operations run, in order, with their complete sources.</summary>
	internal ConcurrentQueue<(string Operation, string Source)> LuaCalls
	{
		get;
	} = [];

	/// <summary>How many dispatches were requested.</summary>
	internal int Dispatches => Volatile.Read(ref _dispatches);

	/// <summary>Answers every Lua operation whose result is <typeparamref name="T" />, given its complete source.</summary>
	internal void Answer<T>(Func<string, T> answer)
	{
		_answers[typeof(T)] = source => new LuaJsonResult<T>(answer(source), null, 0);
	}

	/// <summary>Makes every Lua operation whose result is <typeparamref name="T" /> declare an <c>mcp_error</c>.</summary>
	internal void Declare<T>(string kind, string message, string? hostEffect = "not_started")
	{
		_answers[typeof(T)] = _ => new LuaJsonResult<T>(default, new LuaScriptError(kind, message, hostEffect, null),
			0);
	}

	/// <summary>A Lua entry as the state scripts describe it.</summary>
	internal static LuaStateEntry Entry(string id, string family, string state, bool holdsHostState,
		string? cleanupError = null, long ageMs = 10)
	{
		string[] parts = id.Split('-');
		return new LuaStateEntry(id, parts[0], family, parts[1], state, ageMs, holdsHostState,
			CleanupError: cleanupError, ProcessId: 42);
	}

	private object Respond(object operation)
	{
		Type type = operation.GetType();
		Assert.True(type.IsGenericType, $"Unexpected Lua operation {type.Name}.");
		string name = (string) type.GetProperty("Operation")!.GetValue(operation)!;
		string source = (string) type.GetProperty("Source")!.GetValue(operation)!;
		LuaCalls.Enqueue((name, source));
		Type result = type.GetGenericArguments()[0];
		return _answers.TryGetValue(result, out Func<string, object>? answer)
			? answer(source)
			: throw new XunitException($"No Lua answer was configured for {name} ({result.Name}).");
	}

	private LuaJsonResult<T> Respond<T>(string operation, string source)
	{
		LuaCalls.Enqueue((operation, source));
		return _answers.TryGetValue(typeof(T), out Func<string, object>? answer)
			? Assert.IsType<LuaJsonResult<T>>(answer(source))
			: throw new XunitException($"No Lua answer was configured for {operation} ({typeof(T).Name}).");
	}

	private sealed class FixedLuaExecutor(StateTestHarness harness) : IFixedLuaExecutor
	{
		public LuaJsonResult<T> Execute<T>(string operation, string source, JsonTypeInfo<T> resultType,
			LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return harness.Respond<T>(operation, source);
		}
	}
}

/// <summary>A clock whose timestamp and UTC time only move when a test advances it.</summary>
internal sealed class ManualClock : TimeProvider
{
	private readonly List<ManualTimer> _timers = [];
	private readonly Lock _timersLock = new();
	private long _ticks = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero).UtcTicks;

	public override long TimestampFrequency => TimeSpan.TicksPerSecond;

	public override long GetTimestamp()
	{
		return Volatile.Read(ref _ticks);
	}

	public override DateTimeOffset GetUtcNow()
	{
		return new DateTimeOffset(Volatile.Read(ref _ticks), TimeSpan.Zero);
	}

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		ArgumentNullException.ThrowIfNull(callback);
		ManualTimer timer = new(this, callback, state, dueTime, period);
		lock (_timersLock)
		{
			_timers.Add(timer);
		}

		return timer;
	}

	internal void Advance(TimeSpan elapsed)
	{
		long now = Interlocked.Add(ref _ticks, elapsed.Ticks);
		ManualTimer[] due;
		lock (_timersLock)
		{
			due = [.. _timers.Where(timer => timer.TakeDue(now))];
		}

		foreach (ManualTimer timer in due)
		{
			timer.Fire();
		}
	}

	private void Remove(ManualTimer timer)
	{
		lock (_timersLock)
		{
			_timers.Remove(timer);
		}
	}

	private sealed class ManualTimer : ITimer
	{
		private readonly TimerCallback _callback;
		private readonly ManualClock _clock;
		private readonly object? _state;
		private int _disposed;
		private long _next;
		private long _period;

		internal ManualTimer(ManualClock clock, TimerCallback callback, object? state, TimeSpan dueTime,
			TimeSpan period)
		{
			_clock = clock;
			_callback = callback;
			_state = state;
			Schedule(Volatile.Read(ref clock._ticks), dueTime, period);
		}

		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			lock (_clock._timersLock)
			{
				if (Volatile.Read(ref _disposed) != 0)
				{
					return false;
				}

				Schedule(Volatile.Read(ref _clock._ticks), dueTime, period);
				return true;
			}
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				_clock.Remove(this);
			}
		}

		public ValueTask DisposeAsync()
		{
			Dispose();
			return ValueTask.CompletedTask;
		}

		internal bool TakeDue(long now)
		{
			if (Volatile.Read(ref _disposed) != 0 || _next > now)
			{
				return false;
			}

			if (_period == Timeout.InfiniteTimeSpan.Ticks)
			{
				_next = long.MaxValue;
			}
			else
			{
				_next = _next > long.MaxValue - _period ? long.MaxValue : _next + _period;
			}

			return true;
		}

		internal void Fire()
		{
			if (Volatile.Read(ref _disposed) == 0)
			{
				_callback(_state);
			}
		}

		private void Schedule(long now, TimeSpan dueTime, TimeSpan period)
		{
			if (dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
			{
				throw new ArgumentOutOfRangeException(nameof(dueTime));
			}

			if (period < TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
			{
				throw new ArgumentOutOfRangeException(nameof(period));
			}

			_next = dueTime == Timeout.InfiniteTimeSpan || dueTime > TimeSpan.FromTicks(long.MaxValue - now)
				? long.MaxValue
				: now + dueTime.Ticks;
			_period = period.Ticks;
		}
	}
}
