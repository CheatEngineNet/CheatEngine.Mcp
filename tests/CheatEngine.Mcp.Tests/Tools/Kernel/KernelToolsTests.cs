using System.Reflection;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Kernel;

using Microsoft.Extensions.Options;

using KernelPage = CheatEngine.Mcp.Core.Jobs.LuaJobPage;

namespace CheatEngine.Mcp.Tests.Tools.Kernel;

/// <summary>DBK and DBVM tools stay kernel-gated, bounded, and clean up a watch through the tracked Lua job.</summary>
public sealed class KernelToolsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void EveryKernelMethod_DeclaresTheKernelAccessRequirement()
	{
		MethodInfo[] methods =
		[
			Method(nameof(KernelTools.GetStatus)), Method(nameof(KernelTools.InitializeDbvm)),
			Method(nameof(KernelTools.TranslateAddress)), Method(nameof(KernelTools.ReadPhysical)),
			Method(nameof(KernelTools.WritePhysical)), Method(nameof(KernelTools.StartWatch)),
			Method(nameof(KernelTools.PollWatch))
		];

		Assert.All(methods, static method => Assert.Contains(method.GetCustomAttributes<RequiresFeatureAttribute>(),
			static requirement => requirement.Feature == McpFeature.KernelAccess));
	}

	[Fact]
	public void GetStatus_DisabledKernelAccess_RefusesBeforeDispatch()
	{
		DispatchHarness harness = new(new McpFeatureOptions { EnableKernelAccess = false });

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new KernelTools(harness.Dispatch, harness.Jobs).GetStatus(Token));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Theory]
	[InlineData(0, ToolErrorKind.InvalidArgument)]
	[InlineData(4097, ToolErrorKind.LimitExceeded)]
	public void ReadPhysical_SizeOutsideTheBound_RefusesBeforeLua(int size, ToolErrorKind expected)
	{
		StateTestHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new KernelTools(harness.Dispatch, harness.Jobs).ReadPhysical("1A2B", size, Token));

		Assert.Equal((expected, ToolHostEffect.NotStarted), (exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.Dispatches);
		Assert.Empty(harness.LuaCalls);
	}

	[Theory]
	[InlineData("FFFFFFFFFFFFFFFF", 2)]
	[InlineData("FFFFFFFFFFFFFFFE", 3)]
	public void PhysicalRange_ThatWouldWrap_RefusesBeforeLua(string physicalAddress, int size)
	{
		StateTestHarness readHarness = new();
		CheatEngineToolException read = Assert.Throws<CheatEngineToolException>(() =>
			new KernelTools(readHarness.Dispatch, readHarness.Jobs).ReadPhysical(physicalAddress, size, Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(read.Error.Kind, read.Error.HostEffect));
		Assert.Equal(0, readHarness.Dispatches);
		Assert.Empty(readHarness.LuaCalls);

		StateTestHarness watchHarness = new();
		CheatEngineToolException watch = Assert.Throws<CheatEngineToolException>(() =>
			new KernelTools(watchHarness.Dispatch, watchHarness.Jobs).StartWatch(KernelWatchAccess.Read,
				physicalAddress, size, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(watch.Error.Kind, watch.Error.HostEffect));
		Assert.Equal(0, watchHarness.Dispatches);
		Assert.Empty(watchHarness.LuaCalls);
	}

	[Fact]
	public void WritePhysical_RangeThatWouldWrap_RefusesBeforeLua()
	{
		StateTestHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new KernelTools(harness.Dispatch, harness.Jobs).WritePhysical("FFFFFFFFFFFFFFFF", "AA BB", Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.Dispatches);
		Assert.Empty(harness.LuaCalls);
	}

	[Fact]
	public void WritePhysical_OversizedByteText_RefusesBeforeParsingOrLua()
	{
		StateTestHarness harness = new();
		string bytes = new(' ', KernelSupport.MaximumPhysicalByteTextCharacters + 1);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new KernelTools(harness.Dispatch, harness.Jobs).WritePhysical("1000", bytes, Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.Dispatches);
		Assert.Empty(harness.LuaCalls);
	}

	[Theory]
	[InlineData(0, 0, 1, ToolErrorKind.InvalidArgument)]
	[InlineData(4097, 0, 1, ToolErrorKind.LimitExceeded)]
	[InlineData(1, 0, 0, ToolErrorKind.InvalidArgument)]
	[InlineData(1, 0, 4097, ToolErrorKind.LimitExceeded)]
	[InlineData(1, 16, 1, ToolErrorKind.InvalidArgument)]
	public void StartWatch_UnboundedOrInvalidArguments_RefuseBeforeLua(int byteSize, int options,
		int internalEntries, ToolErrorKind expected)
	{
		StateTestHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new KernelTools(harness.Dispatch, harness.Jobs).StartWatch(KernelWatchAccess.Read, "1000", byteSize,
				options, internalEntries, cancellationToken: Token));

		Assert.Equal((expected, ToolHostEffect.NotStarted), (exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.Dispatches);
		Assert.Empty(harness.LuaCalls);
	}

	[Theory]
	[InlineData("1FFF", 2, 1)]
	[InlineData("1001", 4096, 4095)]
	[InlineData("2800", 2049, 2048)]
	public void StartWatch_RangeThatCrossesAPhysicalPage_RefusesBeforeLua(string physicalAddress, int byteSize,
		int available)
	{
		StateTestHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new KernelTools(harness.Dispatch, harness.Jobs).StartWatch(KernelWatchAccess.Write, physicalAddress,
				byteSize, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("byteSize", exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Contains($"at most {available} bytes", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, harness.Dispatches);
		Assert.Equal(0, harness.Jobs.Count);
	}

	[Theory]
	[InlineData("2000", 4096)]
	[InlineData("2800", 2048)]
	[InlineData("2FFF", 1)]
	public void StartWatch_RangeThatEndsAtThePageBoundary_IsArmed(string physicalAddress, int byteSize)
	{
		StateTestHarness harness = new();
		harness.Answer<LuaKernelWatchArmed>(static _ => new LuaKernelWatchArmed(3));

		KernelWatchStart started = new KernelTools(harness.Dispatch, harness.Jobs).StartWatch(
			KernelWatchAccess.Read, physicalAddress, byteSize, cancellationToken: Token);

		Assert.Equal((physicalAddress, byteSize), (started.PhysicalAddress, started.ByteSize));
		Assert.Equal(CheatEngineToolNames.KernelStartWatch, Assert.Single(harness.LuaCalls).Operation);
	}

	[Fact]
	public void StartWatch_BufferLimitOutsideTheConfiguredLimit_NamesBufferLimit()
	{
		StateTestHarness harness = new(new McpExecutionOptions { JobBufferLimit = 2 });

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new KernelTools(harness.Dispatch, harness.Jobs).StartWatch(KernelWatchAccess.Read, "1000",
				bufferLimit: 3, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("bufferLimit", exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.StartsWith("bufferLimit:", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, harness.Dispatches);
		Assert.Equal(0, harness.Jobs.Count);
	}

	[Fact]
	public void PollWatch_DrainsDbvmThenReadsThePageThroughTheJobPoll()
	{
		StateTestHarness harness = new();
		harness.Answer<LuaKernelWatchArmed>(static _ => new LuaKernelWatchArmed(7));
		KernelTools tools = new(harness.Dispatch, harness.Jobs);
		DateTimeOffset created = harness.Time.GetUtcNow();
		KernelWatchStart started = tools.StartWatch(KernelWatchAccess.Write, "1000", 4, cancellationToken: Token);
		harness.Answer<LuaKernelWatchDrain>(static _ => new LuaKernelWatchDrain(true, 2));
		harness.Answer<KernelPage>(source =>
		{
			Assert.Contains(started.JobId, source, StringComparison.Ordinal);
			return new KernelPage(new JobStatus(started.JobId, "kernelwatch", JobState.Completed, 5, 1_000, 2, 2, 0),
				[Json(new KernelWatchEvent(1, "401000", null, "1", null, null, null, null, null, null, null, null, null,
					null, null, null, null, null, "1AB000")), Json(Event(2))], 1, 2, false, 0);
		});

		KernelWatchPoll page = tools.PollWatch(started.JobId, cancellationToken: Token);

		Assert.Equal((JobState.Completed, created, 1L, 2L, false, 0L),
			(page.Job.State, page.Job.CreatedUtc, page.FirstSequence, page.NextAfterSequence, page.More,
				page.Dropped));
		Assert.Equal([1L, 2L], page.Events.Select(static item => item.SourceIndex));
		Assert.Equal(("401000", "1", "1AB000"), (page.Events[0].Rip, page.Events[0].Rax, page.Events[0].Cr3));
		(string Operation, string Source)[] calls = [.. harness.LuaCalls];
		Assert.Equal([
			CheatEngineToolNames.KernelStartWatch, CheatEngineToolNames.KernelPollWatch,
			CheatEngineToolNames.KernelPollWatch
		], calls.Select(static call => call.Operation));
		Assert.Contains("dbvm_watch_retrievelog", calls[1].Source, StringComparison.Ordinal);
		Assert.DoesNotContain("dbvm_watch_retrievelog", calls[2].Source, StringComparison.Ordinal);
		// The job poll refreshed the managed state: a completed watch no longer holds host state.
		Assert.Equal(TargetResourceState.Ended, Assert.Single(harness.Resources.List()).State);
	}

	[Fact]
	public void PollWatch_JobGoneFromLua_ReportsNotFoundAndRetiresTheManagedJob()
	{
		StateTestHarness harness = new();
		harness.Answer<LuaKernelWatchArmed>(static _ => new LuaKernelWatchArmed(7));
		KernelTools tools = new(harness.Dispatch, harness.Jobs);
		KernelWatchStart started = tools.StartWatch(KernelWatchAccess.Read, "1000", cancellationToken: Token);
		harness.Answer<LuaKernelWatchDrain>(static _ => new LuaKernelWatchDrain(false, 0));
		harness.Declare<KernelPage>("not_found", "Unknown or expired job.");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.PollWatch(started.JobId, cancellationToken: Token));
		int dispatches = harness.Dispatches;
		CheatEngineToolException again = Assert.Throws<CheatEngineToolException>(() =>
			tools.PollWatch(started.JobId, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.NotFound, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(ToolErrorKind.NotFound, again.Error.Kind);
		Assert.Equal(dispatches, harness.Dispatches);
		Assert.Equal(0, harness.Jobs.Count);
	}

	[Fact]
	public void PollWatch_DrainRefused_ReportsTheRefusalWithoutReadingAPage()
	{
		StateTestHarness harness = new();
		harness.Answer<LuaKernelWatchArmed>(static _ => new LuaKernelWatchArmed(7));
		KernelTools tools = new(harness.Dispatch, harness.Jobs);
		KernelWatchStart started = tools.StartWatch(KernelWatchAccess.Read, "1000", cancellationToken: Token);
		harness.Declare<LuaKernelWatchDrain>("host_refused", "DBVM could not retrieve watch events.", "started");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.PollWatch(started.JobId, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Started),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(2, harness.LuaCalls.Count);
		Assert.Equal(1, harness.Jobs.Count);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void InitializeDbvm_PassesCheatEnginesWarningAndTheReasonSeparately(bool offload)
	{
		StateTestHarness harness = new();
		harness.Answer<KernelDbvmInitialization>(_ => new KernelDbvmInitialization(true, offload, "Watch a page"));

		KernelDbvmInitialization result = new KernelTools(harness.Dispatch, harness.Jobs).InitializeDbvm(offload,
			"Watch a page", Token);

		Assert.Equal(new KernelDbvmInitialization(true, offload, "Watch a page"), result);
		(string operation, string source) = Assert.Single(harness.LuaCalls);
		Assert.Equal(CheatEngineToolNames.KernelInitializeDbvm, operation);
		Assert.Contains("There is a high chance running DBVM can crash your system", source, StringComparison.Ordinal);
		Assert.Contains("Watch a page", source, StringComparison.Ordinal);
		Assert.Contains("'invalid_state'", source, StringComparison.Ordinal);
	}

	public static IEnumerable<object[]> FixedLuaBodies()
	{
		yield return [KernelScripts.GetStatus, "dbk_initialized"];
		yield return [KernelScripts.InitializeDbvm, "dbvm_initialize"];
		yield return [KernelScripts.TranslateAddress, "dbk_getPhysicalAddress"];
		yield return [KernelScripts.ReadPhysical, "dbvm_readPhysicalMemory"];
		yield return [KernelScripts.WritePhysical, "dbvm_writePhysicalMemory"];
		yield return [KernelScripts.StartWatch, "dbvm_watch_disable"];
		yield return [KernelScripts.DrainWatch, "dbvm_watch_retrievelog"];
	}

	[Theory]
	[MemberData(nameof(FixedLuaBodies))]
	public void FixedLuaBody_UsesTheReviewedKernelApiAndCannotLoadCallerCode(string script, string api)
	{
		Assert.Contains(api, script, StringComparison.Ordinal);
		LuaFixedScriptAssert.NeverLoadsCode(script);
		Assert.Equal([McpFeature.KernelAccess], LuaFeatureScan.Scan(script));
	}

	public static IEnumerable<object[]> DbvmInitializationGuards()
	{
		yield return [KernelScripts.ReadPhysical, "dbvm_readPhysicalMemory"];
		yield return [KernelScripts.WritePhysical, "dbvm_writePhysicalMemory"];
		yield return [KernelScripts.StartWatch, "dbvm_watch_reads"];
	}

	[Theory]
	[MemberData(nameof(DbvmInitializationGuards))]
	public void DbvmOperation_FixedLuaRefusesAnUninitializedDbvmBeforeTheDeviceCall(string script, string api)
	{
		int stateCheck = script.IndexOf("dbvm_initialized", StringComparison.Ordinal);
		int deviceCall = script.IndexOf(api, StringComparison.Ordinal);

		Assert.True(stateCheck >= 0, script);
		Assert.True(deviceCall > stateCheck, script);
		Assert.Contains("mcp.err('invalid_state'", script, StringComparison.Ordinal);
		Assert.Contains("'not_started'", script, StringComparison.Ordinal);
	}

	[Fact]
	public void StartWatch_UsesABoundedLuaJobAndStopDispatchesTheCleanupHook()
	{
		StateTestHarness harness = new(new McpExecutionOptions
		{
			JobDefaultTtlSeconds = 3,
			JobMaxTtlSeconds = 3,
			JobBufferLimit = 2
		});
		harness.Answer<LuaKernelWatchArmed>(static _ => new LuaKernelWatchArmed(7));
		KernelTools tools = new(harness.Dispatch, harness.Jobs);

		KernelWatchStart started = tools.StartWatch(KernelWatchAccess.Execute, "1000", 4,
			internalEntryCount: 3, lifetimeSeconds: 3, bufferLimit: 2, cancellationToken: Token);

		Assert.Equal(("1000", KernelWatchAccess.Execute, 4, 3),
			(started.PhysicalAddress, started.Access, started.ByteSize, started.InternalEntryCount));
		Assert.Equal($"kernelwatch-{harness.Jobs.Namespace}-1", started.JobId);
		Assert.Single(harness.Resources.List());
		(string operation, string source) = Assert.Single(harness.LuaCalls);
		Assert.Equal(CheatEngineToolNames.KernelStartWatch, operation);
		Assert.Contains("jobStart(a[1], a[2], 'kernelwatch'", source, StringComparison.Ordinal);
		Assert.Contains("active.onStop", source, StringComparison.Ordinal);
		Assert.Contains("dbvm_watch_disable", source, StringComparison.Ordinal);
		Assert.Contains("[3] = 2", source, StringComparison.Ordinal);
		Assert.Contains("[4] = 3000", source, StringComparison.Ordinal);

		harness.Answer<LuaJobStop>(static _ => new LuaJobStop(true, true, true, "stopped"));
		JobStopResult stopped = harness.Jobs.Stop(started.JobId, Token);

		Assert.Equal((started.JobId, true, false), (stopped.JobId, stopped.Released, stopped.AlreadyReleased));
		Assert.Equal("mcp_job_stop", harness.LuaCalls.Last().Operation);
		Assert.Empty(harness.Resources.List());
	}

	private static KernelWatchEvent Event(long sourceIndex)
	{
		return new KernelWatchEvent(sourceIndex, null, null, null, null, null, null, null, null, null, null, null, null,
			null, null, null, null, null, null);
	}

	private static JsonElement Json(KernelWatchEvent value)
	{
		return JsonSerializer.SerializeToElement(value, KernelJsonContext.Default.KernelWatchEvent);
	}

	private static MethodInfo Method(string name)
	{
		return typeof(KernelTools).GetMethod(name, BindingFlags.Instance | BindingFlags.Public)
			   ?? throw new InvalidOperationException($"Missing {name}.");
	}

	private sealed class DispatchHarness
	{
		internal DispatchHarness(McpFeatureOptions features)
		{
			IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
			Dispatch = new ToolDispatch(ClientTestDouble.Client(Dispatcher.Dispatcher, CancellationToken.None),
				new McpFeatureGate(Options.Create(features)), execution, new DispatchStatistics(execution),
				TimeProvider.System, new RecordingLogger<ToolDispatch>());
			Resources = new TargetResources();
			Jobs = new JobRegistry(Dispatch, Resources, execution, TimeProvider.System);
		}

		internal RecordingDispatcher Dispatcher
		{
			get;
		} = new();

		internal ToolDispatch Dispatch
		{
			get;
		}

		internal JobRegistry Jobs
		{
			get;
		}

		internal TargetResources Resources
		{
			get;
		}
	}
}
