using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Pointer;
using CheatEngine.SDK.Engine.Runtime;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Tools.Pointer;

/// <summary><c>pointer_read_chain</c>: every hop in one dispatch, typed reads, and hop-level failures.</summary>
public sealed class PointerChainToolTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ReadChain_NegativeOffset_ReportsEveryHopAndTheFinalValueInOneDispatch()
	{
		await using PointerFixture fixture = new();

		PointerChainResult result = fixture.Chain.ReadChain("game.exe+108", ["-30", "0x20"], McpValueType.Int32,
			null, Token);

		Assert.Equal("[[game.exe+108]-30]+20", result.Expression);
		Assert.Equal("10108", result.Base);
		Assert.Equal("21020", result.Address);
		Assert.Equal("1234", result.Value);
		Assert.Equal(
		[
			new PointerHop("10108", "20040", "-30", "20010"),
			new PointerHop("20010", "21000", "20", "21020")
		], result.Hops);
		Assert.Equal(1, fixture.Dispatches);
	}

	[Fact]
	public async Task ReadChain_UnreadableHop_FailsWithMemoryReadFailedAndTheHopInDetails()
	{
		await using PointerFixture fixture = new();
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();

		CallToolResult result = await pipeline.CallAsync(CheatEngineToolNames.PointerReadChain,
			"""{"base":"game.exe+100","offsets":["10","100000","0"]}""");

		ToolError error = TestMcpPipeline.AssertError(result, ToolErrorKind.MemoryReadFailed);
		Assert.Equal(ToolHostEffect.NotApplied, error.HostEffect);
		Assert.False(error.Retryable);
		JsonElement details = error.Details!.Value;
		Assert.Equal(2, details.GetProperty("hopIndex").GetInt32());
		Assert.Equal("121000", details.GetProperty("readAt").GetString());
		Assert.Contains("Hop 2", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ReadChain_ThirtyTwoBitOverflow_FailsAtTheOverflowingHop()
	{
		await using PointerFixture fixture = new(pointerSize: PointerSize.Bit32);
		fixture.PutPointer(PointerFixture.ModuleBase + 0x100, 0xFFFF_FFF0);

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
			fixture.Chain.ReadChain("game.exe+100", ["20"], cancellationToken: Token));

		Assert.Equal(ToolErrorKind.MemoryReadFailed, error.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, error.Error.HostEffect);
		Assert.Equal(0, error.Error.Details!.Value.GetProperty("hopIndex").GetInt32());
		Assert.Equal(1, fixture.Dispatches);
	}

	[Fact]
	public async Task ReadChain_ThroughTheServer_ReturnsStructuredHops()
	{
		await using PointerFixture fixture = new();
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();

		CallToolResult result = await pipeline.CallAsync(CheatEngineToolNames.PointerReadChain,
			"""{"base":"game.exe+100","offsets":["10","20"],"valueType":"int32"}""");

		Assert.NotEqual(true, result.IsError);
		JsonElement content = Assert.IsType<JsonElement>(result.StructuredContent);
		Assert.Equal("21020", content.GetProperty("address").GetString());
		Assert.Equal("21000", content.GetProperty("hops")[1].GetProperty("pointer").GetString());
		Assert.Equal("1234", content.GetProperty("value").GetString());
	}

	[Theory]
	[InlineData("""{"base":"game.exe+100","offsets":[]}""", ToolErrorKind.InvalidArgument, "offsets")]
	[InlineData("""{"base":" ","offsets":["10"]}""", ToolErrorKind.InvalidArgument, "base")]
	[InlineData("""{"base":"game.exe+100","offsets":["xyz"]}""", ToolErrorKind.InvalidArgument, "offsets[0]")]
	[InlineData("""{"base":"game.exe+100","offsets":["10"],"valueType":"bytes"}""", ToolErrorKind.InvalidArgument,
		"length")]
	[InlineData("""{"base":"game.exe+100","offsets":["10"],"valueType":"bytes","length":65537}""",
		ToolErrorKind.LimitExceeded, "length")]
	public async Task ReadChain_InvalidArguments_RefuseBeforeAnyDispatch(string arguments, ToolErrorKind kind,
		string parameter)
	{
		await using PointerFixture fixture = new();
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();

		ToolError error = TestMcpPipeline.AssertError(
			await pipeline.CallAsync(CheatEngineToolNames.PointerReadChain, arguments), kind);

		Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
		Assert.Equal(parameter, error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, fixture.Dispatches);
		Assert.Empty(fixture.Calls);
	}

	[Fact]
	public async Task ReadChain_SixtyFiveOffsets_IsLimitExceededBeforeAnyDispatch()
	{
		await using PointerFixture fixture = new();

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
			fixture.Chain.ReadChain("game.exe+100", [.. Enumerable.Repeat("0", 65)], null, null, Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, error.Error.Kind);
		Assert.Equal(0, fixture.Dispatches);
	}
}
