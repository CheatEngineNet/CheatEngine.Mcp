using System.Collections.Immutable;

using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Values;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>
///     <c>memory_read_samples</c> on a stepping clock: one short dispatch per tick with the waits between them, change
///     compression, in-band item errors, the tick schedule, target changes and cancellation.
/// </summary>
public sealed class MemorySampleToolsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void ReadSamples_ValueChanges_KeepTheFirstValueAndEveryChangeWithItsTime()
	{
		TargetDouble target = new();
		int[] health = [5, 5, 6, 6, 5];
		int tick = 0;
		Serve(target, address => address == 0x1000 ? health[tick] : 9, () => tick++);
		SteppingClock clock = new()
		{
			Probe = () => target.Dispatcher.Calls
		};

		MemorySampleResult result = new MemorySampleTools(target.Dispatch, clock).ReadSamples(["1000", "2000"],
			FixedValueType.Int32, 100, 400, cancellationToken: Token);

		Assert.Equal((5, 400), (result.Ticks, result.ElapsedMs));
		MemorySampleSeries first = result.Series[0];
		Assert.Equal(("1000", 5, 2, "5", "5", false), (first.Address, first.Samples, first.DistinctCount,
			first.First, first.Last, first.Truncated));
		Assert.Equal([new MemorySamplePoint(0, "5"), new MemorySamplePoint(200, "6"), new MemorySamplePoint(400, "5")],
			first.Changes);
		Assert.Equal([new MemorySamplePoint(0, "9")], result.Series[1].Changes);
		Assert.Null(first.Error);
		Assert.Equal(5, target.Dispatcher.Calls);
		// Each wait starts after the previous tick's dispatch returned: none holds Cheat Engine's main thread.
		Assert.Equal([(100, 1), (100, 2), (100, 3), (100, 4)],
			clock.Waits.Select(static wait => ((int) wait.Due.TotalMilliseconds, wait.Probe)));
	}

	[Fact]
	public void ReadSamples_Addresses_AreResolvedOnceForEveryTick()
	{
		TargetDouble target = new();
		target.Symbols["player.health"] = 0x1000;
		Serve(target, static _ => 1);

		MemorySampleResult result = new MemorySampleTools(target.Dispatch, new SteppingClock()).ReadSamples(
			["player.health", "2000"], FixedValueType.Int32, 100, 1000, cancellationToken: Token);

		Assert.Equal(11, result.Ticks);
		Assert.Equal("1000", result.Series[0].Address);
		Assert.Equal(2, target.CallsTo("Inspection").Length);
		Assert.Equal(11, target.CallsTo("Memory").Length);
	}

	[Fact]
	public void ReadSamples_UnresolvedAddress_ReportsNotFoundInBandAndSamplesTheOthers()
	{
		TargetDouble target = new();
		Serve(target, static _ => 3);

		MemorySampleResult result = new MemorySampleTools(target.Dispatch, new SteppingClock()).ReadSamples(
			["missing.symbol", "2000"], FixedValueType.Int32, 100, 200, cancellationToken: Token);

		MemorySampleSeries missing = result.Series[0];
		Assert.Equal(("missing.symbol", 0, ToolErrorKind.NotFound), (missing.Address, missing.Samples,
			missing.Error!.Kind));
		Assert.Empty(missing.Changes);
		Assert.Equal((3, "3"), (result.Series[1].Samples, result.Series[1].Last));
	}

	[Fact]
	public void ReadSamples_NoAddressResolves_ReturnsWithoutReadingOrWaiting()
	{
		TargetDouble target = new();
		SteppingClock clock = new();

		MemorySampleResult result = new MemorySampleTools(target.Dispatch, clock).ReadSamples(["nowhere"],
			FixedValueType.Float, 100, 10000, cancellationToken: Token);

		Assert.Equal(0, result.Ticks);
		Assert.Equal(ToolErrorKind.NotFound, result.Series[0].Error!.Kind);
		Assert.Empty(target.CallsTo("Memory"));
		Assert.Empty(clock.Waits);
	}

	[Fact]
	public void ReadSamples_ItemReadFailure_StaysInBandForThatTick()
	{
		TargetDouble target = new();
		int tick = 0;
		target.Memory = (method, arguments) =>
		{
			Assert.Equal(nameof(IMemoryClient.ReadPrimitiveBatchDetailed), method.Name);
			tick++;
			return tick == 2
				? new MemoryPrimitiveBatchReadOutcome<int>(2, [], 0,
					new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadPrimitiveBatch",
						"The page is not readable.", hostEffect: CheatEngineHostEffect.Completed))
				: new MemoryPrimitiveBatchReadOutcome<int>(
					((MemoryPrimitiveBatchReadRequest<int>) arguments[0]!).Addresses.Length,
					[.. ((MemoryPrimitiveBatchReadRequest<int>) arguments[0]!).Addresses.Select(static _ => 4)], null,
					null);
		};

		MemorySampleResult result = new MemorySampleTools(target.Dispatch, new SteppingClock()).ReadSamples(
			["1000", "2000"], FixedValueType.Int32, 100, 200, cancellationToken: Token);

		Assert.Equal(3, result.Ticks);
		Assert.Equal((2, ToolErrorKind.MemoryReadFailed), (result.Series[0].Samples, result.Series[0].Error!.Kind));
		Assert.Equal((3, (MemoryItemError?) null), (result.Series[1].Samples, result.Series[1].Error));
	}

	[Fact]
	public void ReadSamples_MoreChangesThanListed_TruncatesThePointsButKeepsTheCountsExact()
	{
		TargetDouble target = new();
		int tick = 0;
		Serve(target, _ => tick, () => tick++);

		MemorySampleResult result = new MemorySampleTools(target.Dispatch, new SteppingClock()).ReadSamples(["1000"],
			FixedValueType.Int32, MemorySampleTools.MinimumIntervalMilliseconds,
			MemorySampleTools.MaximumDurationMilliseconds, cancellationToken: Token);

		MemorySampleSeries series = result.Series[0];
		Assert.Equal(1001, result.Ticks);
		Assert.Equal((1001, 1001, true), (series.Samples, series.DistinctCount, series.Truncated));
		Assert.Equal(MemorySampleTools.MaximumPoints, series.Changes.Length);
		Assert.Equal(("0", "1000"), (series.First, series.Last));
		Assert.Equal(new MemorySamplePoint(2550, "255"), series.Changes[^1]);
	}

	[Fact]
	public void ReadSamples_SlowTicks_SkipMissedSlotsAndEndAtTheDuration()
	{
		TargetDouble target = new();
		SteppingClock clock = new();
		Serve(target, static _ => 1, () => clock.Advance(TimeSpan.FromMilliseconds(250)));

		MemorySampleResult result = new MemorySampleTools(target.Dispatch, clock).ReadSamples(["1000"],
			FixedValueType.Int32, 100, 1000, cancellationToken: Token);

		// The reads happen at 0, 300, 600 and 900 ms; elapsedMs ends at the last read, not after its dispatch.
		Assert.Equal((4, 900, 4), (result.Ticks, result.ElapsedMs, result.Series[0].Samples));
		Assert.Equal([50, 50, 50], clock.Waits.Select(static wait => (int) wait.Due.TotalMilliseconds));
		Assert.Null(result.MissedTicks);
	}

	[Fact]
	public void ReadSamples_SlowFirstDispatch_CountsTimeAndTheDurationFromTheFirstRead()
	{
		TargetDouble target = new();
		int tick = 0;
		Serve(target, _ => tick, () => tick++);
		SteppingClock clock = new();
		// The first dispatch, which resolves and then reads, waits 2 s for Cheat Engine's busy main thread.
		target.Dispatcher.Admission = _ =>
		{
			if (target.Dispatcher.Calls == 1)
			{
				clock.Advance(TimeSpan.FromMilliseconds(2000));
			}
		};

		MemorySampleResult result = new MemorySampleTools(target.Dispatch, clock).ReadSamples(["1000"],
			FixedValueType.Int32, 100, 500, cancellationToken: Token);

		Assert.Equal((6, 500), (result.Ticks, result.ElapsedMs));
		Assert.Equal([0, 100, 200, 300, 400, 500], result.Series[0].Changes.Select(static point => point.TimeMs));
		Assert.Equal([100, 100, 100, 100, 100], clock.Waits.Select(static wait => (int) wait.Due.TotalMilliseconds));
	}

	[Fact]
	public async Task ReadSamples_LaterTickRefusedAsBusy_IsSkippedAndCountedWhileSamplingGoesOn()
	{
		TargetDouble target = new(options: new McpExecutionOptions
		{
			MaxConcurrentDispatches = 1
		});
		int tick = 0;
		Serve(target, _ => tick, () => tick++);
		using ManualResetEventSlim held = new();
		using ManualResetEventSlim release = new();
		using ManualResetEventSlim finished = new();
		Task? other = null;
		SteppingClock clock = new()
		{
			OnWait = index =>
			{
				if (index == 1)
				{
					// Another call holds the only dispatch when the third tick is due.
					other = Task.Run(() => Hold(target, held, release, finished), Token);
					Assert.True(held.Wait(TimeSpan.FromSeconds(10), Token));
				}
				else if (index == 2)
				{
					release.Set();
					Assert.True(finished.Wait(TimeSpan.FromSeconds(10), Token));
				}
			}
		};

		MemorySampleResult result;
		try
		{
			result = new MemorySampleTools(target.Dispatch, clock).ReadSamples(["1000"], FixedValueType.Int32, 100,
				400, cancellationToken: Token);
		}
		finally
		{
			release.Set();
		}

		await other!;

		Assert.Equal((4, 400, (int?) 1), (result.Ticks, result.ElapsedMs, result.MissedTicks));
		Assert.Equal(
		[
			new MemorySamplePoint(0, "0"), new MemorySamplePoint(100, "1"), new MemorySamplePoint(300, "2"),
			new MemorySamplePoint(400, "3")
		], result.Series[0].Changes);
		Assert.Equal(4, target.CallsTo("Memory").Length);
	}

	[Fact]
	public async Task ReadSamples_FirstDispatchRefusedAsBusy_FailsBeforeReading()
	{
		TargetDouble target = new(options: new McpExecutionOptions
		{
			MaxConcurrentDispatches = 1
		});
		Serve(target, static _ => 1);
		using ManualResetEventSlim held = new();
		using ManualResetEventSlim release = new();
		using ManualResetEventSlim finished = new();
		Task other = Task.Run(() => Hold(target, held, release, finished), Token);
		Assert.True(held.Wait(TimeSpan.FromSeconds(10), Token));

		CheatEngineToolException exception;
		try
		{
			exception = Assert.Throws<CheatEngineToolException>(() =>
				new MemorySampleTools(target.Dispatch, new SteppingClock()).ReadSamples(["1000"],
					FixedValueType.Int32, 100, 400, cancellationToken: Token));
		}
		finally
		{
			release.Set();
		}

		await other;

		Assert.Equal((ToolErrorKind.Busy, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Empty(target.CallsTo("Memory"));
	}

	[Fact]
	public void ReadSamples_TargetChangedBetweenTicks_IsTargetChanged()
	{
		TargetDouble target = new();
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(2);
		Serve(target, static _ => 1);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemorySampleTools(target.Dispatch, new SteppingClock()).ReadSamples(["1000"],
				FixedValueType.Int32, 100, 1000, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.TargetChanged, exception.Error.Kind);
		Assert.Equal(3, target.Dispatcher.Calls);
		Assert.Equal(2, target.CallsTo("Memory").Length);
	}

	[Fact]
	public async Task ReadSamples_CancelledDuringAWait_StopsWithoutAnotherDispatch()
	{
		TargetDouble target = new();
		using ManualResetEventSlim read = new();
		Serve(target, static _ => 1, read.Set);
		using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
		MemorySampleTools tools = new(target.Dispatch, new SteppingClock
		{
			Frozen = true
		});

		Task<MemorySampleResult> sampling = Task.Run(() => tools.ReadSamples(["1000"], FixedValueType.Int32, 1000,
			10000, cancellationToken: cancellation.Token), Token);
		Assert.True(read.Wait(TimeSpan.FromSeconds(10), Token));
		await cancellation.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			sampling.WaitAsync(TimeSpan.FromSeconds(10), Token));
		Assert.Equal(1, target.Dispatcher.Calls);
	}

	[Fact]
	public async Task ReadSamples_ActivationStoppingDuringAWait_EndsTheWaitAndIsRefusedAsStopping()
	{
		using CancellationTokenSource stopping = new();
		TargetDouble target = new(stopping: stopping.Token);
		target.Dispatcher.Admission = RecordingDispatcher.ObserveCancellation;
		using ManualResetEventSlim read = new();
		Serve(target, static _ => 1, read.Set);
		MemorySampleTools tools = new(target.Dispatch, new SteppingClock
		{
			Frozen = true
		});

		Task<MemorySampleResult> sampling = Task.Run(() => tools.ReadSamples(["1000"], FixedValueType.Int32, 1000,
			10000, cancellationToken: Token), Token);
		Assert.True(read.Wait(TimeSpan.FromSeconds(10), Token));
		await stopping.CancelAsync();

		CheatEngineToolException exception = await Assert.ThrowsAsync<CheatEngineToolException>(() =>
			sampling.WaitAsync(TimeSpan.FromSeconds(10), Token));
		Assert.Equal(ToolErrorKind.Stopping, exception.Error.Kind);
		Assert.Single(target.CallsTo("Memory"));
	}

	[Fact]
	public async Task ReadSamples_ActivationStoppingWhileAnotherCallHoldsTheDispatch_FailsAsStoppingNotAsMissedTicks()
	{
		using CancellationTokenSource stopping = new();
		TargetDouble target = new(new McpExecutionOptions
		{
			MaxConcurrentDispatches = 1
		}, stopping.Token);
		Serve(target, static _ => 1);
		using ManualResetEventSlim held = new();
		using ManualResetEventSlim release = new();
		using ManualResetEventSlim finished = new();
		Task? other = null;
		SteppingClock clock = new()
		{
			OnWait = index =>
			{
				if (index == 0)
				{
					// The plugin starts stopping during the first wait while another call holds the only dispatch, so
					// the next tick is refused as busy before anything checks the activation.
					other = Task.Run(() => Hold(target, held, release, finished), Token);
					Assert.True(held.Wait(TimeSpan.FromSeconds(10), Token));
					stopping.Cancel();
				}
			}
		};

		CheatEngineToolException exception;
		try
		{
			exception = Assert.Throws<CheatEngineToolException>(() =>
				new MemorySampleTools(target.Dispatch, clock).ReadSamples(["1000"], FixedValueType.Int32, 100,
					1000, cancellationToken: Token));
		}
		finally
		{
			release.Set();
		}

		await other!;

		Assert.Equal((ToolErrorKind.Stopping, ToolHostEffect.NotStarted, false),
			(exception.Error.Kind, exception.Error.HostEffect, exception.Error.Retryable));
		Assert.Equal(CheatEngineToolNames.MemoryReadSamples, exception.Error.Operation);
		Assert.Equal(ToolErrorKind.Busy, Assert.IsType<CheatEngineToolException>(exception.InnerException).Error.Kind);
		// The loop stopped at the refused tick instead of skipping through the nine remaining slots.
		Assert.Single(clock.Waits);
		Assert.Single(target.CallsTo("Memory"));
	}

	[Fact]
	public void ReadSamples_Pointers_AreReadAsTypedAddressBatches()
	{
		TargetDouble target = new();
		target.Memory = (method, arguments) =>
		{
			Assert.Equal(typeof(Address), method.GetGenericArguments()[0]);
			return new MemoryPrimitiveBatchReadOutcome<Address>(1, [new Address(0x7FF6A1B2C3D0)], null, null);
		};

		MemorySampleResult result = new MemorySampleTools(target.Dispatch, new SteppingClock()).ReadSamples(["1000"],
			FixedValueType.Pointer, 1000, 100, cancellationToken: Token);

		Assert.Equal(1, result.Ticks);
		Assert.Equal("7FF6A1B2C3D0", result.Series[0].First);
	}

	[Theory]
	[InlineData(0, FixedValueType.Int32, 100, 2000, "addresses", ToolErrorKind.InvalidArgument)]
	[InlineData(65, FixedValueType.Int32, 100, 2000, "addresses", ToolErrorKind.LimitExceeded)]
	[InlineData(1, (FixedValueType) 11, 100, 2000, "valueType", ToolErrorKind.InvalidArgument)]
	[InlineData(1, FixedValueType.Int32, 9, 2000, "intervalMs", ToolErrorKind.InvalidArgument)]
	[InlineData(1, FixedValueType.Int32, 1001, 2000, "intervalMs", ToolErrorKind.LimitExceeded)]
	[InlineData(1, FixedValueType.Int32, 100, 99, "durationMs", ToolErrorKind.InvalidArgument)]
	[InlineData(1, FixedValueType.Int32, 100, 10001, "durationMs", ToolErrorKind.LimitExceeded)]
	public void ReadSamples_InvalidArguments_RefuseBeforeAnyDispatch(int count, FixedValueType valueType,
		int intervalMs, int durationMs, string parameter, ToolErrorKind kind)
	{
		TargetDouble target = new();
		string[] addresses = [.. Enumerable.Repeat("1000", count)];

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemorySampleTools(target.Dispatch, new SteppingClock()).ReadSamples(addresses, valueType, intervalMs,
				durationMs, cancellationToken: Token));

		Assert.Equal((kind, ToolHostEffect.NotStarted), (exception.Error.Kind, exception.Error.HostEffect));
		Assert.StartsWith(parameter, exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	/// <summary>
	///     Holds one of the target's dispatches, as another tool call would: signals <paramref name="held" /> once
	///     admitted, returns when <paramref name="release" /> is set, then signals <paramref name="finished" />.
	/// </summary>
	private static void Hold(TargetDouble target, ManualResetEventSlim held, ManualResetEventSlim release,
		ManualResetEventSlim finished)
	{
		_ = target.Dispatch.Run("other_call", _ =>
		{
			held.Set();
			release.Wait(Token);
			return 0;
		}, Token);
		finished.Set();
	}

	/// <summary>
	///     Answers typed int32 batch reads with a value per address; <paramref name="afterRead" /> runs after each.
	/// </summary>
	private static void Serve(TargetDouble target, Func<ulong, int> value, Action? afterRead = null)
	{
		target.Memory = (method, arguments) =>
		{
			if (method.Name != nameof(IMemoryClient.ReadPrimitiveBatchDetailed) ||
				method.GetGenericArguments()[0] != typeof(int))
			{
				throw new XunitException($"Unexpected {method.Name}.");
			}

			ImmutableArray<Address> addresses = ((MemoryPrimitiveBatchReadRequest<int>) arguments[0]!).Addresses;
			ImmutableArray<int> values = [.. addresses.Select(address => value(address.ToUInt64()))];
			afterRead?.Invoke();
			return new MemoryPrimitiveBatchReadOutcome<int>(addresses.Length, values.AsSpan(), null, null);
		};
	}
}
