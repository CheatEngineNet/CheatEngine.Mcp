using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Tools;

namespace CheatEngine.Mcp.Tests;

public sealed class TargetResourcesTests
{
	private static readonly int[] ReverseOrder = [2, 1, 0];
	[Fact]
	public void ReleaseAll_CompleteResources_ReleasesInReverseOrderAndForgetsThem()
	{
		List<int> released = [];
		TargetResources resources = new TargetResources();
		for (int index = 0; index < 3; index++)
		{
			int captured = index;
			resources.Track(Lease(() => { released.Add(captured); return Outcome(LeaseReleaseKind.Released); }), () => { });
		}
		Assert.NotNull(resources.PrepareForTargetChange());
		Assert.Empty(released);
		Assert.Null(resources.ReleaseAll());
		Assert.Equal(ReverseOrder, released);
		Assert.Null(resources.PrepareForTargetChange());
		Assert.Null(resources.ReleaseAll());
		Assert.Equal(3, released.Count);
	}

	[Fact]
	public void ReleaseAll_RetryableFailure_RetainsNewestAndDoesNotReleaseDependencies()
	{
		TargetResources resources = new TargetResources();
		int oldCalls = 0;
		int attempts = 0;
		resources.Track(Lease(() => { oldCalls++; return Outcome(LeaseReleaseKind.Released); }), () => { });
		resources.Track(Lease(() => ++attempts == 1 ? Outcome(LeaseReleaseKind.CleanupUnavailable) : Outcome(LeaseReleaseKind.Released)), () => { });
		object? failure = resources.ReleaseAll();
		Assert.NotNull(failure);
		ToolResultAssert.HasPropertyValue(failure, "retryable", true);
		Assert.Equal(0, oldCalls);
		Assert.NotNull(resources.PrepareForTargetChange());
		Assert.Null(resources.ReleaseAll());
		Assert.Equal(2, attempts);
		Assert.Equal(1, oldCalls);
	}

	[Fact]
	public void ReleaseAll_ManualRecovery_ReportsIdentityAndDropsEndedEntry()
	{
		TargetResources resources = new TargetResources();
		bool removed = false;
		object identity = new
		{
			kind = "allocation",
			name = "owned",
			address = "0x1000",
			size = 4096
		};
		resources.Track(Lease(() => Outcome(LeaseReleaseKind.RefusedTargetChanged)), () => removed = true, identity);
		object? failure = resources.ReleaseAll();
		Assert.NotNull(failure);
		ToolResultAssert.HasPropertyValue(failure, "manualRecoveryRequired", true);
		Assert.Same(identity, ToolResultAssert.GetProperty<object>(failure, "resource"));
		Assert.True(removed);
		Assert.Null(resources.PrepareForTargetChange());
	}

	[Fact]
	public void Forget_ExplicitlyReleasedLease_DoesNotReleaseAgain()
	{
		TargetResources resources = new TargetResources();
		ICheatEngineLease lease = Lease(() => throw new InvalidOperationException("must not release"));
		resources.Track(lease, () => throw new Xunit.Sdk.XunitException("must not remove twice"));
		resources.Forget(lease);
		Assert.Null(resources.ReleaseAll());
	}

	private static ICheatEngineLease Lease(Func<LeaseReleaseOutcome> release) =>
		ClientTestDouble.Create<ICheatEngineLease>((method, _) => method.Name == "Release" ? release() : throw new NotSupportedException(method.Name));

	private static LeaseReleaseOutcome Outcome(LeaseReleaseKind kind) => new LeaseReleaseOutcome(kind, CheatEngineHostEffect.NotStarted);

	[Fact]
	public void ReportEndedResources_ChangedTarget_ReportsRecoveryWithoutTouchingActiveResources()
	{
		TargetResources resources = new TargetResources();
		ICheatEngineLease active = ClientTestDouble.Create<ICheatEngineLease>((method, _) =>
			method.Name == "get_IsReleased" ? false : throw new Xunit.Sdk.XunitException("Active lease must stay untouched."));
		ICheatEngineLease ended = ClientTestDouble.Create<ICheatEngineLease>((method, _) => method.Name switch
		{
			"get_IsReleased" => true,
			"Release" => Outcome(LeaseReleaseKind.RefusedTargetChanged),
			_ => throw new NotSupportedException(method.Name)
		});
		resources.Track(active, () => throw new Xunit.Sdk.XunitException("Active lease removed"));
		resources.Track(ended, () => { }, "stale allocation");
		object? failure = resources.ReportEndedResources();
		Assert.NotNull(failure);
		ToolResultAssert.HasPropertyValue(failure, "manualRecoveryRequired", true);
		ToolResultAssert.HasPropertyValue(failure, "resource", "stale allocation");
		Assert.Null(resources.ReportEndedResources());
		Assert.NotNull(resources.PrepareForTargetChange());
	}
}
