using System.ComponentModel;
using System.Reflection;

using CheatEngine.Client.Assembly;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Asm;

using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Tools.Asm;

/// <summary>
///     <c>asm_check</c> stays read-only: Cheat Engine runs <c>{$lua}</c> blocks, <c>luacall</c>, Lua-registered
///     commands and <c>loadlibrary</c>, allocates for <c>globalalloc</c> and evaluates <c>$</c> symbols as Lua while it
///     only checks a script, so every script that needs a switch beyond <c>auto_assembler</c>, uses
///     <c>globalalloc</c> or writes a <c>$</c> symbol is refused before Cheat Engine sees it. Commands are matched only
///     after Cheat Engine removed comments, so a comment cannot make a plain script look like one that runs code.
/// </summary>
public sealed class AsmCheckSafetyTests
{
	private const string Enable = "[ENABLE]\n";
	private const string Disable = "\n[DISABLE]\nnop\n";

	private const string LuaSymbol =
		"writes $ before something other than hexadecimal digits, which Cheat Engine's symbol handler evaluates as Lua";

	/// <summary>
	///     The layout Cheat Engine 7.7 generates for an x64 API hook (<c>frmautoinjectunit.pas</c>
	///     <c>generateAPIHookScript</c>), joined as <c>asm_generate_api_hook</c> returns it.
	/// </summary>
	private const string GeneratedX64Hook =
		"[ENABLE]\nalloc(originalcall0,1024,kernel32.Sleep)\n" +
		"alloc(jumptrampoline0,64,kernel32.Sleep); //special jump trampoline in the current region (64-bit)\n" +
		"label(jumptrampoline0address)\nlabel(returnhere0)\n\n\noriginalcall0:\nmov [rsp+08],rbx\n" +
		"jmp returnhere0\n\njumptrampoline0:\njmp [jumptrampoline0address]\njumptrampoline0address:\n" +
		"dq myHook\n\nkernel32.Sleep:\njmp jumptrampoline0\nreturnhere0:\n\n" +
		"[DISABLE]\nkernel32.Sleep:\ndb 48 89 5C 24 08\ndealloc(originalcall0)\ndealloc(jumptrampoline0)\n";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public static TheoryData<string, string> RefusedScripts => new()
	{
		{ "{$lua}\nprint('checked')\n{$asm}\nnop", "has a {$lua} block, which runs Lua in Cheat Engine" },
		{ "{$luacode}\nprint('x')\n{$asm}", "has a {$luacode} block" },
		{ "{$c}\nint x;\n{$asm}", "has a {$c} or {$ccode} block" },
		{ "{$ccode}\nx=1;\n{$asm}", "has a {$c} or {$ccode} block" },
		{ "luacall(print('checked'))", "calls luacall, which runs Lua in Cheat Engine" },
		{ "loadlibrary(helper.dll)", "calls loadlibrary, which loads a library into the target" },
		{ "define(alloc,loadlibrary)\nalloc(helper.dll)", "calls loadlibrary" },
		{ "include(other.cea)", "includes another file" },
		{ "createthread(newmem)", "calls createthread" },
		{ "loadbinary(newmem,data.bin)", "calls loadbinary" },
		{ "kalloc(buffer,16)", "calls kalloc" },
		{ "USEMONO()", "uses USEMONO, which is not a built-in Auto Assembler command" },
		{ "{$unknown}", "has the unrecognized directive {$unknown}" },
		{ "globalalloc(buffer,16)", "uses globalalloc, which allocates target memory" },
		{ "GLOBALALLOC(buffer,16)", "uses globalalloc" },
		{ "define(alloc,globalalloc)\nalloc(buffer,16)", "uses globalalloc" },
		{ "define(keep,globalalloc)\nkeep(buffer,16)", "uses keep, which is not a built-in Auto Assembler command" },
		{ "nop { globalalloc(buffer,16) }", "uses globalalloc" },
		{ "{ note } luacall(check())", "calls luacall" },
		{ "// {$lua}\nnop", "has a {$lua} block" },
		{ "nop /* a */ loadlibrary(helper.dll)", "calls loadlibrary" },
		{ "{ see // } luacall(check())", "calls luacall" },
		{ "alloc(newmem,64,$myGlobal)", LuaSymbol },
		{ "fullaccess($myGlobal,4)", LuaSymbol },
		{ "$myGlobal:\nnop", LuaSymbol },
		{ "$myGlobal+10:\nnop", LuaSymbol },
		{ "mov rax,[$myGlobal]", LuaSymbol },
		{ "define(base,$myGlobal)\nbase:\nnop", LuaSymbol },
		{ "mov eax,$12G", LuaSymbol },
		{ "/*\nUSEMONO()\n*/\nnop", "uses USEMONO" },
		{ "{\n  usemono()  \n}", "uses USEMONO" },
		{ "/*\nPREPARECHEADER(Player,health)\n*/", "uses PREPARECHEADER" }
	};

