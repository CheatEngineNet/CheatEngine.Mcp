using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Debugger;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     Native Lua coverage for debugger_start_capture's effective-address grouping: Cheat Engine's operand text as its
///     disassembler prints it, parsed once at start and evaluated with the register globals of each execute hit.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private const string CaptureJobId = "debugcapture-bbbb22-1";

	// The debugger, symbol and disassembler APIs the capture body calls, with the instruction at 401000 taken from
	// the instructionOpcode, instructionParameters and instructionBytes globals.
	private const string CaptureHostStubs = """
	                                        bptExecute = 1
	                                        bptAccess = 2
	                                        bptWrite = 3
	                                        wide = true
	                                        debug_isDebugging = function() return true end
	                                        debug_getContext = function(_) return false end
	                                        targetIsX86 = function() return true end
	                                        targetIs64Bit = function() return wide end
	                                        getOpenedProcessID = function() return 77 end
	                                        symbols = {['401000'] = 0x401000, ['game.exe'] = 0x140000000,
	                                        	['my module.dll'] = 0x7FF000000000}
	                                        getAddressSafe = function(name) return symbols[name] end
	                                        disassemble = function(_) return 'instruction' end
	                                        breakpointCalls = 0
	                                        debug_removeBreakpointByID = function(_) return true end
	                                        debug_setBreakpoint = function(address, size, trigger, callback)
	                                        	breakpointCalls = breakpointCalls + 1
	                                        	breakpointAddress = address
	                                        	breakpointTrigger = trigger
	                                        	captureCallback = callback
	                                        	return true, 88
	                                        end
	                                        instructionOpcode = 'mov'
	                                        instructionParameters = '[rax],edx'
	                                        instructionBytes = {0x8B, 0x05, 0x10, 0x00, 0x00, 0x00}
	                                        disassemblersDestroyed = 0
	                                        createDisassembler = function()
	                                        	local disassembler = {}
	                                        	disassembler.disassemble = function(address)
	                                        		disassembledAt = address
	                                        		return ''
	                                        	end
	                                        	disassembler.getLastDisassembleData = function()
	                                        		return {opcode = instructionOpcode,
	                                        			parameters = instructionParameters,
	                                        			bytes = instructionBytes}
	                                        	end
	                                        	disassembler.destroy = function()
	                                        		disassemblersDestroyed = disassemblersDestroyed + 1
	                                        	end
	                                        	return disassembler
	                                        end
	                                        THREADID = 9
	                                        RIP = 0x401000
	                                        EIP = 0x401000
	                                        """;

	[Theory]
	[InlineData("mov", "[rax+rcx*4+10],edx", true, "RAX = 0x1000; RCX = 2", "RAX = 0x2000; RCX = 3", "1018",
		"201C", 0)]
	[InlineData("mov", "eax,[rbp-08]", true, "RBP = 0x5000", "RBP = 0x6000", "4FF8", "5FF8", 0)]
	[InlineData("mov", "rax,qword ptr [r12]", true, "R12 = 0x3000", "R12 = 0x3100", "3000", "3100", 8)]
	[InlineData("mov", "eax,[game.exe+1234]", true, "RAX = 1", "RAX = 2", "140001234", "140001234", 0)]
	[InlineData("mov", "eax,[\"my module.dll\"+10]", true, "RAX = 1", "RAX = 2", "7FF000000010",
		"7FF000000010", 0)]
	[InlineData("mov", "eax,[rip+00000010]", true, "RAX = 1", "RAX = 2", "401016", "401016", 0)]
	[InlineData("mov", "[eax+ebx],ecx", false, "EAX = 0xFFFFFFF0; EBX = 0x20", "EAX = 0x100; EBX = 0x20", "10",
		"120", 0)]
	[InlineData("movss", "xmm0,dword ptr [rsi+rdi*8-00000020]", true, "RSI = 0x8000; RDI = 1",
		"RSI = 0x8000; RDI = 2", "7FE8", "7FF0", 4)]
	[InlineData("mov", "eax,[rax+rcx*4--80]", true, "RAX = 0x1000; RCX = 1", "RAX = 0x1000; RCX = 2", "F84",
		"F88", 0)]
	[InlineData("cmp", "dword ptr [r8d+10],00", true, "R8 = 0x1FFFFFFF8", "R8 = 0x20", "8", "30", 4)]
	[InlineData("movdqu", "xmm1,dqword ptr [00007FF612341000]", true, "RAX = 0", "RAX = 0", "7FF612341000",
		"7FF612341000", 16)]
	public void DebuggerV2_EffectiveAddressCapture_GroupsHitsByTheOperandAddress(string opcode, string parameters,
		bool wide, string firstRegisters, string secondRegisters, string firstAddress, string secondAddress,
		int operandSize)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs);
		InstallStubs($"instructionOpcode = '{opcode}'; instructionParameters = '{parameters}'");
		InstallStubs(wide ? "wide = true" : "wide = false");

		DebuggerJobStarted started = StartEffectiveAddressCapture(8L);
		for (int hit = 0; hit < 3; hit++)
		{
			InstallStubs(firstRegisters + "\ncaptureCallback()");
		}

		InstallStubs(secondRegisters + "\ncaptureCallback()");

		Assert.Equal((CaptureJobId, 77, "401000"), (started.JobId, started.ProcessId, started.Address));
		Assert.Equal((1L, 1L, 0x401000L, 1L),
			(ReadGlobal("breakpointCalls"), ReadGlobal("disassemblersDestroyed"), ReadGlobal("disassembledAt"),
				ReadGlobal("breakpointTrigger")));
		DebuggerCaptureItem[] groups = CaptureItems();
		int? size = operandSize == 0 ? null : operandSize;
		if (firstAddress == secondAddress)
		{
			DebuggerCaptureItem group = Assert.Single(groups);
			Assert.Equal((firstAddress, size, 4L), (group.EffectiveAddress, group.OperandSize, group.HitCount));
			return;
		}

		Assert.Equal(2, groups.Length);
		Assert.Equal((firstAddress, size, 3L), (groups[0].EffectiveAddress, groups[0].OperandSize,
			groups[0].HitCount));
		Assert.Equal((secondAddress, size, 1L), (groups[1].EffectiveAddress, groups[1].OperandSize,
			groups[1].HitCount));
		Assert.Equal(groups[0].Context.Registers, groups[0].FirstContext!.Registers);
		Assert.Equal(groups[0].Context.Registers, groups[0].LastContext!.Registers);
		Assert.NotEqual(groups[0].Context.Registers, groups[1].Context.Registers);
		Assert.Equal(("401000", false), (groups[0].Context.InstructionAddress, groups[0].Context.IsHeuristic));
		LuaFixedScriptAssert.NeverLoadsCode(DebuggerLuaScripts.StartCapture);
		Assert.Empty(LuaFeatureScan.Scan(DebuggerLuaScripts.StartCapture));
	}

	[Theory]
	[InlineData("lea", "rax,[rbx+10]", ToolErrorKind.InvalidArgument, "only computes an address")]
	[InlineData("nop", "dword ptr [rax+rax+00]", ToolErrorKind.InvalidArgument, "only computes an address")]
	[InlineData("ret", "", ToolErrorKind.InvalidArgument, "has no explicit [...] memory operand")]
	[InlineData("??", "", ToolErrorKind.InvalidArgument, "could not decode an instruction")]
	[InlineData("mov", "rax,qword ptr gs:[00000058]", ToolErrorKind.Unsupported, "the gs segment base")]
	[InlineData("mov", "eax,fs:[00000030]", ToolErrorKind.Unsupported, "the fs segment base")]
	[InlineData("vgatherdps", "xmm0,[rax+xmm1*4+10],xmm2", ToolErrorKind.Unsupported, "vector index (VSIB)")]
	[InlineData("movs", "byte ptr [rdi],byte ptr [rsi]", ToolErrorKind.Unsupported, "exactly one memory operand")]
	[InlineData("mov", "eax,[missing.dll+10]", ToolErrorKind.Unsupported, "could not resolve")]
	[InlineData("mov", "eax,[rax*3]", ToolErrorKind.Unsupported, "cannot evaluate")]
	[InlineData("mov", "eax,[rax+]", ToolErrorKind.Unsupported, "cannot evaluate")]
	[InlineData("mov", "eax,[]", ToolErrorKind.Unsupported, "cannot evaluate")]
	public void DebuggerV2_EffectiveAddressCapture_RefusesAnOperandItCannotEvaluateBeforeAnyBreakpoint(
		string opcode, string parameters, ToolErrorKind kind, string reason)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs);
		InstallStubs($"instructionOpcode = '{opcode}'; instructionParameters = '{parameters}'");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			StartEffectiveAddressCapture(8L));

		Assert.Equal((kind, ToolHostEffect.NotStarted), (exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains(reason, exception.Error.Message, StringComparison.Ordinal);
		Assert.Contains("groupByEffectiveAddress", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Equal((0L, 1L), (ReadGlobal("breakpointCalls"), ReadGlobal("disassemblersDestroyed")));
		Assert.False(Parse(RunKernel("return {exists = jobFind(a[1], a[2]) ~= nil}", OwnNamespace, CaptureJobId))
			.GetProperty("exists").GetBoolean());
	}

	[Fact]
	public void DebuggerV2_EffectiveAddressCapture_EvictsItsGroupIndexWithTheBoundedRing()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs);
		InstallStubs("instructionParameters = 'eax,dword ptr [rax+04]'");

		StartEffectiveAddressCapture(2L);
		InstallStubs("""
		             for index = 1, 1000 do
		             	RAX = index * 16
		             	captureCallback()
		             end
		             RAX = 1000 * 16
		             captureCallback()
		             """);

		JsonElement capture = Parse(RunKernel("""
		                                      local job = jobFind(a[1], a[2])
		                                      local groups, groupKeys = 0, 0
		                                      for _ in pairs(job.groups) do groups = groups + 1 end
		                                      for _ in pairs(job.groupKeys) do groupKeys = groupKeys + 1 end
		                                      return {groups = groups, groupKeys = groupKeys, total = job.total,
		                                      	dropped = job.dropped, last = job.last}
		                                      """, OwnNamespace, CaptureJobId));
		Assert.Equal((2L, 2L, 1001L, 998L, 1000L),
			(capture.GetProperty("groups").GetInt64(), capture.GetProperty("groupKeys").GetInt64(),
				capture.GetProperty("total").GetInt64(), capture.GetProperty("dropped").GetInt64(),
				capture.GetProperty("last").GetInt64()));
		DebuggerCaptureItem[] retained = CaptureItems();
		Assert.Equal([("3E74", 1L), ("3E84", 2L)],
			retained.Select(static item => (item.EffectiveAddress, item.HitCount)));
		Assert.All(retained, static item => Assert.Equal(4, item.OperandSize));
	}

	[Fact]
	public void DebuggerV2_EffectiveAddressCapture_MissingOperandRegister_FailsTheJobInsteadOfGuessing()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs);
		InstallStubs("instructionParameters = '[rax+rcx*8],edx'; RAX = 0x1000; RCX = nil");

		StartEffectiveAddressCapture(8L);
		InstallStubs("captureCallback()");

		LuaJobPage page = PollJob(CaptureJobId, 0, 10);
		Assert.Equal(JobState.Failed, page.Job.State);
		Assert.Contains("registers of the memory operand", page.Job.Error, StringComparison.Ordinal);
		Assert.Empty(page.Items);
	}

	// Cheat Engine's 64-bit operand text names 64-bit registers even under an address-size override (67), so only the
	// legacy prefixes in the instruction bytes reveal that the CPU truncates the address to 32 bits.
	[Theory]
	[InlineData("0x67, 0x8B, 0x00", "eax,[rax]", "RAX", "1000", "1008")]
	[InlineData("0x66, 0x67, 0x8B, 0x00", "ax,[rax]", "RAX", "1000", "1008")]
	[InlineData("0x67, 0x48, 0x8B, 0x47, 0x08", "rax,[rdi+08]", "RDI", "1008", "1010")]
	[InlineData("0x48, 0x8B, 0x67, 0x08", "rsp,[rdi+08]", "RDI", "100001008", "200001010")]
	public void DebuggerV2_EffectiveAddressCapture_AddressSizeOverride_WrapsThe64BitAddressAt32Bits(string bytes,
		string parameters, string register, string firstAddress, string secondAddress)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs);
		InstallStubs($"instructionBytes = {{{bytes}}}; instructionParameters = '{parameters}'");

		StartEffectiveAddressCapture(8L);
		InstallStubs($"{register} = 0x100001000; captureCallback(); {register} = 0x200001008; captureCallback()");

		Assert.Equal([(firstAddress, 1L), (secondAddress, 1L)],
			CaptureItems().Select(static item => (item.EffectiveAddress, item.HitCount)));
	}

	[Fact]
	public void DebuggerV2_EffectiveAddressCapture_SixteenBitAddressing_IsRefusedBeforeAnyBreakpoint()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + CaptureHostStubs);
		// A 32-bit disassembly prints no memory operand for 16-bit addressing.
		InstallStubs("wide = false; instructionBytes = {0x67, 0x8B, 0x07}; instructionParameters = 'eax,'");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			StartEffectiveAddressCapture(8L));

		Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("16-bit address-size override", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal((0L, 1L), (ReadGlobal("breakpointCalls"), ReadGlobal("disassemblersDestroyed")));
	}

	private static DebuggerJobStarted StartEffectiveAddressCapture(long maximumHits)
	{
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		return dispatch.RunLua(CheatEngineToolNames.DebuggerStartCapture, DebuggerLuaScripts.StartCapture,
			DebuggerJsonContext.Default.DebuggerJobStarted, Token, OwnNamespace, CaptureJobId, maximumHits, 60_000L,
			"401000", "execute", 1L, false, true);
	}

	private static DebuggerCaptureItem[] CaptureItems()
	{
		LuaJobPage page = PollJob(CaptureJobId, 0, 100);
		return
		[
			.. page.Items.Select(static item =>
				item.Deserialize(DebuggerJsonContext.Default.DebuggerCaptureItem)!)
		];
	}
}
