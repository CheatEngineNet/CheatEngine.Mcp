using System.Collections.Concurrent;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>
///     A clock that never waits: creating a timer advances the clock by the timer's due time and fires it at once, so a
///     timed loop runs deterministically. <see cref="Frozen" /> timers never fire, for tests that cancel a wait.
/// </summary>
internal sealed class SteppingClock : TimeProvider
{
	private long _ticks = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero).UtcTicks;

	public override long TimestampFrequency => TimeSpan.TicksPerSecond;

	/// <summary>When set, timers are recorded but never fire and the clock does not move.</summary>
	internal bool Frozen
	{
		get;
		init;
	}

	/// <summary>A value recorded with each timer, such as how many dispatches ran before the wait.</summary>
	internal Func<int>? Probe
	{
		get;
		init;
	}

	/// <summary>
	///     Runs on the waiting thread when a timer is created, before it fires, with the number of timers created before
	///     it; a test uses it to change the world during a wait.
	/// </summary>
	internal Action<int>? OnWait
	{
		get;
		init;
	}

	/// <summary>Every timer's due time and the probe value when it was created, in creation order.</summary>
	internal ConcurrentQueue<(TimeSpan Due, int Probe)> Waits
	{
		get;
	} = [];

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
		int index = Waits.Count;
		Waits.Enqueue((dueTime, Probe?.Invoke() ?? 0));
		OnWait?.Invoke(index);
		if (!Frozen)
		{
			Advance(dueTime);
			callback(state);
		}

		return new InertTimer();
	}

	/// <summary>Moves the clock, as a slow Cheat Engine call would.</summary>
	/// <param name="elapsed">The time to add.</param>
	internal void Advance(TimeSpan elapsed)
	{
		Interlocked.Add(ref _ticks, elapsed.Ticks);
	}

	private sealed class InertTimer : ITimer
	{
		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			return true;
		}

		public void Dispose()
		{
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}
}
