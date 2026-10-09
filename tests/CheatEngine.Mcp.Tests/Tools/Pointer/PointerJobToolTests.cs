using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Pointer;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Tools.Pointer;

/// <summary>
///     The pointer map capture and path search jobs: their results, limits, stops, target changes and the store that
///     outlives them.
/// </summary>
public sealed class PointerJobToolTests
{
	private const ulong LargeSize = 8 * 1024 * 1024;
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task CreateMapThenFindPaths_Jobs_StoreRankedModuleRootedPaths()
	{
		await using PointerFixture fixture = new();

		PointerMapInfo started = fixture.Maps.CreateMap("run1", cancellationToken: Token);
		PointerMapInfo map = fixture.WaitForMap("run1");

		Assert.StartsWith($"pointermap-{fixture.Jobs.Namespace}-", started.JobId, StringComparison.Ordinal);
		Assert.Equal(started.JobId, map.JobId);
		Assert.Equal(started.JobId, JsonSerializer.SerializeToElement(map,
			PointerJsonContext.Default.PointerMapInfo).GetProperty("jobId").GetString());
		Assert.Equal((PointerJobState.Ready, PointerFixture.ProcessId, 8, 3, false, 100),
			(map.State, map.ProcessId, map.PointerSize, map.Pointers, map.Incomplete, map.ProgressPercent));
		Assert.Equal((0x11000L, 0L), (map.BytesRead, map.UnreadableBytes));
		JobStatus capture = Assert.Single(fixture.Jobs.List(Token));
		Assert.Equal((JobState.Completed, 0x11000L, 0x11000L),
			(capture.State, capture.ProgressDone!.Value, capture.ProgressTotal!.Value));

		PointerScanInfo scan = fixture.Scans.FindPaths("hp", "run1", "21020", maxOffset: 0x40,
			allowNegativeOffsets: true, cancellationToken: Token);
		PointerScanInfo searched = fixture.WaitForScan("hp");

		Assert.StartsWith($"pointerscan-{fixture.Jobs.Namespace}-", scan.JobId, StringComparison.Ordinal);
		Assert.Equal(scan.JobId, JsonSerializer.SerializeToElement(searched,
			PointerJsonContext.Default.PointerScanInfo).GetProperty("jobId").GetString());
		Assert.Equal((PointerJobState.Ready, 2, false, false, "21020"),
			(searched.State, searched.Count, searched.Incomplete, searched.TraversalLimited, searched.Target));
		PointerPathPage page = fixture.Scans.ListPaths("hp", sortBy: PointerPathSort.OffsetSum);
		Assert.Equal((2, (int?) null, false), (page.Total, page.NextOffset, page.Incomplete));
		PointerPathItem best = page.Paths[0];
		Assert.Equal("[[\"game.exe\"+100]+10]+20", best.Expression);
		Assert.Equal(("game.exe", "100", "10100"), (best.Module, best.ModuleOffset, best.Base));
		Assert.Equal(["10", "20"], best.Offsets);
		Assert.Equal(PointerVerification.SnapshotMatch, best.Verification);
		Assert.Equal(["-30", "20"], page.Paths[1].Offsets);
	}

	[Fact]
	public async Task FindPaths_WithoutNegativeOffsets_KeepsOnlyForwardPaths()
	{
		await using PointerFixture fixture = new();
		fixture.Maps.CreateMap("run1", cancellationToken: Token);
		fixture.WaitForMap("run1");

		fixture.Scans.FindPaths("hp", "run1", "game.exe+0", maxOffset: 0x40, cancellationToken: Token);
		PointerScanInfo none = fixture.WaitForScan("hp");
		fixture.Scans.FindPaths("hp2", "run1", "21020", maxOffset: 0x40, cancellationToken: Token);

		Assert.Equal("10000", none.Target);
		Assert.Equal(1, fixture.WaitForScan("hp2").Count);
		PointerPathPage page = fixture.Scans.ListPaths("hp2", moduleContains: "GAME");
		Assert.Equal(["10", "20"], Assert.Single(page.Paths).Offsets);
		Assert.Equal(0, fixture.Scans.ListPaths("hp2", moduleContains: "kernel32").Total);
	}

