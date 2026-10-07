using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Pointer;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>Native Lua regression coverage for fixed supplied-facts pointer-access analysis.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private static readonly byte[] RipBytes = [0x8B, 0x05, 0, 0, 0, 0];
	private static readonly byte[] AddressSizeOverrideBytes = [0x67, 0x8B, 0x40, 0x08];
	private static readonly string[] EcxTwo = ["ECX", "2"];
	private static readonly string[][] GameSymbol = [["GAME.EXE", "140000000"]];
	private static readonly string[][] OnlyEax = [["EAX", "1000"]];

	[Theory]
	[InlineData("mov eax,[rax+10]")]
	[InlineData("mov eax,[game.exe+10]")]
	public void PointerAccess_PaddedHexFacts_NormalizesMapsAndObservedAddress(string instruction)
	{
		using RuntimeScope scope = CreateScope();
		PointerAccessTools tools = new(CreateNativeDispatch(new McpFeatureOptions()));

		PointerAccessInfo result = tools.GetAccessInfo(instruction, " \t0x401000 ", 4, "x64",
			new Dictionary<string, string> { ["RAX"] = " \t0x1000 " },
			observedAccessAddress: " \t0x1010 ",
			symbols: new Dictionary<string, string> { ["game.exe"] = " \t0x1000 " }, cancellationToken: Token);

		Assert.Equal(PointerAccessStatus.Supported, result.Status);
		Assert.Equal("1010", result.EffectiveAddress);
		Assert.False(result.ObservedAddressMismatch);
	}

	[Theory]
	[InlineData("eip", "140001000", "40001017")]
	[InlineData("eip", "FFFFFFF8", "F")]
	[InlineData("rip", "140001000", "140001017")]
	public void PointerAccess_TextInstructionPointer_UsesItsDeclaredAddressWidth(string register,
		string address, string expected)
	{
		using RuntimeScope scope = CreateScope();
		PointerAccessTools tools = new(CreateNativeDispatch(new McpFeatureOptions()));

		PointerAccessInfo result = tools.GetAccessInfo($"mov eax,[{register}+10]", address, 7, "x64",
			new Dictionary<string, string>(), cancellationToken: Token);

		Assert.Equal(PointerAccessStatus.Supported, result.Status);
		Assert.Equal(expected, result.EffectiveAddress);
		Assert.Equal("10", result.Displacement);
	}

	[Theory]
	[InlineData("pop dword ptr [esp+10]", "x86", "ESP", "8F 44 24 10", 4)]
	[InlineData("pop qword ptr [rsp+10]", "x64", "RSP", "8F 44 24 10", 4)]
	[InlineData("pop word ptr [esp+10]", "x86", "ESP", "66 8F 44 24 10", 5)]
	[InlineData("pop word ptr [rsp+10]", "x64", "RSP", "66 8F 44 24 10", 5)]
	public void PointerAccess_StackRelativePop_RefusesIntermediateStackPointerContext(string instruction,
		string architecture, string register, string bytes, int length)
	{
		using RuntimeScope scope = CreateScope();
		PointerAccessTools tools = new(CreateNativeDispatch(new McpFeatureOptions()));

		PointerAccessInfo result = tools.GetAccessInfo(instruction, "401000", length, architecture,
			new Dictionary<string, string> { [register] = "1000" }, instructionBytes: bytes, cancellationToken: Token);

		Assert.Equal(PointerAccessStatus.Unsupported, result.Status);
		Assert.Null(result.EffectiveAddress);
		Assert.Null(result.CandidateStructureBase);
		Assert.Contains("stack pointer", result.Uncertainty, StringComparison.Ordinal);
	}

	[Fact]
	public void PointerAccess_SuppliedFacts_EvaluatesRipRelativeOperandWithoutLiveResolver()
	{
		using RuntimeScope scope = CreateScope();
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		PointerAccessInfo result = dispatch.RunLua(CheatEngineToolNames.PointerGetAccessInfo,
			PointerAccessLuaScripts.GetAccessInfo, PointerAccessJsonContext.Default.PointerAccessInfo, Token,
			"mov", "eax,[rip+10]", 0x401000UL, 6L, true, RipBytes,
			Array.Empty<string[]>(), Array.Empty<string[]>(), null, false);

		Assert.Equal(PointerAccessStatus.Supported, result.Status);
		Assert.Equal("401016", result.EffectiveAddress);
		Assert.Null(result.BaseRegister);
		Assert.Equal("10", result.Displacement);
		LuaFixedScriptAssert.NeverLoadsCode(PointerAccessLuaScripts.GetAccessInfo);
	}

	[Theory]
	[InlineData("bt dword ptr [eax],ecx")]
	[InlineData("btc dword ptr [eax],ecx")]
	[InlineData("btr dword ptr [eax],ecx")]
	[InlineData("bts qword ptr [rax],rcx")]
	[InlineData("lock bts dword ptr [eax],ecx")]
	[InlineData("rep movsb byte ptr [rdi],byte ptr [rsi]")]
	public void PointerAccess_BitStringOrPrefixedInstruction_RefusesUnsupportedAddressSemantics(string instruction)
	{
		using RuntimeScope scope = CreateScope();
		PointerAccessTools tools = new(CreateNativeDispatch(new McpFeatureOptions()));

		PointerAccessInfo result = tools.GetAccessInfo(instruction, "401000", 4, "x64",
			new Dictionary<string, string> { ["RAX"] = "1000", ["RCX"] = "20" }, cancellationToken: Token);

		Assert.Equal(PointerAccessStatus.Unsupported, result.Status);
		Assert.Null(result.EffectiveAddress);
		Assert.Null(result.CandidateStructureBase);
	}

	[Fact]
	public void PointerAccess_MissingRegister_ReturnsTypedMissingFacts()
	{
		using RuntimeScope scope = CreateScope();
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		PointerAccessInfo result = dispatch.RunLua(CheatEngineToolNames.PointerGetAccessInfo,
			PointerAccessLuaScripts.GetAccessInfo, PointerAccessJsonContext.Default.PointerAccessInfo, Token,
			"mov", "eax,[rax+rcx*8-20]", 0x401000UL, 4L, true, Array.Empty<byte>(),
			new[] { new[] { "RAX", "1000" } }, Array.Empty<string[]>(), null, false);

		Assert.Equal(PointerAccessStatus.MissingFacts, result.Status);
		Assert.Contains("RCX", result.Uncertainty, StringComparison.Ordinal);
	}

	[Fact]
	public void PointerAccess_IndexedPostExecutionContext_ReportsDynamicOffsetAndMismatch()
	{
		using RuntimeScope scope = CreateScope();
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		PointerAccessInfo result = dispatch.RunLua(CheatEngineToolNames.PointerGetAccessInfo,
			PointerAccessLuaScripts.GetAccessInfo, PointerAccessJsonContext.Default.PointerAccessInfo, Token,
			"mov", "eax,[rax+rcx*4+10]", 0x401000UL, 4L, true, Array.Empty<byte>(),
			new[] { new[] { "RAX", "1000" }, new[] { "RCX", "2" } }, Array.Empty<string[]>(), "1019", true);

		Assert.Equal((true, true, true), (result.DynamicOffset, result.ContextMayHaveChanged,
			result.ObservedAddressMismatch));
		Assert.Null(result.NextPointerSearchValue);
	}

	[Theory]
	[InlineData("eax,[eax+10]", false, "EAX", "1000", "1010")]
	[InlineData("eax,[eax-10]", false, "EAX", "1000", "FF0")]
	[InlineData("eax,[eax+ecx*4+10]", false, "EAX", "1000", "1018")]
	[InlineData("eax,[r8d+10]", true, "R8", "1000", "1010")]
	public void PointerAccess_RegisterAliasesAndSignedDisplacements_EvaluateExactly(string parameters, bool wide,
		string register, string value, string expected)
	{
		PointerAccessInfo result = Analyze("mov", parameters, wide, [new[] { register, value }, EcxTwo]);

		Assert.Equal(PointerAccessStatus.Supported, result.Status);
		Assert.Equal(expected, result.EffectiveAddress);
		if (parameters.Contains("-10", StringComparison.Ordinal))
		{
			Assert.Equal("-10", result.Displacement);
		}
	}

	[Theory]
	[InlineData("mov", "eax,[missing+10]", "missing_facts")]
	[InlineData("movs", "byte ptr [rdi],byte ptr [rsi]", "unsupported")]
	[InlineData("ret", "", "unsupported")]
	[InlineData("mov", "eax,fs:[30]", "unsupported")]
	[InlineData("vgatherdps", "xmm0,[rax+xmm1*4],xmm2", "unsupported")]
	public void PointerAccess_UnsupportedOrMissingFacts_ReturnTypedStatus(string opcode, string parameters,
		string expected)
	{
		PointerAccessInfo result = Analyze(opcode, parameters, true, Array.Empty<string[]>());

		Assert.Equal(expected, result.Status.ToString().ToLowerInvariant().Replace("facts", "_facts"));
	}

	[Fact]
	public void PointerAccess_SuppliedSymbolMap_ResolvesWithoutLiveSymbolLookup()
	{
		PointerAccessInfo result = Analyze("mov", "eax,[game.exe+20]", true, Array.Empty<string[]>(),
			GameSymbol);

		Assert.Equal((PointerAccessStatus.Supported, "140000020"), (result.Status, result.EffectiveAddress));
		Assert.Equal("20", result.Displacement);
	}

	[Fact]
	public void PointerAccess_AddressSizeOverride_WrapsTheEffectiveAddressAt32Bits()
	{
		using RuntimeScope scope = CreateScope();
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		PointerAccessInfo result = dispatch.RunLua(CheatEngineToolNames.PointerGetAccessInfo,
			PointerAccessLuaScripts.GetAccessInfo, PointerAccessJsonContext.Default.PointerAccessInfo, Token,
			"mov", "eax,[rax+08]", 0x401000UL, 4L, true, AddressSizeOverrideBytes,
			new[] { new[] { "RAX", "100001000" } }, Array.Empty<string[]>(), null, false);

		Assert.Equal("1008", result.EffectiveAddress);
	}

	[Theory]
	[InlineData("RAX", "EAX", "100000001", "0x0001")]
	[InlineData("R8", "R8D", "100000001", "1")]
	public void PointerAccess_ConsistentAliases_PreserveUnknownUpperBits(string fullName, string narrowName,
		string full, string narrow)
	{
		using RuntimeScope scope = CreateScope();
		PointerAccessTools tools = new(CreateNativeDispatch(new McpFeatureOptions()));
		Dictionary<string, string> registers = new()
		{
			[fullName] = full,
			[narrowName] = narrow
		};

		PointerAccessInfo result = tools.GetAccessInfo($"mov eax,[{fullName}+10]", "401000", 4, "x64",
			registers, cancellationToken: Token);

		Assert.Equal(PointerAccessStatus.Supported, result.Status);
		Assert.Equal("100000011", result.EffectiveAddress);
		Assert.Equal("100000001", result.CandidateStructureBase);
	}

	[Theory]
	[InlineData("RAX", "EAX")]
	[InlineData("R8", "R8D")]
	public void PointerAccess_ConflictingAliases_RefusesInsteadOfChoosingARegister(string fullName, string narrowName)
	{
		using RuntimeScope scope = CreateScope();
		PointerAccessTools tools = new(CreateNativeDispatch(new McpFeatureOptions()));
		Dictionary<string, string> registers = new()
		{
			[fullName] = "100000001",
			[narrowName] = "2"
		};

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() => tools.GetAccessInfo(
			$"mov eax,[{fullName}+10]", "401000", 4, "x64", registers, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Error.Kind);
		Assert.Contains("conflicting aliases", error.Error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void PointerAccess_X86Overflow_WrapsAddressAndCandidateBase()
	{
		using RuntimeScope scope = CreateScope();
		PointerAccessTools tools = new(CreateNativeDispatch(new McpFeatureOptions()));
		PointerAccessInfo result = tools.GetAccessInfo("mov eax,[eax+20]", "401000", 3, "x86",
			new Dictionary<string, string> { ["EAX"] = "FFFFFFF0" }, cancellationToken: Token);

		Assert.Equal("10", result.EffectiveAddress);
		Assert.Equal("FFFFFFF0", result.CandidateStructureBase);
	}

	[Fact]
	public void PointerAccess_WideOperandWithOnlyNarrowRegister_ReturnsMissingFacts()
	{
		PointerAccessInfo result = Analyze("mov", "eax,[rax]", true, OnlyEax);

		Assert.Equal(PointerAccessStatus.MissingFacts, result.Status);
	}

	[Fact]
	public void PointerAccess_NarrowOperandWithNarrowRegister_EvaluatesTheAddress()
	{
		PointerAccessInfo result = Analyze("mov", "eax,[eax+10]", true, OnlyEax);

		Assert.Equal((PointerAccessStatus.Supported, "1010"), (result.Status, result.EffectiveAddress));
	}

	private static PointerAccessInfo Analyze(string opcode, string parameters, bool wide, string[][] registers,
		string[][]? symbols = null)
	{
		using RuntimeScope scope = CreateScope();
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		return dispatch.RunLua(CheatEngineToolNames.PointerGetAccessInfo, PointerAccessLuaScripts.GetAccessInfo,
			PointerAccessJsonContext.Default.PointerAccessInfo, Token, opcode, parameters, 0x401000UL, 4L, wide,
			Array.Empty<byte>(), registers, symbols ?? Array.Empty<string[]>(), null, false);
	}
}
