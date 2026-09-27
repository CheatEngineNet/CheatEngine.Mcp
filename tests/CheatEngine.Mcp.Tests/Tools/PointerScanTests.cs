using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tests.Tools;

public sealed class PointerScanTests
{
	[Fact]
	public void PointerSearch_MultilevelGraph_ReturnsDereferenceOrderAndModuleRoot()
	{
		PointerMap map = Map([new PointerEntry(0x1000, 0x2000), new PointerEntry(0x2010, 0x3000)]);

		PointerSearchResult result = map.Search(0x3020, 5, 0x20, true, 100, 1000, CancellationToken.None);

		Assert.Single(result.Paths);
		Assert.Equal(0x1000UL, result.Paths[0].BaseAddress);
		Assert.Equal("game.exe", result.Paths[0].Module);
		Assert.Equal(new long[] { 0x10, 0x20 }, result.Paths[0].Offsets);
		Assert.True(map.TryResolve(result.Paths[0], out ulong destination));
		Assert.Equal(0x3020UL, destination);
		Assert.False(result.Truncated);
	}

	[Fact]
	public void PointerSearch_CyclesDepthAndOffsetLimits_DoNotProduceInvalidPaths()
	{
		PointerMap map = Map([
			new PointerEntry(0x1000, 0x2000), new PointerEntry(0x2000, 0x3000), new PointerEntry(0x3000, 0x2000)
		]);
		Assert.Empty(map.Search(0x3001, 1, 1, true, 10, 100, CancellationToken.None).Paths);
		Assert.Empty(map.Search(0x3001, 5, 0, true, 10, 100, CancellationToken.None).Paths);
		PointerSearchResult result = map.Search(0x3001, 8, 1, true, 10, 100, CancellationToken.None);
		Assert.Single(result.Paths);
		Assert.Equal(new long[] { 0, 1 }, result.Paths[0].Offsets);
		Assert.True(result.VisitedNodes < 10, "Cycle detection should stop repeated graph traversal.");
	}

	[Fact]
	public void PointerSearch_ResultAndTraversalCaps_ReportTruncation()
	{
		PointerMap map = Map([new PointerEntry(0x1000, 0x3000), new PointerEntry(0x1008, 0x3000)]);
		PointerSearchResult resultLimit = map.Search(0x3000, 1, 0, true, 1, 100, CancellationToken.None);
		Assert.Single(resultLimit.Paths);
		Assert.True(resultLimit.Truncated);
		PointerSearchResult nodeLimit = map.Search(0x3000, 1, 0, true, 100, 1, CancellationToken.None);
		Assert.Equal(1, nodeLimit.VisitedNodes);
		Assert.True(nodeLimit.Truncated);
		using CancellationTokenSource stopping = new();
		stopping.Cancel();
		Assert.Throws<OperationCanceledException>(() => map.Search(0x3000, 1, 0, true, 100, 100, stopping.Token));
	}

	[Theory]
	[InlineData(4, 0x89ABCDEFUL)]
	[InlineData(8, 0xFEDCBA9876543210UL)]
	public void PointerMapCopy_TargetWidthAndAbsoluteAlignment_ArePreserved(int width, ulong value)
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