	[Fact]
	public async Task RescanPaths_NewMapAndLiveMemory_KeepOnlyPathsThatStillReachTheTarget()
	{
		await using PointerFixture fixture = new();
		fixture.Maps.CreateMap("before", cancellationToken: Token);
		fixture.WaitForMap("before");
		fixture.Scans.FindPaths("hp", "before", "21020", maxOffset: 0x40, allowNegativeOffsets: true,
			cancellationToken: Token);
		fixture.WaitForScan("hp");
		// The object moves: only the path through game.exe+100 follows it.
		fixture.PutPointer(PointerFixture.ModuleBase + 0x100, 0x22000);
		fixture.PutPointer(0x22010, 0x23000);
		fixture.Maps.CreateMap("after", cancellationToken: Token);
		fixture.WaitForMap("after");

		PointerRescanResult offline = fixture.Scans.RescanPaths("hp", "23020", "after", cancellationToken: Token);
		PointerRescanResult live = fixture.Scans.RescanPaths("hp", "23020", cancellationToken: Token);

		Assert.Equal(new PointerRescanResult("hp", 1, 1, 1, 0, false), offline);
		Assert.Equal(new PointerRescanResult("hp", 1, 1, 0, 0, false), live);
		Assert.Contains("Memory.TryResolvePointerChain", fixture.Calls);
		Assert.Equal(PointerVerification.LiveMatch, Assert.Single(fixture.Scans.ListPaths("hp").Paths).Verification);
	}

	[Fact]
	public async Task RescanPaths_UnreadableHop_KeepsThePathUnresolvedUnlessDropped()
	{
		await using PointerFixture fixture = new();
		fixture.Maps.CreateMap("map", cancellationToken: Token);
		fixture.WaitForMap("map");
		fixture.Scans.FindPaths("hp", "map", "21020", maxOffset: 0x40, cancellationToken: Token);
		fixture.WaitForScan("hp");
		fixture.PutPointer(PointerFixture.ModuleBase + 0x100, 0x900000);

		PointerRescanResult kept = fixture.Scans.RescanPaths("hp", "21020", cancellationToken: Token);
		PointerPathItem unresolved = Assert.Single(fixture.Scans.ListPaths("hp").Paths);
		PointerRescanResult dropped = fixture.Scans.RescanPaths("hp", "21020", dropUnresolved: true,
			cancellationToken: Token);

		Assert.Equal(new PointerRescanResult("hp", 1, 0, 0, 1, true), kept);
		Assert.Equal(PointerVerification.Unresolved, unresolved.Verification);
		Assert.Equal(new PointerRescanResult("hp", 0, 0, 1, 1, true), dropped);
		Assert.True(fixture.Scans.ListScans().Scans.Single().Incomplete);
	}

	[Fact]
	public async Task RescanPaths_TargetChangesMidRescan_IsTargetChangedAndKeepsTheOriginalPaths()
	{
		await using PointerFixture fixture = new();
		fixture.Maps.CreateMap("map", cancellationToken: Token);
		fixture.WaitForMap("map");
		fixture.Scans.FindPaths("hp", "map", "21020", maxOffset: 0x40, cancellationToken: Token);
		fixture.WaitForScan("hp");
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();
		fixture.OnCall = call =>
		{
			if (call == "Inspection.GetModules")
			{
				fixture.ChangeTarget();
			}
		};

		ToolError error = TestMcpPipeline.AssertError(await pipeline.CallAsync(
				CheatEngineToolNames.PointerRescanPaths, """{"scanName":"hp","target":"23020"}"""),
			ToolErrorKind.TargetChanged);

		Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
		Assert.Equal(PointerVerification.SnapshotMatch,
			Assert.Single(fixture.Scans.ListPaths("hp").Paths).Verification);
		Assert.DoesNotContain("Memory.TryResolvePointerChain", fixture.Calls);
	}

	[Fact]
	public async Task CreateMap_PartialRead_ReportsUnreadableBytesAndAnIncompleteMap()
	{
		await using PointerFixture fixture = new()
		{
			UnreadableFrom = PointerFixture.HeapBase + 0x8000
		};

		fixture.Maps.CreateMap("map", cancellationToken: Token);
		PointerMapInfo map = fixture.WaitForMap("map");

		Assert.Equal((PointerJobState.Ready, 3, true, 0x8000L), (map.State, map.Pointers, map.Incomplete,
			map.UnreadableBytes));
	}

