using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Pointer;
using CheatEngine.SDK.Engine.Runtime;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Tools.Pointer;

/// <summary>Opt-in dereference evidence retains the compact batch contract and read budget.</summary>
public sealed class PointerBatchHopTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(4)]
	[InlineData(8)]
	public async Task ReadChains_IncludeHops_ReportsOrderedSignedDereferencesWithoutExtraReads(int pointerBytes)
	{
		await using PointerFixture fixture = new(pointerSize: pointerBytes == 4 ? PointerSize.Bit32 : PointerSize.Bit64);
		PointerBatchTools tools = Tools(fixture);
		PointerChainCandidate[] candidates = [new("negative", "game.exe+108", ["-30", "20"])];

		PointerChainBatchResult compact = tools.ReadChains(candidates, "21020", McpValueType.Int32,
			cancellationToken: Token);
		int firstReads = fixture.PrimitiveReads;
		PointerChainBatchResult detailed = tools.ReadChains(candidates, "21020", McpValueType.Int32,
			includeHops: true, cancellationToken: Token);

		PointerChainCandidateResult result = Assert.Single(detailed.Candidates);
		Assert.Equal(compact.Summary, detailed.Summary);
		Assert.Equal(Assert.Single(compact.Candidates), result with
		{
			Hops = null
		});
		Assert.Equal("1234", result.Value);
		Assert.Equal(
		[
			new("10108", "20040", "-30", "20010"),
			new("20010", "21000", "20", "21020")
		], result.Hops);
		Assert.Equal(firstReads, fixture.PrimitiveReads - firstReads);
	}

	[Fact]
	public async Task ReadChains_UnreadableHop_RetainsOnlyCompletedPrefixAndIndependentCandidates()
	{
		await using PointerFixture fixture = new();
		fixture.PutPointer(PointerFixture.ModuleBase + 0x110, 0x60000);

		PointerChainBatchResult result = Tools(fixture).ReadChains(
		[
			new("partial", "game.exe+110", ["0", "8"]),
			new("complete", "game.exe+100", ["10", "20"])
		], includeHops: true, cancellationToken: Token);

		PointerChainCandidateResult partial = result.Candidates[0];
		Assert.Equal(PointerChainResolutionStatus.Unreadable, partial.ChainStatus);
		Assert.Equal(1, partial.FailedHop);
		Assert.Equal("60000", partial.FailedReadAt);
		Assert.Equal(new PointerHop("10110", "60000", "0", "60000"), Assert.Single(partial.Hops!));
		Assert.Equal("21020", result.Candidates[1].Address);
		Assert.Equal(2, result.Candidates[1].Hops!.Count);
		Assert.Equal(4, fixture.PrimitiveReads);
	}

	[Fact]
	public async Task ReadChains_OverflowAndUnresolvedBase_DoNotInventSuccessfulHops()
	{
		await using PointerFixture fixture = new();
		fixture.PutPointer(PointerFixture.ObjectA + 0x10, ulong.MaxValue);

		PointerChainBatchResult result = Tools(fixture).ReadChains(
		[
			new("overflow", "game.exe+100", ["10", "1"]),
			new("unknown", "missing_symbol", ["0"]),
			new("unreadable", "60000", ["0"])
		], includeHops: true, cancellationToken: Token);

		Assert.Equal(PointerChainResolutionStatus.Error, result.Candidates[0].ChainStatus);
		Assert.Equal(ToolErrorKind.InvalidArgument, result.Candidates[0].ChainError!.Kind);
		Assert.Equal(new PointerHop("10100", "20000", "10", "20010"), Assert.Single(result.Candidates[0].Hops!));
		Assert.Equal(PointerChainResolutionStatus.Error, result.Candidates[1].ChainStatus);
		Assert.Empty(result.Candidates[1].Hops!);
		Assert.Equal(PointerChainResolutionStatus.Unreadable, result.Candidates[2].ChainStatus);
		Assert.Empty(result.Candidates[2].Hops!);
	}

	[Fact]
	public async Task ReadChains_ValueFailureAndMatchesOnly_PreserveTraceAndSummary()
	{
		await using PointerFixture fixture = new();
		fixture.UnreadableFrom = PointerFixture.Target;

		PointerChainBatchResult result = Tools(fixture).ReadChains(
		[
			new("match", "game.exe+100", ["10", "20"]),
			new("miss", "game.exe+108", ["-30", "0"]),
			new("unreadable", "60000", ["0"])
		], "21020", McpValueType.Int32, matchesOnly: true, includeHops: true, cancellationToken: Token);

		PointerChainCandidateResult match = Assert.Single(result.Candidates);
		Assert.Equal("match", match.Id);
		Assert.Equal(PointerChainValueStatus.Failed, match.ValueStatus);
		Assert.Equal(ToolErrorKind.MemoryReadFailed, match.ValueError!.Kind);
		Assert.Equal(new PointerHop("20010", "21000", "20", "21020"), match.Hops![1]);
		Assert.Equal(3, result.Summary.Processed);
		Assert.Equal(1, result.Summary.Misses);
		Assert.Equal(1, result.Summary.Unreadable);
	}

	[Fact]
	public async Task ReadChains_IncludeHops_TargetChangeStillRefusesAllResults()
	{
		await using PointerFixture fixture = new();
		fixture.OnCall = call =>
		{
			if (call == "Memory.TryReadPrimitive")
			{
				fixture.ChangeTarget();
			}
		};

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() => Tools(fixture).ReadChains(
			[new("first", "game.exe+100", ["10", "20"])], includeHops: true, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.TargetChanged, error.Error.Kind);
		Assert.Equal(1, fixture.PrimitiveReads);
	}

	[Fact]
	public async Task ReadChains_McpSerialization_EmitsHopsOnlyWhenRequested()
	{
		await using PointerFixture fixture = new();
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();
		const string candidates = """[{"id":"one","base":"game.exe+108","offsets":["-30","20"]}]""";

		CallToolResult compact = await pipeline.CallAsync(CheatEngineToolNames.PointerReadChains,
			$$"""{"candidates":{{candidates}}}""");
		CallToolResult detailed = await pipeline.CallAsync(CheatEngineToolNames.PointerReadChains,
			$$"""{"candidates":{{candidates}},"includeHops":true}""");

		Assert.False(compact.IsError is true);
		Assert.False(detailed.IsError is true);
		JsonElement compactJson = Assert.IsType<JsonElement>(compact.StructuredContent);
		JsonElement detailedJson = Assert.IsType<JsonElement>(detailed.StructuredContent);
		Assert.False(compactJson.GetProperty("candidates")[0].TryGetProperty("hops", out _));
		JsonElement hops = detailedJson.GetProperty("candidates")[0].GetProperty("hops");
		Assert.Equal(2, hops.GetArrayLength());
		Assert.Equal("10108", hops[0].GetProperty("readAt").GetString());
		Assert.Equal("20040", hops[0].GetProperty("pointer").GetString());
		Assert.Equal("-30", hops[0].GetProperty("offset").GetString());
		Assert.Equal("21020", hops[1].GetProperty("next").GetString());
	}

	private static PointerBatchTools Tools(PointerFixture fixture) =>
		(PointerBatchTools) fixture.Targets.Get(typeof(PointerBatchTools));
}
