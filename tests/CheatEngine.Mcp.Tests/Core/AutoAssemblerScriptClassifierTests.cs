using System.Text;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class AutoAssemblerScriptClassifierTests
{
	private const string A = "AutoAssembler";
	private const string U = "UnsafeLua";
	private const string T = "TargetCodeExecution";
	private const string K = "KernelAccess";

	// The shape of Cheat Engine's own full-injection template, which needs nothing beyond Auto Assembler.
	private const string InjectionTemplate = """
	                                         { Game   : Tutorial-x86_64.exe
	                                           Version:
	                                           Date   : 2026-09-27
	                                           This script does blah blah blah
	                                         }

	                                         define(address,"Tutorial-x86_64.exe"+2B08C)
	                                         define(bytes,29 83 F8 04 00 00)

	                                         [ENABLE]
	                                         assert(address,bytes)
	                                         alloc(newmem,$1000,"Tutorial-x86_64.exe"+2B08C)
	                                         aobscanmodule(INJECT,Tutorial-x86_64.exe,29 83 F8 04 00 00)
	                                         label(code)
	                                         label(return)
	                                         registersymbol(INJECT)

	                                         newmem:
	                                           movss xmm0,[rbx+10]
	                                           dd (float)100
	                                           dq (double)1.5
	                                         code:
	                                           sub [rbx+000007F8],eax
	                                           jmp return

	                                         address:
	                                           jmp newmem
	                                           nop
	                                         return:

	                                         [DISABLE]
	                                         address:
	                                           db bytes
	                                           // sub [rbx+000007F8],eax
	                                         unregistersymbol(INJECT)
	                                         dealloc(newmem)

	                                         {
	                                         // ORIGINAL CODE - INJECTION POINT: "Tutorial-x86_64.exe"+2B08C
	                                         "Tutorial-x86_64.exe"+2B06A: 48 8B 4B 60  -  mov rcx,[rbx+60]
	                                         }
	                                         """;

	[Theory]
	[InlineData("mov eax,1", A)]
	[InlineData(InjectionTemplate, A)]
	[InlineData("{$asm}\n{$strict}\n{$try}\nnop\n{$except}\nnop", A)]
	[InlineData("{$noprologue}\n{$ifdef kernel32.timegettime}\nnop\n{$endif}", A)]
	[InlineData("globalalloc(x,4)\nfullaccess(x,4)\nreadmem(x,4)\nreassemble(x)\nhook5(x,y)\nunhook(x)", A)]
	[InlineData("aobscan(a,90)\naobscanex(b,90)\naobscanregion(c,0,1,90)\naobscanfunction(d,f,90)", A)]
	[InlineData("allocnx(x,4)\nallocxo(y,4)\nalign 10", A)]
	[InlineData("{$lua}\nreturn ''\n{$asm}", A + "," + U)]
	[InlineData("{$LUA}\nreturn ''", A + "," + U)]
	[InlineData("{$Lua }\nreturn ''", A + "," + U)]
	[InlineData("luacall(x)", A + "," + U)]
	[InlineData("LUACALL (x)", A + "," + U)]
	[InlineData("{$luacode x=rax}\nx = 1\n{$asm}", A + "," + U + "," + T)]
	[InlineData("{$c}\nint x;\n{$asm}", A + "," + T)]
	[InlineData("{$C}\nint x;", A + "," + T)]
	[InlineData("{$c linux}\nint x;", A + "," + T)]
	[InlineData("{$ccode x=rax}\nx = 1;\n{$asm}", A + "," + T)]
	[InlineData("loadlibrary(evil.dll)", A + "," + T)]
	[InlineData("LoadLibrary (evil.dll)", A + "," + T)]
	[InlineData("createthread(start)", A + "," + T)]
	[InlineData("createthreadandwait(start,1000)", A + "," + T)]
	[InlineData("include(other.cea)", A + "," + U + "," + T)]
	[InlineData("loadbinary(address,C:\\secret.bin)", A + "," + U + "," + T)]
	[InlineData("kalloc(x,100)", A + "," + K)]
	[InlineData("usemono()", A + "," + U + "," + T)]
	[InlineData("findmonomethod(x,Class:Method)", A + "," + U + "," + T)]
	[InlineData("sharedalloc(x,4)", A + "," + U + "," + T)]
	[InlineData("{$aggressivealloc}", A + "," + U + "," + T)]
	[InlineData("{$foo}", A + "," + U + "," + T)]
	public void Classify_Construct_RequiresItsSwitches(string script, string expected)
	{
		AssertFeatures(expected, AutoAssemblerScriptClassifier.Classify(script));
		// Windows line endings change nothing.
		AssertFeatures(expected, AutoAssemblerScriptClassifier.Classify(script.ReplaceLineEndings("\r\n")));
	}

	[Theory]
	[InlineData("// {$lua}\nnop", A + "," + U)]
	[InlineData("/*\n{$lua}\nprint('x')\n{$asm}\n*/", A + "," + U + "," + T)]
	[InlineData("{\n{$lua}\n}", A + "," + U)]
	[InlineData("// loadlibrary(evil.dll)", A + "," + T)]
	[InlineData("{ loadlibrary(evil.dll) }", A + "," + T)]
	[InlineData("(* loadlibrary(evil.dll) *)", A + "," + T)]
	[InlineData("{ // } luacall(x)", A + "," + U)]
	[InlineData("/* USEMONO() */", A + "," + U + "," + T)]
	[InlineData("db 'loadlibrary(x)'", A + "," + T)]
	[InlineData("[DISABLE]\nloadlibrary(evil.dll)", A + "," + T)]
	[InlineData("[ENABLE]\n{$lua}\nreturn ''\n[DISABLE]\ncreatethread(x)", A + "," + U + "," + T)]
	public void Classify_CommentsStringsAndSections_HideNothing(string script, string expected)
	{
		AssertFeatures(expected, AutoAssemblerScriptClassifier.Classify(script));
	}

	[Theory]
	[InlineData("define(alloc,loadlibrary)\nalloc(evil.dll)", A + "," + T)]
	// The alias name itself also counts as an unknown command: a use before its define is not substituted.
	[InlineData("define(x, luacall)\nx(print)", A + "," + U + "," + T)]
	[InlineData("define(a,alloc)\ndefine(alloc,usemono)\na(1)", A + "," + U + "," + T)]
	[InlineData("define(a,b)\ndefine(b,kalloc)\na(x,4)", A + "," + U + "," + T + "," + K)]
	[InlineData("define(x,usemono())\nx", A + "," + U + "," + T)]
	[InlineData("define(game.exe,createthread)\ngame.exe(x)", A + "," + U + "," + T)]
	[InlineData("define(bytes,F8 04 usemono)\ndb bytes", A)]
	[InlineData("define(inject,\"game.exe\"+10)\nalloc(newmem,$1000,inject)", A)]
	public void Classify_DefineAliases_AreFollowedOnlyIntoCommandPosition(string script, string expected)
	{
		AssertFeatures(expected, AutoAssemblerScriptClassifier.Classify(script));
	}

	[Theory]
	[InlineData("// health value (float)", A + "," + U + "," + T)]
	[InlineData("\"game.exe\"+123 (1 of 2)", A)]
	[InlineData("mov [rax],(float)1", A)]
	public void Classify_ProseBeforeAParenthesis_OverApproximates(string script, string expected)
	{
		AssertFeatures(expected, AutoAssemblerScriptClassifier.Classify(script));
	}

	[Fact]
	public void Classify_Reasons_NameTheConstructThatNeedsEachSwitch()
	{
		McpFeatureRequirements requirements =
			AutoAssemblerScriptClassifier.Classify("{$lua}\nreturn ''\n{$asm}\nloadlibrary(x)\nusemono()");

		Assert.Equal("the tool runs a caller-supplied Auto Assembler script",
			requirements.ReasonFor(McpFeature.AutoAssembler));
		Assert.Equal("the script has a {$lua} block, which runs Lua in Cheat Engine",
			requirements.ReasonFor(McpFeature.UnsafeLua));
		Assert.Equal("the script calls loadlibrary, which loads a library into the target",
			requirements.ReasonFor(McpFeature.TargetCodeExecution));
		Assert.Null(requirements.ReasonFor(McpFeature.KernelAccess));
		Assert.False(requirements.Requires(McpFeature.KernelAccess));
		string[] contractNames = ["unsafe_lua", "auto_assembler", "target_code_execution"];
		Assert.Equal(contractNames, requirements.ContractNames);
	}

	[Fact]
	public void Classify_LongUnknownName_IsShortenedInTheReason()
	{
		string name = new('x', 200);

		string reason = AutoAssemblerScriptClassifier.Classify(name + "()").ReasonFor(McpFeature.UnsafeLua)!;

		Assert.Contains(new string('x', 64) + "...", reason, StringComparison.Ordinal);
		Assert.DoesNotContain(new string('x', 65), reason, StringComparison.Ordinal);
	}

	[Fact]
	public void Classify_LongDefineChain_StaysLinear()
	{
		// 20,000 chained aliases end on usemono; a quadratic closure would take minutes.
		StringBuilder script = new();
		for (int index = 0; index < 20000; index++)
		{
			script.Append("define(n").Append(index).Append(",n").Append(index + 1).Append(")\n");
		}

		script.Append("define(n20000,usemono)\nn0(x)\n");

		AssertFeatures(A + "," + U + "," + T, AutoAssemblerScriptClassifier.Classify(script.ToString()));
	}

	[Fact]
	public void Classify_OverlappingDefinesBeyondTheBudget_AssumesTheWorst()
	{
		// Every define on this line reaches the same last parenthesis: following each value would be quadratic.
		string script = string.Concat(Enumerable.Repeat("define(a,", 50000)) + ")";

		McpFeatureRequirements requirements = AutoAssemblerScriptClassifier.Classify(script);

		AssertFeatures(A + "," + U + "," + T + "," + K, requirements);
		Assert.Contains("too many overlapping define commands", requirements.ReasonFor(McpFeature.KernelAccess),
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("define(\"alloc\",loadlibrary)\nalloc(x,4)")]
	[InlineData("define(al loc,loadlibrary)\nalloc(x,4)")]
	[InlineData("define(alloc loadlibrary)\nalloc(x,4)")]
	[InlineData("define(alloc,\nloadlibrary)\nalloc(x,4)")]
	public void Classify_DefineCheatEngineCannotSubstitute_IsNotFollowed(string script)
	{
		AssertFeatures(A, AutoAssemblerScriptClassifier.Classify(script));
	}

	[Fact]
	public void Enforce_SwitchOff_RefusesWithTheSettingAndTheReason()
	{
		McpFeatureRequirements requirements = AutoAssemblerScriptClassifier.Classify("{$lua}\nreturn ''");
		McpFeatureGate gate = Gate(false);

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => requirements.Enforce(gate, "asm_apply"));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted, false),
			(exception.Error.Kind, exception.Error.HostEffect, exception.Error.Retryable));
		Assert.Equal(
			"asm_apply is disabled by the Mcp:EnableUnsafeLua setting: the script has a {$lua} block, which runs Lua in Cheat Engine.",
			exception.Error.Message);
		Assert.Equal("Set Mcp:EnableUnsafeLua to true in appsettings.json, then disable and re-enable the plugin.",
			exception.Error.Hint);
	}

	[Fact]
	public void Enforce_SeveralSwitchesOff_NamesTheFirstInFeatureOrder()
	{
		McpFeatureRequirements requirements = AutoAssemblerScriptClassifier.Classify("usemono()");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			requirements.Enforce(Gate(autoAssembler: false, unsafeLua: false, codeExecution: false), "asm_check"));

		Assert.Contains("Mcp:EnableUnsafeLua", exception.Error.Message, StringComparison.Ordinal);
		requirements.Enforce(Gate(), "asm_check");
		McpFeatureRequirements.None.Enforce(Gate(false, false, false, false), "asm_check");
		Assert.Empty(McpFeatureRequirements.None.Features);
	}

	private static void AssertFeatures(string expected, McpFeatureRequirements actual)
	{
		string[] names = expected.Split(',');
		Assert.Equal(names.Select(static name => Enum.Parse<McpFeature>(name)).Order(), actual.Features);
	}

	private static McpFeatureGate Gate(bool unsafeLua = true, bool autoAssembler = true, bool codeExecution = true,
		bool kernel = true)
	{
		return new McpFeatureGate(Options.Create(new McpFeatureOptions
		{
			EnableUnsafeLua = unsafeLua,
			EnableAutoAssembler = autoAssembler,
			EnableTargetCodeExecution = codeExecution,
			EnableKernelAccess = kernel
		}));
	}
}
