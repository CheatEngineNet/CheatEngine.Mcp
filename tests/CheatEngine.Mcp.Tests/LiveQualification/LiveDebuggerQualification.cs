using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>
/// Bounded Windows-debugger evidence against the disposable target. It deliberately avoids breakpoints, stepping,
/// capture and tracing because the ordinary live fixture has no stable instruction-level workload for those cases.
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
			using Process target = Process.GetProcessById(sandbox.HostA.TargetProcessId);
			HashSet<int> externalThreadIds = target.Threads.Cast<ProcessThread>().Select(thread => thread.Id).ToHashSet();
			Assert.NotEmpty(externalThreadIds);

			JsonNode listed = await CallAsync(instance, CheatEngineToolNames.ProcessListThreads);
			Assert.False(listed["truncated"]!.GetValue<bool>());
			long threadId = SelectOwnedThreadId(
				listed["threads"]!.AsArray().Select(thread => thread!.GetValue<long>()), externalThreadIds);

			JsonNode before = await CallAsync(instance, CheatEngineToolNames.DebuggerGetStatus);
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

			JsonNode ready = await CallAsync(instance, CheatEngineToolNames.DebuggerGetStatus);
			Assert.True(ready["stateValid"]!.GetValue<bool>());
			Assert.True(ready["attached"]!.GetValue<bool>());
			Assert.True(ready["canBreak"]!.GetValue<bool>());
			Assert.False(ready["broken"]!.GetValue<bool>());
			Assert.Equal("windows", Text(ready, "activeInterface"));

			JsonNode requested = await CallAsync(instance, CheatEngineToolNames.DebuggerBreakThread,
				new Dictionary<string, object?> { ["threadId"] = threadId });
			Assert.Equal(threadId, requested["threadId"]!.GetValue<long>());
			Assert.True(requested["requested"]!.GetValue<bool>());

			JsonNode stopped = await WaitForStatusAsync(instance, status =>
				status["attached"]!.GetValue<bool>() && status["broken"]!.GetValue<bool>());
			Assert.True(stopped["reportedBroken"]!.GetValue<bool>());
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
			string instructionPointer = expected64Bit ? "RIP" : "EIP";
			string stackPointer = expected64Bit ? "RSP" : "ESP";
			ulong instructionPointerAddress = ParseHex(Text(registers, instructionPointer));
			Assert.True(instructionPointerAddress > 0);
			string expectedStackPointer = Text(registers, stackPointer);
			Assert.True(ParseHex(expectedStackPointer) > 0);
			sandbox.Record("all_tools_debugger_get_context", context.DeepClone());

			JsonNode stack = await CallAsync(instance, CheatEngineToolNames.DebuggerGetStackTrace,
				new Dictionary<string, object?> { ["depth"] = 8 });
			Assert.Equal(expectedStackPointer, Text(stack, "stackPointer"), ignoreCase: true);
			Assert.Equal(expected64Bit ? 8 : 4, stack["pointerSize"]!.GetValue<int>());
			Assert.Equal(8, stack["scannedSlots"]!.GetValue<int>());
			ModuleRange[] moduleRanges = ReadModuleRanges(target);
			Assert.NotEmpty(moduleRanges);
			Assert.True(IsAddressInModules(instructionPointerAddress, moduleRanges),
				"The broken context did not stop in a module independently enumerated from the owned target.");
			foreach (JsonNode frame in stack["frames"]!.AsArray().OfType<JsonNode>())
			{
				Assert.True(frame["isHeuristic"]!.GetValue<bool>());
				Assert.True(ParseHex(Text(frame, "stackAddress")) > 0);
				ulong returnAddress = ParseHex(Text(frame, "returnAddress"));
				Assert.True(returnAddress > 0);
				Assert.Contains("call", Text(frame, "callInstruction"), StringComparison.OrdinalIgnoreCase);
				// A stack frame is explicitly heuristic and may point at dynamic code, so its module membership is
				// observed but never treated as an invariant of the production tool.
				_ = IsAddressInModules(returnAddress, moduleRanges);
			}
			sandbox.Record("all_tools_debugger_get_stack_trace", stack.DeepClone());

			JsonNode continued = await CallAsync(instance, CheatEngineToolNames.DebuggerContinue);
			Assert.Equal("run", Text(continued, "mode"));
			Assert.True(continued["continued"]!.GetValue<bool>());
			JsonNode resumed = await WaitForStatusAsync(instance, status =>
				status["attached"]!.GetValue<bool>() && !status["broken"]!.GetValue<bool>());
			Assert.False(resumed["reportedBroken"]!.GetValue<bool>());
			sandbox.Record("all_tools_debugger_continue", new
			{
				continued = continued.DeepClone(),
				resumed = resumed.DeepClone()
			});

			JsonNode detached = await CallAsync(instance, CheatEngineToolNames.DebuggerDetach);
			Assert.Equal(sandbox.HostA.TargetProcessId, detached["processId"]!.GetValue<int>());
			Assert.True(detached["wasAttached"]!.GetValue<bool>());
			Assert.True(detached["detached"]!.GetValue<bool>());
			JsonNode after = await CallAsync(instance, CheatEngineToolNames.DebuggerGetStatus);
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
				instructionPointerInOwnedModule = true,
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
				await RestoreDebuggerAsync(instance, cleanup);
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

	internal static long SelectOwnedThreadId(IEnumerable<long> reportedThreadIds, IEnumerable<int> externalThreadIds)
	{
		ArgumentNullException.ThrowIfNull(reportedThreadIds);
		ArgumentNullException.ThrowIfNull(externalThreadIds);
		HashSet<long> external = externalThreadIds.Where(id => id > 0).Select(static id => (long) id).ToHashSet();
		long selected = reportedThreadIds.Where(id => id > 0 && external.Contains(id)).OrderBy(id => id).FirstOrDefault();
		return selected > 0
			? selected
			: throw new InvalidOperationException(
				"Cheat Engine did not report a thread belonging to the independently observed owned target.");
	}

	internal static bool IsAddressInModules(ulong address, IEnumerable<ModuleRange> modules)
	{
		ArgumentNullException.ThrowIfNull(modules);
		return modules.Any(module => address >= module.Start && address < module.EndExclusive);
	}

	internal readonly record struct ModuleRange(ulong Start, ulong EndExclusive);

	private static async Task RestoreDebuggerAsync(LiveMcpInstanceClient instance, List<Exception> cleanup)
	{
		bool attached = true;
		try
		{
			using CancellationTokenSource statusDeadline = new(TimeSpan.FromSeconds(10));
			JsonNode status = await CallAsync(instance.WithCancellation(statusDeadline.Token),
				CheatEngineToolNames.DebuggerGetStatus);
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
			JsonNode afterDetach = await CallAsync(instance.WithCancellation(readbackDeadline.Token),
				CheatEngineToolNames.DebuggerGetStatus);
			Assert.True(afterDetach["stateValid"]!.GetValue<bool>());
			Assert.False(afterDetach["attached"]!.GetValue<bool>());
			Assert.False(afterDetach["broken"]!.GetValue<bool>());
		}
		catch (Exception exception)
		{
			cleanup.Add(exception);
		}
	}

	private static async Task<JsonNode> WaitForStatusAsync(LiveMcpInstanceClient instance, Func<JsonNode, bool> predicate)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		JsonNode? last = null;
		while (stopwatch.Elapsed < BreakDeadline)
		{
			last = await CallAsync(instance, CheatEngineToolNames.DebuggerGetStatus);
			Assert.True(last["stateValid"]!.GetValue<bool>(),
				last["error"]?.GetValue<string>() ?? "The debugger returned an invalid state without an error message.");
			if (predicate(last))
			{
				return last;
			}

			await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
		}

		throw new TimeoutException($"The debugger did not reach the expected state within {BreakDeadline.TotalSeconds} seconds. Last status: {last}.");
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