	/// <summary>Scripts that stay plain Auto Assembler once Cheat Engine removed their comments.</summary>
	public static TheoryData<string> PlainScripts => new()
	{
		"alloc(newmem,64)\nlabel(myglobalalloc)\nnewmem:\nmyglobalalloc:\nnop",
		"alloc(newmem,64)\nlabel(globalalloc_x)\nnewmem:\nglobalalloc_x:\nnop",
		"aobscanmodule(site,game.exe,48 8B ?? 05)\nassert(site,48 8B)\nsite:\ndb 90 90",
		"alloc(newmem,64,game.exe) // health (x2)\nnewmem:\nnop",
		"alloc(newmem,64) /* region (64-bit) */\nnewmem:\nnop",
		"/* luacall(x)\nloadlibrary(y) */\nnop",
		"nop // globalalloc(buffer,16)",
		"nop // $myGlobal",
		"$1000:\nnop",
		"mov eax,$FF\ndb 'a // b' 90",
		"mov eax,$ff+$10",
		"// USEMONO()\nnop"
	};

	[Theory]
	[MemberData(nameof(RefusedScripts))]
	public void Check_ScriptThatRunsWhileChecked_IsRefusedBeforeCheatEngineSeesIt(string body, string reason)
	{
		StateTestHarness harness = new();
		int checks = 0;
		AsmTools tools = new(harness.Dispatch, harness.Resources, CountingAutoAssembler(() => checks++));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.Check(Enable + body + Disable, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.NotStarted, false),
			(exception.Error.Kind, exception.Error.HostEffect, exception.Error.Retryable));
		Assert.StartsWith(
			"asm_check only checks scripts that need no switch beyond auto_assembler, because Cheat Engine runs parts of other scripts while it checks them: ",
			exception.Error.Message, StringComparison.Ordinal);
		Assert.Contains(reason, exception.Error.Message, StringComparison.Ordinal);
		Assert.Contains("review_aa_script", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Contains("asm_apply", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Equal((0, 0), (checks, harness.Dispatches));
		Assert.Empty(harness.LuaCalls);
	}

	[Fact]
	public void Check_LuaScriptWithUnsafeLuaOff_IsStillUnsupportedRatherThanDisabled()
	{
		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		ToolDispatch dispatch = new(ClientTestDouble.Client(new RecordingDispatcher().Dispatcher,
				CancellationToken.None),
			new McpFeatureGate(Options.Create(new McpFeatureOptions { EnableUnsafeLua = false })), execution,
			new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>());
		AsmTools tools = new(dispatch, new TargetResources(), CountingAutoAssembler(static () => { }));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.Check(Enable + "{$lua}\nreturn ''\n{$asm}" + Disable, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.Unsupported, exception.Error.Kind);
	}

	[Theory]
	[MemberData(nameof(PlainScripts))]
	public void Check_PureAutoAssemblerScript_ReachesCheatEngine(string body)
	{
		StateTestHarness harness = new();
		int checks = 0;
		AsmTools tools = new(harness.Dispatch, harness.Resources, CountingAutoAssembler(() => checks++));

		AsmCheckResult result = tools.Check(Enable + body + Disable, cancellationToken: Token);

		Assert.Equal((false, AsmScriptSection.Enable), (result.Accepted, result.FailedSection));
		Assert.Equal(1, checks);
	}

	[Fact]
	public void Check_CheatEngineGeneratedX64Hook_ReachesCheatEngine()
	{
		StateTestHarness harness = new();
		int checks = 0;
		AsmTools tools = new(harness.Dispatch, harness.Resources, CountingAutoAssembler(() => checks++));

		AsmCheckResult result = tools.Check(GeneratedX64Hook, cancellationToken: Token);

		Assert.Equal((false, AsmScriptSection.Enable, 1), (result.Accepted, result.FailedSection, checks));
	}

	[Fact]
	public void Screen_CheatEngineGeneratedX64Hook_NeedsOnlyAutoAssembler()
	{
		Assert.Equal([McpFeature.AutoAssembler], AsmCheckScreen.Screen(GeneratedX64Hook).Features);
	}

	[Theory]
	[InlineData("a // b\nc", true, "a     \nc")]
	[InlineData("a /* b\nc */ d", true, "a     \n     d")]
	[InlineData("a { b\nc } d", true, "a    \n    d")]
	[InlineData("a { b } d", false, "a { b } d")]
	[InlineData("a { see // } b", true, "a            b")]
	[InlineData("a { see // } b", false, "a { see       ")]
	[InlineData("db 'x // y' // z\nw", true, "db 'x // y'     \nw")]
	[InlineData("db 'x\n// y", true, "db 'x\n    ")]
	[InlineData("a /*/ b", true, "a     b")]
	[InlineData("a\r\n// b\r\nc", true, "a\r\n    \r\nc")]
	public void BlankComments_BlanksWhatRemovecommentsRemoves(string script, bool braceComments, string expected)
	{
		string blanked = AsmCheckScreen.BlankComments(script, braceComments);

		Assert.Equal(expected, blanked);
		Assert.Equal(script.Length, blanked.Length);
	}

	[Theory]
	[InlineData("$1000:", false)]
	[InlineData("mov eax,$ff", false)]
	[InlineData("jmp $+5", false)]
	[InlineData("mov eax,[$10+$20]", false)]
	[InlineData("dd $7FFE0000 ", false)]
	[InlineData("$myGlobal:", true)]
	[InlineData("mov rax,[$Global]", true)]
	[InlineData("alloc(x,4,$10 20)", true)]
	[InlineData("$1000::", true)]
	[InlineData("no dollar here", false)]
	public void HasLuaSymbol_FindsEveryDollarTokenThatIsNotHexadecimal(string code, bool expected)
	{
		Assert.Equal(expected, AsmCheckScreen.HasLuaSymbol(code));
	}

	[Fact]
	public void Check_StaysAnnotatedReadOnlyAndIdempotent()
	{
		McpServerToolAttribute tool = typeof(AsmTools).GetMethod(nameof(AsmTools.Check))!
			.GetCustomAttribute<McpServerToolAttribute>()!;

		Assert.Equal((true, false, true), (tool.ReadOnly, tool.Destructive, tool.Idempotent));
	}

	[Fact]
	public void Check_Description_NamesTheRefusedConstructs()
	{
		string description = typeof(AsmTools).GetMethod(nameof(AsmTools.Check))!
			.GetCustomAttribute<DescriptionAttribute>()!.Description;

		Assert.InRange(description.Length, 1, 1024);
		Assert.DoesNotContain("without running any of it", description, StringComparison.Ordinal);
		Assert.Contains("refuses, as unsupported, any script that needs a switch beyond auto_assembler", description,
			StringComparison.Ordinal);
		Assert.Contains("globalalloc", description, StringComparison.Ordinal);
		Assert.Contains("writes $ before anything but hexadecimal digits", description, StringComparison.Ordinal);
		Assert.Contains("Commands in // and /* */ comments are ignored", description, StringComparison.Ordinal);
		Assert.Contains("symbol-lookup callbacks that Lua registered, such as the shipped luasymbols.lua", description,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("globalalloc(x,4)", true)]
	[InlineData("GlobalAlloc(x,4)", true)]
	[InlineData("x:globalalloc", true)]
	[InlineData("globalalloc", true)]
	[InlineData("myglobalalloc(x,4)", false)]
	[InlineData("globalalloc2(x,4)", false)]
	[InlineData("a.globalalloc(x)", false)]
	[InlineData("globalalloc@", false)]
	[InlineData("\u00E9globalalloc", false)]
	[InlineData("", false)]
	public void ContainsToken_MatchesWholeCheatEngineTokensIgnoringCase(string script, bool expected)
	{
		Assert.Equal(expected, AsmCheckScreen.ContainsToken(script, "globalalloc"));
	}

	/// <summary>An Auto Assembler Client that rejects every ENABLE section and counts the checks.</summary>
	private static IAutoAssemblerClient CountingAutoAssembler(Action checkedOnce)
	{
		return ClientTestDouble.Create<IAutoAssemblerClient>((method, _) =>
		{
			if (method.Name != nameof(IAutoAssemblerClient.Check))
			{
				throw new NotSupportedException(method.Name);
			}

			checkedOnce();
			return new AutoAssemblerCheckResult(false, "Error in line 2", false);
		});
	}
}
