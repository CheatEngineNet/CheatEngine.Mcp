using System.Text.Json;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>Target resources v2: ids, the transition check, newest-first release, ended resources and acknowledgements.</summary>
public sealed class TargetResourcesTests
{
	private static readonly int[] ReverseOrder = [2, 1, 0];
	private static readonly string[] NewestFirstNames = ["second", "first"];
	private static readonly TargetTransition Change = new(42, 43);
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void Track_ClientLease_IssuesANamespacedIdAndABoundedDescriptor()
	{
		TargetResources resources = new();

		ITargetResource tracked = resources.Track(ActiveLease(), "allocation", name: "buffer", address: "1000",
			size: 4096);

		TargetResourceDescriptor descriptor = tracked.Descriptor;
		Assert.Equal($"allocation-{resources.Namespace}-1", descriptor.Id);
		Assert.Equal(("allocation", TargetResourceCategory.ClientLease, TargetResourceState.Active),
			(descriptor.Kind, descriptor.Category, descriptor.State));
		Assert.Equal(("buffer", "1000", 4096L), (descriptor.Name, descriptor.Address, descriptor.Size));
		Assert.False(descriptor.Orphaned);
		Assert.Matches("^[0-9a-f]{8}$", resources.Namespace);
		Assert.Equal($"scan-{resources.Namespace}-2", resources.NextId("scan"));
		Assert.Throws<ArgumentException>(() => resources.NextId("Bad-Kind"));
		Assert.Throws<InvalidOperationException>(() => resources.Track(tracked));
	}

	[Fact]
	public void ReleaseAll_CompleteResources_ReleasesNewestFirstAndForgetsThem()
	{
		List<int> released = [];
		List<int> removed = [];
		TargetResources resources = new();
		for (int index = 0; index < 3; index++)
		{
			int captured = index;
			resources.Track(Lease(() =>
			{
				released.Add(captured);
				return Outcome(LeaseReleaseKind.Released);
			}), "allocation", () => removed.Add(captured));
		}

		Assert.Throws<CheatEngineToolException>(() => resources.EnsureCanChangeTarget(Change, Token));
		ReleaseAllResult result = resources.ReleaseAll(Token);

		Assert.True(result.IsComplete);
		Assert.Same(result, result.ThrowIfIncomplete());
		Assert.Equal(ReverseOrder, released);
		Assert.Equal(ReverseOrder, removed);
		Assert.Equal(3, result.Released.Count);
		Assert.All(result.Released, item => Assert.Equal(ResourceReleaseKind.Released, item.Release.Kind));
		resources.EnsureCanChangeTarget(Change, Token);
		Assert.True(resources.ReleaseAll(Token).IsComplete);
		Assert.Equal(3, released.Count);
	}

	[Fact]
	public void ReleaseAll_RetryableFailure_RetainsTheHandleAndKeepsOlderDependencies()
	{
		TargetResources resources = new();
		int oldCalls = 0;
		int attempts = 0;
		resources.Track(Lease(() =>
		{
			oldCalls++;
			return Outcome(LeaseReleaseKind.Released);
		}), "allocation");
		resources.Track(Lease(() => ++attempts == 1
			? Outcome(LeaseReleaseKind.CleanupUnavailable)
			: Outcome(LeaseReleaseKind.Released)), "patch");

		ReleaseAllResult failed = resources.ReleaseAll(Token);

		Assert.False(failed.IsComplete);
		Assert.NotNull(failed.Failed);
		Assert.Equal((ResourceReleaseKind.CleanupUnavailable, true, false),
			(failed.Failed.Release.Kind, failed.Failed.Release.IsRetryable,
				failed.Failed.Release.RequiresManualRecovery));
		Assert.Equal(2, failed.Remaining.Count);
		Assert.Equal(0, oldCalls);
		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() => failed.ThrowIfIncomplete());
		Assert.Equal((ToolErrorKind.PartialEffect, true), (error.Error.Kind, error.Error.Retryable));
		Assert.Equal("patch", error.Error.Details!.Value.GetProperty("failed").GetProperty("resource")
			.GetProperty("kind").GetString());
		Assert.Throws<CheatEngineToolException>(() => resources.EnsureCanChangeTarget(Change, Token));

