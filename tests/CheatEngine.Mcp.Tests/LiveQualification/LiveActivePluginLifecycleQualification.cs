using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>
///     Exercises disable/enable while the activation owns both a target-memory lease and a debugger callback. This is
///     deliberately separate from the clean three-cycle lifecycle qualification: each cleanup oracle is observed while
///     the old listener is down, before a replacement activation can hide it.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class LiveActivePluginLifecycleQualification
{
	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
	private const string AllocationName = "active-lifecycle-lease";

	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		LiveSandboxSession? sandbox = null;
		LiveLifecycleSession? connection = null;
		Exception? primary = null;
		List<Exception> cleanup = [];
		bool disableAttempted = false;
		bool disabled = false;
		bool reenabled = false;
		bool pauseRequested = false;
		bool debuggerAttached = false;
		bool allocationCreated = false;
		bool allocationFreedWhileDisabled = false;
		bool removalProbeInstalled = false;
		string? firstCaptureId = null;
		string? secondCaptureId = null;
		string? pauseAddress = null;
		ulong breakpointAddress = 0;
		try
		{
			sandbox = await LiveSandboxSession.StartAsync(inputs, LiveSandboxOptions.Smoke with
			{
				LifecycleQualification = true,
				SeedPluginChecklistAtStartup = true,
				ActivePluginLifecycleQualification = true,
				DebuggerQualification = true
			});
			connection = await LiveLifecycleSession.ConnectAsync(sandbox);
			await connection.AttachBothAsync();
			await connection.VerifyBothAsync();
			LiveMcpInstanceClient instance = connection.Gateway.Bind(sandbox.HostA.InstanceId);
			LiveDebuggerExecutionQualification.Manifest manifest = sandbox.HostA.DebuggerExecution
				?? throw new InvalidDataException("The active lifecycle target omitted its debugger loop manifest.");
			manifest.Validate(sandbox.HostA.TargetProcessId);
			pauseAddress = manifest.PauseAddress;
			breakpointAddress = manifest.Entry;

			JsonNode allocation = await Call(instance, CheatEngineToolNames.MemoryAllocate, new Dictionary<string, object?>
			{
				["name"] = AllocationName,
				["size"] = 4096,
				["executable"] = false
			});
			allocationCreated = true;
			RecordDiagnostic(sandbox, "plugin_lifecycle_active_allocation", allocation.DeepClone());
			ulong allocationAddress = ParseHex(Text(allocation, "address"));
			TargetMemoryProbe.MemoryObservation allocationObservation = TargetMemoryProbe.Inspect(sandbox.HostA.TargetProcessId,
				allocationAddress, sizeof(int));
			RecordDiagnostic(sandbox, "plugin_lifecycle_active_allocation_prewrite", new
			{
				address = Hex(allocationAddress),
				allocationObservation
			});
			JsonNode canary = await Call(instance, CheatEngineToolNames.MemoryWrite, new Dictionary<string, object?>
			{
				["address"] = Hex(allocationAddress),
				["valueType"] = "int32",
				["value"] = "1398030675",
				["verify"] = true
			});
			Assert.True(canary["verified"]!.GetValue<bool>());
			Assert.True(TargetMemoryProbe.IsCommittedAndCovering(sandbox.HostA.TargetProcessId, allocationAddress),
				"The active lifecycle allocation was not committed over its canary before disable.");
			debuggerAttached = true;
			JsonNode attached = await Call(instance, CheatEngineToolNames.DebuggerAttach,
				new Dictionary<string, object?> { ["interface"] = "windows" });
			Assert.True(attached["attached"]!.GetValue<bool>());
			JsonNode capture = await Call(instance, CheatEngineToolNames.DebuggerStartCapture, new Dictionary<string, object?>
			{
				["address"] = Hex(manifest.Entry),
				["trigger"] = "execute",
				["size"] = 1,
				["aggregateByInstruction"] = true,
				["maximumHits"] = 8,
				["lifetimeSeconds"] = 60
			});
			firstCaptureId = Text(capture, "jobId");
			await RequireCapturedHitsAsync(instance, firstCaptureId, 2);
			await RequireRunningCaptureAsync(instance, firstCaptureId);
			pauseRequested = true;
			await RequirePausedAsync(instance, manifest);
			await RequirePausedCountersStableAsync(instance, manifest);
			await RequireHealthyDebuggerAsync(instance);
			JsonNode jobsBeforeDisable = await Call(instance, CheatEngineToolNames.RuntimeListJobs);
			JsonNode resourcesBeforeDisable = await Call(instance, CheatEngineToolNames.RuntimeListResources);
			Assert.Contains(jobsBeforeDisable["jobs"]!.AsArray(), job => Text(job!, "jobId") == firstCaptureId);
			Assert.Contains(resourcesBeforeDisable["resources"]!.AsArray(), resource =>
				Text(resource!, "kind") == "debugcapture");
			RecordDiagnostic(sandbox, "plugin_lifecycle_active_pre_disable_owned_state", new
			{
				captureJobId = firstCaptureId,
				jobs = jobsBeforeDisable.DeepClone(),
				resources = resourcesBeforeDisable.DeepClone()
			});
			long hitsBeforeResume = await ReadCounterAsync(instance, manifest.HitCounterAddress);
			await SetPauseAsync(instance, manifest.PauseAddress, false);
			pauseRequested = false;
			long hitsAfterResume = await RequireCounterAdvanceAsync(instance, manifest, hitsBeforeResume);
			RecordDiagnostic(sandbox, "plugin_lifecycle_active_target_running_before_disable", new
			{
				hitsBeforeResume,
				hitsAfterResume
			});
			string oldEndpoint = sandbox.HostA.Endpoint;
			string oldInstance = sandbox.HostA.InstanceId;

			removalProbeInstalled = true;
			await sandbox.SetBreakpointRemovalProbeAsync("A", true, TestContext.Current.CancellationToken);
			disableAttempted = true;
			await sandbox.ReloadPluginAsync("A", false, TestContext.Current.CancellationToken);
			disabled = true;
			firstCaptureId = null;
			await connection.RequireInstancesAsync(sandbox.HostB.InstanceId);
			LivePluginDisabledObservation disabledObservation = await sandbox.ObservePluginDisabledAsync("A", manifest.Entry,
				TestContext.Current.CancellationToken);
			Assert.True(disabledObservation.CleanupRemovalSucceeded,
				"The native remover did not return boolean true successfully for the owned breakpoint.");
			Assert.False(disabledObservation.BreakpointPresent);
			await RequirePortReleasedAsync(oldEndpoint);
			Assert.True(TargetMemoryProbe.IsFree(sandbox.HostA.TargetProcessId, allocationAddress),
				"The old activation allocation remained committed after plugin disable.");
			allocationFreedWhileDisabled = true;
			await sandbox.SetBreakpointRemovalProbeAsync("A", false, TestContext.Current.CancellationToken);
			removalProbeInstalled = false;

			await sandbox.ReloadPluginAsync("A", true, TestContext.Current.CancellationToken);
			reenabled = true;
			disabled = false;
			await connection.RequireInstancesAsync(sandbox.HostA.InstanceId, sandbox.HostB.InstanceId);
			await connection.RejectStaleAsync(oldInstance);
			instance = connection.Gateway.Bind(sandbox.HostA.InstanceId);
			JsonNode jobs = await Call(instance, CheatEngineToolNames.RuntimeListJobs);
			Assert.Empty(jobs["jobs"]!.AsArray());
			JsonNode resources = await Call(instance, CheatEngineToolNames.RuntimeListResources);
			Assert.Empty(resources["resources"]!.AsArray());
			debuggerAttached = true;
			await Call(instance, CheatEngineToolNames.DebuggerAttach, new Dictionary<string, object?> { ["interface"] = "windows" });
			await RequireNoBreakpointAtAsync(instance, manifest.Entry);
			await SetPauseAsync(instance, manifest.PauseAddress, false);
			pauseRequested = false;
			JsonNode resumedCapture = await Call(instance, CheatEngineToolNames.DebuggerStartCapture, new Dictionary<string, object?>
			{
				["address"] = Hex(manifest.Entry),
				["trigger"] = "execute",
				["size"] = 1,
				["aggregateByInstruction"] = true,
				["maximumHits"] = 2,
				["lifetimeSeconds"] = 15
			});
			secondCaptureId = Text(resumedCapture, "jobId");
			await RequireCapturedHitsAsync(instance, secondCaptureId, 1);
			JsonNode releasedCapture = await Call(instance, CheatEngineToolNames.RuntimeStopJob,
				new Dictionary<string, object?> { ["jobId"] = secondCaptureId });
			Assert.True(releasedCapture["released"]!.GetValue<bool>());
			secondCaptureId = null;
			await RequireNoBreakpointAtAsync(instance, manifest.Entry);
			await Call(instance, CheatEngineToolNames.DebuggerDetach);
			debuggerAttached = false;
			await connection.VerifyBothAsync();
			sandbox.Record("plugin_lifecycle_active_completed", new
			{
				allocation,
				canary,
				capture,
				oldEndpoint,
				oldInstance,
				allocationFreedWhileDisabled,
				oldListenerReleasedWhileDisabled = true,
				pluginManagerDisabledObservation = disabledObservation,
				oldCallbackAbsentAfterReenable = true,
				resumedCapture,
				releasedCapture
			});
		}
		catch (Exception exception)
		{
			primary = exception;
		}
		finally
		{
			if (sandbox is not null && removalProbeInstalled)
			{
				try
				{
					await sandbox.SetBreakpointRemovalProbeAsync("A", false, CancellationToken.None);
					removalProbeInstalled = false;
				}
				catch (Exception exception) { cleanup.Add(exception); }
			}
			if (primary is not null && sandbox is not null && connection is not null && allocationCreated
				&& !allocationFreedWhileDisabled && !disableAttempted)
			{
				try
				{
					LiveMcpInstanceClient cleanupInstance = connection.Gateway.Bind(sandbox.HostA.InstanceId);
					JsonNode freed = await Call(cleanupInstance, CheatEngineToolNames.MemoryFree,
						new Dictionary<string, object?> { ["name"] = AllocationName });
					Assert.True(freed["release"]!["isComplete"]!.GetValue<bool>());
					RecordDiagnostic(sandbox, "plugin_lifecycle_active_allocation_failure_cleanup", new
					{
						attempted = true,
						freed
					});
				}
				catch (Exception exception)
				{
					cleanup.Add(exception);
					RecordDiagnostic(sandbox, "plugin_lifecycle_active_allocation_failure_cleanup", new
					{
						attempted = true,
						error = exception.ToString()
					});
				}
			}
			if (sandbox is not null && connection is not null && disableAttempted && !reenabled)
			{
				try
				{
					LivePluginDisabledObservation observed = await sandbox.ObservePluginStateAsync("A", breakpointAddress,
						TestContext.Current.CancellationToken);
					if (observed.PluginChecked)
					{
						sandbox.ClearPendingPluginIdentity("A");
					}
					else
					{
						await sandbox.ReloadPluginAsync("A", true, TestContext.Current.CancellationToken);
					}
					reenabled = true;
					disabled = false;
				}
				catch (Exception exception) { cleanup.Add(exception); }
			}
			if (connection is not null && sandbox is not null && (reenabled || !disableAttempted) && !disabled
				&& (pauseRequested || debuggerAttached || firstCaptureId is not null || secondCaptureId is not null))
			{
				try
				{
					LiveMcpInstanceClient cleanupInstance = connection.Gateway.Bind(sandbox.HostA.InstanceId);
					await CleanupDebuggerAsync(cleanupInstance, pauseAddress, pauseRequested, debuggerAttached, firstCaptureId, secondCaptureId, cleanup);
					pauseRequested = false;
					debuggerAttached = false;
					firstCaptureId = null;
					secondCaptureId = null;
				}
				catch (Exception exception) { cleanup.Add(exception); }
			}
			await LiveLifecycleQualification.DisposeOrderedAsync(primary,
				connection is null ? null : connection.DisposeAsync,
				null,
				sandbox is null ? null : () => { sandbox.MarkPassed(); return ValueTask.CompletedTask; },
				sandbox is null ? null : sandbox.DisposeAsync, cleanup);
		}
		if (cleanup.Count != 0)
		{
			throw primary is null ? new AggregateException(cleanup) : new AggregateException([primary, .. cleanup]);
		}
		if (primary is not null)
		{
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
		}
	}

	private static void RecordDiagnostic(LiveSandboxSession sandbox, string name, object value)
	{
		try
		{
			sandbox.Record(name, value);
		}
		catch { }
	}

	private static async Task RequireCapturedHitsAsync(LiveMcpInstanceClient instance, string jobId, long requiredHits)
	{
		using CancellationTokenSource timeout = new(Deadline);
		while (true)
		{
			JsonNode page = await Call(instance.WithCancellation(timeout.Token), CheatEngineToolNames.DebuggerPollCapture,
				new Dictionary<string, object?> { ["jobId"] = jobId, ["limit"] = 8 });
			if (page["hits"]!.AsArray().Any(hit => hit!["hitCount"]!.GetValue<long>() >= requiredHits))
			{
				return;
			}
			await Task.Delay(50, timeout.Token);
		}
	}

	private static async Task RequireRunningCaptureAsync(LiveMcpInstanceClient instance, string jobId)
	{
		JsonNode jobs = await Call(instance, CheatEngineToolNames.RuntimeListJobs);
		JsonNode job = Assert.Single(jobs["jobs"]!.AsArray(), candidate => candidate is not null && Text(candidate, "jobId") == jobId)!;
		Assert.Equal("debugcapture", Text(job, "kind"));
		Assert.Equal("running", Text(job, "state"));
	}

	private static async Task RequirePausedAsync(LiveMcpInstanceClient instance, LiveDebuggerExecutionQualification.Manifest manifest)
	{
		await SetPauseAsync(instance, manifest.PauseAddress, true);
		using CancellationTokenSource timeout = new(Deadline);
		while (true)
		{
			JsonNode acknowledged = await Call(instance.WithCancellation(timeout.Token), CheatEngineToolNames.MemoryRead,
				new Dictionary<string, object?> { ["address"] = manifest.PauseAcknowledgedAddress, ["valueType"] = "int32" });
			if (long.Parse(Text(acknowledged, "value"), System.Globalization.CultureInfo.InvariantCulture) > 0)
			{
				return;
			}
			await Task.Delay(50, timeout.Token);
		}
	}

	private static async Task RequirePausedCountersStableAsync(LiveMcpInstanceClient instance, LiveDebuggerExecutionQualification.Manifest manifest)
	{
		long hits = await ReadCounterAsync(instance, manifest.HitCounterAddress);
		long progress = await ReadCounterAsync(instance, manifest.ProgressAddress);
		await Task.Delay(100, TestContext.Current.CancellationToken);
		Assert.Equal(hits, await ReadCounterAsync(instance, manifest.HitCounterAddress));
		Assert.Equal(progress, await ReadCounterAsync(instance, manifest.ProgressAddress));
	}

	private static async Task<long> RequireCounterAdvanceAsync(LiveMcpInstanceClient instance,
		LiveDebuggerExecutionQualification.Manifest manifest, long initial)
	{
		using CancellationTokenSource timeout = new(Deadline);
		while (true)
		{
			long current = await ReadCounterAsync(instance, manifest.HitCounterAddress);
			if (current > initial)
			{
				return current;
			}
			if (timeout.IsCancellationRequested)
			{
				throw new TimeoutException("The owned target did not resume its heartbeat before the active-plugin disable check.");
			}
			await Task.Delay(50, timeout.Token);
		}
	}

	private static async Task RequireHealthyDebuggerAsync(LiveMcpInstanceClient instance)
	{
		JsonNode status = await Call(instance, CheatEngineToolNames.DebuggerGetStatus);
		Assert.True(status["stateValid"]!.GetValue<bool>());
		Assert.True(status["attached"]!.GetValue<bool>());
		Assert.False(status["broken"]!.GetValue<bool>());
		Assert.False(status["stepping"]!.GetValue<bool>());
	}

	private static async Task<long> ReadCounterAsync(LiveMcpInstanceClient instance, string address)
	{
		JsonNode value = await Call(instance, CheatEngineToolNames.MemoryRead, new Dictionary<string, object?> { ["address"] = address, ["valueType"] = "int32" });
		return long.Parse(Text(value, "value"), System.Globalization.CultureInfo.InvariantCulture);
	}

	private static async Task CleanupDebuggerAsync(LiveMcpInstanceClient instance, string? pauseAddress, bool pauseRequested,
		bool debuggerAttached, string? firstCaptureId, string? secondCaptureId, List<Exception> cleanup)
	{
		using CancellationTokenSource timeout = new(Deadline);
		foreach (string jobId in new[] { firstCaptureId, secondCaptureId }.Where(static id => id is not null).Cast<string>().Distinct(StringComparer.Ordinal))
		{
			try
			{
				await Call(instance.WithCancellation(timeout.Token), CheatEngineToolNames.RuntimeStopJob, new Dictionary<string, object?> { ["jobId"] = jobId });
			}
			catch (Exception exception) { cleanup.Add(exception); }
		}
		if (pauseRequested && pauseAddress is not null)
		{
			try
			{
				await SetPauseAsync(instance, pauseAddress, false);
			}
			catch (Exception exception) { cleanup.Add(exception); }
		}
		if (debuggerAttached)
		{
			try
			{
				await Call(instance, CheatEngineToolNames.DebuggerDetach);
			}
			catch (Exception exception) { cleanup.Add(exception); }
		}
	}

	private static async Task SetPauseAsync(LiveMcpInstanceClient instance, string pauseAddress, bool paused)
	{
		JsonNode write = await Call(instance, CheatEngineToolNames.MemoryWrite, new Dictionary<string, object?>
		{
			["address"] = pauseAddress,
			["valueType"] = "int32",
			["value"] = paused ? "1" : "0",
			["verify"] = true
		});
		Assert.True(write["verified"]!.GetValue<bool>());
	}

	private static async Task RequireNoBreakpointAtAsync(LiveMcpInstanceClient instance, ulong address)
	{
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		deadline.CancelAfter(TimeSpan.FromSeconds(8));
		JsonNode? last = null;
		while (true)
		{
			last = await Call(instance.WithCancellation(deadline.Token), CheatEngineToolNames.DebuggerListBreakpoints,
				new Dictionary<string, object?> { ["limit"] = 1024 });
			if (!last["breakpoints"]!.AsArray().Any(item => item is not null &&
				string.Equals(Text(item, "address"), Hex(address), StringComparison.OrdinalIgnoreCase)))
			{
				return;
			}

			await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token);
		}
	}

	private static async Task RequirePortReleasedAsync(string endpoint)
	{
		Uri uri = new(endpoint, UriKind.Absolute);
		Assert.True(uri.IsLoopback && uri.Port is > 0 and <= 65535, "The old backend endpoint is not a loopback TCP endpoint.");
		using CancellationTokenSource timeout = new(Deadline);
		while (true)
		{
			using TcpClient client = new();
			try
			{
				await client.ConnectAsync(uri.Host, uri.Port, timeout.Token);
			}
			catch (SocketException) { break; }
			if (timeout.IsCancellationRequested)
			{
				throw new TimeoutException("The old backend endpoint continued accepting TCP connections after plugin disable.");
			}
			await Task.Delay(50, timeout.Token);
		}
		IPAddress address = uri.HostNameType == UriHostNameType.IPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
		using TcpListener listener = new(address, uri.Port);
		listener.Server.ExclusiveAddressUse = true;
		listener.Start();
	}

	private static async Task<JsonNode> Call(LiveMcpInstanceClient instance, string tool, IReadOnlyDictionary<string, object?>? arguments = null)
	{
		JsonNode? result = await instance.CallToolAsync(tool, arguments);
		return result ?? throw new InvalidOperationException($"{tool} returned no structured result.");
	}

	private static string Text(JsonNode node, string name) => node[name]?.GetValue<string>()
		?? throw new InvalidDataException($"The result omitted {name}.");
	private static string Hex(ulong value) => $"0x{value:X}";
	internal static ulong ParseHex(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			throw new InvalidDataException("The allocation returned an empty hexadecimal address.");
		}

		string digits = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
		if (!ulong.TryParse(digits, System.Globalization.NumberStyles.AllowHexSpecifier,
			System.Globalization.CultureInfo.InvariantCulture, out ulong address) || address == 0)
		{
			throw new InvalidDataException("The allocation returned an invalid hexadecimal address.");
		}

		return address;
	}
}
