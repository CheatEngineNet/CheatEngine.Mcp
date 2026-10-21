using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
internal static partial class LiveDebuggerExecutionQualification
{
	internal static async Task VerifyRemainingAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, Manifest manifest)
	{
		LiveMcpInstanceClient cleanupClient = instance;
		using CancellationTokenSource overall = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		overall.CancelAfter(TimeSpan.FromSeconds(90));
		instance = instance.WithCancellation(overall.Token);
		manifest.Validate(sandbox.HostA.TargetProcessId);
		using Process target = Process.GetProcessById(manifest.ProcessId);
		Assert.Contains(manifest.ThreadId, target.Threads.Cast<ProcessThread>().Select(thread => thread.Id));
		JsonNode threads = await Call(instance, CheatEngineToolNames.ProcessListThreads);
		Assert.Contains((long) manifest.ThreadId, threads["threads"]!.AsArray().Select(thread => thread!.GetValue<long>()));
		await RequireNoBreakpointAtAsync(instance, manifest.Entry);
		HashSet<string> baseline = await JobIdsAsync(instance);
		Assert.Empty(baseline);
		HashSet<string> pendingJobs = [];
		HashSet<string> allJobIds = [];
		HashSet<ulong> eventBreakpointsNeedingProof = [];
		List<Exception> cleanup = [];
		ExceptionDispatchInfo? primary = null;
		bool attachAttempted = false;
		bool ignoreAttempted = false;
		bool ignoreRestored = false;
		bool breakpointAttempted = false;
		bool jobAttempted = false;
		bool hookAbsenceProved = false;
		bool detached = false;
		bool entryBreakpointAbsent = false;
		bool heartbeatAdvanced = false;
		try
		{
			attachAttempted = true;
			JsonNode attached = await Call(instance, CheatEngineToolNames.DebuggerAttach,
				new Dictionary<string, object?> { ["interface"] = "windows" });
			Assert.True(attached["attached"]!.GetValue<bool>());
			ignoreAttempted = true;
			JsonNode ignored = await SetIgnoredAsync(instance, manifest.ThreadId, true);
			breakpointAttempted = true;
			JsonNode breakpoint = await SetBreakpointAsync(instance, Hex(manifest.Entry), manifest.ThreadId);
			await RequireOwnedBreakpointAsync(instance, Hex(manifest.Entry), Text(breakpoint, "resourceId"));
			await RequireHeartbeatAsync(sandbox, instance, manifest.ProgressAddress, manifest.HitCounterAddress, "while_thread_ignored");
			JsonNode running = await Call(instance, CheatEngineToolNames.DebuggerGetStatus);
			Assert.True(running["stateValid"]!.GetValue<bool>());
			Assert.True(running["attached"]!.GetValue<bool>());
			Assert.False(running["broken"]!.GetValue<bool>());
			JsonNode unignored = await SetIgnoredAsync(instance, manifest.ThreadId, false);
			ignoreRestored = true;
			await WaitForStoppedAsync(sandbox, instance, manifest, manifest.Entry, "unignored_entry");
			await DeleteAndProveAbsentAsync(instance, Hex(manifest.Entry));
			breakpointAttempted = false;
			await Call(instance, CheatEngineToolNames.DebuggerContinue);
			await RequireHeartbeatAsync(sandbox, instance, manifest.ProgressAddress, manifest.HitCounterAddress, "after_thread_unignored");
			sandbox.Record("all_tools_debugger_set_thread_ignored", new
			{
				ignored,
				running,
				unignored,
				exactOwnedStopObserved = true
			});

			jobAttempted = true;
			eventBreakpointsNeedingProof.Add(manifest.Entry);
			JsonNode capture = await Call(instance, CheatEngineToolNames.DebuggerStartCapture,
				new Dictionary<string, object?>
				{
					["address"] = Hex(manifest.Entry),
					["trigger"] = "execute",
					["size"] = 1,
					["aggregateByInstruction"] = true,
					["maximumHits"] = 8,
					["lifetimeSeconds"] = 10
				});
			string captureId = Remember(capture);
			AssertEventStart(capture, manifest, captureId);
			JsonNode captured = await PollEventsAsync(sandbox, instance, CheatEngineToolNames.DebuggerPollCapture, captureId,
				page => page["hits"]!.AsArray().Count == 1 && page["hits"]![0]!["hitCount"]!.GetValue<long>() >= 2);
			AssertCapturePage(captured, manifest, captureId);
			sandbox.Record("all_tools_debugger_start_capture", capture.DeepClone());
			sandbox.Record("all_tools_debugger_poll_capture", captured.DeepClone());
			await ReleaseKnownJobAsync(captureId);
			await ProveEventBreakpointAbsentAsync(instance, manifest.Entry);
			await RequireHeartbeatAsync(sandbox, instance, manifest.ProgressAddress, manifest.HitCounterAddress, "after_capture_release");

			eventBreakpointsNeedingProof.Add(manifest.Entry);
			JsonNode trace = await StartTraceAsync(3);
			string traceId = Remember(trace);
			AssertEventStart(trace, manifest, traceId);
			JsonNode traced = await PollEventsAsync(sandbox, instance, CheatEngineToolNames.DebuggerPollTrace, traceId,
				page => Text(page["job"]!, "state") == "completed");
			AssertTracePage(traced, manifest, traceId, 3);
			await WaitForStoppedAsync(sandbox, instance, manifest, manifest.RunTo, "trace_three_stop");
			sandbox.Record("all_tools_debugger_start_trace", trace.DeepClone());
			sandbox.Record("all_tools_debugger_poll_trace", traced.DeepClone());
			await ReleaseKnownJobAsync(traceId);
			await Call(instance, CheatEngineToolNames.DebuggerContinue);
			await RequireHeartbeatAsync(sandbox, instance, manifest.ProgressAddress, manifest.HitCounterAddress, "after_trace_release");
			await ProveEventBreakpointAbsentAsync(instance, manifest.Entry);

			// This distinct two-context trace is a cleanup oracle, after the prior job was released and execution resumed.
			// The second context proves the trace reaches terminal cleanup from a single-step callback, after the entry
			// breakpoint callback has returned. Successful admission requires the previous global hook to be absent.
			eventBreakpointsNeedingProof.Add(manifest.Entry);
			JsonNode probe = await StartTraceAsync(2);
			string probeId = Remember(probe);
			AssertEventStart(probe, manifest, probeId);
			JsonNode probePage = await PollEventsAsync(sandbox, instance, CheatEngineToolNames.DebuggerPollTrace, probeId,
				page => Text(page["job"]!, "state") == "completed");
			AssertTracePage(probePage, manifest, probeId, 2);
			await WaitForStoppedAsync(sandbox, instance, manifest, checked(manifest.Entry + 7), "trace_hook_probe_stop");
			await ReleaseKnownJobAsync(probeId);
			await Call(instance, CheatEngineToolNames.DebuggerContinue);
			await RequireHeartbeatAsync(sandbox, instance, manifest.ProgressAddress, manifest.HitCounterAddress,
				"after_trace_hook_probe_release");
			await ProveEventBreakpointAbsentAsync(instance, manifest.Entry);
			hookAbsenceProved = true;
			sandbox.Record("debugger_event_hook_cleanup_probe", new
			{
				probe,
				probePage
			});
		}
		catch (Exception exception) { primary = ExceptionDispatchInfo.Capture(exception); }
		finally
		{
			if (jobAttempted)
			{
				try
				{
					using CancellationTokenSource deadline = new(Deadline);
					JsonNode jobs = await Call(cleanupClient.WithCancellation(deadline.Token), CheatEngineToolNames.RuntimeListJobs);
					foreach (JsonNode? job in jobs["jobs"]!.AsArray())
					{
						if (job is not null && !baseline.Contains(Text(job, "jobId")) && Text(job, "kind") is "debugcapture" or "debugtrace")
						{
							pendingJobs.Add(Text(job, "jobId"));
							allJobIds.Add(Text(job, "jobId"));
						}
					}
				}
				catch (Exception exception) { cleanup.Add(exception); }
				foreach (string jobId in pendingJobs)
				{
					await StopJobAndProveAbsentAsync(cleanupClient, jobId, cleanup);
				}
			}
			if (eventBreakpointsNeedingProof.Count != 0 && attachAttempted)
			{
				try
				{
					using CancellationTokenSource deadline = new(Deadline);
					LiveMcpInstanceClient resumeClient = cleanupClient.WithCancellation(deadline.Token);
					JsonNode status = await Call(resumeClient, CheatEngineToolNames.DebuggerGetStatus);
					if (status["attached"]!.GetValue<bool>() && status["broken"]!.GetValue<bool>())
					{
						await Call(resumeClient, CheatEngineToolNames.DebuggerContinue);
					}
				}
				catch (Exception exception) { cleanup.Add(exception); }
			}
			foreach (ulong address in eventBreakpointsNeedingProof.ToArray())
			{
				try
				{
					await ProveEventBreakpointAbsentAsync(cleanupClient, address);
				}
				catch (Exception exception) { cleanup.Add(exception); }
			}
			if (breakpointAttempted)
			{
				await DeleteAndProveAbsentDuringCleanupAsync(cleanupClient, Hex(manifest.Entry), cleanup);
			}
			if (ignoreAttempted)
			{
				try
				{
					using CancellationTokenSource deadline = new(Deadline);
					await SetIgnoredAsync(cleanupClient.WithCancellation(deadline.Token), manifest.ThreadId, false);
					ignoreRestored = true;
				}
				catch (Exception exception) { cleanup.Add(exception); }
			}
			if (attachAttempted)
			{
				await RestoreAsync(cleanupClient, cleanup);
				using CancellationTokenSource finalDeadline = new(Deadline);
				LiveMcpInstanceClient finalClient = cleanupClient.WithCancellation(finalDeadline.Token);
				try
				{
					await RequireDetachedAsync(finalClient);
					detached = true;
				}
				catch (Exception exception) { cleanup.Add(exception); }
				try
				{
					await RequireNoBreakpointAtAsync(finalClient, manifest.Entry);
					entryBreakpointAbsent = true;
				}
				catch (Exception exception) { cleanup.Add(exception); }
				try
				{
					await RequireHeartbeatAsync(sandbox, finalClient, manifest.ProgressAddress, manifest.HitCounterAddress,
						"after_event_detach");
					heartbeatAdvanced = true;
				}
				catch (Exception exception) { cleanup.Add(exception); }
				sandbox.Record("live_debugger_event_cleanup", new
				{
					stoppedOwnedJobIds = allJobIds.Order(StringComparer.Ordinal).ToArray(),
					hookAbsenceProved,
					ignoredRestored = !ignoreAttempted || ignoreRestored,
					entryBreakpointAbsent,
					detached,
					heartbeatAdvanced
				});
			}
		}
		if (cleanup.Count != 0)
		{
			throw primary is null ? new AggregateException(cleanup) : new AggregateException([primary.SourceException, .. cleanup]);
		}
		primary?.Throw();
		return;

		string Remember(JsonNode start)
		{
			string id = Text(start, "jobId");
			pendingJobs.Add(id);
			allJobIds.Add(id);
			return id;
		}
		async Task ReleaseKnownJobAsync(string jobId)
		{
			List<Exception> failures = [];
			await StopJobAndProveAbsentAsync(instance, jobId, failures);
			if (failures.Count != 0)
			{
				throw new AggregateException(failures);
			}
			pendingJobs.Remove(jobId);
		}
		async Task ProveEventBreakpointAbsentAsync(LiveMcpInstanceClient proofClient, ulong address)
		{
			if (eventBreakpointsNeedingProof.Contains(address))
			{
				await WaitForNoBreakpointAtAsync(proofClient, address);
				eventBreakpointsNeedingProof.Remove(address);
			}
		}
		Task<JsonNode> StartTraceAsync(int count) => Call(instance, CheatEngineToolNames.DebuggerStartTrace,
			new Dictionary<string, object?> { ["address"] = Hex(manifest.Entry), ["mode"] = "into", ["maximumSteps"] = count, ["includeStack"] = true, ["lifetimeSeconds"] = 10 });
	}

	private static async Task<JsonNode> SetIgnoredAsync(LiveMcpInstanceClient instance, int threadId, bool ignored)
	{
		JsonNode result = await Call(instance, CheatEngineToolNames.DebuggerSetThreadIgnored,
			new Dictionary<string, object?> { ["threadId"] = threadId, ["ignored"] = ignored });
		Assert.Equal(((long) threadId, ignored), (result["threadId"]!.GetValue<long>(), result["ignored"]!.GetValue<bool>()));
		return result;
	}

	private static void AssertEventStart(JsonNode start, Manifest manifest, string jobId)
	{
		Assert.Equal((jobId, manifest.ProcessId, Hex(manifest.Entry), 10),
			(Text(start, "jobId"), start["processId"]!.GetValue<int>(), Text(start, "address"), start["lifetimeSeconds"]!.GetValue<int>()));
	}

	private static async Task<JsonNode> PollEventsAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string tool, string jobId, Func<JsonNode, bool> ready)
	{
		using CancellationTokenSource deadline = new(Deadline);
		while (true)
		{
			JsonNode page = await Call(instance.WithCancellation(deadline.Token), tool,
				new Dictionary<string, object?> { ["jobId"] = jobId, ["afterSequence"] = 0, ["limit"] = 8 });
			sandbox.Record("debugger_event_page_observed", new
			{
				tool,
				jobId,
				page = page.DeepClone()
			});
			Assert.Null(page["job"]!["error"]);
			Assert.Null(page["job"]!["cleanupError"]);
			Assert.True(Text(page["job"]!, "state") is "running" or "completed");
			if (ready(page))
			{
				return page;
			}
			await Task.Delay(Poll, deadline.Token);
		}
	}

	internal static void AssertCapturePage(JsonNode page, Manifest manifest, string jobId)
	{
		AssertEventJob(page, manifest, jobId, "debugcapture", "running", 1);
		JsonNode group = Assert.Single(page["hits"]!.AsArray())!;
		long hits = group["hitCount"]!.GetValue<long>();
		Assert.True(hits >= 2);
		Assert.Equal(hits, page["job"]!["progressDone"]!.GetValue<long>());
		string stack = AssertEventContext(group["context"]!, manifest, manifest.Entry, captureCallback: true);
		Assert.Equal(stack, AssertEventContext(group["firstContext"]!, manifest, manifest.Entry, captureCallback: true));
		Assert.Equal(stack, AssertEventContext(group["lastContext"]!, manifest, manifest.Entry, captureCallback: true));
	}

	internal static void AssertTracePage(JsonNode page, Manifest manifest, string jobId, int count)
	{
		Assert.True(count is 1 or 2 or 3);
		AssertEventJob(page, manifest, jobId, "debugtrace", "completed", count);
		Assert.Equal(((long) count, (long) count), (page["job"]!["progressDone"]!.GetValue<long>(), page["job"]!["progressTotal"]!.GetValue<long>()));
		JsonArray steps = page["steps"]!.AsArray();
		Assert.Equal(count, steps.Count);
		ulong[] addresses = [manifest.Entry, checked(manifest.Entry + 7), manifest.RunTo];
		string? stack = null;
		for (int index = 0; index < count; index++)
		{
			string current = AssertEventContext(steps[index]!, manifest, addresses[index]);
			if (stack is not null)
			{
				Assert.Equal(stack, current);
			}
			stack = current;
		}
	}

	private static void AssertEventJob(JsonNode page, Manifest manifest, string jobId, string kind, string state, long count)
	{
		JsonNode job = page["job"]!;
		Assert.Equal((jobId, kind, state, manifest.ProcessId), (Text(job, "jobId"), Text(job, "kind"), Text(job, "state"), job["processId"]!.GetValue<int>()));
		Assert.Equal((count, count, 0L), (job["buffered"]!.GetValue<long>(), job["total"]!.GetValue<long>(), job["dropped"]!.GetValue<long>()));
		Assert.Equal((1L, count, 0L), (page["firstSequence"]!.GetValue<long>(), page["nextAfterSequence"]!.GetValue<long>(), page["dropped"]!.GetValue<long>()));
		Assert.False(page["more"]!.GetValue<bool>());
		Assert.Null(job["error"]);
		Assert.Null(job["cleanupError"]);
		Assert.False(job["requiresManualRecovery"]!.GetValue<bool>());
	}

	private static string AssertEventContext(JsonNode context, Manifest manifest, ulong ip, bool captureCallback = false)
	{
		long reportedThreadId = context["threadId"]!.GetValue<long>();
		// CE's TLuaCaller.BreakpointEvent calls LUA_onBreakpoint(0, context, true), so capture callbacks cannot identify the TID.
		Assert.Equal(captureCallback ? 0L : manifest.ThreadId, reportedThreadId);
		Assert.Equal((ip, ip), (ParseHex(Text(context, "ip")), ParseHex(Text(context, "instructionAddress"))));
		Assert.False(context["isHeuristic"]!.GetValue<bool>());
		Assert.Equal(ip, ParseHex(Text(context["registers"]!, "RIP")));
		string stack = Text(context, "stackPointer");
		Assert.Equal(stack, Text(context["registers"]!, "RSP"));
		Assert.True(ParseHex(stack) > 0);
		return stack;
	}

	private static async Task RequireNoBreakpointAtAsync(LiveMcpInstanceClient instance, ulong address)
	{
		JsonNode page = await Call(instance, CheatEngineToolNames.DebuggerListBreakpoints, new Dictionary<string, object?> { ["limit"] = 1024 });
		Assert.False(page["truncated"]!.GetValue<bool>());
		Assert.False(HasBreakpointAt(page, address), $"A breakpoint remained at {Hex(address)}.");
	}

	private static async Task WaitForNoBreakpointAtAsync(LiveMcpInstanceClient instance, ulong address)
	{
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		deadline.CancelAfter(Deadline);
		JsonNode? last = null;
		try
		{
			while (true)
			{
				last = await Call(instance.WithCancellation(deadline.Token), CheatEngineToolNames.DebuggerListBreakpoints,
					new Dictionary<string, object?> { ["limit"] = 1024 });
				Assert.False(last["truncated"]!.GetValue<bool>());
				if (!HasBreakpointAt(last, address))
				{
					return;
				}

				await Task.Delay(Poll, deadline.Token);
			}
		}
		catch (OperationCanceledException exception) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
		{
			throw new InvalidOperationException(
				$"A breakpoint remained at {Hex(address)} after job cleanup. Last breakpoint page: {last?.ToJsonString() ?? "unavailable"}.",
				exception);
		}
	}

	internal static bool HasBreakpointAt(JsonNode page, ulong address) =>
		page["breakpoints"]!.AsArray().Any(item => item is not null && ParseHex(Text(item, "address")) == address);
}
