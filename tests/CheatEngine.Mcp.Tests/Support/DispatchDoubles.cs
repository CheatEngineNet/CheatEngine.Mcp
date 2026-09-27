using System.Collections.Concurrent;

using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;

using Microsoft.Extensions.Logging;

namespace CheatEngine.Mcp.Tests.Support;

/// <summary>
///     A main-thread dispatcher double that counts every admission attempt, records the token it observed and runs the
///     callback inline, unless <see cref="Admission" /> refuses the call first as the real dispatcher would.
/// </summary>
internal sealed class RecordingDispatcher
{
	private int _calls;

	internal RecordingDispatcher()
	{
		Dispatcher = ClientTestDouble.Create<ICheatEngineDispatcher>((method, arguments) =>
		{
			if (method.Name == "get_IsMainThread")
			{
				return true;
			}

			if (!method.Name.StartsWith("Invoke", StringComparison.Ordinal) || arguments?[0] is not Delegate callback)
			{
				throw new NotSupportedException($"No dispatcher behavior was configured for {method.Name}.");
			}

			Interlocked.Increment(ref _calls);
			CancellationToken token = arguments.Length > 1 && arguments[1] is CancellationToken observed
				? observed
				: CancellationToken.None;
			Tokens.Enqueue(token);
			Admission?.Invoke(token);
			return callback.DynamicInvoke();
		});
	}

	/// <summary>The dispatcher to place in a Client double.</summary>
	internal ICheatEngineDispatcher Dispatcher
	{
		get;
	}

	/// <summary>How many times a dispatch was requested, whether or not its callback ran.</summary>
	internal int Calls => Volatile.Read(ref _calls);

	/// <summary>The tokens the dispatcher observed, in call order.</summary>
	internal ConcurrentQueue<CancellationToken> Tokens
	{
		get;
	} = [];

	/// <summary>Runs before the callback with the observed token; throw to refuse the dispatch before admission.</summary>
	internal Action<CancellationToken>? Admission
	{
		get;
		set;
	}

	/// <summary>Refuses admission like the Client does once the observed token is cancelled.</summary>
	/// <param name="token">The observed token.</param>
	internal static void ObserveCancellation(CancellationToken token)
	{
		if (token.IsCancellationRequested)
		{
			new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Dispatcher.Invoke",
					"The dispatch was cancelled before admission.", hostEffect: CheatEngineHostEffect.NotStarted)
				.Throw(token);
		}
	}
}

/// <summary>A clock that only moves when a test advances it.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
	private long _timestamp = TimeSpan.TicksPerSecond;

	public override long TimestampFrequency => TimeSpan.TicksPerSecond;

	public override long GetTimestamp()
	{
		return Volatile.Read(ref _timestamp);
	}

	internal void Advance(TimeSpan elapsed)
	{
		Interlocked.Add(ref _timestamp, elapsed.Ticks);
	}
}

/// <summary>A logger that keeps every entry for assertions.</summary>
/// <typeparam name="T">The category type.</typeparam>
internal sealed class RecordingLogger<T> : ILogger<T>
{
	internal ConcurrentQueue<(LogLevel Level, EventId EventId, string Message, Exception? Exception)> Entries
	{
		get;
	} = [];

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull
	{
		return null;
	}

	public bool IsEnabled(LogLevel logLevel)
	{
		return true;
	}

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
		Func<TState, Exception?, string> formatter)
	{
		ArgumentNullException.ThrowIfNull(formatter);
		Entries.Enqueue((logLevel, eventId, formatter(state, exception), exception));
	}
}
