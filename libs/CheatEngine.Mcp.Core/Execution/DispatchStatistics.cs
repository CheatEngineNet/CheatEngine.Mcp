using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Core.Execution;

/// <summary>
///     Counts the activation's tool dispatches and their main-thread cost. It is an activation-scoped service updated
///     lock-free from any request thread; it holds no static state.
/// </summary>
public sealed class DispatchStatistics
{
	private readonly int _budgetMilliseconds;
	private readonly long _budgetTicks;
	private int _count;
	private Peak? _maxExecuted;
	private long _maxQueuedTicks;
	private int _overBudget;
	private int _rejected;

	/// <summary>Creates empty statistics for the configured budget.</summary>
	/// <param name="options">The execution limits; their value is read once.</param>
	public DispatchStatistics(IOptions<McpExecutionOptions> options)
	{
		ArgumentNullException.ThrowIfNull(options);
		_budgetMilliseconds = options.Value.DispatchBudgetMilliseconds;
		_budgetTicks = TimeSpan.FromMilliseconds(_budgetMilliseconds).Ticks;
	}

	/// <summary>The configured dispatch budget.</summary>
	public TimeSpan Budget => TimeSpan.FromTicks(_budgetTicks);

	/// <summary>Records one dispatch whose body ran.</summary>
	/// <param name="operation">The dispatched operation.</param>
	/// <param name="queued">How long the call waited for the main thread before its body started.</param>
	/// <param name="executed">How long the body held the main thread.</param>
	/// <returns><see langword="true" /> when the body exceeded the budget.</returns>
	public bool Record(string operation, TimeSpan queued, TimeSpan executed)
	{
		ArgumentNullException.ThrowIfNull(operation);
		Interlocked.Increment(ref _count);
		RaiseMaximum(ref _maxQueuedTicks, queued.Ticks);
		Peak candidate = new(executed.Ticks, operation);
		Peak? current = Volatile.Read(ref _maxExecuted);
		while (current is null || candidate.Ticks > current.Ticks)
		{
			Peak? observed = Interlocked.CompareExchange(ref _maxExecuted, candidate, current);
			if (ReferenceEquals(observed, current))
			{
				break;
			}

			current = observed;
		}

		if (executed.Ticks <= _budgetTicks)
		{
			return false;
		}

		Interlocked.Increment(ref _overBudget);
		return true;
	}

	/// <summary>Records one call refused because the concurrency limit was reached.</summary>
	public void RecordRejected()
	{
		Interlocked.Increment(ref _rejected);
	}

	/// <summary>Reports the statistics so far.</summary>
	/// <returns>A consistent-enough snapshot; concurrent dispatches may land between its fields.</returns>
	public DispatchSummary Snapshot()
	{
		Peak? executed = Volatile.Read(ref _maxExecuted);
		return new DispatchSummary(_budgetMilliseconds, Volatile.Read(ref _count), Volatile.Read(ref _overBudget),
			Volatile.Read(ref _rejected), ToMilliseconds(executed?.Ticks ?? 0), executed?.Operation,
			ToMilliseconds(Interlocked.Read(ref _maxQueuedTicks)));
	}

	private static void RaiseMaximum(ref long maximum, long value)
	{
		long current = Interlocked.Read(ref maximum);
		while (value > current)
		{
			long observed = Interlocked.CompareExchange(ref maximum, value, current);
			if (observed == current)
			{
				return;
			}

			current = observed;
		}
	}

	private static int ToMilliseconds(long ticks)
	{
		return (int) Math.Min(int.MaxValue, TimeSpan.FromTicks(ticks).TotalMilliseconds);
	}

	private sealed record Peak(long Ticks, string Operation);
}
