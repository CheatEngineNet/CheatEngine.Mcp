using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
internal static class LiveDebuggerExecutionQualification
{
	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);

	internal sealed record Manifest(int ProcessId, int ThreadId, ulong CodeStart, ulong Entry, ulong RunTo, ulong CodeEnd,
		string ProgressAddress, string HitCounterAddress)
	{
		internal void Validate(int expectedProcessId)
		{
			if (ProcessId != expectedProcessId || ThreadId <= 0 || CodeStart == 0 || CodeEnd != checked(CodeStart + 55) ||
				LiveDebuggerExecutionQualification.ParseHex(HitCounterAddress) != checked(CodeStart + 0x104) || LiveDebuggerExecutionQualification.ParseHex(ProgressAddress) != checked(CodeStart + 0x108) ||
				Entry != checked(CodeStart + 9) || RunTo != checked(CodeStart + 23))
			{
				throw new InvalidDataException("The owned debugger execution manifest is inconsistent.");
			}
		}
	}

	internal static Manifest ReadManifest(JsonNode value, int processId)
	{
		Manifest result = new(processId, value["threadId"]!.GetValue<int>(), ParseHex(Text(value, "codeStart")),
			ParseHex(Text(value, "entryAddress")), ParseHex(Text(value, "runToAddress")), ParseHex(Text(value, "codeEnd")),
			Text(value, "progressAddress"), Text(value, "hitCounterAddress"));
		result.Validate(processId);
		return result;
	}

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, Manifest manifest)
	{
		manifest.Validate(sandbox.HostA.TargetProcessId);
		ExceptionDispatchInfo? primary = null;
		List<Exception> cleanup = [];
		HashSet<string> baselineJobs = await JobIdsAsync(instance);
		HashSet<string> possibleJobs = [];
		HashSet<string> ownedJobIds = [];
		HashSet<string> possibleBreakpointAddresses = [];
		HashSet<string> ownedBreakpointAddresses = [];
		bool attachAttempted = false;
		try
		{
			using Process target = Process.GetProcessById(manifest.ProcessId);
			Assert.Contains(manifest.ThreadId, target.Threads.Cast<ProcessThread>().Select(thread => thread.Id));
			JsonNode listedThreads = await Call(instance, CheatEngineToolNames.ProcessListThreads);
			Assert.Contains((long) manifest.ThreadId, listedThreads["threads"]!.AsArray().Select(thread => thread!.GetValue<long>()));
			await RequireHeartbeatAsync(sandbox, instance, manifest.ProgressAddress, manifest.HitCounterAddress, "before_attach");

			attachAttempted = true;
			JsonNode attach = await Call(instance, CheatEngineToolNames.DebuggerAttach,
				new Dictionary<string, object?> { ["interface"] = "windows" });
			Assert.True(attach["attached"]!.GetValue<bool>());

			possibleBreakpointAddresses.Add(Hex(manifest.Entry));
			ownedBreakpointAddresses.Add(Hex(manifest.Entry));
			JsonNode entry = await SetBreakpointAsync(instance, Hex(manifest.Entry), manifest.ThreadId);
			Assert.Equal(Hex(manifest.Entry), Text(entry, "address"), ignoreCase: true);
			Assert.Equal((long) manifest.ThreadId, entry["threadId"]!.GetValue<long>());
			await RequireOwnedBreakpointAsync(instance, Hex(manifest.Entry), Text(entry, "resourceId"));
			await WaitForStoppedAsync(sandbox, instance, manifest, manifest.Entry, "entry-stop");

			JsonNode initialContext = await Call(instance, CheatEngineToolNames.DebuggerGetContext,
				new Dictionary<string, object?> { ["includeExtraRegisters"] = false });
			long originalRax = unchecked((long) ParseHex(Text(initialContext["registers"]!, "RAX")));
			const long marker = 0x11223344556677;
			JsonNode written = await Call(instance, CheatEngineToolNames.DebuggerSetRegister,
				new Dictionary<string, object?> { ["register"] = "RAX", ["value"] = marker });
			Assert.True(written["verified"]!.GetValue<bool>());
			Assert.Equal("RAX", Text(written, "contextRegister"));
			Assert.Equal(marker.ToString("X", CultureInfo.InvariantCulture), Text(written, "value"));
			await RequireRegisterAsync(instance, "RAX", marker);
			JsonNode restored = await Call(instance, CheatEngineToolNames.DebuggerSetRegister,
				new Dictionary<string, object?> { ["register"] = "RAX", ["value"] = originalRax });
			Assert.True(restored["verified"]!.GetValue<bool>());
			await RequireRegisterAsync(instance, "RAX", originalRax);

			await DeleteAndProveAbsentAsync(instance, Hex(manifest.Entry));
			possibleBreakpointAddresses.Remove(Hex(manifest.Entry));
			possibleBreakpointAddresses.Add(Hex(manifest.RunTo));
			ownedBreakpointAddresses.Add(Hex(manifest.RunTo));
			JsonNode runToBreakpoint = await SetBreakpointAsync(instance, Hex(manifest.RunTo), manifest.ThreadId);
			await RequireOwnedBreakpointAsync(instance, Hex(manifest.RunTo), Text(runToBreakpoint, "resourceId"));
			await ContinueAndWaitAsync(sandbox, instance, manifest, manifest.RunTo, "nop-stop");
			await DeleteAndProveAbsentAsync(instance, Hex(manifest.RunTo));
			possibleBreakpointAddresses.Remove(Hex(manifest.RunTo));
			JsonNode step = await Call(instance, CheatEngineToolNames.DebuggerStep,
				new Dictionary<string, object?> { ["mode"] = "into" });
			Assert.Equal("into", Text(step, "mode"));
			await WaitForStoppedAsync(sandbox, instance, manifest, checked(manifest.RunTo + 1), "step-stop");
			JsonNode afterStep = await Call(instance, CheatEngineToolNames.DebuggerGetContext,
				new Dictionary<string, object?> { ["includeExtraRegisters"] = false });
			Assert.Equal(Hex(checked(manifest.RunTo + 1)), Text(afterStep["registers"]!, "RIP"), ignoreCase: true);

			possibleBreakpointAddresses.Add(Hex(manifest.Entry));
			ownedBreakpointAddresses.Add(Hex(manifest.Entry));
			JsonNode entryAgain = await SetBreakpointAsync(instance, Hex(manifest.Entry), manifest.ThreadId);
			await ContinueAndWaitAsync(sandbox, instance, manifest, manifest.Entry, "entry-for-run-to");
			await DeleteAndProveAbsentAsync(instance, Hex(manifest.Entry));
			possibleBreakpointAddresses.Remove(Hex(manifest.Entry));
			JsonNode runTo = await Call(instance, CheatEngineToolNames.DebuggerRunTo,
				new Dictionary<string, object?> { ["address"] = Hex(manifest.RunTo), ["lifetimeSeconds"] = 10 });
			possibleJobs.Add(Text(runTo, "jobId"));
			ownedJobIds.Add(Text(runTo, "jobId"));
			Assert.Equal(manifest.ProcessId, runTo["processId"]!.GetValue<int>());
			await RequireRunToJobAsync(instance, Text(runTo, "jobId"));
			Assert.Equal(Hex(manifest.RunTo), Text(runTo, "address"), ignoreCase: true);
			await WaitForStoppedAsync(sandbox, instance, manifest, manifest.RunTo, "run-to-stop");
			sandbox.Record("all_tools_debugger_execution_first_group", new
			{
				entry,
				written,
				restored,
				step,
				runTo
			});
		}
		catch (Exception exception) { primary = ExceptionDispatchInfo.Capture(exception); }
		finally
		{
			// Receipt loss recovery discovers only jobs created after the pre-operation snapshot.
			try
			{
				using CancellationTokenSource discoveryDeadline = new(Deadline);
				possibleJobs.UnionWith(await NewRunToJobsAsync(instance.WithCancellation(discoveryDeadline.Token), baselineJobs));
				ownedJobIds.UnionWith(possibleJobs);
			}
			catch (Exception exception) { cleanup.Add(exception); }
			foreach (string jobId in possibleJobs)
			{
				await StopJobAndProveAbsentAsync(instance, jobId, cleanup);
			}

			foreach (string address in possibleBreakpointAddresses)
			{
				await DeleteAndProveAbsentDuringCleanupAsync(instance, address, cleanup);
			}

			if (attachAttempted)
			{
				await RestoreAsync(instance, cleanup);
			}

		}

		if (cleanup.Count != 0)
		{
			throw primary is null ? new AggregateException(cleanup) : new AggregateException([primary.SourceException, .. cleanup]);
		}

		primary?.Throw();
		await RequireDetachedAsync(instance);
		await RequireHeartbeatAsync(sandbox, instance, manifest.ProgressAddress, manifest.HitCounterAddress, "after_detach");
		sandbox.Record("live_debugger_execution_cleanup", new
		{
			stoppedOwnedJobIds = ownedJobIds.Order(StringComparer.Ordinal).ToArray(),
			ownedBreakpointAddresses = ownedBreakpointAddresses.Order(StringComparer.Ordinal).ToArray(),
			detached = true,
			heartbeatAdvanced = true
		});
	}

	private static async Task RequireDetachedAsync(LiveMcpInstanceClient instance)
	{
		using CancellationTokenSource deadline = new(Deadline);
		JsonNode status = await Call(instance.WithCancellation(deadline.Token), CheatEngineToolNames.DebuggerGetStatus);
		Assert.False(status["attached"]!.GetValue<bool>());
	}

	private static async Task<JsonNode> SetBreakpointAsync(LiveMcpInstanceClient instance, string address, int threadId)
	{
		return await Call(instance, CheatEngineToolNames.DebuggerSetBreakpoint, new Dictionary<string, object?>
		{
			["address"] = address,
			["trigger"] = "execute",
			["size"] = 1,
			["method"] = "int3",
			["threadId"] = threadId,
			["oneShot"] = false
		});
	}

	private static async Task RequireOwnedBreakpointAsync(LiveMcpInstanceClient instance, string address, string resourceId)
	{
		JsonNode page = await Call(instance, CheatEngineToolNames.DebuggerListBreakpoints,
			new Dictionary<string, object?> { ["limit"] = 1024 });
		Assert.False(page["truncated"]!.GetValue<bool>());
		JsonObject item = Assert.IsType<JsonObject>(Assert.Single(page["breakpoints"]!.AsArray(), candidate => candidate is not null &&
			string.Equals(Text(candidate, "address"), address, StringComparison.OrdinalIgnoreCase)));
		Assert.True(item["owned"]!.GetValue<bool>());
		Assert.Equal(resourceId, Text(item, "resourceId"));
	}

	private static async Task DeleteAndProveAbsentAsync(LiveMcpInstanceClient instance, string address)
	{
		JsonNode deleted = await Call(instance, CheatEngineToolNames.DebuggerDeleteBreakpoint,
			new Dictionary<string, object?> { ["address"] = address });
		Assert.True(deleted["released"]!.GetValue<bool>());
		JsonNode page = await Call(instance, CheatEngineToolNames.DebuggerListBreakpoints,
			new Dictionary<string, object?> { ["limit"] = 1024 });
		Assert.False(page["truncated"]!.GetValue<bool>());
		Assert.DoesNotContain(page["breakpoints"]!.AsArray(), candidate => candidate is not null &&
			string.Equals(Text(candidate, "address"), address, StringComparison.OrdinalIgnoreCase) && candidate["owned"]!.GetValue<bool>());
	}

	private static async Task ContinueAndWaitAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, Manifest manifest,
		ulong expectedIp, string stage)
	{
		JsonNode continued = await Call(instance, CheatEngineToolNames.DebuggerContinue);
		Assert.True(continued["continued"]!.GetValue<bool>());
		await WaitForStoppedAsync(sandbox, instance, manifest, expectedIp, stage);
	}

	private static async Task WaitForStoppedAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, Manifest manifest,
		ulong? expectedIp, string stage)
	{
		using CancellationTokenSource deadline = new(Deadline);
		List<JsonNode> observations = [];
		try
		{
			while (true)
			{
				JsonNode status = await Call(instance.WithCancellation(deadline.Token), CheatEngineToolNames.DebuggerGetStatus);
				if (observations.Count < 8)
				{
					observations.Add(status.DeepClone());
				}

				if (status["stateValid"]?.GetValue<bool>() == true && status["attached"]!.GetValue<bool>() && status["broken"]!.GetValue<bool>())
				{
					JsonNode context = await Call(instance.WithCancellation(deadline.Token), CheatEngineToolNames.DebuggerGetContext,
						new Dictionary<string, object?> { ["includeExtraRegisters"] = false });
					JsonNode registers = context["registers"]!;
					ulong ip = ParseHex(Text(registers, "RIP"));
					ulong thread = ParseHex(Text(registers, "THREADID"));
					if (ip >= manifest.CodeStart && ip < manifest.CodeEnd && thread == (ulong) manifest.ThreadId &&
						(!expectedIp.HasValue || ip == expectedIp.Value))
					{
						return;
					}
				}
				await Task.Delay(Poll, deadline.Token);
			}
		}
		finally { sandbox.Record($"debugger_execution_{stage}", observations); }
	}

	internal static async Task RequireHeartbeatAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string progressAddress,
		string hitAddress, string stage)
	{
		JsonNode firstProgress = await ReadInt32Async(instance, progressAddress);
		JsonNode firstHit = await ReadInt32Async(instance, hitAddress);
		await Task.Delay(Poll, TestContext.Current.CancellationToken);
		JsonNode secondProgress = await ReadInt32Async(instance, progressAddress);
		JsonNode secondHit = await ReadInt32Async(instance, hitAddress);
		sandbox.Record($"debugger_execution_heartbeat_{stage}", new
		{
			firstProgress,
			firstHit,
			secondProgress,
			secondHit
		});
		Assert.True(ReadInt32(secondProgress) > ReadInt32(firstProgress) && ReadInt32(secondHit) > ReadInt32(firstHit),
			"The dedicated native execution loop did not advance both counters.");
	}
	private static Task<JsonNode> ReadInt32Async(LiveMcpInstanceClient instance, string address) => Call(instance,
		CheatEngineToolNames.MemoryRead, new Dictionary<string, object?> { ["address"] = address, ["valueType"] = "int32" });
	private static long ReadInt32(JsonNode value) => long.Parse(Text(value, "value"), CultureInfo.InvariantCulture);

	private static async Task<HashSet<string>> JobIdsAsync(LiveMcpInstanceClient instance)
	{
		JsonNode jobs = await Call(instance, CheatEngineToolNames.RuntimeListJobs);
		return [.. jobs["jobs"]!.AsArray().Where(job => job is not null).Select(job => Text(job!, "jobId"))];
	}

	private static async Task RequireRegisterAsync(LiveMcpInstanceClient instance, string name, long expected)
	{
		JsonNode context = await Call(instance, CheatEngineToolNames.DebuggerGetContext,
			new Dictionary<string, object?> { ["includeExtraRegisters"] = false });
		Assert.Equal(unchecked((ulong) expected).ToString("X", CultureInfo.InvariantCulture),
			Text(context["registers"]!, name));
	}

	private static async Task RequireRunToJobAsync(LiveMcpInstanceClient instance, string jobId)
	{
		JsonNode jobs = await Call(instance, CheatEngineToolNames.RuntimeListJobs);
		JsonNode job = Assert.Single(jobs["jobs"]!.AsArray(), candidate => candidate is not null && Text(candidate, "jobId") == jobId)!;
		Assert.Equal("debugrunto", Text(job, "kind"));
	}

	private static async Task<HashSet<string>> NewRunToJobsAsync(LiveMcpInstanceClient instance, HashSet<string> baseline)
	{
		JsonNode jobs = await Call(instance, CheatEngineToolNames.RuntimeListJobs);
		return [.. jobs["jobs"]!.AsArray().Where(job => job is not null && !baseline.Contains(Text(job, "jobId")) &&
			Text(job, "kind") == "debugrunto").Select(job => Text(job!, "jobId"))];
	}

	private static async Task StopJobAndProveAbsentAsync(LiveMcpInstanceClient instance, string jobId, List<Exception> cleanup)
	{
		Exception? uncertain = null;
		try
		{
			using CancellationTokenSource deadline = new(Deadline);
			LiveMcpInstanceClient scoped = instance.WithCancellation(deadline.Token);
			JsonNode stopped = await Call(scoped, CheatEngineToolNames.RuntimeStopJob, new Dictionary<string, object?> { ["jobId"] = jobId });
			Assert.True(stopped["released"]!.GetValue<bool>());
		}
		catch (Exception exception) { uncertain = exception; }
		try
		{
			using CancellationTokenSource deadline = new(Deadline);
			JsonNode jobs = await Call(instance.WithCancellation(deadline.Token), CheatEngineToolNames.RuntimeListJobs);
			bool absent = !jobs["jobs"]!.AsArray().Any(job => job is not null && Text(job, "jobId") == jobId);
			if (!absent)
			{
				throw new InvalidOperationException($"Debugger run-to job '{jobId}' remained after cleanup.");
			}
		}
		catch (Exception exception) { cleanup.Add(uncertain is null ? exception : new AggregateException(uncertain, exception)); return; }
	}

	private static async Task DeleteAndProveAbsentDuringCleanupAsync(LiveMcpInstanceClient instance, string address,
		List<Exception> cleanup)
	{
		Exception? uncertain = null;
		try
		{
			using CancellationTokenSource deadline = new(Deadline);
			await Call(instance.WithCancellation(deadline.Token), CheatEngineToolNames.DebuggerDeleteBreakpoint,
				new Dictionary<string, object?> { ["address"] = address });
		}
		catch (Exception exception) { uncertain = exception; }
		try
		{
			using CancellationTokenSource deadline = new(Deadline);
			JsonNode page = await Call(instance.WithCancellation(deadline.Token), CheatEngineToolNames.DebuggerListBreakpoints,
				new Dictionary<string, object?> { ["limit"] = 1024 });
			Assert.False(page["truncated"]!.GetValue<bool>());
			bool absent = !page["breakpoints"]!.AsArray().Any(item => item is not null && item["owned"]!.GetValue<bool>() &&
				string.Equals(Text(item, "address"), address, StringComparison.OrdinalIgnoreCase));
			if (!absent)
			{
				throw new InvalidOperationException($"Owned breakpoint '{address}' remained after cleanup.");
			}
		}
		catch (Exception exception) { cleanup.Add(uncertain is null ? exception : new AggregateException(uncertain, exception)); }
	}

	private static async Task RestoreAsync(LiveMcpInstanceClient instance, List<Exception> cleanup)
	{
		List<Exception> failures = [];
		bool attached = true;
		bool broken = false;
		try
		{
			using CancellationTokenSource deadline = new(Deadline);
			JsonNode status = await Call(instance.WithCancellation(deadline.Token), CheatEngineToolNames.DebuggerGetStatus);
			attached = status["attached"]!.GetValue<bool>();
			broken = status["broken"]!.GetValue<bool>();
		}
		catch (Exception exception) { failures.Add(exception); }

		if (attached && broken)
		{
			try
			{
				using CancellationTokenSource deadline = new(Deadline);
				await Call(instance.WithCancellation(deadline.Token), CheatEngineToolNames.DebuggerContinue);
			}
			catch (Exception exception) { failures.Add(exception); }
		}

		if (attached)
		{
			try
			{
				using CancellationTokenSource deadline = new(Deadline);
				await Call(instance.WithCancellation(deadline.Token), CheatEngineToolNames.DebuggerDetach);
			}
			catch (Exception exception) { failures.Add(exception); }
		}

		try
		{
			using CancellationTokenSource deadline = new(Deadline);
			while (true)
			{
				JsonNode status = await Call(instance.WithCancellation(deadline.Token), CheatEngineToolNames.DebuggerGetStatus);
				if (!status["attached"]!.GetValue<bool>())
				{
					break;
				}

				await Task.Delay(Poll, deadline.Token);
			}
		}
		catch (Exception exception) { failures.Add(exception); }

		cleanup.AddRange(failures);
	}

	private static async Task<JsonNode> Call(LiveMcpInstanceClient c, string t, IReadOnlyDictionary<string, object?>? a = null) =>
		await c.CallToolAsync(t, a) ?? throw new InvalidDataException($"{t} returned no payload.");
	private static string Hex(ulong value) => value.ToString("X", CultureInfo.InvariantCulture);
	private static ulong ParseHex(string text)
	{
		ReadOnlySpan<char> value = text.AsSpan();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			value = value[2..];
		}

		return ulong.Parse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
	}
	private static string Text(JsonNode n, string p) => n[p]?.GetValue<string>() ?? throw new InvalidDataException($"Missing {p}.");
}
