using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Execution;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Core.Targets;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tests.Tools.Modules;
using CheatEngine.Mcp.Tools.Debugger;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools.Debugger;

/// <summary>Prepared breakpoint navigation over the explicit bounded debugger list.</summary>
public sealed class PreparedBreakpointNavigationTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void ListPreparedBreakpoints_WithoutPreparation_IsInvalidState()
	{
		Harness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.ListPreparedBreakpoints(cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidState, exception.Error.Kind);
		Assert.Contains("debugger_list_breakpoints", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Empty(harness.Target.LuaSources);
	}

	[Fact]
	public void ExplicitList_PreparesMaxCopyAndProjectsRequestedAndDefaultLimits()
	{
		Harness harness = new();
		harness.Serve(Page("401000", "402000", "403000"));

		DebuggerBreakpointPage explicitPage = harness.Tools.ListBreakpoints(2, Token);
		DebuggerBreakpointPage prepared = harness.Tools.ListPreparedBreakpoints(cancellationToken: Token);

		Assert.Equal((2, 3, true), (explicitPage.Breakpoints.Length, explicitPage.Total, explicitPage.Truncated));
		Assert.Equal((3, 3, false), (prepared.Breakpoints.Length, prepared.Total, prepared.Truncated));
		Assert.Contains("[2] = 1024", Assert.Single(harness.Target.LuaSources), StringComparison.Ordinal);
	}

	[Fact]
	public void PreparedList_UsesNoAdditionalBreakpointLuaAndIsCallerIsolated()
	{
		Harness harness = new();
		harness.Serve(Page("401000"));
		DebuggerBreakpointPage explicitPage = harness.Tools.ListBreakpoints(cancellationToken: Token);
		int luaCalls = harness.Target.LuaSources.Count;
		explicitPage.Breakpoints[0] = new DebuggerBreakpoint("mutated", false);
		DebuggerBreakpointPage prepared = harness.Tools.ListPreparedBreakpoints(cancellationToken: Token);
		prepared.Breakpoints[0] = new DebuggerBreakpoint("mutated-again", false);

		Assert.Equal("401000", Assert.Single(harness.Tools.ListPreparedBreakpoints(cancellationToken: Token).Breakpoints).Address);
		Assert.Equal(luaCalls, harness.Target.LuaSources.Count);
	}

	[Fact]
	public void PreparingAcrossATargetChange_FailsAndDoesNotCache()
	{
		Harness harness = new();
		harness.Serve(Page("401000"));
		harness.Target.BeforeLua = () => harness.Target.CurrentProcessId++;

		CheatEngineToolException changed = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.ListBreakpoints(cancellationToken: Token));

		Assert.Equal((ToolErrorKind.TargetChanged, CheatEngineToolNames.DebuggerListBreakpoints, ToolHostEffect.Completed),
			(changed.Error.Kind, changed.Error.Operation, changed.Error.HostEffect));
		harness.Target.BeforeLua = null;
		Assert.Equal(ToolErrorKind.InvalidState, Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.ListPreparedBreakpoints(cancellationToken: Token)).Error.Kind);
	}

	[Fact]
	public void PreparedList_ExpiresAtTheFiveSecondBoundary()
	{
		ManualTimeProvider time = new();
		Harness harness = new(time);
		harness.Serve(Page("401000"));
		harness.Tools.ListBreakpoints(cancellationToken: Token);
		time.Advance(TimeSpan.FromSeconds(5));

		Assert.Equal(ToolErrorKind.InvalidState, Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.ListPreparedBreakpoints(cancellationToken: Token)).Error.Kind);
	}

	[Fact]
	public void DetachedTarget_PreparesAndReadsAnEmptyBreakpointSnapshot()
	{
		Harness harness = new();
		harness.Target.Attached = false;
		harness.Serve(Page());

		DebuggerBreakpointPage explicitPage = harness.Tools.ListBreakpoints(cancellationToken: Token);

		Assert.Empty(explicitPage.Breakpoints);
		Assert.Empty(harness.Tools.ListPreparedBreakpoints(cancellationToken: Token).Breakpoints);
		Assert.Equal(3, harness.Target.Calls("TryGetCurrentProcess"));
	}

	private static DebuggerBreakpointPage Page(params string[] addresses)
	{
		return new DebuggerBreakpointPage([.. addresses.Select(static address => new DebuggerBreakpoint(address, false))],
			addresses.Length, false);
	}

	private sealed class Harness
	{
		internal Harness(TimeProvider? time = null)
		{
			IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
			Resources = new TargetResources();
			Jobs = new JobRegistry(Target.Dispatch, Resources, execution, time ?? TimeProvider.System);
			Tools = new DebuggerTools(Target.Dispatch, Jobs, Resources, time);
		}

		internal ModuleSymbolTarget Target
		{
			get;
		} = new();

		internal TargetResources Resources
		{
			get;
		}

		internal JobRegistry Jobs
		{
			get;
		}

		internal DebuggerTools Tools
		{
			get;
		}

		internal void Serve(DebuggerBreakpointPage page)
		{
			Target.LuaResults[typeof(DebuggerBreakpointPage)] = page;
		}
	}
}
