using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Pointer;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Tools.Pointer;

/// <summary><c>pointer_find_references</c>: map lookups, the exact AOB route and the temporary value scan.</summary>
public sealed class PointerReferenceToolTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task FindReferences_LiveExact_ScansTheAlignedLittleEndianPointerInWritableMemory()
	{
		await using PointerFixture fixture = new();

		PointerReferenceResult result = fixture.References.FindReferences("20000", cancellationToken: Token);

		Assert.Equal(PointerReferenceSource.Live, result.Source);
		Assert.Equal("20000", result.Target);
		PointerReference reference = Assert.Single(result.References);
		Assert.Equal(new PointerReference("10100", "20000", "0", "game.exe+100"), reference);
		Assert.Equal((1, false), (result.Count, result.Truncated));
		AobScanRequest scan = fixture.LastAobScan!.Value;
		Assert.Equal("00 00 02 00 00 00 00 00", scan.Pattern.Value);
		Assert.Equal((ScanAlignmentMode.AlignedTo, 4), (scan.Alignment.Mode, scan.Alignment.Divisor));
		Assert.Equal(ScanProtectionRequirement.Required, scan.Protection.Writable);
		Assert.Equal((0UL, PointerReferenceTools.UserLast64),
			(scan.Range!.Value.Start.ToUInt64(), scan.Range.Value.End.ToUInt64()));
		Assert.DoesNotContain(fixture.Calls, static call => call.StartsWith("ValueScan", StringComparison.Ordinal));
	}

	[Fact]
	public async Task FindReferences_LiveExactNotWritableOnly_AlsoFindsReadOnlyHolders()
	{
		await using PointerFixture fixture = new();

		PointerReferenceResult result = fixture.References.FindReferences("20000", writableOnly: false,
			cancellationToken: Token);

		Assert.Equal(["10100", "40010"], result.References.Select(static reference => reference.Address));
		Assert.Null(result.References[1].Symbol);
	}

	[Fact]
	public async Task FindReferences_LiveRange_UsesATemporaryValueScanAndReleasesIt()
	{
		await using PointerFixture fixture = new();

		PointerReferenceResult result = fixture.References.FindReferences("20040", 0x40, cancellationToken: Token);

		Assert.Equal(["10108", "10100"], result.References.Select(static reference => reference.Address));
		Assert.Equal(["0", "40"], result.References.Select(static reference => reference.Offset));
		Assert.Equal(1, fixture.ValueScanReleases);
		ValueScanFirstRequest scan = fixture.LastValueScan!.Value;
		Assert.Equal(ValueScanComparison.Between, scan.Comparison);
		Assert.Equal(ValueScanValueType.Integer64, scan.ValueType);
		Assert.Equal(("131072", "131136"), (scan.Value!.Value.Text, scan.UpperValue!.Value.Text));
		Assert.Empty(fixture.Resources.List());
	}

	[Fact]
	public async Task FindReferences_LiveRange_PartialRereadMarksTheResultTruncated()
	{
		await using PointerFixture fixture = new()
		{
			PrimitiveBatchReadFailureIndex = 1
		};

		PointerReferenceResult result = fixture.References.FindReferences("20040", 0x40, cancellationToken: Token);

		Assert.Equal((2, true), (result.Count, result.Truncated));
		Assert.Equal(["10100"], result.References.Select(static reference => reference.Address));
		Assert.Equal(1, fixture.ValueScanReleases);
	}

	[Fact]
	public async Task FindReferences_TemporaryScanNotReleased_IsPartialEffectAndStaysTracked()
	{
		await using PointerFixture fixture = new()
		{
			ReleaseOutcome = new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed,
				CheatEngineHostEffect.CleanupUnconfirmed)
		};
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();

		ToolError error = TestMcpPipeline.AssertError(await pipeline.CallAsync(
				CheatEngineToolNames.PointerFindReferences, """{"target":"20040","maxOffset":64}"""),
			ToolErrorKind.PartialEffect);

		string id = error.Details!.Value.GetProperty("resourceId").GetString()!;
		Assert.StartsWith("scan-", id, StringComparison.Ordinal);
		Assert.Equal(id, Assert.Single(fixture.Resources.List()).Id);
		Assert.Equal(ToolHostEffect.CleanupUnconfirmed, error.HostEffect);
	}

	[Fact]
	public async Task FindReferences_Map_ListsNearestHoldersWithoutScanning()
	{
		await using PointerFixture fixture = new();
		fixture.Maps.CreateMap("map", cancellationToken: Token);
		fixture.WaitForMap("map");
		int dispatches = fixture.Dispatches;

		PointerReferenceResult result = fixture.References.FindReferences("20040", 0x40, "map", "GAME.EXE", limit: 1,
			cancellationToken: Token);

		Assert.Equal(PointerReferenceSource.Map, result.Source);
		Assert.Equal((2, true), (result.Count, result.Truncated));
		Assert.Equal(new PointerReference("10108", "20040", "0", "game.exe+108"), Assert.Single(result.References));
		Assert.Equal(dispatches, fixture.Dispatches);
	}

	[Theory]
	[InlineData("""{"target":"20000","maxOffset":65537}""", ToolErrorKind.LimitExceeded, "maxOffset")]
	[InlineData("""{"target":"20000","limit":0}""", ToolErrorKind.InvalidArgument, "limit")]
	[InlineData("""{"target":"20000","limit":1001}""", ToolErrorKind.LimitExceeded, "limit")]
	[InlineData("""{"target":""}""", ToolErrorKind.InvalidArgument, "target")]
	public async Task FindReferences_InvalidArguments_RefuseBeforeAnyDispatch(string arguments, ToolErrorKind kind,
		string parameter)
	{
		await using PointerFixture fixture = new();
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();

		ToolError error = TestMcpPipeline.AssertError(
			await pipeline.CallAsync(CheatEngineToolNames.PointerFindReferences, arguments), kind);

		Assert.Equal(parameter, error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, fixture.Dispatches);
	}

	[Fact]
	public async Task FindReferences_UnknownMap_IsNotFoundBeforeAnyDispatch()
	{
		await using PointerFixture fixture = new();
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();

		CallToolResult result = await pipeline.CallAsync(CheatEngineToolNames.PointerFindReferences,
			"""{"target":"20000","mapName":"missing"}""");

		Assert.Equal(ToolHostEffect.NotStarted, TestMcpPipeline.AssertError(result, ToolErrorKind.NotFound).HostEffect);
		Assert.Equal(0, fixture.Dispatches);
	}
}
