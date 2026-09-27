using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     The Lua state ledger against the real Lua 5.3 runtime: recorded effects, the snapshot, the release of unmanaged
///     entries, acknowledgements, resource TTLs, and the managed classes driving the real kernel end to end.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private const string RecordEffect = """
	                                    resourceRecord(a[1], a[2], a[3], {name = a[4], detail = a[5], address = a[6], size = a[7], ttl = a[8]},
	                                    	function(entry, reason)
	                                    		if failRelease == entry.id then error('restore refused', 0) end
	                                    		released = (released or '') .. entry.id .. ':' .. reason .. ';'
	                                    	end)
	                                    return true
	                                    """;

	private static readonly string[] AcknowledgedIds = ["pause-aaaa11-1", "speedhack-bbbb22-1"];

	[Fact]
	public void StateLedger_RecordedEffects_AreDescribedAndReleasedOnceOrKeptWithTheirCleanupError()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\ngetOpenedProcessID = function() return 77 end");
		Record(OwnNamespace, "speedhack-bbbb22-1", "speedhack", "2.5");
		Record(OwnNamespace, "breakpoint-bbbb22-2", "breakpoint", address: 0x7FF612345678, size: 4);
		InstallStubs("failRelease = 'breakpoint-bbbb22-2'");

		LuaStateSnapshot snapshot = ReadKernel(LuaJobKernelScripts.StateSnapshot,
			StateJsonContext.Default.LuaStateSnapshot, OwnNamespace);
		Assert.Equal((2, false), (snapshot.Total, snapshot.Truncated));
		LuaStateEntry breakpoint = snapshot.Entries.Single(entry => entry.Kind == "breakpoint");
		Assert.Equal(("7FF612345678", 4L, 77, "resource", true), (breakpoint.Address, breakpoint.Size!.Value,
			breakpoint.ProcessId!.Value, breakpoint.Family, breakpoint.HoldsHostState));
		Assert.Equal("2.5", snapshot.Entries.Single(entry => entry.Kind == "speedhack").Detail);

		LuaResourceRelease released = ReadKernel(LuaJobKernelScripts.StateReleaseResource,
			StateJsonContext.Default.LuaResourceRelease, OwnNamespace, "speedhack-bbbb22-1");
		Assert.Equal((true, true), (released.Found, released.Released));
		Assert.Equal("speedhack-bbbb22-1:released;", ReadGlobal("released"));
		Assert.False(ReadKernel(LuaJobKernelScripts.StateReleaseResource, StateJsonContext.Default.LuaResourceRelease,
			OwnNamespace, "speedhack-bbbb22-1").Found);

		LuaResourceRelease refused = ReadKernel(LuaJobKernelScripts.StateReleaseResource,
			StateJsonContext.Default.LuaResourceRelease, OwnNamespace, "breakpoint-bbbb22-2");
		Assert.Equal((true, false, "restore refused"), (refused.Found, refused.Released, refused.CleanupError));
		InstallStubs("failRelease = nil");
		// A failed release is never retried: the entry waits for manual recovery and an acknowledgement.
		Assert.False(ReadKernel(LuaJobKernelScripts.StateReleaseResource, StateJsonContext.Default.LuaResourceRelease,
			OwnNamespace, "breakpoint-bbbb22-2").Released);
		Assert.Equal("speedhack-bbbb22-1:released;", ReadGlobal("released"));
		Assert.Equal("cleanup_failed", Assert.Single(ReadKernel(LuaJobKernelScripts.StateSnapshot,
			StateJsonContext.Default.LuaStateSnapshot, OwnNamespace).Entries).State);
		Assert.False(ReadKernel(LuaJobKernelScripts.StateReleaseResource, StateJsonContext.Default.LuaResourceRelease,
			EarlierNamespace, "breakpoint-bbbb22-2").Found);
	}

	[Fact]
	public void StateLedger_RecordAndForget_ValidateBoundsAndFindEffectsOfAKindInEveryNamespace()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs);
		Record(EarlierNamespace, "speedhack-aaaa11-1", "speedhack");
		Record(OwnNamespace, "pause-bbbb22-1", "pause");

		Assert.Equal(1L, EvaluateLua(Kernel("#stateEntriesOfKind('speedhack')")));
		Assert.Contains("already in use", RunKernelFailure(RecordEffect, OwnNamespace, "pause-bbbb22-1", "pause"),
			StringComparison.Ordinal);
		Assert.Contains("name must be",
			RunKernelFailure(RecordEffect, OwnNamespace, "pause-bbbb22-2", "pause", new string('x', 257)),
			StringComparison.Ordinal);
		Assert.Contains("TTL", RunKernelFailure(RecordEffect, OwnNamespace, "pause-bbbb22-2", "pause", null, null,
			null, null, 300001), StringComparison.Ordinal);
		Assert.True(Parse(RunKernel("return {forgotten = resourceForget(a[1], 'pause-bbbb22-1')}",
			OwnNamespace)).GetProperty("forgotten").GetBoolean());
		Assert.Null(ReadGlobal("released"));
		Assert.Equal(0L, EvaluateLua(Kernel("#stateEntriesOfKind('pause')")));
		Assert.Equal(0L, EvaluateLua("#timers"));
	}

	[Fact]
	public void StateLedger_ReleaseUnmanaged_ReleasesOrphansNewestFirstAndStopsAtTheFirstFailure()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs);
		Record(EarlierNamespace, "speedhack-aaaa11-1", "speedhack");
		InstallStubs("clock = 10");
		string orphanJob = CreateJob("capture", 8, 60000, 1, EarlierNamespace);
		RunKernel(
			"jobFind(a[1], a[2]).onStop = function(job, state) released = (released or '') .. job.id .. ':' .. state .. ';' end; return true",
			EarlierNamespace, orphanJob);
		InstallStubs("clock = 20");
		Record(EarlierNamespace, "pause-aaaa11-3", "pause");
		InstallStubs("clock = 30");
		Record(OwnNamespace, "pause-bbbb22-1", "pause");
		string ownTracked = CreateJob("trace", 8, 60000, 0);
		InstallStubs("clock = 40; failRelease = 'speedhack-aaaa11-1'");

		LuaStateRelease listed = ReadKernel(LuaJobKernelScripts.StateReleaseUnmanaged,
			StateJsonContext.Default.LuaStateRelease, OwnNamespace, new[] { ownTracked }, false);
		Assert.Empty(listed.Released);
		Assert.Equal(["pause-bbbb22-1", "pause-aaaa11-3", orphanJob, "speedhack-aaaa11-1"],
			listed.Remaining.Select(static entry => entry.Id).ToArray());

		LuaStateRelease released = ReadKernel(LuaJobKernelScripts.StateReleaseUnmanaged,
			StateJsonContext.Default.LuaStateRelease, OwnNamespace, new[] { ownTracked }, true);

		Assert.Equal(["pause-bbbb22-1", "pause-aaaa11-3", orphanJob],
			released.Released.Select(static entry => entry.Id).ToArray());
		Assert.Equal(("speedhack-aaaa11-1", "restore refused"), (released.Failed!.Id, released.Failed.CleanupError));
		Assert.Equal("speedhack-aaaa11-1", Assert.Single(released.Remaining).Id);
		Assert.Equal($"pause-bbbb22-1:released;pause-aaaa11-3:released;{orphanJob}:stopped;", ReadGlobal("released"));
		Assert.Equal(ownTracked, Assert.Single(ReadKernel(LuaJobKernelScripts.Statuses,
			StateJsonContext.Default.LuaJobStatuses, OwnNamespace).Jobs).JobId);

		InstallStubs("failRelease = nil");
		LuaStateRelease retried = ReadKernel(LuaJobKernelScripts.StateReleaseUnmanaged,
			StateJsonContext.Default.LuaStateRelease, OwnNamespace, new[] { ownTracked }, true);
		Assert.Empty(retried.Released);
		Assert.Null(retried.Failed);
		Assert.Equal("cleanup_failed", Assert.Single(retried.Remaining).State);
	}

	[Fact]
	public void StateLedger_Acknowledge_RefusesUnknownTrackedOrRunningEntriesAndRemovesTheRest()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\nfailRelease = 'pause-aaaa11-1'");
		Record(EarlierNamespace, "pause-aaaa11-1", "pause");
		ReadKernel(LuaJobKernelScripts.StateReleaseUnmanaged, StateJsonContext.Default.LuaStateRelease, OwnNamespace,
			Array.Empty<string>(), true);
		Record(OwnNamespace, "speedhack-bbbb22-1", "speedhack");
		string running = CreateJob("capture", 8, 60000, 0, EarlierNamespace);

		Assert.Equal("not_found", Acknowledge(["pause-aaaa11-9"], []).Kind);
		Assert.Equal("invalid_state", Acknowledge(["speedhack-bbbb22-1"], ["speedhack-bbbb22-1"]).Kind);
		Assert.Equal("invalid_state", Acknowledge(["pause-aaaa11-1", running], []).Kind);
		Assert.Equal(3L, EvaluateLua(Kernel("#stateCollect(function() return true end)")));

		LuaStateAcknowledgement acknowledged = ReadKernel(LuaJobKernelScripts.StateAcknowledge,
			StateJsonContext.Default.LuaStateAcknowledgement, OwnNamespace,
			AcknowledgedIds, Array.Empty<string>());

		Assert.Equal(("pause-aaaa11-1", "restore refused"),
			(acknowledged.Acknowledged[0].Id, acknowledged.Acknowledged[0].CleanupError));
		Assert.Equal(2, acknowledged.Acknowledged.Length);
		Assert.Equal(running, Assert.Single(ReadKernel(LuaJobKernelScripts.StateSnapshot,
			StateJsonContext.Default.LuaStateSnapshot, OwnNamespace).Entries).Id);
		Assert.Null(ReadGlobal("released"));
	}

	[Fact]
	public void StateLedger_ResourceTtl_IsReleasedByTheSweeperAndKeepsTheTimerOnlyWhilePending()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs);
		Record(OwnNamespace, "breakpoint-bbbb22-1", "breakpoint", ttl: 5000);
		Record(OwnNamespace, "pause-bbbb22-2", "pause");
		Assert.Equal(1L, EvaluateLua("#timers"));

		InstallStubs("clock = 4999; timers[1].OnTimer(timers[1])");
		Assert.Null(ReadGlobal("released"));
		Assert.Equal(false, EvaluateLua("timers[1].destroyed"));

		InstallStubs("clock = 5000");
		Assert.Equal(1, SweepJobs().ResourcesReleased);
		Assert.Equal("breakpoint-bbbb22-1:expired;", ReadGlobal("released"));
		Assert.Equal(true, EvaluateLua("timers[1].destroyed"));
		Assert.Equal("pause-bbbb22-2", Assert.Single(ReadKernel(LuaJobKernelScripts.StateSnapshot,
			StateJsonContext.Default.LuaStateSnapshot, OwnNamespace).Entries).Id);
	}

	[Fact]
	public void StateLedger_ManagedClasses_DriveTheRealKernelEndToEnd()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		McpStateLedger ledger = new(dispatch, TimeProvider.System);
		TargetResources resources = new(ledger);
		JobRegistry jobs = new(dispatch, resources, Options.Create(new McpExecutionOptions()), TimeProvider.System);
		Record(EarlierNamespace, "speedhack-aaaa11-1", "speedhack");

		// An orphan blocks the change until it is released; the ledger view marks it orphaned.
		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			resources.EnsureCanChangeTarget(new TargetTransition(1, 2), Token));
		Assert.Equal(ToolErrorKind.Busy, refused.Error.Kind);
		Assert.True(Assert.Single(resources.ListAll(Token)).Orphaned);

		string effect = resources.NextId("pause");
		dispatch.ExecuteLua("record", LuaJobKernelScripts.Kernel + "\n" + RecordEffect,
			StateTestJsonContext.Default.Boolean, Token, ledger.Namespace, effect, "pause");
		resources.TrackState(effect, "pause");
		LuaJob<long> job = jobs.StartLua("capture", TimeSpan.FromSeconds(60), 4, StateTestJsonContext.Default.Int64,
			start => dispatch.ExecuteLua("start", LuaJobKernelScripts.Strategies + "\n" + """
					local job = jobStart(a[1], a[2], 'capture', a[3], a[4], function(job)
						for i = 1, 6 do jobPush(job, i) end
					end)
					return true
					""", StateTestJsonContext.Default.Boolean, Token, start.Namespace,
				start.Id, start.BufferLimit, start.TimeToLiveMilliseconds));

		JobPoll<long> page = job.Poll(0, 10, Token);
		Assert.Equal(new long[] { 3, 4, 5, 6 }, page.Items);
		Assert.Equal((3L, 6L, false, 2L), (page.FirstSequence, page.NextAfterSequence, page.More, page.Dropped));
		Assert.Equal(JobState.Running, Assert.Single(jobs.List(Token)).State);
		Assert.Equal([job.Id, effect, "speedhack-aaaa11-1"],
			resources.ListAll(Token).Select(static descriptor => descriptor.Id).ToArray());

		ReleaseAllResult result = resources.ReleaseAll(Token, true);

		Assert.True(result.IsComplete, JsonSerializer.Serialize(result, StateJsonContext.Default.ReleaseAllResult));
		Assert.Equal([job.Id, effect, "speedhack-aaaa11-1"],
			result.Released.Select(static item => item.Resource.Id).ToArray());
		Assert.Equal($"{effect}:released;speedhack-aaaa11-1:released;", ReadGlobal("released"));
		resources.EnsureCanChangeTarget(new TargetTransition(1, 2), Token);
		Assert.Equal(ToolErrorKind.NotFound, Assert.Throws<CheatEngineToolException>(() =>
			job.Poll(0, 1, Token)).Error.Kind);
		Assert.True(jobs.Stop(job.Id, Token).AlreadyReleased);
		Assert.Equal(0L, EvaluateLua("rawget(_G, '__cheatengine_mcp_state').namespaces.aaaa11.resourceCount"));
	}

	private static void Record(string jobNamespace, string id, string kind, string? detail = null,
		long? address = null, long? size = null, long? ttl = null)
	{
		RunKernel(RecordEffect, jobNamespace, id, kind, null, detail, address, size, ttl);
	}

	private static string Kernel(string expression)
	{
		return "(function() " + LuaJobKernelScripts.Kernel + "\nreturn " + expression + " end)()";
	}

	private static LuaScriptError Acknowledge(string[] ids, string[] tracked)
	{
		JsonCopy copy = CopyJson(LuaToolRuntime.BuildSource(LuaJobKernelScripts.StateAcknowledge,
			[OwnNamespace, ids, tracked]));
		Assert.NotNull(copy.Error);
		Assert.Equal("not_started", copy.Error.HostEffect);
		return copy.Error;
	}
}
