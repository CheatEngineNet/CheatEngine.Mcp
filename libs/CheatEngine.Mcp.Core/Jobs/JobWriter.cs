using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Jobs;

/// <summary>
///     The bounded item buffer of a managed job: items are numbered from 1, and once <see cref="BufferLimit" /> items are
///     retained each new one evicts the oldest and counts it as dropped. The job's work adds items from any thread;
///     polls read them without consuming them.
/// </summary>
/// <typeparam name="TItem">The item type.</typeparam>
public sealed class JobWriter<TItem>
{
	private readonly Lock _lock = new();
	private bool _closed;
	private long _dropped;
	private long _first = 1;
	private long _last;
	private long? _progressDone;
	private long? _progressTotal;
	private TItem[] _ring;

	internal JobWriter(int bufferLimit)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(bufferLimit, 1);
		_ring = new TItem[bufferLimit];
		BufferLimit = bufferLimit;
	}

	/// <summary>How many items are retained at most.</summary>
	public int BufferLimit
	{
		get;
	}

	/// <summary>Adds one item, evicting the oldest when the buffer is full; once the job ended it adds nothing.</summary>
	/// <param name="item">The item.</param>
	/// <returns><see langword="true" /> when the item was added.</returns>
	public bool Add(TItem item)
	{
		lock (_lock)
		{
			if (_closed)
			{
				return false;
			}

			long sequence = _last + 1;
			_ring[Slot(sequence)] = item;
			_last = sequence;
			if (sequence - _first >= _ring.Length)
			{
				_first++;
				_dropped++;
			}

			return true;
		}
	}

	/// <summary>Reports progress in the job's own unit; once the job ended it reports nothing.</summary>
	/// <param name="done">How much work is done.</param>
	/// <param name="total">How much work there is, when known.</param>
	public void Progress(long done, long? total = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(done);
		if (total is < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(total), total, "The total cannot be negative.");
		}

		lock (_lock)
		{
			if (!_closed)
			{
				_progressDone = done;
				_progressTotal = total;
			}
		}
	}

	/// <summary>Reads up to <paramref name="limit" /> items after <paramref name="afterSequence" />; read-only.</summary>
	/// <param name="afterSequence">The cursor, 0 to the last issued sequence.</param>
	/// <param name="limit">The page size.</param>
	/// <returns>The page parts.</returns>
	/// <exception cref="CheatEngineToolException">The cursor is past the last issued sequence.</exception>
	internal JobWriterPage<TItem> Read(long afterSequence, int limit)
	{
		lock (_lock)
		{
			if (afterSequence > _last)
			{
				throw CheatEngineToolException.InvalidArgument("afterSequence",
					$"must be between 0 and the last issued sequence, {_last}.",
					"Pass 0 first, then the nextAfterSequence of the previous poll.");
			}

			long start = Math.Max(afterSequence + 1, _first);
			long stop = Math.Min(start + limit - 1, _last);
			TItem[] items = stop >= start ? new TItem[stop - start + 1] : [];
			for (long sequence = start; sequence <= stop; sequence++)
			{
				items[sequence - start] = _ring[Slot(sequence)];
			}

			long next = stop >= start ? stop : afterSequence;
			return new JobWriterPage<TItem>(items, _first, next, next < _last, _dropped);
		}
	}

	/// <summary>The counters of the buffer.</summary>
	/// <returns>Retained, produced and evicted items, and the progress.</returns>
	internal (long Buffered, long Total, long Dropped, long? ProgressDone, long? ProgressTotal) Counters()
	{
		lock (_lock)
		{
			return (_last - _first + 1, _last, _dropped, _progressDone, _progressTotal);
		}
	}

	/// <summary>Ends the buffer: later items and progress are ignored.</summary>
	internal void Close()
	{
		lock (_lock)
		{
			_closed = true;
		}
	}

	/// <summary>Ends the buffer and drops its items; the counters stay.</summary>
	internal void Discard()
	{
		lock (_lock)
		{
			_closed = true;
			_first = _last + 1;
			_ring = [];
		}
	}

	private int Slot(long sequence)
	{
		return (int) ((sequence - 1) % BufferLimit);
	}
}

/// <summary>One page read from a <see cref="JobWriter{TItem}" />.</summary>
/// <typeparam name="TItem">The item type.</typeparam>
/// <param name="Items">The items.</param>
/// <param name="FirstSequence">The oldest retained sequence.</param>
/// <param name="NextAfterSequence">The cursor for the next poll.</param>
/// <param name="More">Whether retained items remain.</param>
/// <param name="Dropped">How many items were evicted.</param>
internal sealed record JobWriterPage<TItem>(
	TItem[] Items,
	long FirstSequence,
	long NextAfterSequence,
	bool More,
	long Dropped);
