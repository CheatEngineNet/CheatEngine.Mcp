using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Tools.Pointer;

public sealed class PointerReferenceRetentionTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task FailedCleanup_RefusesRepeatedRangeSearchesWithoutAddingResources(bool scanFailed)
	{
		await using PointerFixture fixture = new()
		{
			ReleaseOutcome = new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed,
				CheatEngineHostEffect.CleanupUnconfirmed),
			ValueScanFirstFailure = scanFailed ? new InvalidOperationException("scan fixture failed") : null
		};
		Assert.Throws<CheatEngineToolException>(() =>
			fixture.References.FindReferences("20040", 0x40, cancellationToken: Token));
		string retainedId = Assert.Single(fixture.Resources.List()).Id;
		int dispatches = fixture.Dispatches;
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();

		for (int attempt = 0; attempt < 3; attempt++)
		{
			ToolError refusal = TestMcpPipeline.AssertError(await pipeline.CallAsync(
				CheatEngineToolNames.PointerFindReferences, """{"target":"20040","maxOffset":64}"""),
				ToolErrorKind.Busy);
			Assert.Equal(ToolHostEffect.NotStarted, refusal.HostEffect);
			Assert.True(refusal.Retryable);
			Assert.Equal(retainedId, Assert.Single(fixture.Resources.List()).Id);
		}

		Assert.Equal(dispatches, fixture.Dispatches);
		Assert.Single(fixture.Calls, static call => call == "ValueScans.CreateSession");
		Assert.Equal(1, fixture.ValueScanReleases);
		Assert.Single(fixture.References.FindReferences("20000", cancellationToken: Token).References);
	}

	[Fact]
	public async Task AcknowledgedFailedCleanup_AllowsANewRangeSearch()
	{
		await using PointerFixture fixture = new()
		{
			ReleaseOutcome = new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed,
				CheatEngineHostEffect.CleanupUnconfirmed)
		};
		Assert.Throws<CheatEngineToolException>(() =>
			fixture.References.FindReferences("20040", 0x40, cancellationToken: Token));
		string retainedId = Assert.Single(fixture.Resources.List()).Id;

		Assert.Equal(retainedId, Assert.Single(fixture.Resources.Acknowledge([retainedId], Token)).Id);
		fixture.ReleaseOutcome = new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed);
		Assert.Equal(2, fixture.References.FindReferences("20040", 0x40, cancellationToken: Token).Count);

		Assert.Empty(fixture.Resources.List());
		Assert.Equal(2, fixture.ValueScanReleases);
	}

	[Fact]
	public async Task RetryableCleanup_HoldsReservationUntilReleaseSucceeds()
	{
		// The case releases managed Client leases only; no Lua ledger participates in its cleanup.
		await using PointerFixture fixture = new(resources: new TargetResources())
		{
			ReleaseOutcome = new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnavailable,
				CheatEngineHostEffect.NotStarted)
		};
		Assert.Throws<CheatEngineToolException>(() =>
			fixture.References.FindReferences("20040", 0x40, cancellationToken: Token));
		string retainedId = Assert.Single(fixture.Resources.List()).Id;
		Assert.False(fixture.Resources.ReleaseAll(Token).IsComplete);
		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			fixture.References.FindReferences("20040", 0x40, cancellationToken: Token));
		Assert.Equal(ToolErrorKind.Busy, refused.Error.Kind);
		Assert.Equal(retainedId, Assert.Single(fixture.Resources.List()).Id);

		fixture.ReleaseOutcome = new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed);
		Assert.True(fixture.Resources.ReleaseAll(Token).IsComplete);
		Assert.Equal(2, fixture.References.FindReferences("20040", 0x40, cancellationToken: Token).Count);
		Assert.Empty(fixture.Resources.List());
	}

	[Fact]
	public async Task ReentrantRangeSearch_IsRefusedBeforeAnotherDispatch()
	{
		await using PointerFixture fixture = new();
		CheatEngineToolException? refused = null;
		fixture.OnDispatch = () =>
		{
			fixture.OnDispatch = null;
			refused = Assert.Throws<CheatEngineToolException>(() =>
				fixture.References.FindReferences("20040", 0x40, cancellationToken: Token));
		};

		Assert.Equal(2, fixture.References.FindReferences("20040", 0x40, cancellationToken: Token).Count);

		Assert.NotNull(refused);
		Assert.Equal((ToolErrorKind.Busy, ToolHostEffect.NotStarted),
			(refused.Error.Kind, refused.Error.HostEffect));
		Assert.Equal(1, fixture.Dispatches);
		Assert.Single(fixture.Calls, static call => call == "ValueScans.CreateSession");
		Assert.Empty(fixture.Resources.List());
	}

	[Fact]
	public async Task DispatchFailure_ReleasesReservationForTheNextRangeSearch()
	{
		await using PointerFixture fixture = new();
		fixture.OnDispatch = () => throw new InvalidOperationException("dispatch fixture failed");
		Assert.Throws<CheatEngineToolException>(() =>
			fixture.References.FindReferences("20040", 0x40, cancellationToken: Token));
		fixture.OnDispatch = null;

		Assert.Equal(2, fixture.References.FindReferences("20040", 0x40, cancellationToken: Token).Count);
		Assert.Equal(2, fixture.References.FindReferences("20040", 0x40, cancellationToken: Token).Count);

		Assert.Empty(fixture.Resources.List());
		Assert.Equal(2, fixture.ValueScanReleases);
	}
}
