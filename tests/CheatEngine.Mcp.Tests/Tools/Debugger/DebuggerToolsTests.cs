using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Client;
using CheatEngine.Client.Processes;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Debugger;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit.Sdk;

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
	[InlineData(null, "nil", "nil")]
	[InlineData("rax=0x0000001a", "\"RAX\"", "\"1A\"")]
	[InlineData("IP = 00007FF612341000", "\"IP\"", "\"7FF612341000\"")]
	[InlineData("EFLAGS=000", "\"EFLAGS\"", "\"0\"")]
	public void StartTrace_StopCondition_ReachesTheBodyAsTheRegisterAndItsSignificantDigits(string? stopCondition,
		string register, string value)
	{
		StateTestHarness harness = new();
		DebuggerTools tools = CreateTools(harness);
		harness.Answer<DebuggerJobStarted>(_ => new DebuggerJobStarted("ignored", 42, "401010", 30));

		tools.StartTrace("game.exe+10", maximumSteps: 5, stopCondition: stopCondition, lifetimeSeconds: 30,
			cancellationToken: Token);

		// Every traced register is formatted without leading zeros, so the value is compared as a number.
		string source = Assert.Single(harness.LuaCalls, static call =>
			call.Operation == CheatEngineToolNames.DebuggerStartTrace).Source;
		Assert.Contains($"[7] = 5, [8] = {register}, [9] = {value}, [10] = false", source, StringComparison.Ordinal);
		Assert.Contains("if a[8] == nil then return false end", DebuggerLuaScripts.StartTrace,
			StringComparison.Ordinal);
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

	[Theory]
	[InlineData("veh", false, true, "VEH", "EnableTargetCodeExecution")]
	[InlineData("kernel", true, false, "kernel", "EnableKernelAccess")]
	public void Attach_DefaultThatSelectsAGatedInterface_IsRefusedBeforeCheatEngineAttaches(string configured,
		bool targetCodeExecution, bool kernelAccess, string name, string setting)
	{
		AttachHarness harness = new(new McpFeatureOptions
		{
			EnableTargetCodeExecution = targetCodeExecution,
			EnableKernelAccess = kernelAccess
		}, new LuaDebuggerDefaultInterface(false, configured));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.Attach(cancellationToken: Token));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted, false),
			(exception.Error.Kind, exception.Error.HostEffect, exception.Error.Retryable));
		Assert.Contains($"the {name} debugger", exception.Error.Message, StringComparison.Ordinal);
		Assert.Contains($"Mcp:{setting}", exception.Error.Message, StringComparison.Ordinal);
		Assert.Contains("interface windows", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Contains($"Mcp:{setting}", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Contains("getSettingsForm", Assert.Single(harness.Sources), StringComparison.Ordinal);
		Assert.Equal(0, harness.ProcessCalls);
	}

	[Theory]
	[InlineData("dbvm", true, true, "DBVM debugger")]
	[InlineData("dbvm", true, false, "DBVM debugger")]
	[InlineData("dbvm", false, false, "DBVM debugger")]
	[InlineData(null, true, true, "could not identify")]
	[InlineData(null, false, true, "could not identify")]
	[InlineData(null, true, false, "could not identify")]
	[InlineData("ceserver", true, true, "network debugger of the ceserver")]
	[InlineData("ceserver", false, false, "network debugger of the ceserver")]
	public void Attach_DefaultThatMcpCannotDriveOrIdentify_IsRefusedAsUnsupportedWhateverTheSwitches(
		string? configured, bool targetCodeExecution, bool kernelAccess, string reason)
	{
		AttachHarness harness = new(new McpFeatureOptions
		{
			EnableTargetCodeExecution = targetCodeExecution,
			EnableKernelAccess = kernelAccess
		}, new LuaDebuggerDefaultInterface(false, configured));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.Attach(cancellationToken: Token));

		Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.NotStarted, false),
			(exception.Error.Kind, exception.Error.HostEffect, exception.Error.Retryable));
		Assert.Contains(reason, exception.Error.Message, StringComparison.Ordinal);
		Assert.Contains("interface windows", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Contains("getSettingsForm", Assert.Single(harness.Sources), StringComparison.Ordinal);
		Assert.Equal(0, harness.ProcessCalls);
	}

	[Fact]
	public void Attach_DefaultWhileADebuggerIsAttached_LeavesTheInterfaceCheckToTheAttachBody()
	{
		// The attach body reports an attached debugger or refuses one that MCP cannot drive; NativeLua proves both.
		AttachHarness harness = new(new McpFeatureOptions
		{
			EnableTargetCodeExecution = false,
			EnableKernelAccess = false
		}, new LuaDebuggerDefaultInterface(true));

		DebuggerAttachment attachment = harness.Tools.Attach(cancellationToken: Token);

		string[] sources = [.. harness.Sources];
		Assert.Equal(2, sources.Length);
		Assert.Contains("getSettingsForm", sources[0], StringComparison.Ordinal);
		Assert.Contains("debugProcess(a[1])", sources[1], StringComparison.Ordinal);
		Assert.Equal(DebuggerInterface.Default, attachment.RequestedInterface);
	}

	[Theory]
	[InlineData("windows", false, false, false)]
	[InlineData("windows", true, true, false)]
	[InlineData("veh", false, true, true)]
	[InlineData("veh", true, true, false)]
	[InlineData("kernel", true, false, true)]
	[InlineData("kernel", true, true, false)]
	[InlineData("veh", true, false, false)]
	[InlineData("kernel", false, true, false)]
	public void Attach_DefaultThatTheSwitchesAllow_AttachesThroughCheatEnginesDefault(string configured,
		bool targetCodeExecution, bool kernelAccess, bool alreadyAttached)
	{
		AttachHarness harness = new(new McpFeatureOptions
		{
			EnableTargetCodeExecution = targetCodeExecution,
			EnableKernelAccess = kernelAccess
		}, new LuaDebuggerDefaultInterface(alreadyAttached, configured));

		DebuggerAttachment attachment = harness.Tools.Attach(cancellationToken: Token);

		Assert.Equal((DebuggerInterface.Default, 42), (attachment.RequestedInterface, attachment.ProcessId));
		string[] sources = [.. harness.Sources];
		Assert.Equal(2, sources.Length);
		Assert.Contains("getSettingsForm", sources[0], StringComparison.Ordinal);
		Assert.Contains("debugProcess(a[1])", sources[1], StringComparison.Ordinal);
		Assert.Contains("[1] = 0", sources[1], StringComparison.Ordinal);
		Assert.Equal(1, harness.ProcessCalls);
	}

	[Fact]
	public void Attach_WindowsWithEverySwitchOff_AttachesWithoutReadingCheatEngineSettings()
	{
		AttachHarness harness = new(new McpFeatureOptions
		{
			EnableTargetCodeExecution = false,
			EnableKernelAccess = false
		}, new LuaDebuggerDefaultInterface(false, "veh"));

		harness.Tools.Attach(DebuggerInterface.Windows, Token);

		string source = Assert.Single(harness.Sources);
		Assert.Contains("[1] = 1", source, StringComparison.Ordinal);
		Assert.Empty(LuaFeatureScan.Scan(DebuggerLuaScripts.Attach));
		Assert.Empty(LuaFeatureScan.Scan(DebuggerLuaScripts.DefaultInterface));
	}

	[Fact]
	public void AttachBody_RefusesACEServerConnectionBeforeMarkingVehOrAttaching()
	{
		// NativeLua proves the refusal; this pins it after the already-attached report and before every host effect.
		string body = DebuggerLuaScripts.Attach;
		int reported = body.IndexOf("if used then", StringComparison.Ordinal);
		int refused = body.IndexOf("isConnectedToCEServer()", StringComparison.Ordinal);
		int marked = body.IndexOf("veh[a[3]] = true", StringComparison.Ordinal);
		int attached = body.IndexOf("debugProcess(a[1])", StringComparison.Ordinal);

		Assert.True(reported >= 0 && reported < refused && refused < marked && marked < attached,
			$"Unexpected order: {reported}, {refused}, {marked}, {attached}.");
		Assert.Contains("pcall(isConnectedToCEServer)", DebuggerLuaScripts.DefaultInterface, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(DebuggerInterface.Windows)]
	[InlineData(DebuggerInterface.Veh)]
	[InlineData(DebuggerInterface.Kernel)]
	public void Attach_ExplicitInterface_NeverReadsCheatEngineSettings(DebuggerInterface @interface)
	{
		AttachHarness harness = new(new McpFeatureOptions(), new LuaDebuggerDefaultInterface(false, "dbvm"));

		harness.Tools.Attach(@interface, Token);

		string source = Assert.Single(harness.Sources);
		Assert.Contains("debugProcess(a[1])", source, StringComparison.Ordinal);
		Assert.Contains($"[1] = {(int) @interface}", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Detach_JobThatExpiredInLua_NoLongerBlocksOnceTheLedgerIsReRead()
	{
		StateTestHarness harness = new();
		DebuggerTools tools = CreateTools(harness);
		harness.Answer<DebuggerJobStarted>(_ => new DebuggerJobStarted("ignored", 42, "401010", 30));
		tools.StartCapture("game.exe+10", lifetimeSeconds: 30, cancellationToken: Token);
		Assert.Equal(TargetResourceState.Active, Assert.Single(harness.Resources.List()).State);
		harness.Answer<DebuggerDetached>(_ => new DebuggerDetached(42, true, false, true));

		// The Lua root no longer lists the job: its TTL sweeper removed it while the managed copy was not observed.
		DebuggerDetached detached = tools.Detach(Token);

		Assert.Equal(new DebuggerDetached(42, true, false, true), detached);
		Assert.Equal(TargetResourceState.Ended, Assert.Single(harness.Resources.List()).State);
		Assert.Equal(2, harness.Dispatches);
		Assert.Equal([
			CheatEngineToolNames.DebuggerStartCapture, "mcp_state_snapshot", CheatEngineToolNames.DebuggerDetach
		], harness.LuaCalls.Select(static call => call.Operation));
	}

	[Fact]
	public void Detach_BreakpointReleasedInLua_NoLongerBlocksOnceTheLedgerIsReRead()
	{
		StateTestHarness harness = new();
		DebuggerTools tools = CreateTools(harness);
		harness.Answer<DebuggerBreakpointSet>(_ => new DebuggerBreakpointSet("returned-id", "401000",
			DebuggerBreakpointTrigger.Execute, 1, DebuggerBreakpointMethod.Default, null, true));
		tools.SetBreakpoint("game.exe+10", oneShot: true, cancellationToken: Token);
		harness.Answer<DebuggerDetached>(_ => new DebuggerDetached(42, true, true, true));

		// The one-shot breakpoint fired and forgot its Lua entry.
		DebuggerDetached detached = tools.Detach(Token);

		Assert.True(detached.Detached);
		Assert.Equal(TargetResourceState.Ended, Assert.Single(harness.Resources.List()).State);
	}

	[Fact]
	public void Detach_JobStillRunningInLua_RefusesBeforeTheDetachBody()
	{
		StateTestHarness harness = new();
		DebuggerTools tools = CreateTools(harness);
		harness.Answer<DebuggerJobStarted>(_ => new DebuggerJobStarted("ignored", 42, "401010", 30));
		DebuggerJobStarted started = tools.StartCapture("game.exe+10", lifetimeSeconds: 30,
			cancellationToken: Token);
		harness.Answer(_ => new LuaStateSnapshot([StateTestHarness.Entry(started.JobId, "job", "running", true)], 1,
			false));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => tools.Detach(Token));

		Assert.Equal((ToolErrorKind.Busy, ToolHostEffect.NotStarted, true),
			(exception.Error.Kind, exception.Error.HostEffect, exception.Error.Retryable));
		Assert.Contains(started.JobId, exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(TargetResourceState.Active, Assert.Single(harness.Resources.List()).State);
		Assert.Equal([CheatEngineToolNames.DebuggerStartCapture, "mcp_state_snapshot"],
			harness.LuaCalls.Select(static call => call.Operation));
	}

	[Fact]
	public void Detach_JobCompletedInLua_NoLongerBlocks()
	{
		StateTestHarness harness = new();
		DebuggerTools tools = CreateTools(harness);
		harness.Answer<DebuggerJobStarted>(_ => new DebuggerJobStarted("ignored", 42, "401010", 30));
		DebuggerJobStarted started = tools.StartTrace("game.exe+10", lifetimeSeconds: 30, cancellationToken: Token);
		harness.Answer(_ => new LuaStateSnapshot([StateTestHarness.Entry(started.JobId, "job", "completed", false)],
			1, false));
		harness.Answer<DebuggerDetached>(_ => new DebuggerDetached(42, true, true, true));

		DebuggerDetached detached = tools.Detach(Token);

		Assert.True(detached.Detached);
		Assert.Equal(TargetResourceState.Ended, Assert.Single(harness.Resources.List()).State);
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

	[Theory]
	[InlineData(DebuggerBreakpointTrigger.Write)]
	[InlineData(DebuggerBreakpointTrigger.Access)]
	public void StartCapture_GroupByEffectiveAddressOnADataTrigger_RefusesBeforeLuaJobReservation(
		DebuggerBreakpointTrigger trigger)
	{
		StateTestHarness harness = new();
		DebuggerTools tools = CreateTools(harness);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.StartCapture("game.exe+10", trigger, groupByEffectiveAddress: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("groupByEffectiveAddress", exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Contains("trigger execute", exception.Error.Message, StringComparison.Ordinal);
		Assert.Contains("instructionAddress", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(0, harness.Dispatches);
		Assert.Equal(0, harness.Jobs.Count);
		Assert.Empty(harness.Resources.List());
	}

	[Fact]
	public void StartCapture_GroupByEffectiveAddressWithInstructionAggregation_RefusesBeforeLuaJobReservation()
	{
		StateTestHarness harness = new();
		DebuggerTools tools = CreateTools(harness);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.StartCapture("game.exe+10", DebuggerBreakpointTrigger.Execute, aggregateByInstruction: true,
				groupByEffectiveAddress: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("groupByEffectiveAddress", exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Contains("aggregateByInstruction", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, harness.Dispatches);
		Assert.Equal(0, harness.Jobs.Count);
	}

	[Theory]
	[InlineData(true, "[6] = \"execute\", [7] = 4, [8] = false, [9] = true }")]
	[InlineData(false, "[6] = \"execute\", [7] = 4, [8] = false, [9] = false }")]
	public void StartCapture_ExecuteTrigger_ForwardsTheEffectiveAddressGroupingToTheFixedScript(bool grouped,
		string arguments)
	{
		StateTestHarness harness = new();
		DebuggerTools tools = CreateTools(harness);
		harness.Answer<DebuggerJobStarted>(_ => new DebuggerJobStarted("ignored", 42, "401010", 30));

		DebuggerJobStarted started = tools.StartCapture("game.exe+10", DebuggerBreakpointTrigger.Execute,
			groupByEffectiveAddress: grouped, maximumHits: 8, lifetimeSeconds: 30, cancellationToken: Token);

		(string operation, string source) = Assert.Single(harness.LuaCalls);
		Assert.Equal(CheatEngineToolNames.DebuggerStartCapture, operation);
		Assert.Contains(arguments, source, StringComparison.Ordinal);
		Assert.Contains("[3] = 8, [4] = 30000, [5] = \"game.exe+10\"", source, StringComparison.Ordinal);
		Assert.Equal(started.JobId, Assert.Single(harness.Resources.List()).Id);
		Assert.Equal(1, harness.Jobs.Count);
	}

	[Fact]
	public void Capture_EffectiveAddressGroups_PollReturnsTheAddressAndOperandSize()
	{
		StateTestHarness harness = new();
		DebuggerTools tools = CreateTools(harness);
		harness.Answer<DebuggerJobStarted>(_ => new DebuggerJobStarted("ignored", 42, "401010", 30));
		DebuggerJobStarted started = tools.StartCapture("game.exe+10", DebuggerBreakpointTrigger.Execute,
			groupByEffectiveAddress: true, lifetimeSeconds: 30, cancellationToken: Token);
		DebuggerCaptureContext first = new("401010", 17, "401010", false, "mov [rax+rcx*4+10],edx",
			new Dictionary<string, string> { ["RAX"] = "1000", ["RCX"] = "2" });
		DebuggerCaptureContext last = first with
		{
			ThreadId = 18
		};
		harness.Answer<KernelPage>(_ => new KernelPage(new JobStatus(started.JobId, "debugcapture", JobState.Running,
				5, 29995, 2, 2, 0),
			[
				Json(new DebuggerCaptureItem(last, 3, first, last, "1018", 4)),
				Json(new DebuggerCaptureItem(first, 1, first, first, "2018"))
			], 1, 2, false, 0));

		DebuggerCapturePage page = tools.PollCapture(started.JobId, cancellationToken: Token);

		Assert.Equal(2, page.Hits.Count);
		Assert.Equal(("1018", 4, 3L, 17L, 18L),
			(page.Hits[0].EffectiveAddress, page.Hits[0].OperandSize, page.Hits[0].HitCount,
				page.Hits[0].FirstContext!.ThreadId, page.Hits[0].LastContext!.ThreadId));
		Assert.Equal(("2018", (int?) null, 1L),
			(page.Hits[1].EffectiveAddress, page.Hits[1].OperandSize, page.Hits[1].HitCount));
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
		Assert.DoesNotContain("removal was refused", exception.Error.Details!.Value.GetRawText(),
			StringComparison.Ordinal);
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
		Assert.Contains("assert(_G.debugger_onBreakpoint == nil, 'Another debugger_onBreakpoint hook is installed')",
			DebuggerLuaScripts.StartTrace, StringComparison.Ordinal);
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

	/// <summary>
	///     Debugger tools with chosen exposure switches over a selected process that reports a start time, whose fixed
	///     Lua answers the default-interface probe with <c>configured</c> and every attach with a Windows attachment.
	/// </summary>
	private sealed class AttachHarness : IFixedLuaExecutor
	{
		private readonly LuaDebuggerDefaultInterface _configured;
		private int _processCalls;

		internal AttachHarness(McpFeatureOptions features, LuaDebuggerDefaultInterface configured)
		{
			_configured = configured;
			IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, _) =>
			{
				Assert.Equal(nameof(IProcessClient.GetCurrentProcess), method.Name);
				Interlocked.Increment(ref _processCalls);
				return new ProcessSnapshot(new TargetProcessId(42), "game.exe", null, TargetBackend.LocalProcess,
					CheatEngineArchitecture.X64, PointerSize.Bit64, 8,
					new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero), 1);
			});
			RecordingDispatcher dispatcher = new();
			ICheatEngineClient client = ClientTestDouble.Client(dispatcher.Dispatcher, CancellationToken.None,
				(nameof(ICheatEngineClient.Processes), processes));
			IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
			ToolDispatch dispatch = new(client, new McpFeatureGate(Options.Create(features)), execution,
				new DispatchStatistics(execution), TimeProvider.System, NullLogger<ToolDispatch>.Instance, this);
			TargetResources resources = new();
			Tools = new DebuggerTools(dispatch, new JobRegistry(dispatch, resources, execution, TimeProvider.System),
				resources);
		}

		internal DebuggerTools Tools
		{
			get;
		}

		/// <summary>The fixed Lua sources run, in order.</summary>
		internal ConcurrentQueue<string> Sources
		{
			get;
		} = [];

		internal int ProcessCalls => Volatile.Read(ref _processCalls);

		public LuaJsonResult<T> Execute<T>(string operation, string source, JsonTypeInfo<T> resultType,
			LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque, CancellationToken cancellationToken)
		{
			Assert.Equal(CheatEngineToolNames.DebuggerAttach, operation);
			Sources.Enqueue(source);
			object value = _configured is T probe
				? probe
				: new DebuggerAttachment(true, false, 42, DebuggerInterface.Default, DebuggerInterface.Windows, false);
			return value is T result
				? new LuaJsonResult<T>(result, null, 0)
				: throw new XunitException($"No fixed Lua answer was configured for {typeof(T).Name}.");
		}
	}
}