	[Fact]
	public async Task CreateMap_ChunkOverlap_UsesTheReadBudgetForProgressAndKeepsOneBoundaryPointer()
	{
		const int PointerWidth = 8;
		int maximumBytes = 0x11000 + PointerCaptureJob.MaximumReadLength + PointerWidth;
		ulong boundary = PointerFixture.LargeBase +
						 (PointerCaptureJob.MaximumReadLength - PointerWidth + 1);
		await using PointerFixture fixture = new(largeRegionSize: (ulong) PointerCaptureJob.MaximumReadLength + 1);
		fixture.PutPointer(boundary, PointerFixture.ObjectA);

		fixture.Maps.CreateMap("map", alignment: 1, maxBytes: maximumBytes, cancellationToken: Token);
		PointerMapInfo map = fixture.WaitForMap("map");
		JobStatus capture = Assert.Single(fixture.Jobs.List(Token));
		PointerEntry boundaryEntry = Assert.Single(fixture.Store.GetMap("map").GetUsableMap().Entries,
			entry => entry.Address == boundary);

		Assert.Equal((PointerJobState.Ready, 4, false, (long) maximumBytes, 100),
			(map.State, map.Pointers, map.Incomplete, map.BytesRead, map.ProgressPercent));
		Assert.Equal((JobState.Completed, (long) maximumBytes, (long) maximumBytes),
			(capture.State, capture.ProgressDone!.Value, capture.ProgressTotal!.Value));
		Assert.Equal(new PointerEntry(boundary, PointerFixture.ObjectA), boundaryEntry);
	}

	[Fact]
	public async Task CreateMap_ModuleCollectionAtLimit_MarksTheMapIncomplete()
	{
		await using PointerFixture fixture = new();
		fixture.ModuleCount = PointerSupport.ModuleLimit;

		fixture.Maps.CreateMap("map", cancellationToken: Token);
		PointerMapInfo map = fixture.WaitForMap("map");

		Assert.Equal(PointerJobState.Ready, map.State);
		Assert.True(map.Incomplete);
	}

	[Fact]
	public async Task CreateMap_StopMidCapture_KeepsWhatWasReadAndARepeatedStopIsAlreadyReleased()
	{
		await using PointerFixture fixture = new(largeRegionSize: LargeSize);
		using Gate gate = new(fixture, PointerFixture.LargeBase + 0x20000);
		PointerMapInfo started = fixture.Maps.CreateMap("map", maxBytes: 16 * 1024 * 1024, cancellationToken: Token);
		gate.WaitUntilBlocked();
		McpJob job = fixture.Store.GetMap("map").Job!;
		Assert.NotNull(started.JobId);

		Task<JobStopResult> stopping = Task.Run(() => fixture.Jobs.Stop(started.JobId, Token), Token);
		Assert.True(SpinWait.SpinUntil(() => job.State is JobState.Stopping, Wait));
		gate.Open();
		JobStopResult stopped = await stopping.WaitAsync(Wait, Token);

		Assert.Equal((true, false), (stopped.Released, stopped.AlreadyReleased));
		PointerMapInfo map = fixture.WaitForMap("map");
		Assert.Equal((PointerJobState.Stopped, 3, true), (map.State, map.Pointers, map.Incomplete));
		Assert.Contains("stopped", map.Error, StringComparison.Ordinal);
		Assert.Equal(JobState.Stopped, job.State);
		Assert.True(fixture.Jobs.Stop(started.JobId, Token).AlreadyReleased);
		Assert.Equal(ToolErrorKind.NotFound, Assert.Throws<CheatEngineToolException>(() =>
			fixture.Jobs.Stop($"pointermap-{fixture.Jobs.Namespace}-99", Token)).Error.Kind);
		// A stopped map stays usable with what it read.
		fixture.Scans.FindPaths("hp", "map", "21020", maxOffset: 0x40, cancellationToken: Token);
		PointerScanInfo scan = fixture.WaitForScan("hp");
		Assert.Equal((1, true), (scan.Count, scan.Incomplete));
	}

	[Fact]
	public async Task CreateMap_TargetChangesMidCapture_DiscardsTheMap()
	{
		await using PointerFixture fixture = new(largeRegionSize: LargeSize);
		fixture.BeforeRead = address =>
		{
			if (address == PointerFixture.ModuleBase)
			{
				fixture.ChangeTarget();
			}
		};

		fixture.Maps.CreateMap("map", maxBytes: 16 * 1024 * 1024, cancellationToken: Token);
		PointerMapInfo map = fixture.WaitForMap("map");

		Assert.Equal((PointerJobState.TargetChanged, 0, true), (map.State, map.Pointers, map.Incomplete));
		Assert.Contains("process changed", map.Error, StringComparison.Ordinal);
		Assert.Equal(JobState.TargetChanged, fixture.Store.GetMap("map").Job!.State);
		CheatEngineToolException unusable = Assert.Throws<CheatEngineToolException>(() =>
			fixture.Scans.FindPaths("hp", "map", "21020", cancellationToken: Token));
		Assert.Equal(ToolErrorKind.InvalidState, unusable.Error.Kind);
	}

