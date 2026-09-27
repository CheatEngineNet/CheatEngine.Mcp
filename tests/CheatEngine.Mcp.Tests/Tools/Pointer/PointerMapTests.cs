using System.Buffers.Binary;

using CheatEngine.Mcp.Tools.Pointer;

namespace CheatEngine.Mcp.Tests.Tools.Pointer;

/// <summary>The managed pointer map: capture parsing, depth-first search, reverse lookup and offline resolution.</summary>
public sealed class PointerMapTests
{
	private static readonly long[] TwoHops = [0x10, 0x20];
	private static readonly long[] ZeroThenOne = [0, 1];
	private static readonly long[] NegativeThenPositive = [-0x30, 0x20];

	[Fact]
	public void Search_MultilevelGraph_ReturnsDereferenceOrderAndModuleRoot()
	{
		PointerMap map = Map([new PointerEntry(0x1000, 0x2000), new PointerEntry(0x2010, 0x3000)]);

		PointerSearchResult result = map.Search(Options(0x3020, 5, 0x20), null, CancellationToken.None);

		PointerPath path = Assert.Single(result.Paths);
		Assert.Equal((0x1000UL, "game.exe", 0UL), (path.BaseAddress, path.Module, path.ModuleOffset));
		Assert.Equal(TwoHops, path.Offsets);
		Assert.True(map.TryResolve(path, out ulong destination));
		Assert.Equal(0x3020UL, destination);
		Assert.False(result.Truncated);
		Assert.False(result.Cancelled);
	}

	[Fact]
	public void Search_CyclesDepthAndOffsetLimits_DoNotProduceInvalidPaths()
	{
		PointerMap map = Map([
			new PointerEntry(0x1000, 0x2000), new PointerEntry(0x2000, 0x3000), new PointerEntry(0x3000, 0x2000)
		]);

		Assert.Empty(map.Search(Options(0x3001, 1, 1), null, CancellationToken.None).Paths);
		Assert.Empty(map.Search(Options(0x3001, 5, 0), null, CancellationToken.None).Paths);
		PointerSearchResult result = map.Search(Options(0x3001, 8, 1), null, CancellationToken.None);
		Assert.Equal(ZeroThenOne, Assert.Single(result.Paths).Offsets);
		Assert.True(result.VisitedNodes < 10, "Cycle detection should stop repeated graph traversal.");
	}

	[Fact]
	public void Search_ResultAndNodeCaps_ReportTruncation()
	{
		PointerMap map = Map([new PointerEntry(0x1000, 0x3000), new PointerEntry(0x1008, 0x3000)]);

		PointerSearchResult results = map.Search(Options(0x3000, 1, 0) with
		{
			MaximumResults = 1
		}, null,
			CancellationToken.None);
		PointerSearchResult nodes = map.Search(Options(0x3000, 1, 0) with
		{
			MaximumNodes = 1
		}, null,
			CancellationToken.None);

		Assert.Single(results.Paths);
		Assert.True(results.Truncated);
		Assert.Equal(1, nodes.VisitedNodes);
		Assert.True(nodes.Truncated);
	}

	[Fact]
	public void Search_CancelledToken_ReturnsCancelledWithoutThrowing()
	{
		PointerMap map = Map([new PointerEntry(0x1000, 0x3000)]);
		using CancellationTokenSource stop = new();
		stop.Cancel();

		PointerSearchResult result = map.Search(Options(0x3000, 1, 0), null, stop.Token);

		Assert.True(result.Cancelled);
		Assert.Empty(result.Paths);
	}

	[Fact]
	public void Search_CancelledToken_EmptyMap_ReturnsCancelledWithoutThrowing()
	{
		using CancellationTokenSource stop = new();
		stop.Cancel();

		PointerSearchResult result = Map([]).Search(Options(0x3000, 1, 0), null, stop.Token);

		Assert.True(result.Cancelled);
		Assert.Empty(result.Paths);
		Assert.Equal(0, result.VisitedNodes);
	}

