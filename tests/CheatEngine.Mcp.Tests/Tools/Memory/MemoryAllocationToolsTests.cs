using CheatEngine.Client.Allocations;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Memory;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>Named allocations: tracked leases, activation caps checked before dispatch, and honest releases.</summary>
public sealed class MemoryAllocationToolsTests
{
	private const long MiB = 1024 * 1024;
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void Allocate_Executable_RequestsExecuteReadWriteAndTracksTheLease()
	{
		Harness harness = new();
		AllocationRequest? request = null;
		harness.Target.Allocations = (_, arguments) =>
		{
			request = (AllocationRequest) arguments[0]!;
			return harness.Target.Lease(0x7FF000010000, request.Value.Size);
		};

		AllocationInfo info = harness.Tools.Allocate("cave", 64, true, "401000", Token);

		Assert.Equal(AllocationProtection.ExecuteReadWrite, request!.Value.Protection);
		Assert.Equal(0x401000UL, request.Value.PreferredAddress!.Value.ToUInt64());
		Assert.Equal(("cave", "7FF000010000", 64L, true), (info.Name, info.Address, info.Size, info.Executable));
		TargetResourceDescriptor tracked = Assert.Single(harness.Resources.List());
		Assert.Equal((info.ResourceId, "allocation", "cave", "7FF000010000", 64L),
			(tracked.Id, tracked.Kind, tracked.Name, tracked.Address, tracked.Size));
	}

	[Fact]
	public void Allocate_TotalAbove64MiB_RefusesBeforeAnyDispatch()
	{
		Harness harness = new();
		harness.Target.Allocations = (_, arguments) =>
			harness.Target.Lease(0x10000, ((AllocationRequest) arguments[0]!).Size);
		for (int index = 0; index < 4; index++)
		{
			harness.Tools.Allocate($"block{index}", 16 * MiB, cancellationToken: Token);
		}

		int dispatches = harness.Target.Dispatcher.Calls;
		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() =>
				harness.Tools.Allocate("block4", 1, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Equal(dispatches, harness.Target.Dispatcher.Calls);
		Assert.Equal(4, harness.Target.CallsTo("Allocations").Length);
	}

	[Fact]
	public void Allocate_128Allocations_RefusesTheNextOneBeforeAnyDispatch()
	{
		Harness harness = new();
		harness.Target.Allocations = (_, _) => harness.Target.Lease(0x10000, 16);
		for (int index = 0; index < AllocationRegistry.MaximumAllocations; index++)
		{
			harness.Tools.Allocate($"small{index}", 16, cancellationToken: Token);
		}

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() =>
				harness.Tools.Allocate("one-more", 16, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Equal(AllocationRegistry.MaximumAllocations, harness.Target.Mutations);
	}

	[Fact]
	public void Allocate_DuplicateName_IsRefusedAndAFailedAllocationFreesItsName()
	{
		Harness harness = new();
		bool fail = true;
		harness.Target.Allocations = (_, _) => fail
			? throw new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, "Allocations.Allocate",
				"Cheat Engine refused the allocation.", hostEffect: CheatEngineHostEffect.NotApplied).ToException()
			: harness.Target.Lease(0x20000, 32);

		Assert.Equal(ToolErrorKind.HostRefused,
			Assert.Throws<CheatEngineToolException>(() => harness.Tools.Allocate("data", 32, cancellationToken: Token))
				.Error.Kind);
		fail = false;
		harness.Tools.Allocate("data", 32, cancellationToken: Token);
		CheatEngineToolException duplicate =
			Assert.Throws<CheatEngineToolException>(() => harness.Tools.Allocate("data", 32, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, duplicate.Error.Kind);
		Assert.Equal(2, harness.Target.CallsTo("Allocations").Length);
	}

	[Fact]
	public void Free_RetryableRelease_KeepsTheAllocationTrackedUntilAReleaseCompletes()
	{
		Harness harness = new();
		harness.Target.Allocations = (_, _) => harness.Target.Lease(0x30000, 64,
			new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted),
			new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed));
		harness.Tools.Allocate("keep", 64, cancellationToken: Token);

		CheatEngineToolException incomplete =
			Assert.Throws<CheatEngineToolException>(() => harness.Tools.Free("keep", Token));
		Assert.Equal(ToolErrorKind.PartialEffect, incomplete.Error.Kind);
		Assert.True(incomplete.Error.Retryable);
		Assert.Single(harness.Resources.List());
		Assert.Equal(1, harness.Registry.Count);

		ReleaseResult released = harness.Tools.Free("keep", Token);

		Assert.Equal(ResourceReleaseKind.Released, released.Release.Kind);
		Assert.Empty(harness.Resources.List());
		Assert.Equal(0, harness.Registry.Count);
		Assert.Equal(2, harness.Target.CallsTo("Lease").Length);
	}

	[Fact]
	public void Free_ReleaseRefusedAfterATargetChange_IsForgottenAndReportsManualRecovery()
	{
		Harness harness = new();
		harness.Target.Allocations = (_, _) => harness.Target.Lease(0x40000, 8,
			new LeaseReleaseOutcome(LeaseReleaseKind.RefusedTargetChanged, CheatEngineHostEffect.NotStarted));
		harness.Tools.Allocate("gone", 8, cancellationToken: Token);

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => harness.Tools.Free("gone", Token));

		Assert.Equal(ToolErrorKind.PartialEffect, exception.Error.Kind);
		Assert.False(exception.Error.Retryable);
		Assert.Empty(harness.Resources.List());
		Assert.Equal(ToolErrorKind.NotFound,
			Assert.Throws<CheatEngineToolException>(() => harness.Tools.Free("gone", Token)).Error.Kind);
	}

	[Fact]
	public void Free_UnknownName_IsNotFoundWithoutAnyDispatch()
	{
		Harness harness = new();

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => harness.Tools.Free("never", Token));

		Assert.Equal(ToolErrorKind.NotFound, exception.Error.Kind);
		Assert.Equal(0, harness.Target.Dispatcher.Calls);
	}

	[Fact]
	public void ReleaseAll_TrackedAllocation_FreesItAndForgetsTheName()
	{
		Harness harness = new();
		harness.Target.Allocations = (_, _) => harness.Target.Lease(0x50000, 8);
		harness.Tools.Allocate("scratch", 8, cancellationToken: Token);

		ReleaseAllResult result = harness.Resources.ReleaseAll(Token);

		Assert.Single(result.Released);
		Assert.Equal(0, harness.Registry.Count);
		Assert.Equal(ToolErrorKind.NotFound,
			Assert.Throws<CheatEngineToolException>(() => harness.Tools.Free("scratch", Token)).Error.Kind);
	}

	private sealed class Harness
	{
		internal Harness()
		{
			Tools = new MemoryAllocationTools(Target.Dispatch, Resources, Registry);
		}

		internal TargetDouble Target
		{
			get;
		} = new();

		internal TargetResources Resources
		{
			get;
		} = new();

		internal AllocationRegistry Registry
		{
			get;
		} = new();

		internal MemoryAllocationTools Tools
		{
			get;
		}
	}
}