		Assert.True(resources.ReleaseAll(Token).IsComplete);
		Assert.Equal((2, 1), (attempts, oldCalls));
	}

	[Fact]
	public void ReleaseAll_ManualRecovery_ReportsTheDescriptorAndForgetsTheHandle()
	{
		TargetResources resources = new();
		bool removed = false;
		resources.Track(Lease(() => Outcome(LeaseReleaseKind.RefusedTargetChanged)), "allocation",
			() => removed = true, "owned", "1000", 4096);

		ReleaseAllResult result = resources.ReleaseAll(Token);

		Assert.NotNull(result.Failed);
		Assert.True(result.Failed.Release.RequiresManualRecovery);
		Assert.Equal(("owned", "1000"), (result.Failed.Resource.Name, result.Failed.Resource.Address));
		Assert.True(removed);
		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() => result.ThrowIfIncomplete());
		Assert.False(error.Error.Retryable);
		Assert.Contains("acknowledge", error.Error.Hint, StringComparison.Ordinal);
		resources.EnsureCanChangeTarget(Change, Token);
	}

	[Fact]
	public void Forget_ExplicitlyReleasedLease_NeverReleasesItAgain()
	{
		TargetResources resources = new();
		ICheatEngineLease lease = Lease(() => throw new InvalidOperationException("must not release"));
		resources.Track(lease, "symbol", () => throw new XunitException("must not remove twice"));

		Assert.True(resources.Forget(lease));
		Assert.False(resources.Forget(lease));
		Assert.True(resources.ReleaseAll(Token).IsComplete);
		Assert.Equal(0, resources.Count);
	}

	[Fact]
	public void ReportEndedResources_ChangedTarget_ReportsRecoveryWithoutTouchingActiveResources()
	{
		TargetResources resources = new();
		ICheatEngineLease active = ClientTestDouble.Create<ICheatEngineLease>((method, _) =>
			method.Name == "get_IsReleased" ? false : throw new XunitException("An active lease must stay untouched."));
		ICheatEngineLease ended = ClientTestDouble.Create<ICheatEngineLease>((method, _) => method.Name switch
		{
			"get_IsReleased" => true,
			"get_RequiresManualRecovery" => true,
			"Release" => Outcome(LeaseReleaseKind.RefusedTargetChanged),
			_ => throw new NotSupportedException(method.Name)
		});
		resources.Track(active, "scan", () => throw new XunitException("Active lease removed"));
		bool removed = false;
		resources.Track(ended, "allocation", () => removed = true, "stale allocation");

		CheatEngineToolException error =
			Assert.Throws<CheatEngineToolException>(() => resources.ReportEndedResources(Token));

		Assert.Equal((ToolErrorKind.PartialEffect, false), (error.Error.Kind, error.Error.Retryable));
		Assert.Equal("stale allocation", error.Error.Details!.Value.GetProperty("failed").GetProperty("resource")
			.GetProperty("name").GetString());
		Assert.True(removed);
		Assert.Empty(resources.ReportEndedResources(Token));
		Assert.Throws<CheatEngineToolException>(() => resources.EnsureCanChangeTarget(Change, Token));
	}

	[Fact]
	public void EnsureCanChangeTarget_ActiveResources_RefusesBusyWithTheirDescriptors()
	{
		TargetResources resources = new();
		resources.Track(ActiveLease(), "allocation", name: "first");
		resources.Track(ActiveLease(), "scan", name: "second");

		CheatEngineToolException error =
			Assert.Throws<CheatEngineToolException>(() =>
				resources.EnsureCanChangeTarget(new TargetTransition(null, null), Token));

		Assert.Equal((ToolErrorKind.Busy, ToolHostEffect.NotStarted), (error.Error.Kind, error.Error.HostEffect));
		Assert.Contains("runtime_release_resources", error.Error.Hint, StringComparison.Ordinal);
		JsonElement listed = error.Error.Details!.Value.GetProperty("resources");
		Assert.Equal(NewestFirstNames,
			listed.EnumerateArray().Select(static item => item.GetProperty("name").GetString()).ToArray());
		Assert.All(listed.EnumerateArray(), item => Assert.Equal("active", item.GetProperty("state").GetString()));
	}

	[Fact]
	public void EnsureCanChangeTarget_ReselectionOrCleanlyEndedResources_IsAllowed()
	{
		TargetResources resources = new();
		resources.Track(ActiveLease(), "allocation");
		resources.EnsureCanChangeTarget(new TargetTransition(42, 42), Token);

		TargetResources ended = new();
		ended.Track(ClientTestDouble.Create<ICheatEngineLease>((method, _) => method.Name switch
		{
			"get_IsReleased" => true,
			"get_RequiresManualRecovery" => false,
			_ => throw new NotSupportedException(method.Name)
		}), "symbol");
		ended.EnsureCanChangeTarget(Change, Token);
		Assert.Equal(TargetResourceState.Ended, Assert.Single(ended.List()).State);
	}

	[Fact]
	public void Acknowledge_TrackedResources_ForgetsEndedOnesAndRefusesActiveOrMalformedIds()
	{
		TargetResources resources = new();
		ITargetResource active = resources.Track(ActiveLease(), "allocation");
		bool removed = false;
		ITargetResource ended = resources.Track(ClientTestDouble.Create<ICheatEngineLease>((method, _) =>
			method.Name switch
			{
				"get_IsReleased" => true,
				"get_RequiresManualRecovery" => true,
				_ => throw new NotSupportedException(method.Name)
			}), "patch", () => removed = true);

		Assert.Equal(ToolErrorKind.InvalidArgument, Assert.Throws<CheatEngineToolException>(() =>
			resources.Acknowledge(["not-an-id"], Token)).Error.Kind);
		Assert.Equal(ToolErrorKind.InvalidArgument, Assert.Throws<CheatEngineToolException>(() =>
			resources.Acknowledge([], Token)).Error.Kind);
		Assert.Equal(ToolErrorKind.InvalidState, Assert.Throws<CheatEngineToolException>(() =>
			resources.Acknowledge([active.Descriptor.Id], Token)).Error.Kind);
		Assert.Equal(ToolErrorKind.NotFound, Assert.Throws<CheatEngineToolException>(() =>
			resources.Acknowledge([$"patch-{resources.Namespace}-99"], Token)).Error.Kind);

		IReadOnlyList<TargetResourceDescriptor> acknowledged = resources.Acknowledge([ended.Descriptor.Id], Token);

		Assert.Equal(ended.Descriptor.Id, Assert.Single(acknowledged).Id);
		Assert.True(removed);
		Assert.Equal(active.Descriptor.Id, Assert.Single(resources.List()).Id);
	}

	private static ICheatEngineLease ActiveLease()
	{
		return ClientTestDouble.Create<ICheatEngineLease>((method, _) => method.Name switch
		{
			"get_IsReleased" => false,
			_ => throw new NotSupportedException(method.Name)
		});
	}

	private static ICheatEngineLease Lease(Func<LeaseReleaseOutcome> release)
	{
		bool released = false;
		bool manual = false;
		return ClientTestDouble.Create<ICheatEngineLease>((method, _) =>
		{
			switch (method.Name)
			{
				case "get_IsReleased":
					return released;
				case "get_RequiresManualRecovery":
					return manual;
				case "Release":
					LeaseReleaseOutcome outcome = release();
					released = !outcome.IsRetryable;
					manual = outcome.RequiresManualRecovery;
					return outcome;
				default:
					throw new NotSupportedException(method.Name);
			}
		});
	}

	private static LeaseReleaseOutcome Outcome(LeaseReleaseKind kind)
	{
		return new LeaseReleaseOutcome(kind, CheatEngineHostEffect.NotStarted);
	}
}
