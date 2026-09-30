using System.Reflection;
using System.Text;
using System.Text.Json;

using CheatEngine.Client.Assembly;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Asm;
using CheatEngine.Mcp.Tools.Debugger;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>Native Lua coverage for the v2 assembler and debugger fixed script bodies.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void AsmV2_GenerateApiHook_JoinsCheatEnginesTwoSectionsAndPreservesEveryArgument()
	{
		using RuntimeScope scope = CreateScope();
		// Cheat Engine 7.7 returns the ENABLE and DISABLE texts separately, without headers and with CRLF breaks.
		InstallStubs("""
		             generateAPIHookScript = function(address, destination, newCall, extension, targetSelf)
		             	observedAddress = address
		             	observedDestination = destination
		             	observedNewCall = newCall
		             	observedExtension = extension
		             	observedTargetSelf = targetSelf
		             	return 'alloc(originalcallx64,1024,401000)\r\n401000:\r\njmp 401005\r\n',
		             		'401000:\r\ndb 55 48 8B EC 90\r\ndealloc(originalcallx64)\r\n'
		             end
		             """);
		AsmTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		AsmGeneratedScript generated = tools.GenerateApiHook("401000", "401005", "newCall", "x64", true, Token);

		Assert.Equal(
			"[ENABLE]\nalloc(originalcallx64,1024,401000)\n401000:\njmp 401005\n\n[DISABLE]\n401000:\ndb 55 48 8B EC 90\ndealloc(originalcallx64)\n",
			generated.Script);
		Assert.Equal((string.Empty, null), (generated.ExpectedBytes, generated.SymbolName));
		Assert.Equal("401000", ReadGlobal("observedAddress"));
		Assert.Equal("401005", ReadGlobal("observedDestination"));
		Assert.Equal("newCall", ReadGlobal("observedNewCall"));
		Assert.Equal("x64", ReadGlobal("observedExtension"));
		Assert.Equal(true, ReadGlobal("observedTargetSelf"));
		LuaFixedScriptAssert.NeverLoadsCode(AsmScripts.GenerateApiHook);
	}

	[Fact]
	public void AsmV2_GenerateApiHook_CheatEnginesX64Layout_PassesTheAsmCheckScreen()
	{
		using RuntimeScope scope = CreateScope();
		// The x64 text of frmautoinjectunit.pas generateAPIHookScript, whose trampoline line ends in a comment that
		// holds "region (64-bit)"; Cheat Engine removes the comment before it matches commands.
		InstallStubs("""
		             generateAPIHookScript = function()
		             	return 'alloc(originalcall0,1024,kernel32.Sleep)\r\n' ..
		             		'alloc(jumptrampoline0,64,kernel32.Sleep); //special jump trampoline in the current region (64-bit)\r\n' ..
		             		'label(jumptrampoline0address)\r\nlabel(returnhere0)\r\n\r\n\r\noriginalcall0:\r\n' ..
		             		'mov [rsp+08],rbx\r\njmp returnhere0\r\n\r\njumptrampoline0:\r\njmp [jumptrampoline0address]\r\n' ..
		             		'jumptrampoline0address:\r\ndq myHook\r\n\r\nkernel32.Sleep:\r\njmp jumptrampoline0\r\nreturnhere0:\r\n',
		             		'kernel32.Sleep:\r\ndb 48 89 5C 24 08\r\ndealloc(originalcall0)\r\ndealloc(jumptrampoline0)\r\n'
		             end
		             """);
		AsmTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		AsmGeneratedScript generated = tools.GenerateApiHook("kernel32.Sleep", "myHook", cancellationToken: Token);

		Assert.StartsWith("[ENABLE]\nalloc(originalcall0,1024,kernel32.Sleep)\n", generated.Script,
			StringComparison.Ordinal);
		Assert.Contains("\n[DISABLE]\nkernel32.Sleep:\ndb 48 89 5C 24 08\n", generated.Script, StringComparison.Ordinal);
		Assert.DoesNotContain('\r', generated.Script);
		Assert.Equal([McpFeature.AutoAssembler], AsmCheckScreen.Screen(generated.Script).Features);
	}

	[Fact]
	public void AsmV2_GenerateApiHook_OneSectionOrNoText_IsADeclaredHostRefusal()
	{
		using RuntimeScope scope = CreateScope();
		AsmTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());
		List<CheatEngineToolException> refused = [];
		foreach (string stub in (string[])
				 [
					 "generateAPIHookScript = function() return 'nop' end",
					 "generateAPIHookScript = function() return '  \\r\\n', '' end",
					 "generateAPIHookScript = function() return nil end"
				 ])
		{
			InstallStubs(stub);
			refused.Add(Assert.Throws<CheatEngineToolException>(() =>
				tools.GenerateApiHook("401000", "401005", cancellationToken: Token)));
		}

		Assert.All(refused, static exception =>
		{
			Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Completed),
				(exception.Error.Kind, exception.Error.HostEffect));
			Assert.Equal("Cheat Engine returned no API-hook script.", exception.Error.Message);
		});
	}

	[Fact]
	public void AsmV2_GenerateApiHook_CheatEngineRaises_ReportsItsMessageAsAHostRefusal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("generateAPIHookScript = function(address) error(address .. ':Unknown symbol') end");
		AsmTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.GenerateApiHook("missingSymbol", "401005", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Completed),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.StartsWith("Cheat Engine could not generate an API-hook script: ", exception.Error.Message,
			StringComparison.Ordinal);
		Assert.Contains("missingSymbol:Unknown symbol", exception.Error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AsmV2_GenerateApiHook_FunctionMissing_IsUnsupported()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("generateAPIHookScript = nil");
		AsmTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.GenerateApiHook("401000", "401005", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	[Fact]
	public void AsmV2_CheckDisable_ChecksOnlyTheDisableSectionAndBoundsTheMessage()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             autoAssembleCheck = function(script, enable, targetSelf)
		             	observedScript = script
		             	observedEnable = enable
		             	observedTargetSelf = targetSelf
		             	return false, string.rep('x', 5000)
		             end
		             """);
		const string script = "[ENABLE]\nnop\n[DISABLE]\nbad";

		AsmCheckResult result = AcceptingEnableCheck().Check(script, cancellationToken: Token);

		Assert.Equal((false, AsmScriptSection.Disable, true),
			(result.Accepted, result.FailedSection, result.HostMessagesTruncated));
		Assert.Equal(new string('x', AsmTools.MaximumHostMessageBytes), result.HostMessages);
		Assert.Equal(script, ReadGlobal("observedScript"));
		Assert.Equal(false, ReadGlobal("observedEnable"));
		Assert.Equal(false, ReadGlobal("observedTargetSelf"));
		LuaFixedScriptAssert.NeverLoadsCode(AsmScripts.CheckDisable);
	}

	[Fact]
	public void AsmV2_CheckDisable_AcceptedSection_ReportsBothSectionsAccepted()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("autoAssembleCheck = function(_, _, _) return true end");

		AsmCheckResult result = AcceptingEnableCheck().Check("[ENABLE]\nnop\n[DISABLE]\nnop", cancellationToken: Token);

		Assert.Equal(new AsmCheckResult(true), result);
	}

	[Fact]
	public void AsmV2_CheckDisable_RejectionWithoutText_OmitsHostMessages()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("autoAssembleCheck = function(_, _, _) return false end");

		AsmCheckResult result = AcceptingEnableCheck().Check("[ENABLE]\nnop\n[DISABLE]\nnop", cancellationToken: Token);

		Assert.Equal(new AsmCheckResult(false, null, false, AsmScriptSection.Disable), result);
	}

	[Fact]
	public void AsmV2_CheckDisable_RaisingHost_ReportsHostRefusal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("autoAssembleCheck = function(_, _, _) error('checker fault') end");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			AcceptingEnableCheck().Check("[ENABLE]\nnop\n[DISABLE]\nnop", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Unknown),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	[Fact]
	public void DebuggerV2_FixedLuaBodies_CompileAndNeverLoadCallerCode()
	{
		using RuntimeScope scope = CreateScope();
		KeyValuePair<string, string>[] scripts =
		[
			.. typeof(DebuggerLuaScripts)
				.GetFields(BindingFlags.NonPublic | BindingFlags.Static)
				.Where(static field => field.FieldType == typeof(string))
				.Select(static field =>
					new KeyValuePair<string, string>(field.Name, (string) field.GetRawConstantValue()!))
		];

		Assert.NotEmpty(scripts);
		foreach ((string name, string body) in scripts)
		{
			Assert.DoesNotContain("load(", body, StringComparison.Ordinal);
			Assert.DoesNotContain("loadstring(", body, StringComparison.Ordinal);
			Assert.DoesNotContain("dofile(", body, StringComparison.Ordinal);
			Assert.DoesNotContain("loadfile(", body, StringComparison.Ordinal);
			Assert.DoesNotContain("require(", body, StringComparison.Ordinal);
			AssertDebuggerScriptCompiles(name, LuaToolRuntime.BuildSource(body, 100,
				["mcp", "resource", 8L, 1_000L, "401000", "execute", 1L, false, null, 0L, false]));
		}
	}

	[Fact]
	public void DebuggerV2_StatusContextAndRegisterWrite_ExecuteWithTypedResults()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             RAX = 1
		             guiUpdates = 0
		             debug_isDebugging = function() return true end
		             debug_canBreak = function() return true end
		             debug_isBroken = function() return true end
		             debug_isStepping = function() return false end
		             debug_getContext = function(_) return true end
		             debug_getCurrentDebuggerInterface = function() return 1 end
		             targetIs64Bit = function() return true end
		             targetIsX86 = function() return true end
		             debug_getCurrentContextTable = function(_) return {RAX = RAX, RIP = 4096} end
		             debug_setContext = function(_) return true end
		             debug_updateGUI = function() guiUpdates = guiUpdates + 1 end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		DebuggerStatus status = dispatch.RunLua("debugger_get_status", DebuggerLuaScripts.Status,
			DebuggerJsonContext.Default.DebuggerStatus, Token);
		DebuggerContext context = dispatch.RunLua("debugger_get_context", DebuggerLuaScripts.GetContext,
			DebuggerJsonContext.Default.DebuggerContext, Token, false);
		DebuggerRegisterSet written = dispatch.RunLua("debugger_set_register", DebuggerLuaScripts.SetRegister,
			DebuggerJsonContext.Default.DebuggerRegisterSet, Token, "EAX", -1L);

		Assert.True(status.StateValid);
		Assert.True(status.Attached);
		Assert.True(status.CanBreak);
		Assert.True(status.Broken);
		Assert.True(status.ReportedBroken);
		Assert.False(status.Stepping);
		Assert.Equal(DebuggerInterface.Windows, status.ActiveInterface);
		Assert.True(context.Is64Bit);
		Assert.False(context.IncludesExtraRegisters);
		Assert.Equal("1", context.Registers["RAX"]);
		Assert.Equal("1000", context.Registers["RIP"]);
		Assert.Equal(new DebuggerRegisterSet("EAX", "RAX", "FFFFFFFF", true), written);
		Assert.Equal((4294967295L, 1L), (ReadGlobal("RAX"), ReadGlobal("guiUpdates")));
	}

	[Fact]
	public void DebuggerV2_Status_UnattachedWithOpaqueBrokenResult_IsValidAndNotStopped()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
			debug_isDebugging = function() return false end
			debug_canBreak = function() return false end
			debug_isBroken = function() error('An unattached debugger has no broken state') end
			debug_isStepping = function() return false end
			debug_getContext = function(_) error('An unattached debugger has no context') end
			""");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		DebuggerStatus status = dispatch.RunLua("debugger_get_status", DebuggerLuaScripts.Status,
			DebuggerJsonContext.Default.DebuggerStatus, Token);

		Assert.Equal(new DebuggerStatus(true, false, false, false, false, false), status);
	}

	[Fact]
	public void DebuggerV2_Attach_EmitsContractInterfaceNames()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             debugAttached = false
		             activeInterface = 0
		             debugProcessCalls = 0
		             debug_isDebugging = function() return debugAttached end
		             debug_getCurrentDebuggerInterface = function() return activeInterface end
		             getOpenedProcessID = function() return 77 end
		             getProcesslist = function() return {[77] = true} end
		             debugProcess = function(requested)
		             	debugProcessCalls = debugProcessCalls + 1
		             	requestedInterface = requested
		             	activeInterface = requested == 0 and 1 or requested
		             	debugAttached = true
		             end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		DebuggerAttachment veh = dispatch.RunLua("debugger_attach", DebuggerLuaScripts.Attach,
			DebuggerJsonContext.Default.DebuggerAttachment, Token, 2L, 77L, "77:1");
		DebuggerAttachment @default = dispatch.RunLua("debugger_attach", DebuggerLuaScripts.Attach,
			DebuggerJsonContext.Default.DebuggerAttachment, Token, 0L, 77L, "77:1");

		Assert.Equal((DebuggerInterface.Veh, DebuggerInterface.Veh, false, false),
			(veh.RequestedInterface, veh.ActiveInterface, veh.AlreadyAttached, veh.UsedFallback));
		Assert.Equal((DebuggerInterface.Default, DebuggerInterface.Veh, true, false),
			(@default.RequestedInterface, @default.ActiveInterface, @default.AlreadyAttached, @default.UsedFallback));
		Assert.Equal((1L, 2L), (ReadGlobal("debugProcessCalls"), ReadGlobal("requestedInterface")));
	}

	[Fact]
	public void DebuggerV2_Attach_InterfaceMcpCannotDrive_IsUnsupportedInsteadOfAnAssertion()
	{
		using RuntimeScope scope = CreateScope();
		// CE 7.7 pushes no integer for its DBVM or ceserver interface, so Lua sees whatever the stack holds.
		InstallStubs("""
		             debugAttached = false
		             debug_isDebugging = function() return debugAttached end
		             debug_getCurrentDebuggerInterface = function() return debug_getCurrentDebuggerInterface end
		             getOpenedProcessID = function() return 77 end
		             getProcesslist = function() return {[77] = true} end
		             debugProcessCalls = 0
		             debugProcess = function(_)
		             	debugProcessCalls = debugProcessCalls + 1
		             	debugAttached = true
		             end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		CheatEngineToolException attached = Assert.Throws<CheatEngineToolException>(() =>
			dispatch.RunLua(CheatEngineToolNames.DebuggerAttach, DebuggerLuaScripts.Attach,
				DebuggerJsonContext.Default.DebuggerAttachment, Token, 0L, 77L, "77:1"));
		// A GDB server reports 5, which MCP cannot drive either.
		InstallStubs("debug_getCurrentDebuggerInterface = function() return 5 end");
		CheatEngineToolException already = Assert.Throws<CheatEngineToolException>(() =>
			dispatch.RunLua(CheatEngineToolNames.DebuggerAttach, DebuggerLuaScripts.Attach,
				DebuggerJsonContext.Default.DebuggerAttachment, Token, 1L, 77L, "77:1"));

		Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.Completed, false),
			(attached.Error.Kind, attached.Error.HostEffect, attached.Error.Retryable));
		Assert.Contains("attached and remains attached through an interface that MCP cannot drive",
			attached.Error.Message, StringComparison.Ordinal);
		Assert.Contains("debugger_detach", attached.Error.Hint, StringComparison.Ordinal);
		Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.NotStarted),
			(already.Error.Kind, already.Error.HostEffect));
		Assert.Contains("is already attached through an interface", already.Error.Message, StringComparison.Ordinal);
		Assert.Contains("interface windows", already.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(1L, ReadGlobal("debugProcessCalls"));
		LuaFixedScriptAssert.NeverLoadsCode(DebuggerLuaScripts.Attach);
	}

	[Fact]
	public void DebuggerV2_Attach_ConnectedToCEServer_IsRefusedBeforeCheatEngineAttaches()
	{
		using RuntimeScope scope = CreateScope();
		// While connected, debugProcess creates Cheat Engine's network debugger whatever interface is requested.
		InstallStubs("""
		             debugAttached = false
		             remoteConnected = true
		             isConnectedToCEServer = function() return remoteConnected end
		             debug_isDebugging = function() return debugAttached end
		             debug_getCurrentDebuggerInterface = function() return 2 end
		             getOpenedProcessID = function() return 77 end
		             getProcesslist = function() return {[77] = true} end
		             debugProcessCalls = 0
		             debugProcess = function(_)
		             	debugProcessCalls = debugProcessCalls + 1
		             	debugAttached = true
		             end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		List<CheatEngineToolException> refused = [];
		foreach (long @interface in (long[]) [0L, 1L, 2L, 3L])
		{
			refused.Add(Assert.Throws<CheatEngineToolException>(() =>
				dispatch.RunLua(CheatEngineToolNames.DebuggerAttach, DebuggerLuaScripts.Attach,
					DebuggerJsonContext.Default.DebuggerAttachment, Token, @interface, 77L, "77:1")));
		}

		object? refusedCalls = ReadGlobal("debugProcessCalls");
		InstallStubs("remoteConnected = false");
		// The refused VEH request did not mark this process incarnation as having used the VEH debugger.
		DebuggerAttachment veh = dispatch.RunLua(CheatEngineToolNames.DebuggerAttach, DebuggerLuaScripts.Attach,
			DebuggerJsonContext.Default.DebuggerAttachment, Token, 2L, 77L, "77:1");

		Assert.All(refused, static exception =>
		{
			Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.NotStarted, false),
				(exception.Error.Kind, exception.Error.HostEffect, exception.Error.Retryable));
			Assert.Contains("connected to a ceserver", exception.Error.Message, StringComparison.Ordinal);
			Assert.Contains("Disconnect Cheat Engine from the ceserver", exception.Error.Hint,
				StringComparison.Ordinal);
		});
		Assert.Equal(4, refused.Count);
		Assert.Equal(0L, refusedCalls);
		Assert.Equal((DebuggerInterface.Veh, false), (veh.ActiveInterface, veh.AlreadyAttached));
		Assert.Equal(1L, ReadGlobal("debugProcessCalls"));
	}

	[Fact]
	public void DebuggerV2_GetContext_ExtraRegisters_ReturnsByteTablesAsSpacedHexadecimal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             debug_isDebugging = function() return true end
		             debug_getContext = function(_) return true end
		             targetIs64Bit = function() return true end
		             debug_getCurrentContextTable = function(extra)
		             	local context = {RAX = 1, RIP = 4096, EFlags = 0x246}
		             	if extra then
		             		context.FP0 = {0, 0, 0, 0, 0, 0, 0, 0x80, 0xFF, 0x3F}
		             		context.XMM0 = {0, 0, 0x80, 0x3F, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}
		             		context.XMM1 = {}
		             		context.XMM2 = {1, 256}
		             		context.XMM3 = {1.5}
		             		local wide = {}
		             		for index = 1, 65 do wide[index] = 0 end
		             		context.XMM4 = wide
		             	end
		             	return context
		             end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		DebuggerContext basic = dispatch.RunLua(CheatEngineToolNames.DebuggerGetContext, DebuggerLuaScripts.GetContext,
			DebuggerJsonContext.Default.DebuggerContext, Token, false);
		DebuggerContext extended = dispatch.RunLua(CheatEngineToolNames.DebuggerGetContext,
			DebuggerLuaScripts.GetContext, DebuggerJsonContext.Default.DebuggerContext, Token, true);

		Assert.False(basic.IncludesExtraRegisters);
		Assert.Equal(["EFLAGS", "RAX", "RIP"], basic.Registers.Keys.Order(StringComparer.Ordinal));
		Assert.True(extended.IncludesExtraRegisters);
		Assert.Equal(["EFLAGS", "FP0", "RAX", "RIP", "XMM0"], extended.Registers.Keys.Order(StringComparer.Ordinal));
		Assert.Equal("00 00 00 00 00 00 00 80 FF 3F", extended.Registers["FP0"]);
		Assert.Equal("00 00 80 3F 00 00 00 00 00 00 00 00 00 00 00 00", extended.Registers["XMM0"]);
		Assert.Equal(("1", "1000", "246"),
			(extended.Registers["RAX"], extended.Registers["RIP"], extended.Registers["EFLAGS"]));
	}

	[Fact]
	public void DebuggerV2_DefaultInterface_ReadsTheSettingThatDebugProcessZeroUses()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             debugAttached = false
		             debug_isDebugging = function() return debugAttached end
		             settings = {cbUseWindowsDebugger = {Checked = false}, cbUseVEHDebugger = {Checked = true},
		             	cbKDebug = {Checked = false}, cbUseDBVMDebugger = {Checked = false}}
		             getSettingsForm = function() return settings end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		LuaDebuggerDefaultInterface veh = DefaultInterface(dispatch);
		InstallStubs("settings.cbUseVEHDebugger.Checked = false; settings.cbKDebug.Checked = true");
		LuaDebuggerDefaultInterface kernel = DefaultInterface(dispatch);
		InstallStubs("settings.cbKDebug.Checked = false; settings.cbUseDBVMDebugger.Checked = true");
		LuaDebuggerDefaultInterface dbvm = DefaultInterface(dispatch);
		InstallStubs("settings.cbUseDBVMDebugger.Checked = false; settings.cbUseWindowsDebugger.Checked = true");
		LuaDebuggerDefaultInterface windows = DefaultInterface(dispatch);
		InstallStubs("settings = {cbUseGDBServer = {Checked = true}}");
		LuaDebuggerDefaultInterface other = DefaultInterface(dispatch);
		InstallStubs("getSettingsForm = function() error('no settings form') end");
		LuaDebuggerDefaultInterface failed = DefaultInterface(dispatch);
		InstallStubs("getSettingsForm = nil; debugAttached = true");
		LuaDebuggerDefaultInterface attached = DefaultInterface(dispatch);
		// Cheat Engine's debugger thread tests a ceserver connection before any setting.
		InstallStubs("""
		             debugAttached = false
		             isConnectedToCEServer = function() return true end
		             settings = {cbUseWindowsDebugger = {Checked = true}}
		             getSettingsForm = function() return settings end
		             """);
		LuaDebuggerDefaultInterface remote = DefaultInterface(dispatch);

		Assert.Equal(new LuaDebuggerDefaultInterface(false, "veh"), veh);
		Assert.Equal(new LuaDebuggerDefaultInterface(false, "kernel"), kernel);
		Assert.Equal(new LuaDebuggerDefaultInterface(false, "dbvm"), dbvm);
		Assert.Equal(new LuaDebuggerDefaultInterface(false, "windows"), windows);
		Assert.Equal(new LuaDebuggerDefaultInterface(false), other);
		Assert.Equal(new LuaDebuggerDefaultInterface(false), failed);
		Assert.Equal(new LuaDebuggerDefaultInterface(true), attached);
		Assert.Equal(new LuaDebuggerDefaultInterface(false, "ceserver"), remote);
	}

	[Fact]
	public void DebuggerV2_AggregatedCapture_EvictsItsGroupIndexWithTheBoundedRing()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + """
		                                   bptExecute = 1
		                                   bptAccess = 2
		                                   bptWrite = 3
		                                   debug_isDebugging = function() return true end
		                                   debug_getContext = function(_) return false end
		                                   targetIsX86 = function() return true end
		                                   targetIs64Bit = function() return true end
		                                   getOpenedProcessID = function() return 77 end
		                                   getAddressSafe = function(_) return 0x401000 end
		                                   debug_removeBreakpointByID = function(_) return true end
		                                   debug_setBreakpoint = function(_, _, _, callback) captureCallback = callback; return true, 88 end
		                                   """);

		LuaJsonResult<DebuggerJobStarted> result = ReadJson(LuaToolRuntime.BuildSource(DebuggerLuaScripts.StartCapture,
				[OwnNamespace, "debugcapture-bbbb22-1", 2L, 60_000L, "401000", "execute", 4L, true]),
			DebuggerJsonContext.Default.DebuggerJobStarted);
		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal("debugcapture-bbbb22-1", result.Value.JobId);

		InstallStubs("""
		             for index = 1, 1000 do
		             	RIP = index
		             	THREADID = index
		             	captureCallback()
		             end
		             """);

		JsonElement capture = Parse(RunKernel("""
		                                      local job = jobFind(a[1], a[2])
		                                      local groups, groupKeys = 0, 0
		                                      for _ in pairs(job.groups) do groups = groups + 1 end
		                                      for _ in pairs(job.groupKeys) do groupKeys = groupKeys + 1 end
		                                      return {groups = groups, groupKeys = groupKeys, total = job.total, dropped = job.dropped, last = job.last, first = job.first}
		                                      """, OwnNamespace, "debugcapture-bbbb22-1"));
		Assert.Equal((2L, 2L), (capture.GetProperty("groups").GetInt64(), capture.GetProperty("groupKeys").GetInt64()));
		Assert.Equal((1000L, 998L, 1000L, 2L),
			(capture.GetProperty("total").GetInt64(), capture.GetProperty("dropped").GetInt64(),
				capture.GetProperty("last").GetInt64(),
				capture.GetProperty("last").GetInt64() - capture.GetProperty("first").GetInt64() + 1));
	}

	private static LuaDebuggerDefaultInterface DefaultInterface(ToolDispatch dispatch)
	{
		return dispatch.RunLua(CheatEngineToolNames.DebuggerAttach, DebuggerLuaScripts.DefaultInterface,
			DebuggerLuaJsonContext.Default.LuaDebuggerDefaultInterface, Token);
	}

	/// <summary>Assembler tools whose Client accepts every ENABLE section, so the DISABLE check runs on real Lua.</summary>
	private static AsmTools AcceptingEnableCheck()
	{
		IAutoAssemblerClient autoAssembler = ClientTestDouble.Create<IAutoAssemblerClient>((method, _) =>
			method.Name == nameof(IAutoAssemblerClient.Check)
				? new AutoAssemblerCheckResult(true, null, false)
				: throw new NotSupportedException(method.Name));
		return new AsmTools(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources(), autoAssembler);
	}

	private static void AssertDebuggerScriptCompiles(string name, string source)
	{
		LuaAdmissionStatus admission = LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation acquired);
		Assert.Equal(LuaAdmissionStatus.Admitted, admission);
		using LuaRuntimeOperation operation = acquired;
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		LuaStatus status = state.TryLoad(Encoding.UTF8.GetBytes(source),
			Encoding.UTF8.GetBytes("=CheatEngine.Mcp/" + name));
		Assert.True(status.IsOk, status.IsOk ? null : LuaError.FromStack(state, status).Message);
	}
}
