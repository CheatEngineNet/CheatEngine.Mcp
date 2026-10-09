using System.Text;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Aob;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.Mcp.Tools.Scan;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     The fixed scripts of the memory and AOB tools, and the shared <c>MEM_MAPPED</c> override scripts that the AOB
///     scans run, on a real Lua 5.3 state: each compiles, loads no code, needs no exposure switch, and runs against
///     stubbed Cheat Engine functions into its result record or declared failure.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	public static TheoryData<string> MemoryAobScripts => new()
	{
		nameof(MemoryScripts.AddressExtras), nameof(MemoryScripts.Protection), nameof(AobScripts.UniqueAob),
		nameof(MappedMemoryOverride.SetScript), nameof(MappedMemoryOverride.EndScript)
	};

	[Theory]
	[MemberData(nameof(MemoryAobScripts))]
	public void MemoryAobScript_Compiles_WithoutLoadingCodeOrNeedingASwitch(string name)
	{
		string body = MemoryAobScript(name);
		LuaFixedScriptAssert.NeverLoadsCode(body);
		Assert.Empty(LuaFeatureScan.Scan(body));
		using RuntimeScope scope = CreateScope();
		using LuaRuntimeOperation operation = AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);

		string source = LuaToolRuntime.BuildSource(body, 100, [new ulong[] { 0x401000 }, 4096L, true, false, true]);
		LuaStatus status = state.TryLoad(Encoding.UTF8.GetBytes(source), "=CheatEngine.Mcp/compile_probe"u8);

		Assert.True(status.IsOk, status.IsOk ? null : LuaError.FromStack(state, status).Message);
	}

	[Fact]
	public void AddressExtras_StubbedCheatEngine_ReportsSystemModulesAndRttiClasses()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             inSystemModule = function(address) return address == 0x7FF800001000 end
		             getRTTIClassName = function(address)
		               if address == 0x401000 then return 'Player' end
		               error('no RTTI here')
		             end
		             """);

		AddressExtras withRtti = RunExtras(true);
		AddressExtras withoutRtti = RunExtras(false);
		InstallStubs("inSystemModule = nil; getRTTIClassName = nil");
		AddressExtras missing = RunExtras(true);
		InstallStubs("inSystemModule = function() error('unavailable') end");
		AddressExtras raised = RunExtras(false);

		Assert.Equal<bool?[]>([false, true], withRtti.System);
		Assert.Equal(["Player", null], withRtti.Rtti);
		Assert.Equal([null, null], withoutRtti.Rtti);
		Assert.Equal<bool?[]>([null, null], missing.System);
		Assert.Equal([null, null], missing.Rtti);
		Assert.Equal<bool?[]>([null, null], raised.System);
	}

	[Fact]
	public void Protection_StubbedCheatEngine_SetsOrGrantsFullAccessAndReadsBothStates()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CheatEngine77Protection);

		ProtectionProbe set = RunProtection(true, true, false);
		Assert.Equal("set", ReadGlobal("lastCall"));
		ProtectionProbe full = RunProtection(true, true, true);

		Assert.Equal(
			new ProtectionProbe(new ProtectionFlags(true, false, true), new ProtectionFlags(true, true, false)),
			set);
		Assert.Equal(new ProtectionFlags(true, true, true), full.Current);
		Assert.Equal("full", ReadGlobal("lastCall"));
		Assert.Equal(0x401000L, ReadGlobal("seenAddress"));
		Assert.Equal(4096L, ReadGlobal("seenSize"));
	}

	[Theory]
	[InlineData(true, false, false, true, false, false)]
	[InlineData(false, false, false, false, false, false)]
	[InlineData(false, true, false, true, true, false)]
	[InlineData(false, false, true, true, false, true)]
	[InlineData(true, false, true, true, false, true)]
	public void Protection_CheatEngine77Keys_ApplyTheRequestedAccessRatherThanNoAccess(bool read, bool write,
		bool execute, bool currentRead, bool currentWrite, bool currentExecute)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CheatEngine77Protection);

		ProtectionProbe probe = RunProtection(read, write, execute);

		// With the lower-case keys alone, Cheat Engine 7.7 reads R, W and X as nil and applies PAGE_NOACCESS.
		Assert.Equal(new ProtectionFlags(currentRead, currentWrite, currentExecute), probe.Current);
		Assert.Equal("set", ReadGlobal("lastCall"));
	}

	[Fact]
	public void Protection_WriteAndExecuteRefused_IsADeclaredHostRefusalWithCheatEnginesReason()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CheatEngine77Protection + "\nwritableExecutable = false");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			RunProtection(false, true, true));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.NotApplied),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.StartsWith("Cheat Engine refused the protection change: This system does not support writable",
			exception.Error.Message, StringComparison.Ordinal);
		Assert.Contains("memory_get_address_info", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Equal("set", ReadGlobal("lastCall"));
	}

	[Theory]
	[InlineData(true, true, true, "full")]
	[InlineData(true, true, false, "set")]
	public void Protection_CheatEngineReportsFailure_IsADeclaredHostRefusalThatChangedNothing(bool read, bool write,
		bool execute, string call)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CheatEngine77Protection + "\nprotectFails = true");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			RunProtection(read, write, execute));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.NotApplied),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("Cheat Engine refused the protection change.", exception.Error.Message);
		Assert.Equal(call, ReadGlobal("lastCall"));
	}

	[Fact]
	public void Protection_FailureAfterTheAccessChanged_IsAStartedHostRefusal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CheatEngine77Protection + """

		                                       setMemoryProtection = function()
		                                         protection = {r = true, w = true, x = false}
		                                         return false
		                                       end
		                                       """);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			RunProtection(true, true, false));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Started),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	[Fact]
	public void Protection_FailureWithTheAccessNoLongerReadable_IsAnUnknownHostRefusal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CheatEngine77Protection + """

		                                       setMemoryProtection = function()
		                                         getMemoryProtection = function() return nil end
		                                         return false
		                                       end
		                                       """);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			RunProtection(true, false, false));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Unknown),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	[Fact]
	public void Protection_NoProtectionReported_IsADeclaredInvalidStateBeforeAnyChange()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             getMemoryProtection = function() return nil end
		             setMemoryProtection = function() changed = true end
		             fullAccess = function() changed = true end
		             """);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			RunProtection(true, false, false));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Null(ReadGlobal("changed"));
	}

	[Fact]
	public void UniqueAob_StubbedCheatEngine_ReturnsItsSignatureAndOffset()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("getUniqueAOB = function(address) seen = address return '48 8B ** 05', 2 end");

		UniqueAobProbe probe = RunUniqueAob();

		Assert.Equal(new UniqueAobProbe(true, "48 8B ** 05", 2), probe);
		Assert.Equal("48 8B ?? 05", SignatureBuilder.NormalizeCheatEnginePattern(probe.Pattern));
		Assert.Equal(0x401002L, ReadGlobal("seen"));
	}

	[Fact]
	public void UniqueAob_CheatEngineReturnsItsFailureText_ReportsNoSignatureAndTheTriedCode()
	{
		using RuntimeScope scope = CreateScope();
		// Cheat Engine 7.7 returns this text as the signature and leaves the offset uninitialized.
		InstallStubs("""
		             getUniqueAOB = function()
		               return 'ERROR: Could not find unique AOB, tried code "48 8B 05 10"', 305419896
		             end
		             """);

		UniqueAobProbe probe = RunUniqueAob();

		Assert.Equal(new UniqueAobProbe(false, Tried: "48 8B 05 10"), probe);
		Assert.Equal("48 8B 05 10", SignatureBuilder.NormalizeCheatEnginePattern(probe.Tried));
	}

	[Fact]
	public void UniqueAob_FailureTextWithoutQuotedCodeOrNoResult_ReportsNoSignature()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("getUniqueAOB = function() return 'ERREUR : aucun AOB unique', 0 end");
		UniqueAobProbe translated = RunUniqueAob();
		InstallStubs("getUniqueAOB = function() return nil end");
		UniqueAobProbe missing = RunUniqueAob();
		InstallStubs("getUniqueAOB = function() return '', 0 end");
		UniqueAobProbe empty = RunUniqueAob();

		Assert.Equal(new UniqueAobProbe(false), translated);
		Assert.Equal(new UniqueAobProbe(false), missing);
		Assert.Equal(new UniqueAobProbe(false), empty);
	}

	[Fact]
	public void UniqueAob_WildcardSignature_IsNotMistakenForFailureText()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("getUniqueAOB = function() return 'E8 * * * * 48 8b 4c 24', 0 end");

		Assert.Equal(new UniqueAobProbe(true, "E8 * * * * 48 8b 4c 24", 0), RunUniqueAob());
	}

	[Fact]
	public void UniqueAob_AnyRaisedError_IsADeclaredHostRefusal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("getUniqueAOB = function() error('Could not find a unique AOB, tried code \"48 8B *\"') end");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(RunUniqueAob);

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Completed),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.StartsWith("getUniqueAOB failed: ", exception.Error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void UniqueAob_UnexpectedCheatEngineError_IsADeclaredHostRefusal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("getUniqueAOB = function() error('The target memory cannot be inspected') end");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(RunUniqueAob);

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Completed),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	[Fact]
	public void UniqueAob_NonIntegerOffset_IsADeclaredHostRefusal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("getUniqueAOB = function() return '48 8B 05', 'not an integer' end");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(RunUniqueAob);

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Completed),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	[Fact]
	public void UniqueAob_FunctionMissing_IsADeclaredUnsupportedFailure()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("getUniqueAOB = nil");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(RunUniqueAob);

		Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	/// <summary>
	///     Cheat Engine 7.7's page protection as <c>LuaHandler.pas</c> implements it: <c>setMemoryProtection</c>
	///     reads the upper-case keys R, W and X (a missing key is false), grants read with write or execute, refuses
	///     write and execute together where the system forbids them with <c>false</c> and a reason, and returns the
	///     old protection on success; <c>fullAccess</c> returns whether it succeeded; <c>getMemoryProtection</c>
	///     reports lower-case keys.
	/// </summary>
	private const string CheatEngine77Protection = """
	                                               protection = {r = true, w = false, x = true}
	                                               writableExecutable = true
	                                               protectFails = false
	                                               getMemoryProtection = function(address)
	                                                 seenAddress = address
	                                                 return protection
	                                               end
	                                               setMemoryProtection = function(address, size, p)
	                                                 lastCall = 'set'
	                                                 seenSize = size
	                                                 local r = p.R ~= nil and p.R ~= false
	                                                 local w = p.W ~= nil and p.W ~= false
	                                                 local x = p.X ~= nil and p.X ~= false
	                                                 if not writableExecutable and w and x then
	                                                   return false, 'This system does not support writable '
	                                                     .. 'executable memory.  Tip: Pause the process, make '
	                                                     .. 'writable, write, make executable, continue'
	                                                 end
	                                                 if protectFails then return false end
	                                                 protection = {r = r or w or x, w = w, x = x}
	                                                 return 4
	                                               end
	                                               fullAccess = function(address, size)
	                                                 lastCall = 'full'
	                                                 seenSize = size
	                                                 if protectFails then return false end
	                                                 protection = {r = true, w = true, x = true}
	                                                 return true
	                                               end
	                                               """;

	private static string MemoryAobScript(string name)
	{
		return name switch
		{
			nameof(MemoryScripts.AddressExtras) => MemoryScripts.AddressExtras,
			nameof(MemoryScripts.Protection) => MemoryScripts.Protection,
			nameof(MappedMemoryOverride.SetScript) => MappedMemoryOverride.SetScript,
			nameof(MappedMemoryOverride.EndScript) => MappedMemoryOverride.EndScript,
			_ => AobScripts.UniqueAob
		};
	}

	private static AddressExtras RunExtras(bool rtti)
	{
		return PluginLuaToolRuntime.Execute(CreateJsonLuaClient(), CheatEngineToolNames.MemoryGetAddressInfo,
			MemoryScripts.AddressExtras, MemoryJsonContext.Default.AddressExtras, Token,
			new ulong[] { 0x401000, 0x7FF800001000 }, rtti);
	}

	private static ProtectionProbe RunProtection(bool read, bool write, bool execute)
	{
		return PluginLuaToolRuntime.Execute(CreateJsonLuaClient(), CheatEngineToolNames.MemorySetProtection,
			MemoryScripts.Protection, MemoryJsonContext.Default.ProtectionProbe, Token, 0x401000UL, 4096L, read, write,
			execute);
	}

	private static UniqueAobProbe RunUniqueAob()
	{
		return PluginLuaToolRuntime.Execute(CreateJsonLuaClient(), CheatEngineToolNames.AobGenerateSignature,
			AobScripts.UniqueAob, AobJsonContext.Default.UniqueAobProbe, Token, 0x401002UL);
	}
}
