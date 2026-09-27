using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Debugger;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using KernelPage = CheatEngine.Mcp.Core.Jobs.LuaJobPage;

namespace CheatEngine.Mcp.Tests.Tools.Debugger;

/// <summary>
///     The debugger v2 contract: input refusal before host work, feature gates, bounded Lua-job polling and release
///     failures that preserve their tracked breakpoint for recovery.
/// </summary>
public sealed class DebuggerToolsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(0)]
	[InlineData(3)]
	[InlineData(9)]
	public void SetBreakpoint_InvalidSize_RefusesBeforeLuaOrResourceTracking(int size)
	{
		StateTestHarness harness = new();
		DebuggerTools tools = CreateTools(harness);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.SetBreakpoint("game.exe+10", DebuggerBreakpointTrigger.Write, size,
				cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.Dispatches);
		Assert.Empty(harness.LuaCalls);
		Assert.Empty(harness.Resources.List());
	}

	[Fact]
	public void SetBreakpoint_Int3DataWatch_RefusesBeforeLuaOrResourceTracking()
	{
		StateTestHarness harness = new();
		DebuggerTools tools = CreateTools(harness);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.SetBreakpoint("game.exe+10", DebuggerBreakpointTrigger.Access, 4,
				DebuggerBreakpointMethod.Int3, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("method", exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, harness.Dispatches);
		Assert.Empty(harness.Resources.List());
	}

	[Theory]
	[InlineData("XMM0=1", "register")]
	[InlineData("RAX=not-hex", "stopCondition")]
	[InlineData("RAX=0123456789ABCDEF0", "stopCondition")]
	public void StartTrace_InvalidStopCondition_RefusesBeforeLuaJobReservation(string stopCondition, string parameter)
	{
		StateTestHarness harness = new();
		DebuggerTools tools = CreateTools(harness);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.StartTrace("game.exe+20", stopCondition: stopCondition, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(parameter, exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, harness.Dispatches);
		Assert.Equal(0, harness.Jobs.Count);
		Assert.Empty(harness.Resources.List());
	}

	[Theory]
	[InlineData(DebuggerInterface.Veh, "EnableTargetCodeExecution")]
	[InlineData(DebuggerInterface.Kernel, "EnableKernelAccess")]
	public void Attach_DisabledInterfaceCapability_RefusesBeforeDispatch(DebuggerInterface @interface,
		string setting)
	{
		RecordingDispatcher dispatcher = new();
		McpFeatureOptions features = new();
		if (@interface is DebuggerInterface.Veh)
		{
			features.EnableTargetCodeExecution = false;
		}
		else
		{
			features.EnableKernelAccess = false;
		}

		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		ToolDispatch dispatch = new(ClientTestDouble.Client(dispatcher.Dispatcher, CancellationToken.None),
			new McpFeatureGate(Options.Create(features)), execution, new DispatchStatistics(execution),
			TimeProvider.System, NullLogger<ToolDispatch>.Instance);
		TargetResources resources = new();
		DebuggerTools tools = new(dispatch, new JobRegistry(dispatch, resources, execution, TimeProvider.System),
			resources);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.Attach(@interface, Token));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains(setting, exception.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(0, dispatcher.Calls);
	}

	[Fact]
	public void Capture_PollThenStop_RetainsTypedHitsAndReleasesTheLuaJob()
	{
		StateTestHarness harness = new();
		DebuggerTools tools = CreateTools(harness);
		harness.Answer<DebuggerJobStarted>(_ => new DebuggerJobStarted("ignored", 42, "401010", 30));

		DebuggerJobStarted started = tools.StartCapture("game.exe+10", maximumHits: 2,
			lifetimeSeconds: 30, cancellationToken: Token);
		DebuggerCaptureContext context = new("401010", 17, "40100F", true, "mov [rax],ebx",
			new Dictionary<string, string> { ["RAX"] = "1" }, "7FF0");
		harness.Answer<KernelPage>(source =>
		{
			Assert.Contains(started.JobId, source, StringComparison.Ordinal);
			return new KernelPage(new JobStatus(started.JobId, "debugcapture", JobState.Running, 5,
					29995, 1, 1, 0),
				[Json(new DebuggerCaptureItem(context, 1))], 1, 1, false, 0);
		});

		DebuggerCapturePage page = tools.PollCapture(started.JobId, limit: 1, cancellationToken: Token);
		harness.Answer<LuaJobStop>(_ => new LuaJobStop(true, true, true, "stopped"));
		JobStopResult stopped = harness.Jobs.Stop(started.JobId, Token);

		DebuggerCaptureItem hit = Assert.Single(page.Hits);
		Assert.Equal((started.JobId, JobState.Running, 1L, 1L, false, 0L),
			(page.Job.JobId, page.Job.State, page.FirstSequence, page.NextAfterSequence, page.More,
				page.Dropped));
		Assert.Equal(("401010", 17L, "1"),
			(hit.Context.Ip, hit.Context.ThreadId, hit.Context.Registers["RAX"]));
		Assert.Equal((started.JobId, true, false), (stopped.JobId, stopped.Released, stopped.AlreadyReleased));
		Assert.Empty(harness.Resources.List());
		Assert.Equal(0, harness.Jobs.Count);
		Assert.True(harness.Jobs.Stop(started.JobId, Token).AlreadyReleased);
		Assert.Equal([
			CheatEngineToolNames.DebuggerStartCapture, CheatEngineToolNames.DebuggerPollCapture,
			"mcp_job_stop"
		], harness.LuaCalls.Select(static call => call.Operation));
	}

	[Fact]
	public void DeleteBreakpoint_CleanupUnconfirmed_ReportsPartialEffectAndKeepsTheTrackedBreakpoint()
	{
		StateTestHarness harness = new();
		DebuggerTools tools = CreateTools(harness);
		harness.Answer<DebuggerBreakpointSet>(_ => new DebuggerBreakpointSet("returned-id", "401000",
			DebuggerBreakpointTrigger.Execute, 1, DebuggerBreakpointMethod.Default, null, false));

		tools.SetBreakpoint("game.exe+10", cancellationToken: Token);
		TargetResourceDescriptor tracked = Assert.Single(harness.Resources.List());
		harness.Answer<DebuggerBreakpointDeleted>(_ =>
			new DebuggerBreakpointDeleted("returned-id", "401000", false, "removal was refused"));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.DeleteBreakpoint("game.exe+10", Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.CleanupUnconfirmed, false),
			(exception.Error.Kind, exception.Error.HostEffect, exception.Error.Retryable));
		Assert.Equal("returned-id", exception.Error.Details!.Value.GetProperty("resourceId").GetString());
		Assert.Equal("The recorded cleanup did not complete.",
			exception.Error.Details!.Value.GetProperty("cleanupError").GetString());
		Assert.DoesNotContain("removal was refused", exception.Error.Details!.Value.GetRawText(), StringComparison.Ordinal);
		Assert.Contains("manual recovery", exception.Error.Hint, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(tracked, Assert.Single(harness.Resources.List()));
		Assert.Equal([
				CheatEngineToolNames.DebuggerSetBreakpoint,
				CheatEngineToolNames.DebuggerDeleteBreakpoint
			],
			harness.LuaCalls.Select(static call => call.Operation));
	}

	[Fact]
	public void FixedScripts_TrackOnlyOwnedBreakpointsAndBoundJobCleanup()
	{
		Assert.Contains("pcall(resourceRecord, a[1], a[2], 'breakpoint'", DebuggerLuaScripts.SetBreakpoint,
			StringComparison.Ordinal);
		Assert.Contains("resourceRelease(matched, 'deleted')", DebuggerLuaScripts.DeleteBreakpoint,
			StringComparison.Ordinal);
		Assert.Contains("job.onStop", DebuggerLuaScripts.StartCapture, StringComparison.Ordinal);
		Assert.Contains("debug_removeBreakpointByID(current.breakpointId)", DebuggerLuaScripts.StartCapture,
			StringComparison.Ordinal);
		Assert.Contains("_G.debugger_onBreakpoint = nil", DebuggerLuaScripts.StartTrace,
			StringComparison.Ordinal);
		Assert.DoesNotContain("${", DebuggerLuaScripts.SetBreakpoint, StringComparison.Ordinal);
		Assert.DoesNotContain("${", DebuggerLuaScripts.StartCapture, StringComparison.Ordinal);
		Assert.DoesNotContain("${", DebuggerLuaScripts.StartTrace, StringComparison.Ordinal);
	}

	private static DebuggerTools CreateTools(StateTestHarness harness)
	{
		return new DebuggerTools(harness.Dispatch, harness.Jobs, harness.Resources);
	}

	private static JsonElement Json(DebuggerCaptureItem value)
	{
		return JsonSerializer.SerializeToElement(value, DebuggerJsonContext.Default.DebuggerCaptureItem);
	}
}
