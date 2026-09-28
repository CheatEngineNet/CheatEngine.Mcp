using System.Reflection;

using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Mono;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     Native Lua coverage for the fixed Mono v2 bodies against stubs shaped like Cheat Engine 7.7's
///     <c>monoscript.lua</c>: the collector state lives in <c>libmono</c> (<c>ProcessID</c>, <c>IL2CPP</c>,
///     <c>abort</c>, <c>fail</c>, <c>displayingTimeoutDialog</c>, <c>monopipes</c>, <c>HeartBeat</c>, <c>terminate</c>,
///     and the metatable that connects <c>libmono.monopipe</c>), and there is no <c>monoBase</c> global.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private const string MonoAttachedStubs = """
	                                         pid = 77
	                                         getOpenedProcessID = function() return pid end
	                                         libmono = {ProcessID = 77, monopipes = {}}
	                                         """;

	private const string MonoAttachmentId = "mono-0badc0de-1";

	/// <summary>
	///     The guard's recovery hint: mono_detach tells which owner holds the attachment, since it reports not_found or
	///     alreadyEnded when Cheat Engine made the current one and mono_attach would then be refused as busy.
	/// </summary>
	private const string MonoRecoveryHint =
		"Call mono_detach. If it reports not_found or alreadyEnded, Cheat Engine owns the current attachment: ask the "
		+ "user to deactivate, then re-activate Mono features in Cheat Engine's Mono menu. Otherwise call mono_attach "
		+ "while the target runs normally.";

	/// <summary>
	///     The record <see cref="MonoLuaScripts.Attach" /> leaves for the attachment <see cref="MonoAttachmentId" />,
	///     with the heart beat of the launch that made it.
	/// </summary>
	private const string MonoOwnedStubs = """
	                                      beat = {name = 'heart beat of the MCP launch'}
	                                      libmono.HeartBeat = beat
	                                      __cheatengine_mcp_mono = {id = 'mono-0badc0de-1', processId = 77}
	                                      __cheatengine_mcp_mono.heartBeat = beat
	                                      """;

	/// <summary>
	///     A main-thread pipe after <c>libmono.fail</c> was set, with the destroy override Cheat Engine's
	///     <c>getMonoPipe</c> installs on it; the native destroy is bound to its pipe, as Cheat Engine's object methods
	///     are.
	/// </summary>
	private const string MonoFailedPipeStubs = """
	                                          getCurrentThreadID = function() return 5 end
	                                          libmono.fail = true
	                                          libmono.terminate = function() terminations = (terminations or 0) + 1 end
	                                          stale = {name = 'stale'}
	                                          local native = function() destroyed = 'stale' end
	                                          stale.destroy = function(skip)
	                                            destroyedWith = skip
	                                            monopipe.destroy = native
	                                            if not skip then libmono.terminate() else monopipe.destroy() end
	                                            monopipe = nil
	                                          end
	                                          libmono.monopipes[5] = stale
	                                          monopipe = stale
	                                          mono_enumAssemblies = function()
	                                            enumerations = (enumerations or 0) + 1
	                                            return {}
	                                          end
	                                          """;

	private static readonly string[] MonoGatedBodies =
		[nameof(MonoLuaScripts.Attach), nameof(MonoLuaScripts.CompileMethod), nameof(MonoLuaScripts.InvokeMethod)];

	[Fact]
	public void MonoV2_FixedBodies_NeverLoadCodeAndOnlyAttachCompileAndInvokeNeedCodeExecution()
	{
		FieldInfo[] bodies = typeof(MonoLuaScripts).GetFields(BindingFlags.NonPublic | BindingFlags.Static)
			.Where(static field => field.IsLiteral && field.FieldType == typeof(string)).ToArray();

		Assert.NotEmpty(bodies);
		foreach (FieldInfo field in bodies)
		{
			string body = (string) field.GetRawConstantValue()!;
			LuaFixedScriptAssert.NeverLoadsCode(body);
			Assert.DoesNotContain("monoBase", body, StringComparison.Ordinal);
			McpFeature[] expected = MonoGatedBodies.Contains(field.Name) ? [McpFeature.TargetCodeExecution] : [];
			Assert.True(expected.SequenceEqual(LuaFeatureScan.Scan(body)), field.Name);
		}
	}

	[Theory]
	[InlineData("pid = 0", "not_attached", "not_started", "no selected target process")]
	[InlineData("libmono = nil", "not_attached", "not_started", "not attached to the selected target")]
	[InlineData("libmono.ProcessID = nil", "not_attached", "not_started", "not attached to the selected target")]
	[InlineData("libmono.ProcessID = 78", "not_attached", "not_started", "not attached to the selected target")]
	[InlineData("libmono.abort = true", "not_attached", "not_started", "aborted")]
	// No pipe of this thread to close: the one connection attempt failed and changed nothing.
	[InlineData("libmono.fail = true", "invalid_state", "not_applied", "timeout or an error")]
	[InlineData("debug_isBroken = function() return true end", "invalid_state", "not_started", "debugger is stopped")]
	[InlineData("isPaused = function() return true end", "invalid_state", "not_started", "target is paused")]
	public void MonoV2_Guard_RefusesFromTheStateCheatEngine77Keeps(string stubs, string kind, string effect,
		string message)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("mono_enumAssemblies = function() enumerations = (enumerations or 0) + 1 return {} end");
		InstallStubs(stubs);

		LuaJsonResult<MonoAssemblyPage> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Assemblies, 100, new object?[] { 0, 100 }),
			MonoJsonContext.Default.MonoAssemblyPage);

		Assert.True(result.IsError);
		Assert.Equal((kind, effect), (result.Error!.Kind, result.Error.HostEffect));
		Assert.Contains(message, result.Error.Message, StringComparison.Ordinal);
		Assert.Null(ReadGlobal("enumerations"));
	}

	[Theory]
	[InlineData("libmono.abort = true")]
	[InlineData("libmono.fail = true")]
	public void MonoV2_Guard_BrokenConnection_HintTellsHowEachOwnerRecovers(string stubs)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs(stubs);

		LuaJsonResult<MonoAssemblyPage> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Assemblies, 100, new object?[] { 0, 100 }),
			MonoJsonContext.Default.MonoAssemblyPage);

		Assert.True(result.IsError);
		Assert.Equal(MonoRecoveryHint, result.Error!.Hint);
	}

	[Fact]
	public void MonoV2_Guard_AcceptsAnAttachmentCheatEngineRecordsForTheSelectedProcess()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             libmono.ProcessID = '77'
		             libmono.fail = false
		             debug_isBroken = function() return false end
		             isPaused = function() return false end
		             mono_enumAssemblies = function() return {0x1000, 0x2000, 0x3000} end
		             mono_getImageFromAssembly = function(assembly) return assembly + 1 end
		             mono_image_get_name = function(image) return 'image' .. image end
		             """);

		LuaJsonResult<MonoAssemblyPage> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Assemblies, 100, new object?[] { 1, 1 }),
			MonoJsonContext.Default.MonoAssemblyPage);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal((3, 2), (result.Value!.Total, result.Value.NextOffset));
		Assert.Equal([new MonoAssembly("8192", "8193", "image8193")], result.Value.Assemblies);
	}

	[Fact]
	public void MonoV2_Guard_FailedPipe_ReplacesThisThreadsPipeWithoutTerminatingThenServesTheCall()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs(MonoFailedPipeStubs);
		InstallStubs("""
		             fresh = {name = 'fresh'}
		             setmetatable(libmono, {__index = function(t, k)
		               if k ~= 'monopipe' then return nil end
		               connects = (connects or 0) + 1
		               local current = rawget(t, 'monopipes')[5]
		               if current == nil then
		                 current = fresh
		                 t.monopipes[5] = fresh
		                 t.fail = false
		                 monopipe = fresh
		               end
		               return current
		             end})
		             """);

		LuaJsonResult<MonoAssemblyPage> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Assemblies, 100, new object?[] { 0, 100 }),
			MonoJsonContext.Default.MonoAssemblyPage);
		InstallStubs("""
		             failCleared = rawget(libmono, 'fail') == false
		             freshServes = libmono.monopipes[5] == fresh and monopipe == fresh
		             """);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(0, result.Value!.Total);
		Assert.Equal(("stale", true), (ReadGlobal("destroyed"), ReadGlobal("destroyedWith")));
		Assert.Null(ReadGlobal("terminations"));
		Assert.Equal((1L, 1L), (ReadGlobal("connects"), ReadGlobal("enumerations")));
		Assert.Equal((true, true), (ReadGlobal("failCleared"), ReadGlobal("freshServes")));
	}

	[Theory]
	// The global monopipe normally names the main thread's pipe, which carries the destroy override.
	[InlineData("monopipe = stale", "nil")]
	// A drifted global: the override would index nil, so the guard points it at the stale pipe.
	[InlineData("monopipe = nil", "nil")]
	// A drifted global naming another pipe is restored and never destroyed.
	[InlineData("other = {destroy = function() otherDestroyed = true end}; monopipe = other", "other")]
	public void MonoV2_Guard_FailedPipeThatCannotReconnect_RefusesWithTheRecoveryForEitherOwner(string global,
		string expectedGlobal)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs(MonoFailedPipeStubs);
		InstallStubs(global);
		// Cheat Engine's getMonoPipe: while fail is set it tries one connection, and a failed one leaves fail set.
		InstallStubs("""
		             setmetatable(libmono, {__index = function(t, k)
		               if k == 'monopipe' then connects = (connects or 0) + 1 end
		               return nil
		             end})
		             """);

		LuaJsonResult<MonoAssemblyPage> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Assemblies, 100, new object?[] { 0, 100 }),
			MonoJsonContext.Default.MonoAssemblyPage);
		InstallStubs($"""
		              staleDropped = libmono.monopipes[5] == nil
		              globalRestored = rawequal(monopipe, {expectedGlobal})
		              """);

		Assert.True(result.IsError);
		// The guard closed this thread's pipe before the failed connection, so the refusal cannot claim no effect.
		Assert.Equal(("invalid_state", "started"), (result.Error!.Kind, result.Error.HostEffect));
		Assert.EndsWith("could not open a new connection to the collector. The failed pipe was closed.",
			result.Error.Message, StringComparison.Ordinal);
		Assert.Equal(MonoRecoveryHint, result.Error.Hint);
		Assert.Equal(("stale", true), (ReadGlobal("destroyed"), ReadGlobal("destroyedWith")));
		Assert.Null(ReadGlobal("terminations"));
		Assert.Null(ReadGlobal("otherDestroyed"));
		Assert.Equal((1L, null), (ReadGlobal("connects"), ReadGlobal("enumerations")));
		Assert.Equal((true, true), (ReadGlobal("staleDropped"), ReadGlobal("globalRestored")));
	}

	[Theory]
	[InlineData("isPaused = function() return true end", "target is paused")]
	[InlineData("debug_isBroken = function() return true end", "debugger is stopped")]
	[InlineData("libmono.displayingTimeoutDialog = true", "timeout dialog")]
	public void MonoV2_Guard_FailedPipe_IsLeftAloneWhileTheTargetCannotAnswer(string stubs, string message)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs(MonoFailedPipeStubs);
		InstallStubs("""
		             setmetatable(libmono, {__index = function(t, k)
		               if k == 'monopipe' then connects = (connects or 0) + 1 end
		               return nil
		             end})
		             """);
		InstallStubs(stubs);

		LuaJsonResult<MonoAssemblyPage> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Assemblies, 100, new object?[] { 0, 100 }),
			MonoJsonContext.Default.MonoAssemblyPage);
		InstallStubs("staleKept = libmono.monopipes[5] == stale and monopipe == stale");

		Assert.True(result.IsError);
		Assert.Equal(("invalid_state", "not_started"), (result.Error!.Kind, result.Error.HostEffect));
		Assert.Contains(message, result.Error.Message, StringComparison.Ordinal);
		Assert.Null(ReadGlobal("destroyed"));
		Assert.Null(ReadGlobal("connects"));
		Assert.Null(ReadGlobal("enumerations"));
		Assert.Equal(true, ReadGlobal("staleKept"));
	}

	[Fact]
	public void MonoV2_Status_ReadsLibmonoAndListsTheDomains()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             libmono.IL2CPP = true
		             monopipe = {IL2CPP = false}
		             mono_getMonoDatacollectorDLLVersion = function() return 21102025 end
		             mono_enumDomains = function() return {0x1000, 0x2000} end
		             """);

		LuaJsonResult<MonoStatus> result = ReadJson(LuaToolRuntime.BuildSource(MonoLuaScripts.Status, 100, []),
			MonoJsonContext.Default.MonoStatus);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal((true, true, 21102025L), (result.Value!.Attached, result.Value.Il2Cpp,
			result.Value.CollectorVersion));
		Assert.Equal(["4096", "8192"], result.Value.Domains!);
	}

	[Theory]
	[InlineData("libmono.ProcessID = 78", false)]
	[InlineData("libmono = nil; monopipe = {}", false)]
	[InlineData("libmono.fail = true", true)]
	[InlineData("isPaused = function() return true end", true)]
	public void MonoV2_Status_MakesNoPipeCallForAnotherProcessOrAFailedPipe(string stubs, bool attached)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             mono_getMonoDatacollectorDLLVersion = function() pipeCalls = (pipeCalls or 0) + 1 return 1 end
		             mono_enumDomains = function() pipeCalls = (pipeCalls or 0) + 1 return {} end
		             """);
		InstallStubs(stubs);

		LuaJsonResult<MonoStatus> result = ReadJson(LuaToolRuntime.BuildSource(MonoLuaScripts.Status, 100, []),
			MonoJsonContext.Default.MonoStatus);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(new MonoStatus(attached), result.Value);
		Assert.Null(ReadGlobal("pipeCalls"));
	}

	[Fact]
	public void MonoV2_Attach_ReportsTheIl2CppFlagCheatEngineStoresInLibmono()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             pid = 77
		             getOpenedProcessID = function() return pid end
		             libmono = {monopipes = {}}
		             LaunchMonoDataCollector = function()
		               launches = (launches or 0) + 1
		               libmono.ProcessID = pid
		               libmono.IL2CPP = true
		               libmono.HeartBeat = {name = 'heart beat of this launch'}
		               monopipe = {IL2CPP = false}
		               return true
		             end
		             mono_getMonoDatacollectorDLLVersion = function() return 21102025 end
		             """);

		LuaJsonResult<MonoAttachResult> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Attach, 100, new object?[] { MonoAttachmentId }),
			MonoJsonContext.Default.MonoAttachResult);
		InstallStubs("""
		             recordId, recordProcess = __cheatengine_mcp_mono.id, __cheatengine_mcp_mono.processId
		             recordBeat = rawequal(__cheatengine_mcp_mono.heartBeat, libmono.HeartBeat)
		             """);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal((true, true, 21102025L), (result.Value!.Attached, result.Value.Il2Cpp,
			result.Value.CollectorVersion));
		Assert.Equal(["collector_injected", "cheat_engine_hooks_installed", "uses_mono_table_option"],
			result.Value.HostEffects);
		Assert.Equal(1L, ReadGlobal("launches"));
		Assert.Equal((MonoAttachmentId, 77L, true),
			(ReadGlobal("recordId"), ReadGlobal("recordProcess"), ReadGlobal("recordBeat")));
	}

	[Theory]
	[InlineData("getTableOption = function(name) assert(name == 'UsesMono') return false end",
		new[] { "collector_injected", "mono_error_ok_patched", "cheat_engine_hooks_installed" })]
	[InlineData("getTableOption = function() error('no option') end",
		new[]
		{
			"collector_injected", "mono_error_ok_patched", "cheat_engine_hooks_installed", "uses_mono_table_option"
		})]
	public void MonoV2_Attach_ReportsOnlyTheHostEffectsCheatEngineCanHaveApplied(string stubs, string[] expected)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             pid = 77
		             getOpenedProcessID = function() return pid end
		             libmono = {monopipes = {}}
		             LaunchMonoDataCollector = function() libmono.ProcessID = pid libmono.IL2CPP = false return true end
		             """);
		InstallStubs(stubs);

		LuaJsonResult<MonoAttachResult> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Attach, 100, []), MonoJsonContext.Default.MonoAttachResult);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal((true, false, null), (result.Value!.Attached, result.Value.Il2Cpp, result.Value.CollectorVersion));
		Assert.Equal(expected, result.Value.HostEffects);
	}

	[Theory]
	[InlineData("libmono.ProcessID = 77", "busy", "not_started")]
	[InlineData("mono_AttachedProcess = 77", "busy", "not_started")]
	[InlineData("LaunchMonoDataCollector = function() launches = 1 return 0 end", "host_refused", "unknown")]
	[InlineData("LaunchMonoDataCollector = function() launches = 1 return true end", "host_refused", "unknown")]
	[InlineData("LaunchMonoDataCollector = nil", "unsupported", "not_started")]
	[InlineData("pid = 0", "not_attached", "not_started")]
	[InlineData("isPaused = function() return true end", "invalid_state", "not_started")]
	public void MonoV2_Attach_RefusesAForeignAttachmentAndReportsALaunchThatDidNotAttach(string stubs, string kind,
		string effect)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             pid = 77
		             getOpenedProcessID = function() return pid end
		             libmono = {monopipes = {}}
		             LaunchMonoDataCollector = function() launches = 1 libmono.ProcessID = pid return true end
		             """);
		InstallStubs(stubs);
		bool launchExpected = kind == "host_refused";

		LuaJsonResult<MonoAttachResult> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Attach, 100, new object?[] { MonoAttachmentId }),
			MonoJsonContext.Default.MonoAttachResult);
		InstallStubs("recorded = __cheatengine_mcp_mono ~= nil");

		Assert.True(result.IsError);
		Assert.Equal((kind, effect), (result.Error!.Kind, result.Error.HostEffect));
		Assert.Equal(launchExpected ? 1L : null, ReadGlobal("launches"));
		Assert.Equal(false, ReadGlobal("recorded"));
	}

	[Fact]
	public void MonoV2_Detach_FollowsCheatEngineOwnDetachWithoutTheTimeoutHandler()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             MONOCMD_TERMINATE = 22
		             getOpenedProcessID = function() return 77 end
		             getCurrentThreadID = function() return 5 end
		             written = {}
		             pipe = {writeByte = function(value) written[#written + 1] = value end,
		               OnTimeout = function() timeouts = (timeouts or 0) + 1 error('mono pipe timeout') end}
		             monopipe = pipe
		             mono_AttachedProcess = 77
		             mono_SymbolLookupID = 9
		             unregisterSymbolLookupCallback = function(id) unregistered = id end
		             libmono = {ProcessID = 77, monopipes = {[5] = pipe}}
		             libmono.terminate = function()
		               terminations = (terminations or 0) + 1
		               libmono.abort = true
		               libmono.monopipes = {}
		               monopipe = nil
		             end
		             """);
		InstallStubs(MonoOwnedStubs);

		LuaJsonResult<MonoDetachResult> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Detach, 100, new object?[] { MonoAttachmentId }),
			MonoJsonContext.Default.MonoDetachResult);
		InstallStubs("""
		             firstWritten = written[1]
		             processCleared = libmono.ProcessID == nil
		             recordCleared = __cheatengine_mcp_mono == nil
		             """);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal((true, false), (result.Value!.Detached, result.Value.AlreadyEnded));
		Assert.Equal(22L, ReadGlobal("firstWritten"));
		Assert.Equal(1L, ReadGlobal("terminations"));
		Assert.Equal(true, ReadGlobal("processCleared"));
		Assert.Null(ReadGlobal("mono_AttachedProcess"));
		Assert.Equal(9L, ReadGlobal("unregistered"));
		Assert.Null(ReadGlobal("mono_SymbolLookupID"));
		Assert.Null(ReadGlobal("timeouts"));
		Assert.Equal(true, ReadGlobal("recordCleared"));
	}

	[Fact]
	public void MonoV2_Detach_DeadPipeStillTerminatesButAFailedTerminateIsReportedAndItsRetryRunsAgain()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             MONOCMD_TERMINATE = 22
		             getOpenedProcessID = function() return 77 end
		             getCurrentThreadID = function() return 5 end
		             monopipe = {writeByte = function() error('mono pipe error') end}
		             libmono = {ProcessID = 77, monopipes = {}}
		             libmono.terminate = function() terminations = (terminations or 0) + 1 end
		             """);
		InstallStubs(MonoOwnedStubs);

		LuaJsonResult<MonoDetachResult> detached = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Detach, 100, new object?[] { MonoAttachmentId }),
			MonoJsonContext.Default.MonoDetachResult);
		InstallStubs("""
		             libmono.ProcessID = 77
		             mono_AttachedProcess = 77
		             libmono.terminate = function() error('called libmono.terminate from outside the main thread') end
		             """);
		InstallStubs(MonoOwnedStubs);
		LuaJsonResult<MonoDetachResult> failed = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Detach, 100, new object?[] { MonoAttachmentId }),
			MonoJsonContext.Default.MonoDetachResult);
		InstallStubs("""
		             processCleared = libmono.ProcessID == nil
		             recordIncomplete = __cheatengine_mcp_mono.incomplete == true
		             libmono.terminate = function() terminations = terminations + 1 end
		             """);
		LuaJsonResult<MonoDetachResult> retried = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Detach, 100, new object?[] { MonoAttachmentId }),
			MonoJsonContext.Default.MonoDetachResult);
		InstallStubs("recordCleared = __cheatengine_mcp_mono == nil");

		Assert.False(detached.IsError, detached.Error?.Message);
		Assert.False(detached.Value!.AlreadyEnded);
		Assert.True(failed.IsError);
		Assert.Equal(("host_refused", "started"), (failed.Error!.Kind, failed.Error.HostEffect));
		Assert.Contains("outside the main thread", failed.Error.Message, StringComparison.Ordinal);
		Assert.Equal((true, true), (ReadGlobal("processCleared"), ReadGlobal("recordIncomplete")));
		Assert.Null(ReadGlobal("mono_AttachedProcess"));
		Assert.False(retried.IsError, retried.Error?.Message);
		Assert.False(retried.Value!.AlreadyEnded);
		Assert.Equal((2L, true), (ReadGlobal("terminations"), ReadGlobal("recordCleared")));
	}

	[Theory]
	// The user deactivated and re-activated Mono features: Cheat Engine launched a new heart beat.
	[InlineData("libmono.HeartBeat = {name = 'heart beat of a later launch'}", false)]
	// Cheat Engine or the user detached the collector: libmono.terminate cleared ProcessID.
	[InlineData("libmono.ProcessID = nil; mono_AttachedProcess = nil", false)]
	// A target switch in Cheat Engine, after which UsesMono attached the new process.
	[InlineData("pid = 88; libmono.ProcessID = 88; mono_AttachedProcess = 88", false)]
	// No record: this activation's attachment was already released.
	[InlineData("__cheatengine_mcp_mono = nil", false)]
	// An incomplete detach, after which the user re-activated Mono features.
	[InlineData("__cheatengine_mcp_mono.incomplete = true; libmono.HeartBeat = {name = 'heart beat of a later launch'}",
		false)]
	// The record of another activation.
	[InlineData("__cheatengine_mcp_mono.id = 'mono-feedface-1'", true)]
	public void MonoV2_Detach_AnAttachmentThatEndedOutsideMcp_LeavesTheCurrentOneUnchanged(string stubs,
		bool recordKept)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             MONOCMD_TERMINATE = 22
		             pid = 77
		             getOpenedProcessID = function() return pid end
		             getCurrentThreadID = function() return 5 end
		             pipe = {writeByte = function(value) written = value end}
		             monopipe = pipe
		             mono_AttachedProcess = 77
		             mono_SymbolLookupID = 9
		             unregisterSymbolLookupCallback = function(id) unregistered = id end
		             libmono = {ProcessID = 77, monopipes = {[5] = pipe}}
		             libmono.terminate = function() terminations = (terminations or 0) + 1 end
		             """);
		InstallStubs(MonoOwnedStubs);
		InstallStubs(stubs);
		InstallStubs("""
		             expectedProcess = libmono.ProcessID
		             expectedAttached = mono_AttachedProcess
		             """);

		LuaJsonResult<MonoDetachResult> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Detach, 100, new object?[] { MonoAttachmentId }),
			MonoJsonContext.Default.MonoDetachResult);
		InstallStubs("""
		             processKept = libmono.ProcessID == expectedProcess
		             attachedKept = mono_AttachedProcess == expectedAttached
		             recordKept = __cheatengine_mcp_mono ~= nil
		             """);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal((true, true), (result.Value!.Detached, result.Value.AlreadyEnded));
		Assert.Equal(
		[
			"collector_dll_remains_loaded", "mono_error_ok_patch_remains_until_target_restart",
			"uses_mono_table_option_remains"
		], result.Value.RemainingEffects);
		Assert.Null(ReadGlobal("written"));
		Assert.Null(ReadGlobal("terminations"));
		Assert.Null(ReadGlobal("unregistered"));
		Assert.Equal(9L, ReadGlobal("mono_SymbolLookupID"));
		Assert.Equal((true, true, recordKept),
			(ReadGlobal("processKept"), ReadGlobal("attachedKept"), ReadGlobal("recordKept")));
	}

	[Fact]
	public void MonoV2_Detach_AnotherSelectedProcess_TerminatesTheRecordedAttachmentWithoutWritingToItsPipe()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             MONOCMD_TERMINATE = 22
		             getOpenedProcessID = function() return 88 end
		             getCurrentThreadID = function() return 5 end
		             pipe = {writeByte = function(value) written = value end}
		             monopipe = pipe
		             libmono = {ProcessID = 77, monopipes = {[5] = pipe}}
		             libmono.terminate = function() terminations = (terminations or 0) + 1 end
		             """);
		InstallStubs(MonoOwnedStubs);

		LuaJsonResult<MonoDetachResult> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Detach, 100, new object?[] { MonoAttachmentId }),
			MonoJsonContext.Default.MonoDetachResult);
		InstallStubs("processCleared = libmono.ProcessID == nil");

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal((true, false), (result.Value!.Detached, result.Value.AlreadyEnded));
		Assert.Null(ReadGlobal("written"));
		Assert.Equal(1L, ReadGlobal("terminations"));
		Assert.Equal(true, ReadGlobal("processCleared"));
	}

	[Fact]
	public void MonoV2_ListFields_BypassesCheatEngineIncludeParentsBlindCacheAndRestoresIt()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             monocache = {fields = {[4096] = {{field = 1, name = 'stale', offset = 0, flags = 0, isStatic = false}}}}
		             mono_class_enumFields = function(class, includeParents)
		               observedParents = includeParents
		               if monocache.fields[class] then return monocache.fields[class] end
		               local list = {{field = 2, name = 'own', typename = 'System.Int32', offset = 16, flags = 6, isStatic = false, isConst = false}}
		               if includeParents then
		                 list[2] = {field = 3, name = 'Instance', typename = 'Game', offset = 8, flags = 0x16, isStatic = true, isConst = false, staticAddress = 0x7FF00008}
		                 list[3] = {field = 4, name = 'Max', typename = 'System.Int32', offset = 0, flags = 0x56, isStatic = true, isConst = true}
		               end
		               monocache.fields[class] = list
		               return list
		             end
		             """);

		LuaJsonResult<MonoFieldList> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Fields, 100, new object?[] { "4096", true, 512 }),
			MonoJsonContext.Default.MonoFieldList);
		InstallStubs("restoredName = monocache.fields[4096][1].name");

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(true, ReadGlobal("observedParents"));
		Assert.Equal(
		[
			new MonoField("2", "own", "System.Int32", 16, false, false, 6),
			new MonoField("3", "Instance", "Game", 8, true, false, 0x16, "7FF00008"),
			new MonoField("4", "Max", "System.Int32", 0, true, true, 0x56)
		], result.Value!.Fields);
		Assert.Equal("stale", ReadGlobal("restoredName"));
	}

	[Theory]
	[InlineData(null, 0, 100, "256,512,768,1024,1280", 5, null)]
	// Namespace-qualified and ASCII case-insensitive: Game.Player, PlayerStats and Game.PLAYER.Health match.
	[InlineData("player", 1, 1, "768", 3, 2)]
	[InlineData("GAME.p", 0, 100, "256,1024", 2, null)]
	// Matched literally, never as a Lua pattern: every name with a dot, which PlayerStats lacks.
	[InlineData(".", 0, 100, "256,512,1024,1280", 4, null)]
	[InlineData("%a", 0, 100, "", 0, null)]
	public void MonoV2_ListClasses_NameContainsFiltersQualifiedNamesBeforeThePage(string? filter, int offset,
		int limit, string handles, int total, int? nextOffset)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             mono_image_enumClasses = function(image)
		               return {{class = 0x100, classname = 'Player', namespace = 'Game'}, {class = 0x200, classname = 'Enemy', namespace = 'Game'},
		                 {class = 0x300, classname = 'PlayerStats', namespace = ''}, {class = 0x400, classname = 'Health', namespace = 'Game.PLAYER'},
		                 {class = 0x500, classname = 'Other', namespace = 'System'}}
		             end
		             """);

		LuaJsonResult<MonoClassPage> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Classes, 100, new object?[] { "4096", offset, limit, filter }),
			MonoJsonContext.Default.MonoClassPage);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(handles, string.Join(',', result.Value!.Classes.Select(static item => item.Handle)));
		Assert.Equal((total, nextOffset), (result.Value.Total, result.Value.NextOffset));
	}

	[Fact]
	public void MonoV2_ListFields_NameContainsFiltersBeforeTheFieldLimit()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             mono_class_enumFields = function()
		               return {{field = 1, name = 'armor', offset = 16}, {field = 2, name = 'health', offset = 20},
		                 {field = 3, name = '<Health>k__BackingField', offset = 24}, {field = 4, name = 'maxHealth', offset = 28}}
		             end
		             """);

		LuaJsonResult<MonoFieldList> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Fields, 100, new object?[] { "4096", false, 2, "HEALTH" }),
			MonoJsonContext.Default.MonoFieldList);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(["health", "<Health>k__BackingField"], result.Value!.Fields.Select(static field => field.Name));
	}

	[Theory]
	[InlineData(null, "Heal,TakeDamage,GetHealth,Update", 4)]
	[InlineData("heal", "Heal,GetHealth", 2)]
	public void MonoV2_ListMethods_NameContainsDescribesOnlyTheMatchingMethods(string? filter, string names,
		long signatures)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             mono_class_enumMethods = function()
		               return {{method = 1, name = 'Heal', flags = 0}, {method = 2, name = 'TakeDamage', flags = 0},
		                 {method = 3, name = 'GetHealth', flags = 0}, {method = 4, name = 'Update', flags = 0}}
		             end
		             mono_method_getSignature = function() signatureCalls = (signatureCalls or 0) + 1 return '', {}, 'System.Void' end
		             """);

		LuaJsonResult<MonoMethodPage> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Methods, 100, new object?[] { "4096", 0, 100, filter }),
			MonoJsonContext.Default.MonoMethodPage);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(names, string.Join(',', result.Value!.Methods.Select(static method => method.Name)));
		Assert.Equal(((int) signatures, (int?) null), (result.Value.Total, result.Value.NextOffset));
		Assert.Equal(signatures, ReadGlobal("signatureCalls"));
	}

	[Fact]
	public void MonoV2_ListMethods_ReportsParameterNamesFlagsAndTheStaticBit()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             mono_class_enumMethods = function() return {{method = 100, name = 'Heal', flags = 0x86}, {method = 200, name = 'Create', flags = 0x96}} end
		             mono_method_getSignature = function(method)
		               if method == 100 then return 'int,float', {'amount', 'scale'}, 'System.Void' end
		               return '', {}, 'Game.Player'
		             end
		             """);

		LuaJsonResult<MonoMethodPage> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Methods, 100, new object?[] { "4096", 0, 100 }),
			MonoJsonContext.Default.MonoMethodPage);

		Assert.False(result.IsError, result.Error?.Message);
		MonoMethod heal = result.Value!.Methods[0];
		MonoMethod create = result.Value.Methods[1];
		Assert.Equal(("100", "Heal", "int,float", "System.Void", 0x86L, false),
			(heal.Handle, heal.Name, heal.Signature, heal.ReturnType, heal.Flags, heal.IsStatic));
		Assert.Equal(["amount", "scale"], heal.ParameterNames!);
		Assert.Equal(("200", "Create", "", "Game.Player", 0x96L, true),
			(create.Handle, create.Name, create.Signature, create.ReturnType, create.Flags, create.IsStatic));
		Assert.Empty(create.ParameterNames!);
	}

	[Fact]
	public void MonoV2_FindMethod_ReadsTheFlagsOfTheFoundMethod()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             mono_class_findMethod = function(class, name) observedName = name return 300 end
		             mono_method_getName = function() return 'Parse' end
		             mono_method_getFlags = function() return 0x16 end
		             mono_method_getSignature = function() return 'string', {'text'}, 'System.Boolean' end
		             """);

		LuaJsonResult<MonoMethod> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.FindMethod, 100, new object?[] { "4096", "Parse" }),
			MonoJsonContext.Default.MonoMethod);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(("300", "Parse", "string", "System.Boolean", 0x16L, true),
			(result.Value!.Handle, result.Value.Name, result.Value.Signature, result.Value.ReturnType,
				result.Value.Flags, result.Value.IsStatic));
		Assert.Equal(["text"], result.Value.ParameterNames!);
		Assert.Equal("Parse", ReadGlobal("observedName"));
	}

	[Fact]
	public void MonoV2_StaticFieldAddress_RefusesANonNumericDomainAndDefaultsToTheFirstDomain()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             mono_enumDomains = function() return {0x3000} end
		             mono_class_getStaticFieldAddress = function(domain, class) observedDomain = domain return 0x7FF01000 end
		             """);

		LuaJsonResult<MonoStaticFieldAddress> refused = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.StaticFieldAddress, 100, new object?[] { "4096", "root" }),
			MonoJsonContext.Default.MonoStaticFieldAddress);
		LuaJsonResult<MonoStaticFieldAddress> defaulted = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.StaticFieldAddress, 100, new object?[] { "4096", null }),
			MonoJsonContext.Default.MonoStaticFieldAddress);

		Assert.True(refused.IsError);
		Assert.Equal(("invalid_argument", "not_started"), (refused.Error!.Kind, refused.Error.HostEffect));
		Assert.False(defaulted.IsError, defaulted.Error?.Message);
		Assert.Equal(new MonoStaticFieldAddress("4096", "12288", "7FF01000"), defaulted.Value);
		Assert.Equal(0x3000L, ReadGlobal("observedDomain"));
	}

	[Fact]
	public void MonoV2_InvokeMethod_ResolvesAHexInstanceAddressBeforeCallingTheTarget()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             getAddressSafe = function(value)
		               observedExpression = value
		               if value == '401000' then return 0x401000 end
		               return nil
		             end
		             mono_method_get_parameters = function(method)
		               observedMethod = method
		               return {parameters = {{name = 'amount', type = 8}}}
		             end
		             mono_invoke_method = function(domain, method, instance, arguments)
		               observedDomain = domain
		               observedInvokeMethod = method
		               observedInstance = instance
		               observedArgumentType = arguments[1].type
		               observedArgumentValue = arguments[1].value
		               return 17, nil
		             end
		             """);

		LuaJsonResult<MonoMethodInvocation> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.InvokeMethod, 100,
				new object?[] { "42", "401000", new object?[] { new object?[] { 2L, 7L } } }),
			MonoJsonContext.Default.MonoMethodInvocation);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(new MonoMethodInvocation(true, "17"), result.Value);
		Assert.Equal("401000", ReadGlobal("observedExpression"));
		Assert.Equal(42L, ReadGlobal("observedMethod"));
		Assert.Null(ReadGlobal("observedDomain"));
		Assert.Equal(42L, ReadGlobal("observedInvokeMethod"));
		Assert.Equal(0x401000L, ReadGlobal("observedInstance"));
		Assert.Equal(2L, ReadGlobal("observedArgumentType"));
		Assert.Equal(7L, ReadGlobal("observedArgumentValue"));
	}

	[Fact]
	public void MonoV2_InvokeMethod_PassesAPointerArgumentAsAnAddressNumberNotAString()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             getAddressSafe = function(value)
		               if value == 'player+10' then return 0x5010 end
		               if value == '0' then return 0 end
		               return nil
		             end
		             mono_method_get_parameters = function() return {parameters = {{type = 18}, {type = 18}, {type = 14}}} end
		             mono_invoke_method = function(_, _, _, arguments)
		               firstValue = arguments[1].value
		               nullValue = arguments[2].value
		               textValue = arguments[3].value
		               return {y = 2, x = 1}, nil
		             end
		             """);

		LuaJsonResult<MonoMethodInvocation> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.InvokeMethod, 100,
				new object?[]
				{
					"42", null,
					new object?[]
					{
						new object?[] { 12L, "player+10" }, new object?[] { 12L, "0" }, new object?[] { 6L, "text" }
					}
				}), MonoJsonContext.Default.MonoMethodInvocation);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(new MonoMethodInvocation(true, "x=1, y=2"), result.Value);
		Assert.Equal(0x5010L, ReadGlobal("firstValue"));
		Assert.Equal(0L, ReadGlobal("nullValue"));
		Assert.Equal("text", ReadGlobal("textValue"));
	}

	[Theory]
	[InlineData("unresolved", 1, "invalid_argument", "not_started")]
	[InlineData("401000", 2, "invalid_argument", "not_started")]
	public void MonoV2_InvokeMethod_RefusesBeforeCallingTheTarget(string address, int count, string kind,
		string effect)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             getAddressSafe = function(value) if value == '401000' then return 0x401000 end return nil end
		             mono_method_get_parameters = function() return {parameters = {{type = 18}}} end
		             mono_invoke_method = function()
		               invocationCount = (invocationCount or 0) + 1
		               return 17, nil
		             end
		             """);
		object?[] arguments = [.. Enumerable.Repeat<object?>(new object?[] { 12L, address }, count)];

		LuaJsonResult<MonoMethodInvocation> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.InvokeMethod, 100, new object?[] { "42", null, arguments }),
			MonoJsonContext.Default.MonoMethodInvocation);

		Assert.True(result.IsError);
		Assert.Equal((kind, effect), (result.Error!.Kind, result.Error.HostEffect));
		Assert.Null(ReadGlobal("invocationCount"));
	}

	[Fact]
	public void MonoV2_InvokeMethod_UnresolvableInstanceAddress_RefusesBeforeCallingTheTarget()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             getAddressSafe = function(_) return nil end
		             mono_invoke_method = function()
		               invocationCount = (invocationCount or 0) + 1
		               return 17, nil
		             end
		             """);

		LuaJsonResult<MonoMethodInvocation> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.InvokeMethod, 100,
				new object?[] { "42", "not-an-address", Array.Empty<object?>() }),
			MonoJsonContext.Default.MonoMethodInvocation);

		Assert.True(result.IsError);
		Assert.Equal(("invalid_argument", "not_started"), (result.Error!.Kind, result.Error.HostEffect));
		Assert.Null(ReadGlobal("invocationCount"));
	}

	[Fact]
	public void MonoV2_InvokeMethod_SeparatesAManagedExceptionFromACheatEngineFailure()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             mono_method_get_parameters = function() return {parameters = {}} end
		             mono_invoke_method = function() return 0, 'System.NullReferenceException: Object reference not set' end
		             """);

		LuaJsonResult<MonoMethodInvocation> threw = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.InvokeMethod, 100, new object?[] { "42", null, null }),
			MonoJsonContext.Default.MonoMethodInvocation);
		InstallStubs("mono_invoke_method = function() return nil, 'mono pipe timeout' end");
		LuaJsonResult<MonoMethodInvocation> failed = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.InvokeMethod, 100, new object?[] { "42", null, null }),
			MonoJsonContext.Default.MonoMethodInvocation);

		Assert.False(threw.IsError, threw.Error?.Message);
		Assert.Equal(new MonoMethodInvocation(true, "0",
			"System.NullReferenceException: Object reference not set"), threw.Value);
		Assert.True(failed.IsError);
		Assert.Equal(("host_refused", "unknown"), (failed.Error!.Kind, failed.Error.HostEffect));
		Assert.Contains("mono pipe timeout", failed.Error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void MonoV2_Instances_ScanForTheVtableWithoutRunningManagedCode()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             soExactValue, vtDword, vtQword, rtRounded, fsmAligned = 0, 2, 3, 2, 1
		             targetIs64Bit = function() return true end
		             mono_class_getVTable = function(domain, class) observedVTableArguments = {domain, class} return 0x7FF012340 end
		             mono_class_findInstancesOfClassListOnly = function() managedCalls = (managedCalls or 0) + 1 return {} end
		             mono_invoke_method = function() managedCalls = (managedCalls or 0) + 1 end
		             createMemScan = function()
		               local scan = {}
		               scan.firstScan = function(option, vartype, rounding, input1, input2, start, stop, protection, alignment, alignmentParameter, hexadecimal)
		                 scanned = {option = option, vartype = vartype, input1 = input1, start = start, stop = stop, alignment = alignment, alignmentParameter = alignmentParameter, hexadecimal = hexadecimal}
		               end
		               scan.waitTillDone = function() waited = true end
		               scan.destroy = function() scanDestroyed = true end
		               return scan
		             end
		             createFoundList = function(scan)
		               local list = {Count = 3, [0] = '1A0000', [1] = '0000001B0010', [2] = '1C0020'}
		               list.initialize = function() initialized = true end
		               list.destroy = function() listDestroyed = true end
		               return list
		             end
		             """);

		LuaJsonResult<MonoInstanceBatch> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Instances, 100, new object?[] { "4096", 2 }),
			MonoJsonContext.Default.MonoInstanceBatch);
		InstallStubs("""
		             scannedVartype, scannedInput, scannedStop = scanned.vartype, scanned.input1, scanned.stop
		             scannedAlignment, scannedHex = scanned.alignmentParameter, scanned.hexadecimal
		             vtableClass = observedVTableArguments[1]
		             """);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(3, result.Value!.Total);
		Assert.Equal([new MonoInstance("1A0000"), new MonoInstance("1B0010")], result.Value.Instances);
		Assert.Equal(4096L, ReadGlobal("vtableClass"));
		Assert.Equal((3L, "7FF012340", long.MaxValue, "8", true),
			(ReadGlobal("scannedVartype"), ReadGlobal("scannedInput"), ReadGlobal("scannedStop"),
				ReadGlobal("scannedAlignment"), ReadGlobal("scannedHex")));
		Assert.Equal((true, true, true, true),
			(ReadGlobal("waited"), ReadGlobal("initialized"), ReadGlobal("listDestroyed"),
				ReadGlobal("scanDestroyed")));
		Assert.Null(ReadGlobal("managedCalls"));
	}

	[Theory]
	[InlineData("mono_class_getVTable = function() return nil end", "not_found", "not_started", false)]
	[InlineData("createMemScan = function() local s = {} s.firstScan = function() error('scan failed') end " +
				"s.destroy = function() scanDestroyed = true end return s end", "host_refused", "unknown", true)]
	public void MonoV2_Instances_NoVtableOrAFailedScanIsReportedAndTheScannerFreed(string stubs, string kind,
		string effect, bool scanned)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs("""
		             soExactValue, vtDword, vtQword, rtRounded, fsmAligned = 0, 2, 3, 2, 1
		             mono_class_getVTable = function() return 0x1000 end
		             createMemScan = function() scans = (scans or 0) + 1 return {} end
		             """);
		InstallStubs(stubs);

		LuaJsonResult<MonoInstanceBatch> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Instances, 100, new object?[] { "4096", 2 }),
			MonoJsonContext.Default.MonoInstanceBatch);

		Assert.True(result.IsError);
		Assert.Equal((kind, effect), (result.Error!.Kind, result.Error.HostEffect));
		Assert.Equal(scanned ? true : null, ReadGlobal("scanDestroyed"));
		Assert.Null(ReadGlobal("scans"));
	}
}
