using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Speedhack;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     Native Lua coverage for the fixed <c>speedhack_*</c> bodies against stubs shaped like Cheat Engine 7.7's
///     <c>speedhack_setSpeed</c> (<c>ce_speedhack2_setSpeed</c> in <c>pluginexports.pas</c>) and the hooks of
///     <c>autorun/SpeedhackV3.lua</c>.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	/// <summary>
	///     speedhack_setSpeed ticks the Enable Speedhack checkbox for every speed, 1 included, and each activation or
	///     change runs initSpeedHackHooks. That creates speedhack_wantedspeed unless the helper library fails first, in
	///     which case Cheat Engine clears the checkbox and speedhack_getSpeed reports 1; once the symbol exists, it
	///     never hooks again, and OnSetSpeed writes the speed to it. Opening another process clears the checkbox and
	///     frees Cheat Engine's speedhack without restoring the target, so speedhack_getSpeed reports 1 while the
	///     target's own speedhack_wantedspeed (<c>wantedSpeed</c>) keeps its value.
	/// </summary>
	private const string SpeedhackStubs = """
	                                      configuredSpeed = 1
	                                      speedhackSymbol = nil
	                                      wantedSpeed = 1
	                                      hookAttempts = 0
	                                      setCalls = 0
	                                      helperLibraryFails = false
	                                      openedProcess = 77
	                                      paused = false
	                                      broken = false
	                                      getOpenedProcessID = function() return openedProcess end
	                                      isPaused = function() return paused or broken end
	                                      debug_isBroken = function() return broken end
	                                      debug_isDebugging = function() return broken end
	                                      debug_getContext = function(_) return broken end
	                                      targetIsX86 = function() return true end
	                                      speedhack_getSpeed = function() return configuredSpeed end
	                                      speedhack_setSpeed = function(speed)
	                                        setCalls = setCalls + 1
	                                        requestedSpeed = speed
	                                        if speedhackSymbol == nil then
	                                          hookAttempts = hookAttempts + 1
	                                          if not helperLibraryFails then speedhackSymbol = 0x7FF6A0000000 end
	                                        end
	                                        configuredSpeed = speedhackSymbol == nil and 1 or speed
	                                        if speedhackSymbol ~= nil then wantedSpeed = speed end
	                                      end
	                                      getAddressSafe = function(name)
	                                        if name == 'speedhack_wantedspeed' then return speedhackSymbol end
	                                        return nil
	                                      end
	                                      readFloat = function(address)
	                                        if speedhackSymbol == nil or address ~= speedhackSymbol then return nil end
	                                        return wantedSpeed
	                                      end
	                                      """;

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void SpeedhackV2_SetSpeedOne_AtNormalSpeed_ChangesNothingAndNeverHooks(bool symbolBefore)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SpeedhackStubs + (symbolBefore ? "\nspeedhackSymbol = 0x7FF6A0000000" : ""));
		SpeedhackTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		SpeedhackSetResult result = tools.SetSpeed(1, Token);

		// speedhack_setSpeed(1) would tick Enable Speedhack and hook a process that was never sped up.
		Assert.Equal(new SpeedhackSetResult(1, symbolBefore, false), result);
		Assert.Equal((0L, 0L), (ReadGlobal("setCalls"), ReadGlobal("hookAttempts")));
		Assert.Null(ReadGlobal("requestedSpeed"));
		LuaFixedScriptAssert.NeverLoadsCode(SpeedhackScripts.SetNormal);
	}

	[Fact]
	public void SpeedhackV2_ZeroSymbolAddress_IsAbsentAndNeverActivatesAtNormalSpeed()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SpeedhackStubs + "\nspeedhackSymbol = 0");
		SpeedhackTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		SpeedhackState state = tools.GetState(Token);
		SpeedhackSetResult normal = tools.SetSpeed(1, Token);

		Assert.Equal(new SpeedhackState(1, false), state);
		Assert.Equal(new SpeedhackSetResult(1, false, false), normal);
		Assert.Equal((0L, 0L), (ReadGlobal("setCalls"), ReadGlobal("hookAttempts")));
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	public void SpeedhackV2_SetSpeedOne_AtNormalSpeed_IsAllowedWhilePausedOrStopped(bool paused, bool broken)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SpeedhackStubs + $"\npaused = {LuaBoolean(paused)}; broken = {LuaBoolean(broken)}");
		SpeedhackTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		Assert.Equal(new SpeedhackSetResult(1, false, false), tools.SetSpeed(1, Token));
		Assert.Equal(0L, ReadGlobal("setCalls"));
	}

	[Theory]
	[InlineData("configuredSpeed = 2.5; wantedSpeed = 2.5")]
	[InlineData("configuredSpeed = 1; wantedSpeed = 2.5")]
	public void SpeedhackV2_SetSpeedOne_WhileTheTargetIsSpedUp_RestoresWithoutActivating(string stubs)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SpeedhackStubs + "\nspeedhackSymbol = 0x7FF6A0000000\n" + stubs);
		SpeedhackTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		SpeedhackSetResult result = tools.SetSpeed(1, Token);

		// Cheat Engine reports 1 once a process switch freed its speedhack, while the target keeps its speed: the
		// symbol already exists, so the call only writes the speed and never hooks.
		Assert.Equal(new SpeedhackSetResult(1, true, false), result);
		Assert.Equal((1L, 0L, 1L, 1L), (ReadGlobal("setCalls"), ReadGlobal("hookAttempts"),
			ReadGlobal("requestedSpeed"), ReadGlobal("wantedSpeed")));
	}

	[Fact]
	public void SpeedhackV2_SetSpeedOne_ASpeedOfAnotherProcess_IsInvalidStateAndNeverHooksTheOpenedOne()
	{
		using RuntimeScope scope = CreateScope();
		// Opening a process while keeping the address list leaves Cheat Engine's speedhack of the previous one.
		InstallStubs(SpeedhackStubs + "\nconfiguredSpeed = 2");
		SpeedhackTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => tools.SetSpeed(1, Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("no speedhack_wantedspeed symbol", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal((0L, 0L), (ReadGlobal("setCalls"), ReadGlobal("hookAttempts")));
	}

	[Theory]
	[InlineData("getAddressSafe = nil")]
	[InlineData("getAddressSafe = function(_) error('symbol lookup failed') end")]
	public void SpeedhackV2_SymbolProbeUnavailable_RefusesStateAndChangesBeforeAnyMutation(string stubs)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SpeedhackStubs + "\n" + stubs);
		SpeedhackTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException state = Assert.Throws<CheatEngineToolException>(() => tools.GetState(Token));
		CheatEngineToolException normal = Assert.Throws<CheatEngineToolException>(() => tools.SetSpeed(1, Token));
		CheatEngineToolException changed = Assert.Throws<CheatEngineToolException>(() => tools.SetSpeed(2, Token));

		Assert.All([state, normal, changed], static exception =>
			Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.NotStarted),
				(exception.Error.Kind, exception.Error.HostEffect)));
		Assert.Equal((0L, 0L), (ReadGlobal("setCalls"), ReadGlobal("hookAttempts")));
	}

	[Fact]
	public void SpeedhackV2_SymbolProbeFailsAfterChangingSpeed_TracksTheRestoreAndReturnsAnUnknownObservation()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + SpeedhackStubs + """

			                              local lookup = getAddressSafe
			                              local lookups = 0
			                              getAddressSafe = function(name)
			                                lookups = lookups + 1
			                                if lookups > 1 then error('symbol lookup failed after changing speed') end
			                                return lookup(name)
			                              end
			                              """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		TargetResources resources = new(new McpStateLedger(dispatch, TimeProvider.System));
		SpeedhackTools tools = new(dispatch, resources);

		SpeedhackSetResult changed = tools.SetSpeed(2, Token);

		Assert.Equal((2d, (bool?) null, true), (changed.Speed, changed.HooksInstalled, changed.FirstActivation));
		Assert.NotNull(changed.ResourceId);
		Assert.Single(resources.List());
		Assert.Equal((1L, 1L), (ReadGlobal("setCalls"), ReadGlobal("hookAttempts")));
	}

	[Fact]
	public void SpeedhackV2_SymbolProbeFailsAfterRestoringSpeed_ReturnsAnUnknownObservation()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SpeedhackStubs + """

		                              speedhackSymbol = 0x7FF6A0000000
		                              configuredSpeed = 2
		                              wantedSpeed = 2
		                              local lookup = getAddressSafe
		                              local lookups = 0
		                              getAddressSafe = function(name)
		                                lookups = lookups + 1
		                                if lookups > 1 then error('symbol lookup failed after restoring speed') end
		                                return lookup(name)
		                              end
		                              """);
		SpeedhackTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		SpeedhackSetResult restored = tools.SetSpeed(1, Token);

		Assert.Equal((1d, (bool?) null, false), (restored.Speed, restored.HooksInstalled, restored.FirstActivation));
		// The existing hooks restore their speed without another activation, even when the later probe fails.
		Assert.Equal((1L, 0L, 1L, 1L), (ReadGlobal("setCalls"), ReadGlobal("hookAttempts"),
			ReadGlobal("requestedSpeed"), ReadGlobal("wantedSpeed")));
	}

	[Theory]
	[InlineData("openedProcess = 0", ToolErrorKind.InvalidState, ToolHostEffect.NotStarted)]
	[InlineData("configuredSpeed = 2; paused = true", ToolErrorKind.InvalidState, ToolHostEffect.NotStarted)]
	[InlineData("configuredSpeed = 2; broken = true", ToolErrorKind.InvalidState, ToolHostEffect.NotStarted)]
	[InlineData("wantedSpeed = 2; paused = true", ToolErrorKind.InvalidState, ToolHostEffect.NotStarted)]
	[InlineData("wantedSpeed = 2; broken = true", ToolErrorKind.InvalidState, ToolHostEffect.NotStarted)]
	[InlineData("speedhack_getSpeed = function() return 0 / 0 end", ToolErrorKind.HostRefused,
		ToolHostEffect.NotStarted)]
	public void SpeedhackV2_SetSpeedOne_RefusesBeforeCallingSetSpeed(string stubs, ToolErrorKind kind,
		ToolHostEffect effect)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SpeedhackStubs + "\nspeedhackSymbol = 0x7FF6A0000000\n" + stubs);
		SpeedhackTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => tools.SetSpeed(1, Token));

		Assert.Equal((kind, effect), (exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal((0L, 0L), (ReadGlobal("setCalls"), ReadGlobal("hookAttempts")));
	}

	[Fact]
	public void SpeedhackV2_SetSpeedOne_ANonFiniteSpeedAfterTheRestore_IsHostRefusedStarted()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SpeedhackStubs + """

		                              speedhackSymbol = 0x7FF6A0000000
		                              configuredSpeed = 2
		                              local set = speedhack_setSpeed
		                              speedhack_setSpeed = function(speed) set(speed); configuredSpeed = 0 / 0 end
		                              """);
		SpeedhackTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => tools.SetSpeed(1, Token));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Started),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal((1L, 1L), (ReadGlobal("setCalls"), ReadGlobal("requestedSpeed")));
	}

	[Theory]
	[InlineData("", true, 2L, 1L)]
	[InlineData("configuredSpeed = 1; wantedSpeed = 1", true, 1L, 2L)]
	[InlineData("configuredSpeed = 1", true, 2L, 1L)]
	[InlineData("openedProcess = 88; configuredSpeed = 1", false, 1L, 2L)]
	public void SpeedhackV2_RestoreResource_RestoresOnlyTheSpedUpProcess(string afterSet, bool complete,
		long setCalls, long requestedSpeed)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + SpeedhackStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		TargetResources resources = new(new McpStateLedger(dispatch, TimeProvider.System));
		SpeedhackTools tools = new(dispatch, resources);

		SpeedhackSetResult changed = tools.SetSpeed(2, Token);
		InstallStubs(afterSet);
		ReleaseAllResult release = resources.ReleaseAll(Token);

		// Another opened process never gets the restore: ticking Enable Speedhack there would hook it instead. Back
		// in the sped-up process after a switch, Cheat Engine reports 1 while the target still runs at 2.
		Assert.Equal((2d, true, true), (changed.Speed, changed.HooksInstalled, changed.FirstActivation));
		Assert.Equal(complete, release.IsComplete);
		Assert.Equal((setCalls, requestedSpeed, 1L), (ReadGlobal("setCalls"), ReadGlobal("requestedSpeed"),
			ReadGlobal("hookAttempts")));
		Assert.Equal(complete ? null : ResourceReleaseKind.CleanupFailed, release.Failed?.Release.Kind);
	}

	[Theory]
	[InlineData(2)]
	[InlineData(1)]
	public void SpeedhackV2_UnattachedDebuggerReturnsOpaqueValue_ChangesSpeed(double speed)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + SpeedhackStubs + """

			debug_isBroken = function() return debug_isBroken end
			debug_getContext = function(_) error('An unattached debugger has no context') end
			speedhackSymbol = 0x7FF6A0000000
			configuredSpeed = 2.5
			wantedSpeed = 2.5
			""");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		TargetResources resources = new(new McpStateLedger(dispatch, TimeProvider.System));
		SpeedhackTools tools = new(dispatch, resources);

		SpeedhackSetResult result = tools.SetSpeed(speed, Token);

		Assert.Equal(speed, result.Speed);
		Assert.Equal((1L, 0L), (ReadGlobal("setCalls"), ReadGlobal("hookAttempts")));
		Assert.Equal(speed, Convert.ToDouble(ReadGlobal("wantedSpeed"), System.Globalization.CultureInfo.InvariantCulture));
	}

	[Theory]
	[InlineData(2)]
	[InlineData(1)]
	public void SpeedhackV2_StoppedContextWithOpaqueReportedState_RefusesBeforeChangingSpeed(double speed)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SpeedhackStubs + """

			debug_isBroken = function() return debug_isBroken end
			broken = true
			speedhackSymbol = 0x7FF6A0000000
			configuredSpeed = 2.5
			wantedSpeed = 2.5
			""");
		SpeedhackTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => tools.SetSpeed(speed, Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("stopped at a breakpoint", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal((0L, 0L), (ReadGlobal("setCalls"), ReadGlobal("hookAttempts")));
	}

	[Theory]
	[InlineData("debug_isDebugging = function() return debug_isDebugging end")]
	[InlineData("broken = true; debug_getContext = function(_) return debug_getContext end")]
	public void SpeedhackV2_InvalidDebuggerState_RefusesBeforeChangingSpeed(string stubs)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SpeedhackStubs + "\n" + stubs);
		SpeedhackTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => tools.SetSpeed(2, Token));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal((0L, 0L), (ReadGlobal("setCalls"), ReadGlobal("hookAttempts")));
	}

	private static string LuaBoolean(bool value)
	{
		return value ? "true" : "false";
	}
}
