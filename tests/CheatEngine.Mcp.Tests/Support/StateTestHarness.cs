using System.Collections.Concurrent;

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
			new DispatchStatistics(execution), Time, new RecordingLogger<ToolDispatch>());
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
}

/// <summary>A clock whose timestamp and UTC time only move when a test advances it.</summary>
internal sealed class ManualClock : TimeProvider
{
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

	internal void Advance(TimeSpan elapsed)
	{
		Interlocked.Add(ref _ticks, elapsed.Ticks);
	}
}