	[Fact]
	public void Search_CancelledToken_WithoutInRangeCandidate_ReturnsCancelledWithoutThrowing()
	{
		PointerMap map = Map([new PointerEntry(0x1000, 0x4000)]);
		using CancellationTokenSource stop = new();
		stop.Cancel();

		PointerSearchResult result = map.Search(Options(0x3000, 1, 0), null, stop.Token);

		Assert.True(result.Cancelled);
		Assert.Empty(result.Paths);
		Assert.Equal(0, result.VisitedNodes);
	}

	[Fact]
	public void Search_NegativeOffsets_AreFollowedOnlyWhenAllowed()
	{
		PointerMap map = Map([new PointerEntry(0x1000, 0x2040), new PointerEntry(0x2010, 0x3000)]);

		Assert.Empty(map.Search(Options(0x3020, 2, 0x40), null, CancellationToken.None).Paths);
		PointerPath path = Assert.Single(map.Search(Options(0x3020, 2, 0x40) with
		{
			AllowNegativeOffsets = true
		},
			null, CancellationToken.None).Paths);

		Assert.Equal(NegativeThenPositive, path.Offsets);
		Assert.True(map.TryResolve(path, out ulong destination));
		Assert.Equal(0x3020UL, destination);
	}

	[Fact]
	public void Search_StaticRootsOnly_DropsHeapRootsButKeepsFollowingThem()
	{
		PointerMap map = Map([new PointerEntry(0x1000, 0x5000), new PointerEntry(0x5000, 0x3000)]);

		PointerSearchResult roots = map.Search(Options(0x3000, 2, 0), null, CancellationToken.None);
		PointerSearchResult all = map.Search(Options(0x3000, 2, 0) with
		{
			StaticRootsOnly = false
		}, null,
			CancellationToken.None);

		Assert.Equal(0x1000UL, Assert.Single(roots.Paths).BaseAddress);
		Assert.Equal([0x5000UL, 0x1000UL], all.Paths.Select(static path => path.BaseAddress));
		Assert.Null(all.Paths[0].Module);
	}

