using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>
///     The Lua state ledger seen through <see cref="TargetResources" />: orphans of earlier activations and failed Lua
///     cleanups join the listing, the transition check, the release and the acknowledgement path. Lua answers are canned.
/// </summary>
public sealed class McpStateLedgerTests
{
	private const string Foreign = "0badf00d";
	private static readonly TargetTransition Change = new(42, 43);
	private static readonly string[] ReleaseOrder = ["lease", "lua", "lease", "lua"];

	private static readonly ResourceReleaseKind[] BothReleased =
		[ResourceReleaseKind.Released, ResourceReleaseKind.Released];

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void ListAll_LuaStateOfEarlierActivations_IsListedAsOrphanedAfterTrackedResources()
	{
		StateTestHarness harness = new();
		string own = harness.Ledger!.Namespace;
		harness.Resources.TrackState($"speedhack-{own}-1", "speedhack", detail: "2.5");
		harness.Answer(_ => Snapshot(
			StateTestHarness.Entry($"speedhack-{own}-1", "resource", "active", true),
			StateTestHarness.Entry($"capture-{Foreign}-3", "job", "running", true),
			StateTestHarness.Entry($"pause-{own}-2", "resource", "cleanup_failed", true, "unpause refused"),
			StateTestHarness.Entry($"breakpoint-{Foreign}-1", "resource", "active", true),
			StateTestHarness.Entry($"trace-{Foreign}-4", "job", "completed", false)));

		IReadOnlyList<TargetResourceDescriptor> listed = harness.Resources.ListAll(Token);

		Assert.Equal([$"speedhack-{own}-1", $"capture-{Foreign}-3", $"pause-{own}-2", $"breakpoint-{Foreign}-1"],
			listed.Select(static descriptor => descriptor.Id).ToArray());
		Assert.Equal((TargetResourceCategory.LuaState, false, "2.5"),
			(listed[0].Category, listed[0].Orphaned, listed[0].Detail));
		Assert.Equal((TargetResourceCategory.Job, true, TargetResourceState.Active),
			(listed[1].Category, listed[1].Orphaned, listed[1].State));
		Assert.Equal((false, TargetResourceState.CleanupFailed, true, McpStateLedger.CleanupFailedMessage),
			(listed[2].Orphaned, listed[2].State, listed[2].RequiresManualRecovery, listed[2].CleanupError));
		Assert.DoesNotContain("unpause refused", listed[2].CleanupError!, StringComparison.Ordinal);
		Assert.Equal((TargetResourceCategory.LuaState, true), (listed[3].Category, listed[3].Orphaned));
		Assert.Equal(42, listed[3].ProcessId);
		Assert.Equal("mcp_state_snapshot", Assert.Single(harness.LuaCalls).Operation);
	}

	[Fact]
	public void EnsureCanChangeTarget_RunningOrphan_RefusesBusyAndAReselectionSkipsLua()
	{
		StateTestHarness harness = new();
		harness.Answer(_ => Snapshot(StateTestHarness.Entry($"capture-{Foreign}-1", "job", "running", true)));

		harness.Resources.EnsureCanChangeTarget(new TargetTransition(42, 42), Token);
		Assert.Empty(harness.LuaCalls);

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
			harness.Resources.EnsureCanChangeTarget(Change, Token));

