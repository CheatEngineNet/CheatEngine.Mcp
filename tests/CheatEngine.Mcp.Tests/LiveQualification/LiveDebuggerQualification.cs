using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.LiveTarget;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>
/// Bounded Windows-debugger evidence against the disposable target's published native execution loop.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class LiveDebuggerQualification
{
	private static readonly TimeSpan BreakDeadline = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		ArgumentNullException.ThrowIfNull(instance);

		bool attachAttempted = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			LiveDebuggerExecutionQualification.Manifest loop = sandbox.HostA.DebuggerExecution
				?? throw new InvalidOperationException("The debugger scenario requires the owned advancing thread manifest.");
			loop.Validate(sandbox.HostA.TargetProcessId);
			using Process target = Process.GetProcessById(sandbox.HostA.TargetProcessId);
			HashSet<int> externalThreadIds = target.Threads.Cast<ProcessThread>().Select(thread => thread.Id).ToHashSet();
			Assert.NotEmpty(externalThreadIds);

			JsonNode listed = await CallAsync(instance, CheatEngineToolNames.ProcessListThreads);
			Assert.False(listed["truncated"]!.GetValue<bool>());
			long threadId = SelectOwnedThreadId(
				listed["threads"]!.AsArray().Select(thread => thread!.GetValue<long>()), externalThreadIds, loop.ThreadId);
			sandbox.Record("debugger_thread_admission", new
			{
				threadId,
				externalThreadIds = externalThreadIds.Order().ToArray(),
				reportedThreads = listed.DeepClone(),
				loop
			});
			await LiveDebuggerExecutionQualification.RequireHeartbeatAsync(sandbox, instance,
				loop.ProgressAddress, loop.HitCounterAddress, "basic_before_attach");

			JsonNode before = await WaitForStatusAsync(sandbox, instance, "initial", status =>
				!status["attached"]!.GetValue<bool>() && !status["broken"]!.GetValue<bool>());
			Assert.True(before["stateValid"]!.GetValue<bool>());
			Assert.False(before["attached"]!.GetValue<bool>());
			Assert.False(before["broken"]!.GetValue<bool>());

			attachAttempted = true;
			JsonNode attached = await CallAsync(instance, CheatEngineToolNames.DebuggerAttach,
				new Dictionary<string, object?> { ["interface"] = "windows" });
			Assert.True(attached["attached"]!.GetValue<bool>());
			Assert.False(attached["alreadyAttached"]!.GetValue<bool>());
			Assert.Equal(sandbox.HostA.TargetProcessId, attached["processId"]!.GetValue<int>());
			Assert.Equal("windows", Text(attached, "requestedInterface"));
			Assert.Equal("windows", Text(attached, "activeInterface"));
			Assert.False(attached["usedFallback"]!.GetValue<bool>());
			sandbox.Record("all_tools_debugger_attach", attached.DeepClone());

			JsonNode ready = await WaitForStatusAsync(sandbox, instance, "ready", status =>
				status["attached"]!.GetValue<bool>() && !status["broken"]!.GetValue<bool>());
			Assert.True(ready["stateValid"]!.GetValue<bool>());
			Assert.True(ready["attached"]!.GetValue<bool>());
			Assert.False(ready["broken"]!.GetValue<bool>());
			Assert.Equal("windows", Text(ready, "activeInterface"));

			JsonNode requested = await CallAsync(instance, CheatEngineToolNames.DebuggerBreakThread,
				new Dictionary<string, object?> { ["threadId"] = threadId });
			Assert.Equal(threadId, requested["threadId"]!.GetValue<long>());
			Assert.True(requested["requested"]!.GetValue<bool>());
			sandbox.Record("debugger_break_request_observed", requested.DeepClone());

			JsonNode stopped = await WaitForStatusAsync(sandbox, instance, "stopped", status =>
				status["attached"]!.GetValue<bool>() && status["broken"]!.GetValue<bool>());
			sandbox.Record("all_tools_debugger_break_thread", new
			{
				requested = requested.DeepClone(),
				stopped = stopped.DeepClone()
			});
			sandbox.Record("all_tools_debugger_get_status", stopped.DeepClone());

			JsonNode context = await CallAsync(instance, CheatEngineToolNames.DebuggerGetContext,
				new Dictionary<string, object?> { ["includeExtraRegisters"] = false });
			bool expected64Bit = sandbox.TargetArchitecture == "x64";
			Assert.Equal(expected64Bit, context["is64Bit"]!.GetValue<bool>());
			JsonObject registers = context["registers"]!.AsObject();
			Assert.Equal((ulong) loop.ThreadId, ParseHex(Text(registers, "THREADID")));
			string instructionPointer = expected64Bit ? "RIP" : "EIP";
			string stackPointer = expected64Bit ? "RSP" : "ESP";
			ulong instructionPointerAddress = ParseHex(Text(registers, instructionPointer));
			Assert.True(instructionPointerAddress > 0);
			string expectedStackPointer = Text(registers, stackPointer);
			Assert.True(ParseHex(expectedStackPointer) > 0);
			sandbox.Record("all_tools_debugger_get_context", context.DeepClone());

			JsonNode stack = await CallAsync(instance, CheatEngineToolNames.DebuggerGetStackTrace,
				new Dictionary<string, object?> { ["depth"] = 128 });
			sandbox.Record("debugger_stack_trace_observed", stack.DeepClone());
			Assert.Equal(expectedStackPointer, Text(stack, "stackPointer"), ignoreCase: true);
			Assert.Equal(expected64Bit ? 8 : 4, stack["pointerSize"]!.GetValue<int>());
			Assert.Equal(128, stack["scannedSlots"]!.GetValue<int>());
			ModuleRange[] moduleRanges = ReadModuleRanges(target);
			Assert.NotEmpty(moduleRanges);
			Assert.True(IsAddressInModules(instructionPointerAddress, moduleRanges)
				|| (instructionPointerAddress >= loop.CodeStart && instructionPointerAddress < loop.CodeEnd),
				"The owned thread did not stop in its exact executable range or an independently enumerated module.");
			JsonArray frames = stack["frames"]!.AsArray();
			Assert.NotEmpty(frames);
			ulong stackStart = ParseHex(expectedStackPointer);
			foreach (JsonNode frame in frames.OfType<JsonNode>())
			{
				Assert.True(frame["isHeuristic"]!.GetValue<bool>());
				ulong slot = ParseHex(Text(frame, "stackAddress"));
				Assert.True(slot >= stackStart && slot < checked(stackStart + (128 * 8)) && (slot - stackStart) % 8 == 0);
				ulong returnAddress = ParseHex(Text(frame, "returnAddress"));
				Assert.True(returnAddress > 0);
				Assert.Contains("call", Text(frame, "callInstruction"), StringComparison.OrdinalIgnoreCase);
				// A stack frame is explicitly heuristic and may point at dynamic code, so its module membership is
				// observed but never treated as an invariant of the production tool.
				_ = IsAddressInModules(returnAddress, moduleRanges);
			}
			// Match the return after the encoded Sleep call, including the loop's pause-handshake prefix.
			Assert.Contains(frames, frame => frame is not null &&
				ParseHex(Text(frame, "returnAddress")) == checked(loop.CodeStart + DebuggerExecutionLoopEncoding.SleepReturnOffset));
			sandbox.Record("all_tools_debugger_get_stack_trace", stack.DeepClone());

			JsonNode continued = await CallAsync(instance, CheatEngineToolNames.DebuggerContinue);
			Assert.Equal("run", Text(continued, "mode"));
			Assert.True(continued["continued"]!.GetValue<bool>());
			JsonNode resumed = await WaitForStatusAsync(sandbox, instance, "resumed", status =>
				status["attached"]!.GetValue<bool>() && !status["broken"]!.GetValue<bool>());
			sandbox.Record("all_tools_debugger_continue", new
			{
				continued = continued.DeepClone(),
				resumed = resumed.DeepClone()
			});

			JsonNode detached = await CallAsync(instance, CheatEngineToolNames.DebuggerDetach);
			Assert.Equal(sandbox.HostA.TargetProcessId, detached["processId"]!.GetValue<int>());
			Assert.True(detached["wasAttached"]!.GetValue<bool>());
			Assert.True(detached["detached"]!.GetValue<bool>());
			JsonNode after = await WaitForStatusAsync(sandbox, instance, "detached", status =>
				!status["attached"]!.GetValue<bool>() && !status["broken"]!.GetValue<bool>());
			Assert.True(after["stateValid"]!.GetValue<bool>());
			Assert.False(after["attached"]!.GetValue<bool>());
			Assert.False(after["broken"]!.GetValue<bool>());
			sandbox.Record("all_tools_debugger_detach", detached.DeepClone());

			sandbox.Record("all_tools_debugger_windows", new
			{
				threadId,
				externalThreadIdentityMatched = true,
				attached = attached.DeepClone(),
				stopped = stopped.DeepClone(),
				context = context.DeepClone(),
				stack = stack.DeepClone(),
				continued = continued.DeepClone(),
				detached = detached.DeepClone(),
				instructionPointerInOwnedCodeOrModule = true,
				stackFramesInKnownModules = stack["frames"]!.AsArray().Count(frame =>
					frame is not null && IsAddressInModules(ParseHex(Text(frame, "returnAddress")), moduleRanges))
			});
		}
		catch (Exception exception)
		{
			failure = exception;
			throw;
		}
		finally
		{
			if (attachAttempted)
			{
				await RestoreDebuggerAsync(sandbox, instance, cleanup);
			}

			if (attachAttempted && cleanup.Count == 0)
			{
				sandbox.Record("live_debugger_windows_cleanup", new
				{
					attachAttempted,
					detachedAndReadBack = true
				});
			}
			ThrowCleanup(failure, cleanup);
		}
	}

	internal static long SelectOwnedThreadId(IEnumerable<long> reportedThreadIds, IEnumerable<int> externalThreadIds, int expectedThreadId)
	{
		ArgumentNullException.ThrowIfNull(reportedThreadIds);
		ArgumentNullException.ThrowIfNull(externalThreadIds);
		return expectedThreadId > 0 && externalThreadIds.Contains(expectedThreadId) && reportedThreadIds.Contains(expectedThreadId)
			? expectedThreadId
			: throw new InvalidOperationException(
				"Cheat Engine did not report the published thread in the independently observed owned target.");
	}

	internal static bool IsAddressInModules(ulong address, IEnumerable<ModuleRange> modules)
	{
		ArgumentNullException.ThrowIfNull(modules);
		return modules.Any(module => address >= module.Start && address < module.EndExclusive);
	}

	internal readonly record struct ModuleRange(ulong Start, ulong EndExclusive);

	private static async Task RestoreDebuggerAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance,
		List<Exception> cleanup)
	{
		bool attached = true;
		try
		{
			using CancellationTokenSource statusDeadline = new(TimeSpan.FromSeconds(10));
			JsonNode status = await WaitForStatusAsync(sandbox, instance.WithCancellation(statusDeadline.Token),
				"cleanup-initial", _ => true, includeTestCancellation: false, cancellationToken: statusDeadline.Token);
			attached = status["attached"]!.GetValue<bool>();
			if (attached && status["broken"]!.GetValue<bool>())
			{
				JsonNode continued = await CallAsync(instance.WithCancellation(statusDeadline.Token),
					CheatEngineToolNames.DebuggerContinue);
				Assert.True(continued["continued"]!.GetValue<bool>());
			}
		}
		catch (Exception exception)
		{
			// Detach itself resumes a stopped context, so still attempt it when status cannot be read.
			cleanup.Add(exception);
		}

		if (attached)
		{
			try
			{
				using CancellationTokenSource detachDeadline = new(TimeSpan.FromSeconds(10));
				JsonNode detached = await CallAsync(instance.WithCancellation(detachDeadline.Token),
					CheatEngineToolNames.DebuggerDetach);
				Assert.True(detached["detached"]!.GetValue<bool>());
			}
			catch (Exception exception)
			{
				cleanup.Add(exception);
			}
		}

		try
		{
			using CancellationTokenSource readbackDeadline = new(TimeSpan.FromSeconds(10));
			JsonNode afterDetach = await WaitForStatusAsync(sandbox, instance.WithCancellation(readbackDeadline.Token),
				"cleanup-detached", status => !status["attached"]!.GetValue<bool>() && !status["broken"]!.GetValue<bool>(),
				includeTestCancellation: false, cancellationToken: readbackDeadline.Token);
			Assert.True(afterDetach["stateValid"]!.GetValue<bool>());
			Assert.False(afterDetach["attached"]!.GetValue<bool>());
			Assert.False(afterDetach["broken"]!.GetValue<bool>());
		}
		catch (Exception exception)
		{
			cleanup.Add(exception);
		}
	}

	private static async Task<JsonNode> WaitForStatusAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance,
		string stage, Func<JsonNode, bool> predicate, bool includeTestCancellation = true,
		CancellationToken cancellationToken = default)
	{
		JsonNode? last = null;
		List<JsonNode> observations = [];
		int observationCount = 0;
		using CancellationTokenSource timeout = new(BreakDeadline);
		using CancellationTokenSource deadline = includeTestCancellation
			? CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken, cancellationToken, timeout.Token)
			: CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
		LiveMcpInstanceClient scoped = instance.WithCancellation(deadline.Token);
		try
		{
			while (true)
			{
				last = await CallAsync(scoped, CheatEngineToolNames.DebuggerGetStatus);
				observationCount++;
				if (observations.Count < 8)
				{
					observations.Add(last.DeepClone());
				}
				if (last["stateValid"]?.GetValue<bool>() == true && predicate(last))
				{
					return last;
				}

				await Task.Delay(PollInterval, deadline.Token);
			}
		}
		catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
		{
			throw new TimeoutException(
				$"The debugger did not reach '{stage}' within {BreakDeadline.TotalSeconds} seconds. Last status: {last}.", exception);
		}
		finally
		{
			sandbox.Record($"live_debugger_status_{stage}", new
			{
				observationCount,
				observations,
				last = last?.DeepClone()
			});
		}
	}

	private static ModuleRange[] ReadModuleRanges(Process process)
	{
		return [.. process.Modules.Cast<ProcessModule>()
			.Where(module => module.BaseAddress != IntPtr.Zero && module.ModuleMemorySize > 0)
			.Select(module =>
			{
				ulong start = unchecked((ulong) module.BaseAddress.ToInt64());
				return new ModuleRange(start, checked(start + (ulong) module.ModuleMemorySize));
			})];
	}

	private static ulong ParseHex(string value) => ulong.Parse(
		value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.AsSpan(2) : value.AsSpan(),
		NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
	private static string Text(JsonNode node, string property) => node[property]?.GetValue<string>() ??
		throw new InvalidDataException($"Missing string '{property}'.");
	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool,
		IReadOnlyDictionary<string, object?>? arguments = null) => await instance.CallToolAsync(tool, arguments) ??
		throw new InvalidDataException($"Tool '{tool}' returned no payload.");
	private static void ThrowCleanup(Exception? failure, List<Exception> cleanup)
	{
		if (cleanup.Count != 0)
		{
			throw failure is null ? new AggregateException(cleanup) : new AggregateException([failure, .. cleanup]);
		}
	}
}
