using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Diagnoses a breakpoint transition without polling context before an independently observed callback.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveDebuggerTransitionDiagnostic
{
	internal static async Task RunAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		LiveDebuggerExecutionQualification.Manifest manifest = sandbox.HostA.DebuggerExecution
			?? throw new InvalidDataException("The owned debugger execution manifest is missing.");
		manifest.Validate(sandbox.HostA.TargetProcessId);
		sandbox.Record("diagnosticOnly", true);
		ExceptionDispatchInfo? primary = null;
		List<Exception> cleanup = [];
		bool installAttempted = false;
		bool attachAttempted = false;
		bool breakpointAttempted = false;
		bool exactEventSeen = false;
		string address = manifest.Entry.ToString("X", CultureInfo.InvariantCulture);
		try
		{
			sandbox.Record("debugger_transition_memory_before_attach", sandbox.ReadDebuggerTransitionMemory("A"));
			await LiveDebuggerExecutionQualification.RequireHeartbeatAsync(sandbox, instance,
				manifest.ProgressAddress, manifest.HitCounterAddress, "transition_diagnostic_before_attach");
			installAttempted = true;
			await sandbox.DebuggerTransitionProbeAsync("A", "install", manifest.ThreadId, manifest.Entry);
			attachAttempted = true;
			JsonNode attached = await CallAsync(instance, CheatEngineToolNames.DebuggerAttach,
				new Dictionary<string, object?> { ["interface"] = "windows" });
			Assert.True(attached["attached"]!.GetValue<bool>());
			breakpointAttempted = true;
			JsonNode breakpoint = await CallAsync(instance, CheatEngineToolNames.DebuggerSetBreakpoint,
				new Dictionary<string, object?>
				{
					["address"] = address,
					["threadId"] = manifest.ThreadId,
					["trigger"] = "execute",
					["size"] = 1,
					["method"] = "int3",
					["oneShot"] = false
				});
			sandbox.Record("debugger_transition_diagnostic_breakpoint", breakpoint.DeepClone());
			sandbox.Record("debugger_transition_memory_after_set_breakpoint", sandbox.ReadDebuggerTransitionMemory("A"));
			Stopwatch elapsed = Stopwatch.StartNew();
			while (true)
			{
				LiveDebuggerTransitionReceipt receipt = await sandbox.DebuggerTransitionProbeAsync("A", "sample");
				sandbox.Record("debugger_transition_memory_sample", new
				{
					elapsedMilliseconds = elapsed.ElapsedMilliseconds,
					receipt.CallbackCount,
					receipt.EventCount,
					memory = sandbox.ReadDebuggerTransitionMemory("A")
				});
				if (receipt.EventCount == 1)
				{
					Assert.Equal(manifest.ThreadId, receipt.ThreadId);
					Assert.Equal(manifest.Entry, receipt.Rip);
					exactEventSeen = true;
					break;
				}
				if (elapsed.Elapsed > TimeSpan.FromSeconds(10))
				{
					throw new TimeoutException("The exact owned debugger callback event was not observed.");
				}
				await Task.Delay(50, TestContext.Current.CancellationToken);
			}
			using CancellationTokenSource contextDeadline = new(TimeSpan.FromSeconds(10));
			JsonNode context = await CallAsync(instance.WithCancellation(contextDeadline.Token), CheatEngineToolNames.DebuggerGetContext,
				new Dictionary<string, object?> { ["includeExtraRegisters"] = false });
			Assert.Equal((ulong) manifest.ThreadId,
				ParseHex(context["registers"]!["THREADID"]!.GetValue<string>()));
			Assert.Equal(manifest.Entry,
				ParseHex(context["registers"]!["RIP"]!.GetValue<string>()));
			sandbox.Record("debugger_transition_diagnostic_context_after_event", context.DeepClone());
		}
		catch (Exception exception) { primary = ExceptionDispatchInfo.Capture(exception); }
		finally
		{
			if (breakpointAttempted)
			{
				await LiveDebuggerExecutionQualification.DeleteAndProveAbsentDuringCleanupAsync(instance, address, cleanup);
			}
			if (installAttempted)
			{
				await TryCleanupAsync(async () => { await sandbox.DebuggerTransitionProbeAsync("A", "restore"); });
			}
			if (attachAttempted && exactEventSeen)
			{
				await TryCleanupAsync(async () =>
				{
					using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
					await CallAsync(instance.WithCancellation(deadline.Token), CheatEngineToolNames.DebuggerContinue);
				});
			}
			if (attachAttempted)
			{
				await TryCleanupAsync(async () =>
				{
					using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
					JsonNode detached = await CallAsync(instance.WithCancellation(deadline.Token), CheatEngineToolNames.DebuggerDetach);
					Assert.True(detached["detached"]!.GetValue<bool>());
				});
			}
		}
		if (cleanup.Count != 0)
		{
			throw primary is null ? new AggregateException(cleanup) : new AggregateException([primary.SourceException, .. cleanup]);
		}
		primary?.Throw();
		await LiveDebuggerExecutionQualification.RequireHeartbeatAsync(sandbox, instance,
			manifest.ProgressAddress, manifest.HitCounterAddress, "transition_diagnostic_after_detach");
		sandbox.Record("debugger_transition_diagnostic_completed", new
		{
			diagnosticOnly = true,
			contextRequestedOnlyAfterExactEvent = true
		});
		return;

		async Task TryCleanupAsync(Func<Task> action)
		{
			try
			{
				await action();
			}
			catch (Exception exception) { cleanup.Add(exception); }
		}
	}

	private static ulong ParseHex(string text) => ulong.Parse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text,
		NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool,
		IReadOnlyDictionary<string, object?>? arguments = null) => await instance.CallToolAsync(tool, arguments)
		?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
}
