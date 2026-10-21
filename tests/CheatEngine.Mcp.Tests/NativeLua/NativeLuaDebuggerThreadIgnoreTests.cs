using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Debugger;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed partial class NativeLuaToolRuntimeTests
{
	[Theory]
	[InlineData(true, "true", true)]
	[InlineData(false, "true", true)]
	[InlineData(true, "false", false)]
	[InlineData(false, "false", false)]
	[InlineData(true, "nil", false)]
	[InlineData(false, "nil", false)]
	public void DebuggerAdapter_ThreadIgnore_RequiresHostAcknowledgement(bool ignored, string hostResult, bool accepted)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs($$"""
		             debug_isDebugging = function() return true end
		             addCalls, removeCalls = 0, 0
		             debug_addThreadToNoBreakList = function(thread)
		                assert(thread == 19); addCalls = addCalls + 1; return {{hostResult}}
		             end
		             debug_removeThreadFromNoBreakList = function(thread)
		                assert(thread == 19); removeCalls = removeCalls + 1; return {{hostResult}}
		             end
		             """);
		using DebuggerToolHarness harness = CreateDebuggerTools();
		if (accepted)
		{
			DebuggerThreadIgnoreChanged result = harness.Tools.SetThreadIgnored(19, ignored, Token);
			Assert.Equal((19L, ignored), (result.ThreadId, result.Ignored));
		}
		else
		{
			CheatEngineToolException failure = Assert.Throws<CheatEngineToolException>(() => harness.Tools.SetThreadIgnored(19, ignored, Token));
			Assert.False(failure.Error.Retryable);
			Assert.Contains("Cheat Engine rejected the thread no-break change", failure.Message, StringComparison.Ordinal);
		}
		Assert.Equal(ignored ? 1L : 0L, ReadGlobal("addCalls"));
		Assert.Equal(ignored ? 0L : 1L, ReadGlobal("removeCalls"));
	}
}
