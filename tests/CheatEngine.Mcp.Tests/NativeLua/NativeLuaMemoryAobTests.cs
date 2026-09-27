using System.Text;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Aob;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     The fixed scripts of the memory and AOB tools on a real Lua 5.3 state: each compiles, loads no code, needs no
///     exposure switch, and runs against stubbed Cheat Engine functions into its result record or declared failure.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	public static TheoryData<string> MemoryAobScripts => new()
	{
		nameof(MemoryScripts.AddressExtras), nameof(MemoryScripts.Protection), nameof(AobScripts.UniqueAob)
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

		Assert.Equal([false, true], withRtti.System);
		Assert.Equal(["Player", null], withRtti.Rtti);
		Assert.Equal([null, null], withoutRtti.Rtti);
		Assert.Equal([false, false], missing.System);
		Assert.Equal([null, null], missing.Rtti);
	}

	[Fact]
	public void Protection_StubbedCheatEngine_SetsOrGrantsFullAccessAndReadsBothStates()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             protection = {r = true, w = false, x = true}
		             getMemoryProtection = function(address) seenAddress = address return protection end
		             setMemoryProtection = function(address, size, p) lastCall = 'set' seenSize = size
		               protection = {r = p.r, w = p.w, x = p.x} end
		             fullAccess = function(address, size) lastCall = 'full' protection = {r = true, w = true, x = true} end
		             """);

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
	public void UniqueAob_CheatEngineRaises_ReportsNoSignatureAndItsLastAttempt()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("getUniqueAOB = function() error('Could not find a unique AOB, tried code \"48 8B *\"') end");

		Assert.Equal(new UniqueAobProbe(false, Tried: "48 8B *"), RunUniqueAob());

		InstallStubs("getUniqueAOB = function() return nil end");
		Assert.Equal(new UniqueAobProbe(false), RunUniqueAob());
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

	private static string MemoryAobScript(string name)
	{
		return name switch
		{
			nameof(MemoryScripts.AddressExtras) => MemoryScripts.AddressExtras,
			nameof(MemoryScripts.Protection) => MemoryScripts.Protection,
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