	[Fact]
	public async Task CreateMap_ActivationStops_CancelsTheCapture()
	{
		await using PointerFixture fixture = new(largeRegionSize: LargeSize);
		using Gate gate = new(fixture, PointerFixture.LargeBase + 0x20000);
		fixture.Maps.CreateMap("map", maxBytes: 16 * 1024 * 1024, cancellationToken: Token);
		gate.WaitUntilBlocked();

		await fixture.Stopping.CancelAsync();
		gate.Open();

		PointerMapInfo map = fixture.WaitForMap("map");
		Assert.Equal((PointerJobState.Cancelled, 0), (map.State, map.Pointers));
		Assert.Equal(PointerJobs.ActivationEnded, map.Error);
	}

	[Fact]
	public async Task DeleteMap_RunningCapture_StopsItsJobAndForgetsTheMap()
	{
		await using PointerFixture fixture = new(largeRegionSize: LargeSize);
		using Gate gate = new(fixture, PointerFixture.LargeBase + 0x20000);
		PointerMapInfo started = fixture.Maps.CreateMap("map", maxBytes: 16 * 1024 * 1024, cancellationToken: Token);
		Assert.NotNull(started.JobId);
		gate.WaitUntilBlocked();

		Task<PointerDeleteResult> deleting = Task.Run(() => fixture.Maps.DeleteMap("map", Token), Token);
		Assert.True(SpinWait.SpinUntil(() => fixture.Store.GetMap("map").Job!.State is JobState.Stopping, Wait));
		gate.Open();
		PointerDeleteResult deleted = await deleting.WaitAsync(Wait, Token);

		Assert.Equal(new PointerDeleteResult("map", true), deleted);
		Assert.Empty(fixture.Maps.ListMaps().Maps);
		Assert.Equal(ToolErrorKind.NotFound, Assert.Throws<CheatEngineToolException>(() =>
			fixture.Jobs.Get(started.JobId)).Error.Kind);
		Assert.Equal(ToolErrorKind.NotFound,
			Assert.Throws<CheatEngineToolException>(() => fixture.Maps.DeleteMap("map", Token)).Error.Kind);
	}

	[Fact]
	public async Task DeleteScanAndMap_EndedJobs_AreNotCancelledAndFreeTheStore()
	{
		await using PointerFixture fixture = new();
		fixture.Maps.CreateMap("map", cancellationToken: Token);
		fixture.WaitForMap("map");
		fixture.Scans.FindPaths("hp", "map", "21020", maxOffset: 0x40, cancellationToken: Token);
		PointerScanInfo scan = fixture.WaitForScan("hp");
		Assert.NotNull(scan.JobId);

		PointerDeleteResult deletedMap = fixture.Maps.DeleteMap("map", Token);

		// Scans keep their paths after their map is gone.
		Assert.Equal(new PointerDeleteResult("map", false), deletedMap);
		Assert.Equal(1, fixture.Scans.ListPaths("hp").Total);
		Assert.Equal(new PointerDeleteResult("hp", false), fixture.Scans.DeleteScan("hp", Token));
		Assert.Empty(fixture.Scans.ListScans().Scans);
		Assert.Equal(0, fixture.Jobs.Count);
		Assert.Equal(ToolErrorKind.NotFound, Assert.Throws<CheatEngineToolException>(() =>
			fixture.Jobs.Get(scan.JobId)).Error.Kind);
	}

	[Theory]
	[InlineData("""{"mapName":""}""", ToolErrorKind.InvalidArgument, "mapName")]
	[InlineData("""{"mapName":"m","alignment":3}""", ToolErrorKind.InvalidArgument, "alignment")]
	[InlineData("""{"mapName":"m","maxBytes":0}""", ToolErrorKind.InvalidArgument, "maxBytes")]
	[InlineData("""{"mapName":"m","maxBytes":536870913}""", ToolErrorKind.LimitExceeded, "maxBytes")]
	[InlineData("""{"mapName":"m","maxPointers":4194305}""", ToolErrorKind.LimitExceeded, "maxPointers")]
	[InlineData("""{"mapName":"m","startAddress":"1000"}""", ToolErrorKind.InvalidArgument, "endAddress")]
	[InlineData("""{"mapName":"m","lifetimeSeconds":301}""", ToolErrorKind.InvalidArgument, "lifetimeSeconds")]
	public async Task CreateMap_InvalidArguments_RefuseBeforeAnyDispatch(string arguments, ToolErrorKind kind,
		string parameter)
	{
		await using PointerFixture fixture = new();
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();

		ToolError error = TestMcpPipeline.AssertError(
			await pipeline.CallAsync(CheatEngineToolNames.PointerCreateMap, arguments), kind);

		Assert.Equal((ToolHostEffect.NotStarted, parameter),
			(error.HostEffect, error.Details!.Value.GetProperty("parameter").GetString()));
		Assert.Equal(0, fixture.Dispatches);
		Assert.Empty(fixture.Maps.ListMaps().Maps);
		Assert.Equal(0, fixture.Jobs.Count);
	}