	[Theory]
	[InlineData(4, 0x89ABCDEFUL)]
	[InlineData(8, 0xFEDCBA9876543210UL)]
	public void CopyEntries_TargetWidthAndAbsoluteAlignment_ArePreserved(int width, ulong value)
	{
		byte[] bytes = new byte[16];
		if (width == 8)
		{
			BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(3), value);
		}
		else
		{
			BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(3), (uint) value);
		}

		PointerEntryBuffer entries = new();
		PointerMap.CopyEntries(entries, 0x1001, bytes, width, 4, 1, null);

		Assert.Equal([new PointerEntry(0x1004, value)], entries.ToArray());
	}

	[Fact]
	public void CopyEntries_ValueFilter_KeepsOnlyValuesThatPointIntoMemory()
	{
		byte[] bytes = new byte[24];
		BinaryPrimitives.WriteUInt64LittleEndian(bytes, 0x2008);
		BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 1234);
		BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), 0x4FFF);
		PointerValueFilter filter = new([(0x4000, 0x4FFF), (0x2000, 0x2FFF), (0x3000, 0x3FFF)]);
		PointerEntryBuffer entries = new();

		PointerMap.CopyEntries(entries, 0x1000, bytes, 8, 8, 10, filter);

		Assert.Equal(1, filter.RangeCount);
		Assert.Equal([new PointerEntry(0x1000, 0x2008), new PointerEntry(0x1010, 0x4FFF)], entries.ToArray());
		Assert.False(filter.Contains(0x1FFF));
		Assert.False(filter.Contains(0x5000));
	}

	[Fact]
	public void EntryBuffer_ManySegments_KeepsInsertionOrder()
	{
		PointerEntryBuffer entries = new();
		for (ulong index = 0; index < PointerEntryBuffer.SegmentLength + 3; index++)
		{
			entries.Add(new PointerEntry(index, index + 1));
		}

		PointerEntry[] copy = entries.ToArray();

		Assert.Equal(PointerEntryBuffer.SegmentLength + 3, copy.Length);
		Assert.Equal(new PointerEntry(PointerEntryBuffer.SegmentLength + 2, PointerEntryBuffer.SegmentLength + 3),
			copy[^1]);
	}

	[Fact]
	public void TryResolve_NewSnapshot_RebasesModulesAndRejectsMissingOrAmbiguousRoots()
	{
		PointerPath path = new(0x1008, "game.exe", 8, [0x10, 0x20]);
		PointerMap rebased = Map([new PointerEntry(0x5008, 0x6000), new PointerEntry(0x6010, 0x7000)],
			[new PointerModule("GAME.EXE", 0x5000, 0x100)]);

		Assert.True(rebased.TryResolve(path, out ulong destination));
		Assert.Equal(0x7020UL, destination);
		Assert.False(Map([], []).TryResolve(path, out _));
		Assert.False(Map([],
				[new PointerModule("game.exe", 0x5000, 0x100), new PointerModule("game.exe", 0x8000, 0x100)])
			.TryResolve(path, out _));
		Assert.False(Map([new PointerEntry(0x1008, ulong.MaxValue)]).TryResolve(path, out _),
			"Offset addition must not wrap.");
	}

	[Fact]
	public void FindReferences_Range_ListsNearestFirstAndCountsBeyondTheLimit()
	{
		PointerMap map = Map([
			new PointerEntry(0x1010, 0x3000), new PointerEntry(0x5000, 0x3010), new PointerEntry(0x1000, 0x3010),
			new PointerEntry(0x1020, 0x2FF0), new PointerEntry(0x1030, 0x3011)
		], [new PointerModule("game.exe", 0x1000, 0x100)]);
		List<PointerEntry> found = [];

		int count = map.FindReferences(0x3010, 0x10, null, 2, found);
		List<PointerEntry> scoped = [];
		int inModule = map.FindReferences(0x3010, 0x10, map.Modules[0], 10, scoped);

		Assert.Equal(3, count);
		Assert.Equal([new PointerEntry(0x1000, 0x3010), new PointerEntry(0x5000, 0x3010)], found);
		Assert.Equal(2, inModule);
		Assert.Equal([0x1000UL, 0x1010UL], scoped.Select(static entry => entry.Address));
	}

	[Fact]
	public void FindReferences_EqualValueTieAtLimit_KeepsLowestHolderAddresses()
	{
		PointerMap map = Map([
			new PointerEntry(0x3000, 0x4000), new PointerEntry(0x1000, 0x4000), new PointerEntry(0x2000, 0x4000),
			new PointerEntry(0x4000, 0x3FFF)
		]);
		List<PointerEntry> found = [];

		int count = map.FindReferences(0x4000, 1, null, 2, found);

		Assert.Equal(4, count);
		Assert.Equal([new PointerEntry(0x1000, 0x4000), new PointerEntry(0x2000, 0x4000)], found);
	}

	[Fact]
	public void Constructor_UnsortedEntries_AreSortedByAddress()
	{
		PointerMap map = Map([new PointerEntry(0x2000, 0x3000), new PointerEntry(0x1000, 0x2000)]);

		Assert.Equal([0x1000UL, 0x2000UL], map.Entries.Select(static entry => entry.Address));
		Assert.True(map.TryResolve(new PointerPath(0x1000, null, 0, [0, 0]), out ulong destination));
		Assert.Equal(0x3000UL, destination);
	}

	[Theory]
	[InlineData(new long[] { 0x10, 0x4C8 }, "[[game.exe+1A2B30]+10]+4C8")]
	[InlineData(new long[] { -8 }, "[game.exe+1A2B30]-8")]
	[InlineData(new long[] { 0, 0, 0 }, "[[[game.exe+1A2B30]+0]+0]+0")]
	public void ChainExpression_Offsets_UseCheatEngineBracketNotation(long[] offsets, string expected)
	{
		Assert.Equal(expected, PointerSupport.ChainExpression("game.exe+1A2B30", offsets));
	}

	private static PointerSearchOptions Options(ulong target, int depth, int offset)
	{
		return new PointerSearchOptions(target, depth, offset, false, true, 100, 1000);
	}

	private static PointerMap Map(PointerEntry[] entries, PointerModule[]? modules = null)
	{
		return new PointerMap(42, 8, entries, modules ?? [new PointerModule("game.exe", 0x1000, 0x100)], false, 0,
			0);
	}
}
