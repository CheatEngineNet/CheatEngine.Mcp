using System.Collections.Immutable;

using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>
///     The snapshot tools over a Client double: chunked copies, zero-filled unreadable pages, the store's caps, typed
///     comparisons with another snapshot or with live memory, and the epoch rule of a live comparison.
/// </summary>
public sealed class MemorySnapshotToolsTests
{
	private const ulong Base = 0x10000;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void CreateSnapshot_ReadableRange_CopiesTheBytesAndDescribesTheTarget()
	{
		TargetDouble target = new();
		target.Symbols["player"] = Base;
		byte[] memory = Pattern(64);
		Serve(target, memory);
		SteppingClock clock = new();
		MemorySnapshotStore store = new();

		MemorySnapshotInfo info = Tools(target, store, clock).CreateSnapshot("before", "player", 64, Token);

		Assert.Equal(("before", "10000", 64, 42, 1L), (info.Name, info.Address, info.Size, info.ProcessId,
			info.SelectionEpoch));
		Assert.Equal((clock.GetUtcNow(), 0, false, 8), (info.CreatedUtc, info.UnreadableBytes,
			info.UnreadableTruncated, info.PointerSize!.Value));
		Assert.Empty(info.Unreadable);
		Assert.Equal(memory, store.Get("before", "name").Image.Bytes);
		Assert.Equal(1, target.Dispatcher.Calls);
		Assert.Equal(64L, store.TotalBytes);
	}

	[Fact]
	public void CreateSnapshot_LargerThanOneChunk_ReadsOneMiBPerDispatch()
	{
		TargetDouble target = new();
		byte[] memory = Pattern(MemoryTargets.ChunkBytes + 16);
		List<int> lengths = [];
		Serve(target, memory, lengths);
		MemorySnapshotStore store = new();

		MemorySnapshotInfo info = Tools(target, store).CreateSnapshot("big", "10000", memory.Length, Token);

		Assert.Equal(memory.Length, info.Size);
		Assert.Equal([MemoryTargets.ChunkBytes, 16], lengths);
		Assert.Equal(2, target.Dispatcher.Calls);
		Assert.Equal(memory, store.Get("big", "name").Image.Bytes);
	}

	[Fact]
	public void CreateSnapshot_UnreadablePages_AreZeroFilledListedAndSkippedByComparisons()
	{
		TargetDouble target = new();
		byte[] memory = Pattern(0x4000);
		// 0x11000-0x12FFF is reserved memory.
		Serve(target, memory, hole: (0x11000, 0x13000));
		MemorySnapshotTools tools = Tools(target);

		MemorySnapshotInfo before = tools.CreateSnapshot("before", "10000", memory.Length, Token);
		memory[0x10] ^= 0xFF;
		memory[0x3000] ^= 0xFF;
		tools.CreateSnapshot("after", "10000", memory.Length, Token);
		MemorySnapshotDiff diff = tools.CompareSnapshot("before", "after", FixedValueType.UInt8, limit: 10,
			cancellationToken: Token);

		Assert.Equal((0x2000, false), (before.UnreadableBytes, before.UnreadableTruncated));
		Assert.Equal([new UnreadableRange("1000", 0x2000)], before.Unreadable);
		Assert.Equal((0x2000, 0x2000, 2), (diff.Compared, diff.Skipped, diff.Total));
		Assert.Equal(["10", "3000"], diff.Changes.Select(static change => change.Offset));
	}

	[Fact]
	public void CreateSnapshot_TargetChangedBetweenChunks_IsTargetChangedAndFreesTheName()
	{
		TargetDouble target = new();
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(2);
		Serve(target, new byte[MemoryTargets.ChunkBytes + 1]);
		MemorySnapshotStore store = new();
		MemorySnapshotTools tools = Tools(target, store);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.CreateSnapshot("x", "10000", MemoryTargets.ChunkBytes + 1, Token));
		MemorySnapshotInfo retried = tools.CreateSnapshot("x", "10000", 16, Token);