	[Theory]
	[InlineData("""{"scanName":"s","mapName":"map","target":"21020","maxDepth":9}""", ToolErrorKind.LimitExceeded,
		"maxDepth")]
	[InlineData("""{"scanName":"s","mapName":"map","target":"21020","maxNodes":0}""", ToolErrorKind.InvalidArgument,
		"maxNodes")]
	[InlineData("""{"scanName":"s","mapName":"map","target":"21020","maxResults":100001}""",
		ToolErrorKind.LimitExceeded, "maxResults")]
	[InlineData("""{"scanName":"s","mapName":"map","target":"21020","maxOffset":1048577}""",
		ToolErrorKind.LimitExceeded, "maxOffset")]
	public async Task FindPaths_InvalidArguments_RefuseBeforeAnyDispatch(string arguments, ToolErrorKind kind,
		string parameter)
	{
		await using PointerFixture fixture = new();
		fixture.Maps.CreateMap("map", cancellationToken: Token);
		fixture.WaitForMap("map");
		int dispatches = fixture.Dispatches;
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();

		ToolError error = TestMcpPipeline.AssertError(
			await pipeline.CallAsync(CheatEngineToolNames.PointerFindPaths, arguments), kind);

		Assert.Equal(parameter, error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(dispatches, fixture.Dispatches);
		Assert.Empty(fixture.Scans.ListScans().Scans);
	}

	[Fact]
	public async Task CreateMap_StoreLimits_RefuseTakenNamesAFifthMapAndThePointerBudget()
	{
		await using PointerFixture fixture = new(largeRegionSize: LargeSize);
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();
		using Gate gate = new(fixture, PointerFixture.LargeBase + 0x20000);
		fixture.Maps.CreateMap("big", maxPointers: PointerMapTools.MaximumPointers, cancellationToken: Token);
		gate.WaitUntilBlocked();
		fixture.Maps.CreateMap("big2", maxPointers: PointerMapTools.MaximumPointers, startAddress: "10000",
			endAddress: "10FFF", cancellationToken: Token);
		fixture.WaitForMap("big2");
		int dispatches = fixture.Dispatches;

		// big2 kept only its 2 pointers of its reservation, so 4194302 pointers remain beside the running big.
		ToolError budget = TestMcpPipeline.AssertError(await pipeline.CallAsync(CheatEngineToolNames.PointerCreateMap,
			"""{"mapName":"big3","maxPointers":4194303}"""), ToolErrorKind.LimitExceeded);
		ToolError taken = TestMcpPipeline.AssertError(await pipeline.CallAsync(CheatEngineToolNames.PointerCreateMap,
			"""{"mapName":"big2"}"""), ToolErrorKind.InvalidState);

		Assert.Equal("maxPointers", budget.Details!.Value.GetProperty("parameter").GetString());
		Assert.Contains("already exists", taken.Message, StringComparison.Ordinal);
		Assert.Equal(dispatches, fixture.Dispatches);
		fixture.Maps.CreateMap("m3", maxPointers: 1, startAddress: "10000", endAddress: "10FFF",
			cancellationToken: Token);
		fixture.Maps.CreateMap("m4", maxPointers: 1, startAddress: "10000", endAddress: "10FFF",
			cancellationToken: Token);
		ToolError full = TestMcpPipeline.AssertError(await pipeline.CallAsync(CheatEngineToolNames.PointerCreateMap,
			"""{"mapName":"m5","maxPointers":1}"""), ToolErrorKind.LimitExceeded);
		Assert.Equal("mapName", full.Details!.Value.GetProperty("parameter").GetString());
		gate.Open();
	}

	[Fact]
	public async Task CreateMap_MaxJobsRetained_IsBusyBeforeAnyDispatchUntilTheTtlEnds()
	{
		await using PointerFixture fixture = new(new McpExecutionOptions { MaxJobs = 1 });
		fixture.Maps.CreateMap("a", cancellationToken: Token);
		fixture.WaitForMap("a");
		int dispatches = fixture.Dispatches;

		CheatEngineToolException busy = Assert.Throws<CheatEngineToolException>(() =>
			fixture.Maps.CreateMap("b", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.Busy, ToolHostEffect.NotStarted, true),
			(busy.Error.Kind, busy.Error.HostEffect, busy.Error.Retryable));
		Assert.Equal(dispatches, fixture.Dispatches);
		Assert.Single(fixture.Maps.ListMaps().Maps);
		fixture.Clock.Advance(TimeSpan.FromSeconds(121));
		fixture.Maps.CreateMap("b", cancellationToken: Token);
		Assert.Equal(PointerJobState.Ready, fixture.WaitForMap("b").State);
		Assert.Equal(PointerJobState.Ready, fixture.WaitForMap("a").State);
	}

	[Fact]
	public async Task FindPaths_RunningOrUnknownMap_IsRefusedBeforeAnyDispatch()
	{
		await using PointerFixture fixture = new(largeRegionSize: LargeSize);
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();
		using Gate gate = new(fixture, PointerFixture.LargeBase + 0x20000);
		fixture.Maps.CreateMap("map", maxBytes: 16 * 1024 * 1024, cancellationToken: Token);
		gate.WaitUntilBlocked();
		int dispatches = fixture.Dispatches;

		ToolError running = TestMcpPipeline.AssertError(await pipeline.CallAsync(CheatEngineToolNames.PointerFindPaths,
			"""{"scanName":"s","mapName":"map","target":"21020"}"""), ToolErrorKind.InvalidState);
		ToolError unknown = TestMcpPipeline.AssertError(await pipeline.CallAsync(CheatEngineToolNames.PointerFindPaths,
			"""{"scanName":"s","mapName":"none","target":"21020"}"""), ToolErrorKind.NotFound);
		ToolError deleteUnknown = TestMcpPipeline.AssertError(await pipeline.CallAsync(
			CheatEngineToolNames.PointerDeleteScan, """{"scanName":"none"}"""), ToolErrorKind.NotFound);

		Assert.Contains("still being captured", running.Message, StringComparison.Ordinal);
		Assert.Equal((ToolHostEffect.NotStarted, ToolHostEffect.NotStarted),
			(unknown.HostEffect, deleteUnknown.HostEffect));
		Assert.Equal(dispatches, fixture.Dispatches);
		gate.Open();
		Assert.Equal(PointerJobState.Ready, fixture.WaitForMap("map").State);
	}

	[Fact]
	public async Task ListTools_ThroughTheServer_ReturnStructuredMapsAndScans()
	{
		await using PointerFixture fixture = new();
		fixture.Maps.CreateMap("map", cancellationToken: Token);
		fixture.WaitForMap("map");
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();

		CallToolResult maps = await pipeline.CallAsync(CheatEngineToolNames.PointerListMaps);
		CallToolResult paths = await pipeline.CallAsync(CheatEngineToolNames.PointerListPaths,
			"""{"scanName":"none","sortBy":"offset_sum"}""");

		Assert.NotEqual(true, maps.IsError);
		JsonElement map = Assert.IsType<JsonElement>(maps.StructuredContent)
			.GetProperty("maps")[0];
		Assert.Equal(("map", "ready"), (map.GetProperty("mapName").GetString(), map.GetProperty("state").GetString()));
		Assert.False(map.TryGetProperty("error", out _));
		TestMcpPipeline.AssertError(paths, ToolErrorKind.NotFound);
	}

	/// <summary>Blocks the capture's first read at or beyond one address until the test opens it.</summary>
	private sealed class Gate : IDisposable
	{
		private readonly TaskCompletionSource _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

		internal Gate(PointerFixture fixture, ulong address)
		{
			fixture.BeforeRead = read =>
			{
				if (read >= address && _blocked.TrySetResult())
				{
					_open.Task.Wait(Wait);
				}
			};
		}

		public void Dispose()
		{
			_open.TrySetResult();
		}

		internal void WaitUntilBlocked()
		{
			Assert.True(_blocked.Task.Wait(Wait), "The capture never reached the gate.");
		}

		internal void Open()
		{
			_open.TrySetResult();
		}
	}
}
