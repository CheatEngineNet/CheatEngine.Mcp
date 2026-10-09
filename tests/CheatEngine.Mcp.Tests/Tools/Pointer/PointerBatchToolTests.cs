using System.Globalization;

using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Pointer;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Mcp.Tests.Tools.Pointer;

/// <summary><c>pointer_read_chains</c>: bounded independent supplied-chain validation.</summary>
public sealed class PointerBatchToolTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ReadChains_X64SignedOffsetsAndComparison_ReportsStableCandidateOutcomes()
	{
		await using PointerFixture fixture = new();
		PointerBatchTools batch = Tools(fixture);

		PointerChainBatchResult result = batch.ReadChains(
		[
			new PointerChainCandidate("negative", "game.exe+108", ["-30", "20"]),
			new PointerChainCandidate("miss", "game.exe+100", ["10", "0"])
		], "21020", McpValueType.Int32, cancellationToken: Token);

		Assert.Collection(result.Candidates,
			match =>
			{
				Assert.Equal("negative", match.Id);
				Assert.Equal(PointerChainResolutionStatus.Resolved, match.ChainStatus);
				Assert.Equal("21020", match.Address);
				Assert.Equal(PointerChainComparisonStatus.Match, match.ComparisonStatus);
				Assert.Equal(PointerChainValueStatus.Read, match.ValueStatus);
				Assert.Equal("1234", match.Value);
			},
			miss =>
			{
				Assert.Equal("miss", miss.Id);
				Assert.Equal(PointerChainResolutionStatus.Resolved, miss.ChainStatus);
				Assert.Equal(PointerChainComparisonStatus.Miss, miss.ComparisonStatus);
			});
		Assert.Equal(2, result.Summary.Processed);
		Assert.Equal(1, result.Summary.Matches);
		Assert.Equal(1, result.Summary.Misses);
		Assert.Equal(2, fixture.Dispatches);
	}

	[Fact]
	public async Task ReadChains_X86_UsesFourBytePointers()
	{
		await using PointerFixture fixture = new(pointerSize: PointerSize.Bit32);

		PointerChainBatchResult result = Tools(fixture).ReadChains(
			[new PointerChainCandidate("x86", "game.exe+100", ["10", "20"])], "21020", cancellationToken: Token);

		PointerChainCandidateResult candidate = Assert.Single(result.Candidates);
		Assert.Equal(PointerChainResolutionStatus.Resolved, candidate.ChainStatus);
		Assert.Equal("21020", candidate.Address);
		Assert.Equal(PointerChainComparisonStatus.Match, candidate.ComparisonStatus);

		PointerChainBatchResult maximumAddress = Tools(fixture).ReadChains(
			[new PointerChainCandidate("maximum", "game.exe+100", ["10", "20"])], "FFFFFFFF", cancellationToken: Token);
		Assert.Equal(PointerChainComparisonStatus.Miss, Assert.Single(maximumAddress.Candidates).ComparisonStatus);

		CheatEngineToolException tooWide = Assert.Throws<CheatEngineToolException>(() => Tools(fixture).ReadChains(
			[new PointerChainCandidate("wide", "game.exe+100", ["10", "20"])], "100000000", cancellationToken: Token));
		Assert.Equal(ToolErrorKind.InvalidArgument, tooWide.Error.Kind);

		PointerChainCandidateResult wideRoot = Assert.Single(Tools(fixture).ReadChains(
			[new PointerChainCandidate("wide-root", "100000000", ["0"])], cancellationToken: Token).Candidates);
		Assert.Equal(PointerChainResolutionStatus.Error, wideRoot.ChainStatus);
		Assert.Equal(ToolErrorKind.InvalidArgument, wideRoot.ChainError!.Kind);
	}

	[Fact]
	public async Task ReadChains_UnreadableHopAndValueReadFailure_AreIndependentStatuses()
	{
		await using PointerFixture fixture = new();
		fixture.UnreadableFrom = PointerFixture.Target;

		PointerChainBatchResult result = Tools(fixture).ReadChains(
		[
			new PointerChainCandidate("value", "game.exe+100", ["10", "20"]),
			new PointerChainCandidate("hop", "60000", ["0"])
		], "21020", McpValueType.Int32, cancellationToken: Token);

		PointerChainCandidateResult value = result.Candidates[0];
		Assert.Equal(PointerChainResolutionStatus.Resolved, value.ChainStatus);
		Assert.Equal("21020", value.Address);
		Assert.Equal(PointerChainComparisonStatus.Match, value.ComparisonStatus);
		Assert.Equal(PointerChainValueStatus.Failed, value.ValueStatus);
		Assert.NotNull(value.ValueError);

		PointerChainCandidateResult hop = result.Candidates[1];
		Assert.Equal(PointerChainResolutionStatus.Unreadable, hop.ChainStatus);
		Assert.Equal(0, hop.FailedHop);
		Assert.Equal("60000", hop.FailedReadAt);
		Assert.Equal(PointerChainComparisonStatus.NotResolved, hop.ComparisonStatus);
		Assert.Equal(PointerChainValueStatus.NotAttempted, hop.ValueStatus);
		Assert.Equal(1, result.Summary.Unreadable);
		Assert.Equal(1, result.Summary.ValueErrors);
	}

	[Fact]
	public async Task ReadChains_MatchesOnly_RetainsErrorSummary()
	{
		await using PointerFixture fixture = new();

		PointerChainBatchResult result = Tools(fixture).ReadChains(
		[
			new PointerChainCandidate("match", "game.exe+100", ["10", "20"]),
			new PointerChainCandidate("unreadable", "60000", ["0"]),
			new PointerChainCandidate("miss", "game.exe+100", ["10", "0"])
		], "21020", matchesOnly: true, cancellationToken: Token);

		PointerChainCandidateResult candidate = Assert.Single(result.Candidates);
		Assert.Equal("match", candidate.Id);
		Assert.Equal(3, result.Summary.Processed);
		Assert.Equal(1, result.Summary.Matches);
		Assert.Equal(1, result.Summary.Misses);
		Assert.Equal(1, result.Summary.Unreadable);
	}

	[Fact]
	public async Task ReadChains_Bounds_RefuseBeforeDispatch()
	{
		await using PointerFixture fixture = new();
		PointerChainCandidate[] tooMany = [.. Enumerable.Range(0, PointerBatchTools.MaximumCandidates + 1)
			.Select(index => new PointerChainCandidate(index.ToString(CultureInfo.InvariantCulture), "game.exe+100", ["0"]))];

		CheatEngineToolException candidates = Assert.Throws<CheatEngineToolException>(() =>
			Tools(fixture).ReadChains(tooMany, cancellationToken: Token));
		CheatEngineToolException offsets = Assert.Throws<CheatEngineToolException>(() => Tools(fixture).ReadChains(
			[new PointerChainCandidate("deep", "game.exe+100", [.. Enumerable.Repeat("0", 65)])], cancellationToken: Token));
		CheatEngineToolException values = Assert.Throws<CheatEngineToolException>(() => Tools(fixture).ReadChains(
		[
			new PointerChainCandidate("first", "game.exe+100", ["10"]),
			new PointerChainCandidate("second", "game.exe+100", ["10"])
		], valueType: McpValueType.Bytes, length: McpValueCodec.MaxLength, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, candidates.Error.Kind);
		Assert.Equal(ToolErrorKind.LimitExceeded, offsets.Error.Kind);
		Assert.Equal(ToolErrorKind.LimitExceeded, values.Error.Kind);
		Assert.Equal(0, fixture.Dispatches);
	}

	[Fact]
	public async Task ReadChains_TargetChangesMidChain_RefusesReplacementTargetResults()
	{
		await using PointerFixture fixture = new();
		int reads = 0;
		fixture.OnCall = call =>
		{
			if (call == "Memory.TryReadPrimitive" && Interlocked.Increment(ref reads) == 1)
			{
				fixture.ChangeTarget();
			}
		};
		PointerChainCandidate[] candidates =
		[
			new PointerChainCandidate("first", "game.exe+100", ["10", "20"]),
			new PointerChainCandidate("replacement-must-not-run", "game.exe+100", ["10", "20"])
		];

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
			Tools(fixture).ReadChains(candidates, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.TargetChanged, error.Error.Kind);
		Assert.Equal(1, reads);
		Assert.Equal(2, fixture.Dispatches);
	}

	[Fact]
	public async Task ReadChains_SlicesDeepCandidatesAtTheReadBudgetWithoutReordering()
	{
		await using PointerFixture fixture = new();
		fixture.PutPointer(PointerFixture.ObjectA, PointerFixture.ObjectA);
		List<int> readsByCompletedDispatch = [];
		int priorReads = 0;
		fixture.OnDispatch = () =>
		{
			if (fixture.Dispatches > 2)
			{
				readsByCompletedDispatch.Add(fixture.PrimitiveReads - priorReads);
			}

			priorReads = fixture.PrimitiveReads;
		};
		PointerChainCandidate[] candidates = [.. Enumerable.Range(0, PointerBatchTools.CandidatesPerDispatch)
			.Select(index => new PointerChainCandidate($"candidate-{index}", "game.exe+100",
				[.. Enumerable.Repeat("0", PointerChainTools.MaximumOffsets)]))];

		PointerChainBatchResult result = Tools(fixture).ReadChains(candidates, cancellationToken: Token);
		readsByCompletedDispatch.Add(fixture.PrimitiveReads - priorReads);

		Assert.Equal(PointerBatchTools.CandidatesPerDispatch, result.Summary.Processed);
		Assert.Equal(candidates.Select(static candidate => candidate.Id), result.Candidates.Select(static candidate => candidate.Id));
		Assert.All(readsByCompletedDispatch, reads => Assert.InRange(reads, 1, PointerBatchTools.MaximumReadsPerDispatch));
		Assert.Equal(PointerBatchTools.CandidatesPerDispatch * PointerChainTools.MaximumOffsets, fixture.PrimitiveReads);
	}

	[Fact]
	public async Task ReadChains_PrimitiveTargetChangedFailure_AbortsWithoutCandidateResults()
	{
		await using PointerFixture fixture = new();
		fixture.PrimitiveReadFailureKind = CheatEngineFailureKind.TargetChanged;

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() => Tools(fixture).ReadChains(
		[
			new PointerChainCandidate("first", "game.exe+100", ["10", "20"]),
			new PointerChainCandidate("second", "game.exe+100", ["10", "20"])
		], cancellationToken: Token));

		Assert.Equal(ToolErrorKind.TargetChanged, error.Error.Kind);
		Assert.Equal(1, fixture.PrimitiveReads);
	}

	[Fact]
	public async Task ReadChains_Cancellation_ReturnsHonestPartialProgress()
	{
		await using PointerFixture fixture = new();
		using CancellationTokenSource cancellation = new();
		int reads = 0;
		fixture.OnCall = call =>
		{
			if (call == "Memory.TryReadPrimitive" && Interlocked.Increment(ref reads) == 3)
			{
				cancellation.Cancel();
			}
		};

		PointerChainBatchResult result = Tools(fixture).ReadChains(
		[
			new PointerChainCandidate("first", "game.exe+100", ["10", "20"]),
			new PointerChainCandidate("second", "game.exe+100", ["10", "20"])
		], cancellationToken: cancellation.Token);

		Assert.Equal("first", Assert.Single(result.Candidates).Id);
		Assert.True(result.Summary.Cancelled);
		Assert.Equal(1, result.Summary.Processed);
		Assert.Equal(2, result.Summary.Submitted);
	}

	private static PointerBatchTools Tools(PointerFixture fixture)
	{
		return (PointerBatchTools) fixture.Targets.Get(typeof(PointerBatchTools));
	}
}