		Assert.Equal((ToolErrorKind.Busy, ToolHostEffect.NotStarted), (error.Error.Kind, error.Error.HostEffect));
		Assert.Contains($"capture-{Foreign}-1", error.Error.Message, StringComparison.Ordinal);
		Assert.True(error.Error.Details!.Value.GetProperty("resources")[0].GetProperty("orphaned").GetBoolean());
	}

	[Fact]
	public void EnsureCanChangeTarget_OnlyEndedOrphans_IsAllowed()
	{
		StateTestHarness harness = new();
		harness.Answer(_ => Snapshot(StateTestHarness.Entry($"trace-{Foreign}-2", "job", "expired", false)));

		harness.Resources.EnsureCanChangeTarget(Change, Token);

		Assert.Single(harness.LuaCalls);
	}

	[Fact]
	public void ReleaseAll_IncludeOrphans_ReleasesTrackedResourcesFirstThenTheUnmanagedLuaState()
	{
		StateTestHarness harness = new();
		List<string> order = [];
		harness.Resources.Track(Lease(() =>
		{
			order.Add("lease");
			return new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed);
		}), "allocation");
		harness.Answer(source =>
		{
			order.Add("lua");
			return source.Contains("[3] = true", StringComparison.Ordinal)
				? new LuaStateRelease([StateTestHarness.Entry($"capture-{Foreign}-1", "job", "stopped", false)], null,
					[])
				: new LuaStateRelease([], null,
					[StateTestHarness.Entry($"capture-{Foreign}-1", "job", "running", true)]);
		});

		ReleaseAllResult listedOnly = harness.Resources.ReleaseAll(Token);
		Assert.False(listedOnly.IsComplete);
		Assert.True(Assert.Single(listedOnly.Remaining).Orphaned);
		Assert.Throws<CheatEngineToolException>(() => listedOnly.ThrowIfIncomplete());

		harness.Resources.Track(Lease(() =>
		{
			order.Add("lease");
			return new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed);
		}), "patch");
		ReleaseAllResult released = harness.Resources.ReleaseAll(CancellationToken.None, true);

		Assert.True(released.IsComplete);
		Assert.Equal(ReleaseOrder, order);
		Assert.Equal(BothReleased, released.Released.Select(static item => item.Release.Kind).ToArray());
		Assert.True(released.Released[1].Resource.Orphaned);
		Assert.Contains("[3] = true", harness.LuaCalls.Last().Source, StringComparison.Ordinal);
	}

	[Fact]
	public void TrackState_FailedLuaCleanup_IsReportedThenBlocksUntilAcknowledged()
	{
		StateTestHarness harness = new();
		string own = harness.Ledger!.Namespace;
		string id = harness.Resources.NextId("pause");
		bool removed = false;
		harness.Resources.TrackState(id, "pause", () => removed = true);
		harness.Answer(_ => new LuaResourceRelease(true, false, "unpause refused"));
		harness.Answer(_ => new LuaStateRelease([], null,
			[StateTestHarness.Entry(id, "resource", "cleanup_failed", true, "unpause refused")]));

		ReleaseAllResult result = harness.Resources.ReleaseAll(Token);

		Assert.NotNull(result.Failed);
		Assert.Equal((ResourceReleaseKind.CleanupFailed, true, McpStateLedger.CleanupFailedMessage),
			(result.Failed.Release.Kind, result.Failed.Release.RequiresManualRecovery,
				result.Failed.Resource.CleanupError));
		Assert.DoesNotContain("unpause refused", result.Failed.Resource.CleanupError!, StringComparison.Ordinal);
		Assert.True(removed);
		Assert.Empty(harness.Resources.List());

		harness.Answer(_ =>
			Snapshot(StateTestHarness.Entry(id, "resource", "cleanup_failed", true, "unpause refused")));
		Assert.Throws<CheatEngineToolException>(() => harness.Resources.EnsureCanChangeTarget(Change, Token));

		harness.Answer(source =>
		{
			Assert.Contains($"[2] = {{\"{id}\",}}", source, StringComparison.Ordinal);
			return new LuaStateAcknowledgement([
				StateTestHarness.Entry(id, "resource", "cleanup_failed", true,
					"unpause refused")
			]);
		});
		IReadOnlyList<TargetResourceDescriptor> acknowledged = harness.Resources.Acknowledge([id], Token);

		Assert.Equal(id, Assert.Single(acknowledged).Id);
		Assert.StartsWith($"pause-{own}-", id, StringComparison.Ordinal);
	}

	[Fact]
	public void TrackState_EntryGoneFromLua_EndsTheTrackedHandle()
	{
		StateTestHarness harness = new();
		string id = harness.Resources.NextId("speedhack");
		harness.Resources.TrackState(id, "speedhack");
		harness.Answer(_ => Snapshot());

		harness.Resources.EnsureCanChangeTarget(Change, Token);

		Assert.Equal(TargetResourceState.Ended, Assert.Single(harness.Resources.ListAll(Token)).State);
		harness.Answer(_ => new LuaStateRelease([], null, []));
		Assert.True(harness.Resources.ReleaseAll(Token).IsComplete);
		Assert.Empty(harness.Resources.List());
	}

	[Fact]
	public void TrackState_ForeignMalformedOrLedgerlessIds_AreRefused()
	{
		StateTestHarness harness = new();
		Assert.Throws<ArgumentException>(() => harness.Resources.TrackState($"pause-{Foreign}-1", "pause"));
		Assert.Throws<ArgumentException>(() =>
			harness.Resources.TrackState(harness.Resources.NextId("pause"), "speedhack"));
		Assert.Throws<ArgumentException>(() => harness.Resources.TrackState("pause", "pause"));
		Assert.Throws<InvalidOperationException>(() =>
			new TargetResources().TrackState("pause-abc-1", "pause"));
	}

	[Fact]
	public void Acknowledge_LedgerDeclaresAnUnknownId_IsNotFoundNotStarted()
	{
		StateTestHarness harness = new();
		harness.Declare<LuaStateAcknowledgement>("not_found", "No MCP state entry has the id.");

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
			harness.Resources.Acknowledge([$"capture-{Foreign}-7"], Token));

		Assert.Equal((ToolErrorKind.NotFound, ToolHostEffect.NotStarted), (error.Error.Kind, error.Error.HostEffect));
	}

	private static LuaStateSnapshot Snapshot(params LuaStateEntry[] entries)
	{
		return new LuaStateSnapshot(entries, entries.Length, false);
	}

	private static ICheatEngineLease Lease(Func<LeaseReleaseOutcome> release)
	{
		bool released = false;
		return ClientTestDouble.Create<ICheatEngineLease>((method, _) =>
		{
			switch (method.Name)
			{
				case "get_IsReleased":
					return released;
				case "get_RequiresManualRecovery":
					return false;
				case "Release":
					released = true;
					return release();
				default:
					throw new NotSupportedException(method.Name);
			}
		});
	}
}