		Assert.Equal(ToolErrorKind.TargetChanged, exception.Error.Kind);
		Assert.Equal(2L, retried.SelectionEpoch);
		Assert.Equal(16L, store.TotalBytes);
	}

	[Theory]
	[InlineData("", "10000", 16, "name", ToolErrorKind.InvalidArgument)]
	[InlineData("two words", "10000", 16, "name", ToolErrorKind.InvalidArgument)]
	[InlineData("a/b", "10000", 16, "name", ToolErrorKind.InvalidArgument)]
	[InlineData("n12345678901234567890123456789012345678901234567890123456789012345", "10000", 16, "name",
		ToolErrorKind.InvalidArgument)]
	[InlineData("ok", " ", 16, "address", ToolErrorKind.InvalidArgument)]
	[InlineData("ok", "10000", 0, "size", ToolErrorKind.InvalidArgument)]
	[InlineData("ok", "10000", (16 * 1024 * 1024) + 1, "size", ToolErrorKind.LimitExceeded)]
	public void CreateSnapshot_InvalidArguments_RefuseBeforeAnyDispatch(string name, string address, int size,
		string parameter, ToolErrorKind kind)
	{
		TargetDouble target = new();
		MemorySnapshotStore store = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target, store).CreateSnapshot(name, address, size, Token));

		Assert.Equal((kind, ToolHostEffect.NotStarted), (exception.Error.Kind, exception.Error.HostEffect));
		Assert.StartsWith(parameter, exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, target.Dispatcher.Calls);
		Assert.Equal(0L, store.TotalBytes);
	}

	[Fact]
	public void CreateSnapshot_TakenName_IsInvalidStateBeforeAnyDispatch()
	{
		TargetDouble target = new();
		Serve(target, Pattern(16));
		MemorySnapshotTools tools = Tools(target);
		tools.CreateSnapshot("same", "10000", 16, Token);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.CreateSnapshot("same", "10000", 16, Token));

		Assert.Equal(ToolErrorKind.InvalidState, exception.Error.Kind);
		Assert.Contains("memory_delete_snapshot", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(1, target.Dispatcher.Calls);
	}

	[Fact]
	public void CreateSnapshot_SeventeenthSnapshot_IsLimitExceededBeforeAnyDispatch()
	{
		TargetDouble target = new();
		Serve(target, Pattern(16));
		MemorySnapshotTools tools = Tools(target);
		for (int index = 0; index < MemorySnapshotStore.MaximumSnapshots; index++)
		{
			tools.CreateSnapshot($"s{index}", "10000", 16, Token);
		}

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.CreateSnapshot("one-more", "10000", 16, Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Equal("name", exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(MemorySnapshotStore.MaximumSnapshots, target.Dispatcher.Calls);
	}

	[Fact]
	public void CreateSnapshot_Above64MiBInTotal_IsLimitExceededBeforeAnyDispatch()
	{
		TargetDouble target = new();
		MemorySnapshotStore store = new();
		for (int index = 0; index < 4; index++)
		{
			store.Reserve($"big{index}", MemorySnapshotStore.MaximumSnapshotBytes);
		}

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target, store).CreateSnapshot("small", "10000", 1, Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Equal("size", exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void CreateSnapshot_RangeCrossingTheEndOfTheAddressSpace_IsRefusedWithoutReading()
	{
		TargetDouble target = new();
		MemorySnapshotStore store = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target, store).CreateSnapshot("edge", "FFFFFFFFFFFFFFF0", 32, Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.StartsWith("size", exception.Error.Message, StringComparison.Ordinal);
		Assert.Empty(target.CallsTo("Memory"));
		Assert.Equal(0L, store.TotalBytes);
	}

	[Fact]
	public void CreateSnapshot_UnresolvedAddress_IsNotFoundAndFreesTheName()
	{
		TargetDouble target = new();
		MemorySnapshotStore store = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target, store).CreateSnapshot("x", "missing.symbol", 16, Token));

		Assert.Equal(ToolErrorKind.NotFound, exception.Error.Kind);
		Assert.Empty(store.List());
		Assert.Equal(0L, store.TotalBytes);
	}

	[Fact]
	public void CompareSnapshot_TwoSnapshots_ListsChangedSlotsWithoutAnyDispatch()
	{
		TargetDouble target = new();
		byte[] memory = new byte[16];
		Serve(target, memory);
		MemorySnapshotTools tools = Tools(target);
		tools.CreateSnapshot("before", "10000", 16, Token);
		memory[4] = 2;
		memory[12] = 0xFF;
		tools.CreateSnapshot("after", "10000", 16, Token);
		int dispatches = target.Dispatcher.Calls;

		MemorySnapshotDiff diff = tools.CompareSnapshot("before", "after", cancellationToken: Token);

		Assert.Equal(("before", "10000", FixedValueType.Int32, 4, SnapshotChangeFilter.Changed),
			(diff.Name, diff.Address, diff.ValueType, diff.Alignment, diff.Change));
		Assert.Equal((4, 0, 2, "after", (int?) null), (diff.Compared, diff.Skipped, diff.Total, diff.CompareTo,
			diff.NextOffset));
		Assert.Equal([new SnapshotChange("10004", "4", "0", "2"), new SnapshotChange("1000C", "C", "0", "255")],
			diff.Changes);
		Assert.Equal(dispatches, target.Dispatcher.Calls);
	}

	[Fact]
	public void CompareSnapshot_DifferentAddresses_ComparesTwoObjectsFieldByField()
	{
		TargetDouble target = new();
		byte[] memory = new byte[32];
		memory[16 + 8] = 7;
		Serve(target, memory);
		MemorySnapshotTools tools = Tools(target);
		tools.CreateSnapshot("first", "10000", 16, Token);
		tools.CreateSnapshot("second", "10010", 16, Token);

		MemorySnapshotDiff diff = tools.CompareSnapshot("first", "second", cancellationToken: Token);

		Assert.Equal([new SnapshotChange("10008", "8", "0", "7")], diff.Changes);
	}

	[Fact]
	public void CompareSnapshot_LiveMemoryInTheSameEpoch_ReadsTheRangeAgain()
	{
		TargetDouble target = new();
		byte[] memory = new byte[8];
		Serve(target, memory);
		MemorySnapshotTools tools = Tools(target);
		tools.CreateSnapshot("before", "10000", 8, Token);
		memory[0] = 10;

		MemorySnapshotDiff increased = tools.CompareSnapshot("before", change: SnapshotChangeFilter.Increased,
			cancellationToken: Token);
		MemorySnapshotDiff unchanged = tools.CompareSnapshot("before", change: SnapshotChangeFilter.Unchanged,
			cancellationToken: Token);

		Assert.Null(increased.CompareTo);
		Assert.Equal([new SnapshotChange("10000", "0", "0", "10")], increased.Changes);
		Assert.Equal([new SnapshotChange("10004", "4", "0", "0")], unchanged.Changes);
		Assert.Equal(3, target.Dispatcher.Calls);
	}

	[Fact]
	public void CompareSnapshot_LiveAfterTheTargetChanged_IsTargetChangedWithoutReadingMemory()
	{
		TargetDouble target = new();
		Serve(target, Pattern(16));
		MemorySnapshotTools tools = Tools(target);
		tools.CreateSnapshot("before", "10000", 16, Token);
		tools.CreateSnapshot("copy", "10000", 16, Token);
		int reads = target.CallsTo("Memory").Length;
		target.Epochs.Clear();
		target.Epochs.Enqueue(2);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.CompareSnapshot("before", cancellationToken: Token));
		MemorySnapshotDiff stored = tools.CompareSnapshot("before", "copy", cancellationToken: Token);

		Assert.Equal((ToolErrorKind.TargetChanged, ToolHostEffect.NotApplied),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("compareTo", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(reads, target.CallsTo("Memory").Length);
		Assert.Equal(0, stored.Total);
	}

	[Fact]
	public void CompareSnapshot_LiveTargetChangedBetweenChunks_IsTargetChanged()
	{
		TargetDouble target = new();
		Serve(target, new byte[MemoryTargets.ChunkBytes + 1]);
		MemorySnapshotTools tools = Tools(target);
		tools.CreateSnapshot("big", "10000", MemoryTargets.ChunkBytes + 1, Token);
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(2);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.CompareSnapshot("big", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.TargetChanged, exception.Error.Kind);
		Assert.Contains("between two chunks", exception.Error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void CompareSnapshot_PointersOfA32BitTarget_UseFourByteSlots()
	{
		TargetDouble target = new()
		{
			Bitness = PointerSize.Bit32
		};
		byte[] memory = new byte[8];
		Serve(target, memory);
		MemorySnapshotTools tools = Tools(target);
		tools.CreateSnapshot("before", "10000", 8, Token);
		memory[4] = 0x10;
		memory[5] = 0x20;
		memory[6] = 0x40;

		MemorySnapshotDiff diff = tools.CompareSnapshot("before", valueType: FixedValueType.Pointer,
			cancellationToken: Token);

		Assert.Equal((4, 2), (diff.Alignment, diff.Compared));
		Assert.Equal([new SnapshotChange("10004", "4", "0", "402010")], diff.Changes);
	}

	[Fact]
	public void CompareSnapshot_Paging_ReturnsTheWindowAndTheNextOffset()
	{
		TargetDouble target = new();
		byte[] memory = new byte[10];
		Serve(target, memory);
		MemorySnapshotTools tools = Tools(target);
		tools.CreateSnapshot("before", "10000", 10, Token);
		Array.Fill(memory, (byte) 1);
		tools.CreateSnapshot("after", "10000", 10, Token);

		MemorySnapshotDiff middle = tools.CompareSnapshot("before", "after", FixedValueType.UInt8, offset: 3,
			limit: 4, cancellationToken: Token);
		MemorySnapshotDiff last = tools.CompareSnapshot("before", "after", FixedValueType.UInt8, offset: 8,
			limit: 4, cancellationToken: Token);
		MemorySnapshotDiff past =
			tools.CompareSnapshot("before", "after", FixedValueType.UInt8, offset: 20, cancellationToken: Token);

		Assert.Equal(["3", "4", "5", "6"], middle.Changes.Select(static change => change.Offset));
		Assert.Equal((10, 7), (middle.Total, middle.NextOffset!.Value));
		Assert.Equal(["8", "9"], last.Changes.Select(static change => change.Offset));
		Assert.Null(last.NextOffset);
		Assert.Empty(past.Changes);
		Assert.Null(past.NextOffset);
	}

	[Theory]
	[InlineData("missing", null, FixedValueType.Int32, null, 0, 100, "name", ToolErrorKind.NotFound)]
	[InlineData("before", "missing", FixedValueType.Int32, null, 0, 100, "compareTo", ToolErrorKind.NotFound)]
	[InlineData("before", "short", FixedValueType.Int32, null, 0, 100, "compareTo", ToolErrorKind.InvalidArgument)]
	[InlineData("before", null, (FixedValueType) 11, null, 0, 100, "valueType", ToolErrorKind.InvalidArgument)]
	[InlineData("before", null, FixedValueType.Int32, 3, 0, 100, "alignment", ToolErrorKind.InvalidArgument)]
	[InlineData("before", null, FixedValueType.Int32, 8, 0, 100, "alignment", ToolErrorKind.InvalidArgument)]
	[InlineData("before", null, FixedValueType.Int16, 0, 0, 100, "alignment", ToolErrorKind.InvalidArgument)]
	[InlineData("before", null, FixedValueType.Int32, null, -1, 100, "offset", ToolErrorKind.InvalidArgument)]
	[InlineData("before", null, FixedValueType.Int32, null, 0, 0, "limit", ToolErrorKind.InvalidArgument)]
	[InlineData("before", null, FixedValueType.Int32, null, 0, 1001, "limit", ToolErrorKind.LimitExceeded)]
	[InlineData("bad name", null, FixedValueType.Int32, null, 0, 100, "name", ToolErrorKind.InvalidArgument)]
	public void CompareSnapshot_InvalidArguments_RefuseBeforeAnyDispatch(string name, string? compareTo,
		FixedValueType valueType, int? alignment, int offset, int limit, string parameter, ToolErrorKind kind)
	{
		TargetDouble target = new();
		Serve(target, Pattern(16));
		MemorySnapshotTools tools = Tools(target);
		tools.CreateSnapshot("before", "10000", 16, Token);
		tools.CreateSnapshot("short", "10000", 8, Token);
		int dispatches = target.Dispatcher.Calls;

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.CompareSnapshot(name, compareTo, valueType, alignment, offset: offset, limit: limit,
				cancellationToken: Token));

		Assert.Equal((kind, ToolHostEffect.NotStarted), (exception.Error.Kind, exception.Error.HostEffect));
		Assert.StartsWith(parameter, exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(dispatches, target.Dispatcher.Calls);
	}

	[Fact]
	public void ListSnapshots_OldestFirst_ReportsTheBytesWithoutAnyDispatch()
	{
		TargetDouble target = new();
		Serve(target, Pattern(32));
		MemorySnapshotTools tools = Tools(target);
		tools.CreateSnapshot("one", "10000", 8, Token);
		tools.CreateSnapshot("two", "10010", 16, Token);
		int dispatches = target.Dispatcher.Calls;

		MemorySnapshotList list = tools.ListSnapshots();

		Assert.Equal(["one", "two"], list.Snapshots.Select(static snapshot => snapshot.Name));
		Assert.Equal(["10000", "10010"], list.Snapshots.Select(static snapshot => snapshot.Address));
		Assert.Equal((24L, MemorySnapshotStore.MaximumTotalBytes, MemorySnapshotStore.MaximumSnapshots),
			(list.TotalBytes, list.MaximumTotalBytes, list.MaximumSnapshots));
		Assert.Equal(dispatches, target.Dispatcher.Calls);
	}

	[Fact]
	public void DeleteSnapshot_FreesItsBytesAndAnUnknownNameIsNotFound()
	{
		TargetDouble target = new();
		Serve(target, Pattern(16));
		MemorySnapshotStore store = new();
		MemorySnapshotTools tools = Tools(target, store);
		tools.CreateSnapshot("gone", "10000", 16, Token);
		int dispatches = target.Dispatcher.Calls;

		MemorySnapshotDeleted deleted = tools.DeleteSnapshot("gone");
		CheatEngineToolException again = Assert.Throws<CheatEngineToolException>(() => tools.DeleteSnapshot("gone"));

		Assert.Equal(new MemorySnapshotDeleted("gone", 16), deleted);
		Assert.Equal(ToolErrorKind.NotFound, again.Error.Kind);
		Assert.Empty(tools.ListSnapshots().Snapshots);
		Assert.Equal(0L, store.TotalBytes);
		Assert.Equal(dispatches, target.Dispatcher.Calls);
	}

	[Fact]
	public void SnapshotStillBeingTaken_IsBusyForCompareAndDeleteAndIsNotListed()
	{
		TargetDouble target = new();
		MemorySnapshotStore store = new();
		store.Reserve("pending", 16);
		MemorySnapshotTools tools = Tools(target, store);

		CheatEngineToolException compare = Assert.Throws<CheatEngineToolException>(() =>
			tools.CompareSnapshot("pending", cancellationToken: Token));
		CheatEngineToolException delete = Assert.Throws<CheatEngineToolException>(() =>
			tools.DeleteSnapshot("pending"));

		Assert.Equal((ToolErrorKind.Busy, ToolErrorKind.Busy), (compare.Error.Kind, delete.Error.Kind));
		Assert.Empty(tools.ListSnapshots().Snapshots);
		Assert.Equal(16L, tools.ListSnapshots().TotalBytes);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	private static MemorySnapshotTools Tools(TargetDouble target, MemorySnapshotStore? store = null,
		SteppingClock? clock = null)
	{
		return new MemorySnapshotTools(target.Dispatch, store ?? new MemorySnapshotStore(),
			clock ?? new SteppingClock());
	}

	private static byte[] Pattern(int length)
	{
		return [.. Enumerable.Range(0, length).Select(static value => (byte) (value * 7))];
	}

	/// <summary>
	///     Serves <paramref name="memory" /> at <see cref="Base" /> through detailed byte reads; the optional hole is
	///     reserved memory that fails to read, as Cheat Engine reports it.
	/// </summary>
	private static void Serve(TargetDouble target, byte[] memory, List<int>? lengths = null,
		(ulong Start, ulong End)? hole = null)
	{
		target.Memory = (method, arguments) =>
		{
			if (method.Name != nameof(IMemoryClient.ReadBytesDetailed))
			{
				throw new XunitException($"Unexpected {method.Name}.");
			}

			MemoryBytesReadRequest request = (MemoryBytesReadRequest) arguments[0]!;
			lengths?.Add(request.Length);
			ulong start = request.Address.ToUInt64();
			int readable = request.Length;
			if (hole is { } reserved && start < reserved.End && start + (ulong) request.Length > reserved.Start)
			{
				readable = start >= reserved.Start ? 0 : (int) (reserved.Start - start);
			}

			ImmutableArray<byte> bytes = memory.AsSpan((int) (start - Base), readable).ToImmutableArray();
			return readable == request.Length
				? new MemoryBytesReadOutcome(request.Length, bytes, null)
				: new MemoryBytesReadOutcome(request.Length, bytes,
					new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadBytes",
						"Partial read.", hostEffect: CheatEngineHostEffect.Completed));
		};
		if (hole is not { } region)
		{
			return;
		}

		target.Inspection = (method, arguments) =>
		{
			Assert.Equal(nameof(IInspectionClient.TryGetMemoryRegion), method.Name);
			arguments[1] = new MemoryRegionInfo(new Address(region.Start), new Address(region.Start),
				MemoryProtection.None, new MemorySize(region.End - region.Start), MemoryRegionState.Reserved,
				MemoryProtection.None, MemoryRegionType.Private, null);
			arguments[2] = default(CheatEngineFailure);
			return true;
		};
	}
}