		List<PointerEntry> entries = [];
		PointerMap.CopyEntries(entries, 0x1001, bytes, width, 4, 1);
		Assert.Single(entries);
		Assert.Equal(new PointerEntry(0x1004, value), entries[0]);
	}

	[Fact]
	public void PointerRescan_NewSnapshot_RebasesModulesAndRejectsMissingOrAmbiguousRoots()
	{
		PointerPath path = new(0x1008, "game.exe", 8, [0x10, 0x20]);
		PointerMap rebased = Map([new PointerEntry(0x5008, 0x6000), new PointerEntry(0x6010, 0x7000)],
			[new PointerModule("game.exe", 0x5000, 0x100)]);
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
	public void PointerTools_CaptureScanPageAndRescan_UseClientReadsAndKeepOnlyMatchingPaths()
	{
		Fixture fixture = new(0x4000);
		fixture.Put(0x1000, 0x2000);
		fixture.Put(0x1008, 0x2000);
		fixture.Put(0x2010, 0x3000);
		PointerScanTool tool = new(fixture.Client);
		ToolResultAssert.IsSuccess(tool.GeneratePointerMap("before"));
		ToolResultAssert.IsSuccess(tool.PointerScan("health", "before", "3020", maximumOffset: 0x20));
		object page = tool.GetPointerScanResults("health", maximumResults: 1);
		ToolResultAssert.HasPropertyValue(page, "count", 2);
		ToolResultAssert.HasPropertyValue(page, "hasMore", true);
		object[] results = ToolResultAssert.GetProperty<object[]>(page, "results");
		Assert.Equal(new long[] { 0x10, 0x20 }, ToolResultAssert.GetProperty<long[]>(results[0], "offsets"));
		Assert.Equal(new long[] { 0x20, 0x10 }, ToolResultAssert.GetProperty<long[]>(results[0], "addressListOffsets"));
		fixture.Put(0x1008, 0x2500);
		fixture.Put(0x2510, 0x3800);
		fixture.Put(0x2010, 0x3500);
		ToolResultAssert.IsSuccess(tool.GeneratePointerMap("after"));
		object rescan = tool.RescanPointerScan("health", "3520", "after");
		ToolResultAssert.HasPropertyValue(rescan, "count", 1);
		ToolResultAssert.HasPropertyValue(rescan, "removed", 1);
		ToolResultAssert.IsSuccess(tool.DeletePointerMap("before"));
		ToolResultAssert.HasPropertyValue(tool.GetPointerScanResults("health"), "count", 1);
		ToolResultAssert.IsSuccess(tool.ResetPointerScan("health"));
	}

	[Fact]
	public void PointerCapture_ChunkBoundaryAndByteBudget_DoNotLoseCrossingPointersOrClaimCompleteness()
	{
		Fixture fixture = new(70_000);
		fixture.Put(0x1000 + 65532, 0x8000);
		PointerScanTool tool = new(fixture.Client);
		ToolResultAssert.IsSuccess(tool.GeneratePointerMap("full"));
		ToolResultAssert.HasPropertyValue(tool.PointerScan("scan", "full", "8000", 1, 0, false), "count", 1);
		object capture = tool.GeneratePointerMap("limited", maximumBytes: 32);
		ToolResultAssert.HasPropertyValue(ToolResultAssert.GetProperty<object>(capture, "map"), "incomplete", true);
	}

	[Fact]
	public void PointerCapture_PartialRead_ReportsMissingBytesAndTargetChangeDiscardsMap()
	{
		Fixture fixture = new(64) { FailReads = true };
		PointerScanTool tool = new(fixture.Client);
		object capture = tool.GeneratePointerMap("partial");
		object map = ToolResultAssert.GetProperty<object>(capture, "map");
		ToolResultAssert.HasPropertyValue(map, "unreadableBytes", 32UL);
		ToolResultAssert.HasPropertyValue(map, "incomplete", true);
		fixture.ChangeTargetDuringRead = true;
		ToolResultAssert.IsFailure(tool.GeneratePointerMap("changed"),
			"Target selection changed during capture; the snapshot was discarded.");
		Assert.Single(ToolResultAssert.GetProperty<object[]>(tool.ListPointerMaps(), "maps"));
	}

	[Fact]
	public void PointerRescan_LiveTarget_UsesTypedChainApiAndRetainsOriginalOnFailure()
	{
		Fixture fixture = new(0x4000);
		fixture.Put(0x1000, 0x2000);
		PointerScanTool tool = new(fixture.Client);
		ToolResultAssert.IsSuccess(tool.GeneratePointerMap("initial"));
		ToolResultAssert.IsSuccess(tool.PointerScan("scan", "initial", "2000", 1, 0));
		fixture.LiveDestination = 0x3000;
		ToolResultAssert.HasPropertyValue(tool.RescanPointerScan("scan", "3000"), "count", 1);
		Assert.Equal(1, fixture.LiveReads);
		fixture.LiveFailure = new CheatEngineFailure(CheatEngineFailureKind.TargetChanged, "test", "Target changed");
		ToolResultAssert.IsFailure(tool.RescanPointerScan("scan", "4000"), "Target changed");
		ToolResultAssert.HasPropertyValue(tool.GetPointerScanResults("scan"), "count", 1);
	}

	[Fact]
	public void PointerRescan_UnreadableHop_PreservesCandidateForLaterRecovery()
	{
		Fixture fixture = new(0x4000);
		fixture.Put(0x1000, 0x2000);
		PointerScanTool tool = new(fixture.Client);
		ToolResultAssert.IsSuccess(tool.GeneratePointerMap("initial"));
		ToolResultAssert.IsSuccess(tool.PointerScan("scan", "initial", "2000", 1, 0));
		fixture.LiveFailure =
			new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "test", "temporarily unreadable");
		object uncertain = tool.RescanPointerScan("scan", "3000");
		ToolResultAssert.HasPropertyValue(uncertain, "count", 1);
		ToolResultAssert.HasPropertyValue(uncertain, "unresolved", 1);
		ToolResultAssert.HasPropertyValue(uncertain, "verifiedMatches", 0);
		ToolResultAssert.HasPropertyValue(uncertain, "incomplete", true);
		object[] paths = ToolResultAssert.GetProperty<object[]>(tool.GetPointerScanResults("scan"), "results");
		ToolResultAssert.HasPropertyValue(paths[0], "verification", "unresolved");
		fixture.LiveFailure = null;
		fixture.LiveDestination = 0x4000;
		ToolResultAssert.HasPropertyValue(tool.RescanPointerScan("scan", "3000"), "count", 0);
	}

	[Fact]
	public void PointerTools_InvalidBounds_RefuseBeforeHostRead()
	{
		PointerScanTool tool = new(ClientTestDouble.Client());
		ToolResultAssert.IsFailure(tool.GeneratePointerMap("map", maximumBytes: 0),
			"Invalid byte, pointer, or alignment limit.");
		ToolResultAssert.IsFailure(tool.PointerScan("scan", "none", "0", 9),
			"Invalid depth, offset, result, or traversal limit.");
		ToolResultAssert.IsFailure(tool.GetPointerScanResults("none", -1),
			"startIndex must be nonnegative and maximumResults must be 1-1024.");
	}

	private static PointerMap Map(PointerEntry[] entries, PointerModule[]? modules = null)
	{
		return new PointerMap(default, 8, entries,
			modules ?? [new PointerModule("game.exe", 0x1000, 0x100)], false, 0, 0);
	}

	private sealed class Fixture
	{
		private readonly byte[] _bytes;
		private int _epoch = 1;

		internal Fixture(int length)
		{
			_bytes = new byte[length];
			IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, _) =>
				method.Name == "GetCurrentProcess"
					? new ProcessSnapshot(new TargetProcessId(42), "test", null, TargetBackend.LocalProcess,
						CheatEngineArchitecture.X64, PointerSize.Bit64, 8, null, _epoch)
					: throw new NotSupportedException(method.Name));
			IInspectionClient inspection = ClientTestDouble.Create<IInspectionClient>((method, arguments) =>
				method.Name switch
				{
					"GetMemoryRegions" => ImmutableArray.Create(new MemoryRegionInfo(new Address(0x1000),
						new Address(0x1000), MemoryProtection.ReadWrite,
						new MemorySize((ulong) _bytes.Length), MemoryRegionState.Committed, MemoryProtection.ReadWrite,
						default, null)),
					"GetModules" => ImmutableArray.Create(new ModuleInfo("game.exe", new Address(0x1000),
						new MemorySize(0x100), true, "game.exe")),
					"ResolveAddress" => new Address(ulong.Parse(((SymbolExpression) arguments![0]!).Value,
						NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
					_ => throw new NotSupportedException(method.Name)
				});
			IMemoryClient memory = ClientTestDouble.Create<IMemoryClient>((method, arguments) =>
			{
				if (method.Name == "ReadBytesDetailed")
				{
					MemoryBytesReadRequest request = (MemoryBytesReadRequest) arguments![0]!;
					int count = FailReads ? request.Length / 2 : request.Length;
					if (ChangeTargetDuringRead)
					{
						_epoch++;
					}

					return new MemoryBytesReadOutcome(request.Length,
						_bytes.AsSpan((int) (request.Address.Value - 0x1000), count).ToArray().ToImmutableArray(),
						FailReads
							? new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "test", "unreadable")
							: null);
				}

				if (method.Name == "TryResolvePointerChain")
				{
					LiveReads++;
					arguments![1] = new Address(LiveDestination);
					arguments[2] = LiveFailure ?? default;
					return LiveFailure is null;
				}

				throw new NotSupportedException(method.Name);
			});
			Client = ClientTestDouble.Client((nameof(ICheatEngineClient.Processes), processes),
				(nameof(ICheatEngineClient.Inspection), inspection), (nameof(ICheatEngineClient.Memory), memory));
		}

		internal bool FailReads
		{
			get;
			set;
		}

		internal bool ChangeTargetDuringRead
		{
			get;
			set;
		}

		internal ulong LiveDestination
		{
			get;
			set;
		}

		internal int LiveReads
		{
			get;
			private set;
		}

		internal CheatEngineFailure? LiveFailure
		{
			get;
			set;
		}

		internal ICheatEngineClient Client
		{
			get;
		}

		internal void Put(int address, ulong value)
		{
			BinaryPrimitives.WriteUInt64LittleEndian(_bytes.AsSpan(address - 0x1000), value);
		}
	}
}
