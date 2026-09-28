using System.ComponentModel;
using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>
///     The <c>memory_read_samples</c> tool: watches a few addresses for a few seconds. Every tick is one short
///     dispatch, and the call waits between ticks on its own thread, so Cheat Engine's main thread is never held across
///     a wait.
/// </summary>
[McpServerToolType]
public sealed class MemorySampleTools
{
	/// <summary>The most addresses one call samples.</summary>
	internal const int MaximumAddresses = 64;

	/// <summary>The shortest interval between two ticks.</summary>
	internal const int MinimumIntervalMilliseconds = 10;

	/// <summary>The longest interval between two ticks.</summary>
	internal const int MaximumIntervalMilliseconds = 1000;

	/// <summary>The shortest sampling.</summary>
	internal const int MinimumDurationMilliseconds = 100;

	/// <summary>The longest sampling, 10 seconds.</summary>
	internal const int MaximumDurationMilliseconds = 10_000;

	/// <summary>The most points one series lists.</summary>
	internal const int MaximumPoints = 256;

	private readonly ToolDispatch _dispatch;
	private readonly TimeProvider _time;

	/// <summary>Creates the tool; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="time">The clock that times the ticks and the waits between them.</param>
	public MemorySampleTools(ToolDispatch dispatch, TimeProvider time)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(time);
		_dispatch = dispatch;
		_time = time;
	}

	/// <summary>Reads up to 64 addresses every interval for a bounded duration and keeps each value change.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryReadSamples, Title = "Sample values over time", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read up to 64 addresses as one fixed-size value type every intervalMs for durationMs (at most 10 s from the first read, so the call returns within about that time), for example to find which scan result follows a value while it changes in the target. Addresses are resolved once; each tick is one short Cheat Engine dispatch, and the call waits between ticks without holding Cheat Engine's main thread. Each series keeps its first value and every change, at most 256 points with their time since the first read, plus first, last and distinct counts. An address that does not resolve or cannot be read reports its error in band; a later tick that Cheat Engine refuses as busy is skipped and counted in missedTicks. Fails with target_changed if Cheat Engine selects another process between ticks.")]
	public MemorySampleResult ReadSamples(
		[Description("The addresses or Cheat Engine address expressions to sample, 1 to 64, resolved once at the start.")]
		string[] addresses,
		[Description("The type every address is read as: int8 to uint64, float, double or pointer.")]
		FixedValueType valueType,
		[Description("The time between two ticks in milliseconds, 10 to 1000.")]
		int intervalMs = 100,
		[Description(
			"How long to sample in milliseconds, 100 to 10000, from the first read; a tick runs then and every interval.")]
		int durationMs = 2000,
		[Description(MemoryByteOrders.Description)]
		MemoryByteOrder byteOrder = MemoryByteOrder.LittleEndian,
		CancellationToken cancellationToken = default)
	{
		string[] expressions = CheckAddresses(addresses);
		McpValueType type = MemoryTargets.RequireFixedType(valueType, "valueType");
		MemoryByteOrders.Require(type, byteOrder, "byteOrder");
		MemoryTargets.RequireRange(intervalMs, "intervalMs", MinimumIntervalMilliseconds, MaximumIntervalMilliseconds);
		MemoryTargets.RequireRange(durationMs, "durationMs", MinimumDurationMilliseconds, MaximumDurationMilliseconds);
		Series[] series = [.. expressions.Select(static expression => new Series(expression))];
		// Time counts from the first read, taken inside the dispatch after the resolution, so neither the resolution
		// nor the wait for Cheat Engine's main thread skews the schedule or the reported times.
		(Address[] resolved, int[] readable, long epoch, long started, MemoryReadBatchEntry?[] entries) =
			_dispatch.Run(CheatEngineToolNames.MemoryReadSamples, token =>
			{
				ICheatEngineClient client = _dispatch.Client;
				Address[] targets = Resolve(client, expressions, series, token);
				int[] indices = [.. Enumerable.Range(0, expressions.Length).Where(index => series[index].Resolved)];
				long observed = MemoryTargets.SelectionEpoch(client, token);
				long first = _time.GetTimestamp();
				return (targets, indices, observed, first, Read(client, type, byteOrder, targets, indices, token));
			}, cancellationToken);
		if (readable.Length == 0)
		{
			return new MemorySampleResult(0, 0, [.. series.Select(static item => item.Describe())]);
		}

		using CancellationTokenSource waits =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _dispatch.Client.Stopping);
		int ticks = 0;
		int missed = 0;
		int slot = 0;
		int time = 0;
		int last = 0;
		MemoryReadBatchEntry?[]? read = entries;
		while (true)
		{
			if (read is not null)
			{
				foreach (int index in readable)
				{
					series[index].Record(time, read[index]!);
				}

				ticks++;
				last = time;
			}

			int now = Elapsed(started, _time.GetTimestamp());
			int next = Math.Max(slot + 1, (now + intervalMs - 1) / intervalMs);
			long due = (long) next * intervalMs;
			if (due > durationMs)
			{
				break;
			}

			if (due > now)
			{
				Wait(TimeSpan.FromMilliseconds(due - now), waits.Token, cancellationToken);
			}

			slot = next;
			(time, read) = Tick(type, byteOrder, resolved, readable, epoch, started, cancellationToken);
			if (read is null)
			{
				missed++;
			}
		}

		return new MemorySampleResult(ticks, last, [.. series.Select(static item => item.Describe())],
			missed > 0 ? missed : null);
	}

	private static string[] CheckAddresses(string[]? addresses)
	{
		if (addresses is null || addresses.Length == 0)
		{
			throw CheatEngineToolException.InvalidArgument("addresses", "must list at least one address.");
		}

		if (addresses.Length > MaximumAddresses)
		{
			throw CheatEngineToolException.LimitExceeded("addresses",
				$"accepts at most {MaximumAddresses} addresses.");
		}

		string[] expressions = new string[addresses.Length];
		for (int index = 0; index < addresses.Length; index++)
		{
			expressions[index] = MemoryTargets.RequireExpression(addresses[index],
				$"addresses[{index.ToString(CultureInfo.InvariantCulture)}]");
		}

		return expressions;
	}

	/// <summary>Resolves every expression once; one that does not resolve keeps its error in band.</summary>
	private static Address[] Resolve(ICheatEngineClient client, string[] expressions, Series[] series,
		CancellationToken cancellationToken)
	{
		Address[] addresses = new Address[expressions.Length];
		for (int index = 0; index < expressions.Length; index++)
		{
			if (MemoryTargets.TryResolve(client, expressions[index], out addresses[index],
					out CheatEngineFailure failure, cancellationToken))
			{
				series[index].Resolve(addresses[index]);
				continue;
			}

			series[index].Fail(MemoryTargets.IsItemFailure(failure)
				? MemoryTargets.ItemError(client, failure)
				: throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested));
		}

		return addresses;
	}

	/// <summary>One tick: reads the resolved addresses in typed batches inside the current dispatch.</summary>
	private static MemoryReadBatchEntry?[] Read(ICheatEngineClient client, McpValueType valueType,
		MemoryByteOrder byteOrder, Address[] addresses, int[] indices, CancellationToken cancellationToken)
	{
		MemoryReadBatchEntry?[] entries = new MemoryReadBatchEntry?[addresses.Length];
		if (indices.Length > 0)
		{
			MemoryReadTools.ReadFixed(client, valueType, byteOrder, indices, addresses, entries, cancellationToken);
		}

		return entries;
	}

	/// <summary>
	///     One tick after the first: its own dispatch, which checks the target and reads. A tick refused as <c>busy</c>
	///     before it started, because other calls fill the dispatch limit, is skipped and returns no entries: the
	///     observations collected so far are worth more than one missing tick. Once the activation is stopping, the
	///     refusal fails the call as <c>stopping</c> instead, as the next dispatch would, because a stopped wait no
	///     longer paces the loop and every remaining tick would be skipped at once.
	/// </summary>
	private (int Time, MemoryReadBatchEntry?[]? Entries) Tick(McpValueType valueType, MemoryByteOrder byteOrder,
		Address[] addresses, int[] indices, long epoch, long started, CancellationToken cancellationToken)
	{
		try
		{
			return _dispatch.Run<(int, MemoryReadBatchEntry?[]?)>(CheatEngineToolNames.MemoryReadSamples, token =>
			{
				ICheatEngineClient client = _dispatch.Client;
				MemoryTargets.RequireSameTarget(client, epoch, CheatEngineToolNames.MemoryReadSamples, token);
				int time = Elapsed(started, _time.GetTimestamp());
				return (time, Read(client, valueType, byteOrder, addresses, indices, token));
			}, cancellationToken);
		}
		catch (CheatEngineToolException exception) when (exception.Error.Kind is ToolErrorKind.Busy &&
														 exception.Error.HostEffect is ToolHostEffect.NotStarted)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (_dispatch.Client.Stopping.IsCancellationRequested)
			{
				throw CheatEngineToolException.FromFailure(new CheatEngineFailure(CheatEngineFailureKind.Cancelled,
					CheatEngineToolNames.MemoryReadSamples, "The plugin activation is stopping.", exception,
					CheatEngineHostEffect.NotStarted), true);
			}

			return (0, null);
		}
	}

	private int Elapsed(long started, long now)
	{
		double milliseconds = _time.GetElapsedTime(started, now).TotalMilliseconds;
		return milliseconds >= int.MaxValue ? int.MaxValue : milliseconds <= 0 ? 0 : (int) milliseconds;
	}

	/// <summary>
	///     Waits on the calling thread, never on Cheat Engine's. The caller's cancellation ends the call; the
	///     activation's stop ends the wait early and the next dispatch reports it.
	/// </summary>
	private void Wait(TimeSpan delay, CancellationToken waits, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		TaskCompletionSource elapsed = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using ITimer timer = _time.CreateTimer(static state => ((TaskCompletionSource) state!).TrySetResult(),
			elapsed, delay, Timeout.InfiniteTimeSpan);
		try
		{
			elapsed.Task.Wait(waits);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// The activation is stopping: the next dispatch is refused as stopping.
		}
	}

	/// <summary>The samples of one address: its first value, every change up to a bound, and counts.</summary>
	/// <param name="expression">The caller's expression, reported until it resolves.</param>
	private sealed class Series(string expression)
	{
		private readonly HashSet<string> _distinct = new(StringComparer.Ordinal);
		private readonly List<MemorySamplePoint> _points = [];
		private string _address = expression;
		private MemoryItemError? _error;
		private string? _first;
		private string? _last;
		private int _samples;
		private bool _truncated;

		/// <summary>Whether the expression resolved.</summary>
		internal bool Resolved
		{
			get;
			private set;
		}

		internal void Resolve(Address address)
		{
			_address = HexFormat.Address(address);
			Resolved = true;
		}

		internal void Fail(MemoryItemError error)
		{
			_error = error;
		}

		internal void Record(int time, MemoryReadBatchEntry entry)
		{
			if (entry.Value is not { } value)
			{
				_error = entry.Error;
				return;
			}

			_samples++;
			_first ??= value;
			_distinct.Add(value);
			if (!string.Equals(_last, value, StringComparison.Ordinal))
			{
				if (_points.Count < MaximumPoints)
				{
					_points.Add(new MemorySamplePoint(time, value));
				}
				else
				{
					_truncated = true;
				}
			}

			_last = value;
		}

		internal MemorySampleSeries Describe()
		{
			return new MemorySampleSeries(_address, _samples, _distinct.Count, [.. _points], _first, _last, _truncated,
				_error);
		}
	}
}
