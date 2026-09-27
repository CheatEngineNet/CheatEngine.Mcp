using System.Reflection;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Kernel;

using Microsoft.Extensions.Options;

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

	public static IEnumerable<object[]> FixedLuaBodies()
	{
		yield return [KernelScripts.GetStatus, "dbk_initialized"];
		yield return [KernelScripts.InitializeDbvm, "dbvm_initialize"];
		yield return [KernelScripts.TranslateAddress, "dbk_getPhysicalAddress"];
		yield return [KernelScripts.ReadPhysical, "dbvm_readPhysicalMemory"];
		yield return [KernelScripts.WritePhysical, "dbvm_writePhysicalMemory"];
		yield return [KernelScripts.StartWatch, "dbvm_watch_disable"];
		yield return [KernelScripts.PollWatch, "dbvm_watch_retrievelog"];
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
