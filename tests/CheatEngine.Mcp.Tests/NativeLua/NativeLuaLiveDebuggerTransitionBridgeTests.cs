using CheatEngine.Mcp.Tests.LiveQualification;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>Real-Lua coverage for the private owned-INT3 transition diagnostic bridge.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private static readonly string[] TransitionAdvisoryFields = ["ok", "1", "sample", "true", "boolean", "true", "true", "boolean", "true", "true", "nil", "nil", "false", "string", "string"];
	[Fact]
	public void DebuggerTransitionBridge_ExactEvent_ForwardsMultipleReturnsAndPublishesReceipt()
	{
		using RuntimeScope scope = CreateScope();
		RunDebuggerTransitionBridge("""
			debugger_onBreakpoint = function() return 'first', nil, 'third' end
			request = [[1
			install
			42
			1000
			]]; createdTimer.OnTimer(); clearReceipt()
			THREADID=42; RIP=0x1000; returns=table.pack(debugger_onBreakpoint())
			request = [[2
			sample
			]]; createdTimer.OnTimer()
			""");

		Assert.Equal((3L, "first", "third"), ((long) EvaluateLua("returns.n")!, EvaluateLua("returns[1]"), EvaluateLua("returns[3]")));
		Assert.Null(EvaluateLua("returns[2]"));
		Assert.Contains("ok\n2\nsample", (string) ReadGlobal("response")!, StringComparison.Ordinal);
		Assert.EndsWith("1\n42\n1000\n1\nnumber\ninteger\n42\nnumber\ninteger\n4096\nnil\nnot_number\nnil\n",
			(string) ReadGlobal("response")!, StringComparison.Ordinal);
	}

	[Fact]
	public void DebuggerTransitionBridge_UnrelatedEvent_IsIgnoredAndCallbackErrorIsPreserved()
	{
		using RuntimeScope scope = CreateScope();
		RunDebuggerTransitionBridge("""
			debugger_onBreakpoint = function() error('sentinel', 0) end
			request = [[1
			install
			42
			1000
			]]; createdTimer.OnTimer(); clearReceipt()
			THREADID=43; RIP=0x1000; callbackOk, callbackError=pcall(debugger_onBreakpoint)
			request = [[2
			sample
			]]; createdTimer.OnTimer()
			""");

		Assert.False((bool) ReadGlobal("callbackOk")!);
		Assert.Equal("sentinel", ReadGlobal("callbackError"));
		Assert.EndsWith("0\n\n\n1\nnumber\ninteger\n43\nnumber\ninteger\n4096\nnil\nnot_number\nnil\n",
			(string) ReadGlobal("response")!, StringComparison.Ordinal);
		Assert.Equal(0L, ReadGlobal("contextCalls"));
		Assert.Equal(0L, ReadGlobal("continueCalls"));
	}

	[Fact]
	public void DebuggerTransitionBridge_NoOriginalCallback_ReturnsSourceBackedZero()
	{
		using RuntimeScope scope = CreateScope();
		RunDebuggerTransitionBridge("""
			request = [[1
			install
			42
			1000
			]]; createdTimer.OnTimer()
			THREADID=42; RIP=0x1000; callbackResult=debugger_onBreakpoint()
			""");

		Assert.Equal(0L, ReadGlobal("callbackResult"));
	}

	[Fact]
	public void DebuggerTransitionBridge_Restore_PreservesOriginalCallbackIdentity()
	{
		using RuntimeScope scope = CreateScope();
		RunDebuggerTransitionBridge("""
			original=function() return 'kept' end; debugger_onBreakpoint=original
			request = [[1
			install
			42
			1000
			]]; createdTimer.OnTimer(); clearReceipt()
			request = [[2
			restore
			]]; createdTimer.OnTimer()
			""");

		Assert.Equal(true, EvaluateLua("debugger_onBreakpoint == original"));
		Assert.Equal("ok\n2\nrestore\n", ReadGlobal("response"));
	}

	[Fact]
	public void DebuggerTransitionBridge_RejectsReplacementWithoutOverwritingIt()
	{
		using RuntimeScope scope = CreateScope();
		RunDebuggerTransitionBridge("""
			request = [[1
			install
			42
			1000
			]]; createdTimer.OnTimer(); clearReceipt()
			replacement=function() return 9 end; debugger_onBreakpoint=replacement
			request = [[2
			restore
			]]; createdTimer.OnTimer()
			""");

		Assert.Equal(true, EvaluateLua("debugger_onBreakpoint == replacement"));
		Assert.StartsWith("error\n2\nrestore\n", (string) ReadGlobal("response")!, StringComparison.Ordinal);
	}

	[Fact]
	public void DebuggerTransitionBridge_ConsumesMalformedAndOverlongRequestsBeforeLaterSample()
	{
		using RuntimeScope scope = CreateScope();
		RunDebuggerTransitionBridge("""
			request = [[3
			compile
			]]; createdTimer.OnTimer(); malformed=response; clearReceipt()
			request = '4\nsample\n' .. string.rep('x', 128); createdTimer.OnTimer(); overlong=response; clearReceipt()
			request = [[5
			sample
			]]; createdTimer.OnTimer(); valid=response
			""");

		Assert.StartsWith("error\n3\ncompile\n", (string) ReadGlobal("malformed")!, StringComparison.Ordinal);
		Assert.StartsWith("error\n4\nsample\n", (string) ReadGlobal("overlong")!, StringComparison.Ordinal);
		Assert.StartsWith("ok\n5\nsample\n", (string) ReadGlobal("valid")!, StringComparison.Ordinal);
		Assert.Equal(3L, ReadGlobal("removeCalls"));
	}

	[Fact]
	public void DebuggerTransitionBridge_RejectsExtraArgumentsAndKeepsOnlyFirstOwnedEvent()
	{
		using RuntimeScope scope = CreateScope();
		RunDebuggerTransitionBridge("""
			request = [[1
			install
			42
			1000
			]]; createdTimer.OnTimer(); clearReceipt()
			THREADID=42; RIP=0x1000; debugger_onBreakpoint(); RIP=0x1000; debugger_onBreakpoint()
			request = [[2
			sample
			extra
			]]; createdTimer.OnTimer(); extra=response; clearReceipt()
			request = [[3
			sample
			]]; createdTimer.OnTimer()
			""");

		Assert.StartsWith("error\n2\nsample\n", (string) ReadGlobal("extra")!, StringComparison.Ordinal);
		Assert.EndsWith("1\n42\n1000\n2\nnumber\ninteger\n42\nnumber\ninteger\n4096\nnil\nnot_number\nnil\n",
			(string) ReadGlobal("response")!, StringComparison.Ordinal);
	}

	[Fact]
	public void DebuggerTransitionBridge_CallbackCountCapsAndRetainsFirstOpaqueRawValues()
	{
		using RuntimeScope scope = CreateScope();
		RunDebuggerTransitionBridge("""
			request = [[1
			install
			42
			1000
			]]; createdTimer.OnTimer(); clearReceipt()
			THREADID={opaque=true}; RIP='not-an-address'; EIP=false
			for i=1,65536 do debugger_onBreakpoint() end
			request = [[2
			sample
			]]; createdTimer.OnTimer()
			""");

		Assert.EndsWith("0\n\n\n65535\ntable\nnot_number\ntable\nstring\nnot_number\nnot-an-address\nboolean\nnot_number\nfalse\n",
			(string) ReadGlobal("response")!, StringComparison.Ordinal);
	}

	[Fact]
	public void DebuggerTransitionBridge_RejectsNonOwnedThreadAndUnsupportedRipShapes()
	{
		using RuntimeScope scope = CreateScope();
		RunDebuggerTransitionBridge("""
			request = [[1
			install
			0
			1000
			]]; createdTimer.OnTimer(); zeroThread=response; clearReceipt()
			request = [[2
			install
			42
			00000000000000000
			]]; createdTimer.OnTimer(); wideRip=response; clearReceipt()
			request = [[3
			install
			42
			0000
			]]; createdTimer.OnTimer(); zeroRip=response
			""");

		Assert.StartsWith("error\n1\ninstall\n", (string) ReadGlobal("zeroThread")!, StringComparison.Ordinal);
		Assert.StartsWith("error\n2\ninstall\n", (string) ReadGlobal("wideRip")!, StringComparison.Ordinal);
		Assert.StartsWith("error\n3\ninstall\n", (string) ReadGlobal("zeroRip")!, StringComparison.Ordinal);
	}

	[Fact]
	public void DebuggerTransitionBridge_SamplePreservesOpaqueRawStateAsPrimitiveMetadata()
	{
		using RuntimeScope scope = CreateScope();
		RunDebuggerTransitionBridge("""
			request = [[1
			sample
			]]; createdTimer.OnTimer()
			""");

		string[] lines = ((string) ReadGlobal("response")!).Split('\n');
		Assert.Equal(string.Empty, lines[^1]);
		LiveDebuggerTransitionReceipt receipt = LiveDebuggerTransitionReceipt.Parse(lines[..^1], "1", "sample");
		Assert.Equal(TransitionAdvisoryFields, receipt.Lines[..15]);
		Assert.Equal(0, receipt.EventCount);
		Assert.Equal(0, receipt.CallbackCount);
		Assert.Null(receipt.FirstCallback);
	}

	private static void RunDebuggerTransitionBridge(string tail)
	{
		InstallStubs(BridgeStubs + "\n" + LiveDebuggerTransitionBridge.Render("C:/run/request", "C:/run/response", QuoteTransition) + "\n" + tail);
	}

	private static string QuoteTransition(string value) => '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal)
		.Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal)
		.Replace("\n", "\\n", StringComparison.Ordinal) + '"';

	private const string BridgeStubs = """
		request = nil; response = ''; responseTemporary = ''; responsePresent = false; removeCalls = 0; contextCalls = 0; continueCalls = 0
		io = { open = function(path, mode)
		  assert(path == 'C:/run/request' or path == 'C:/run/response.tmp')
		  if mode == 'r' then
		    if request == nil then return nil end
		    return { read = function(_, count) return request:sub(1, count) end, close = function() end }
		  end
		  responseTemporary = ''
		  return { write = function(_, value) responseTemporary = responseTemporary .. value end, close = function() end }
		end }
		os = { remove = function(path) assert(path == 'C:/run/request'); request = nil; removeCalls = removeCalls + 1; return true end,
		       rename = function(from, to) assert(from == 'C:/run/response.tmp' and to == 'C:/run/response'); assert(not responsePresent, 'Windows destination exists'); response = responseTemporary; responsePresent = true; return true end }
		createTimer = function(_, _) createdTimer = {}; return createdTimer end
		clearReceipt = function() response = ''; responsePresent = false end
		debug_isDebugging = function() return true end
		debug_canBreak = function() return true end
		debug_isBroken = function() return nil end
		debug_isStepping = function() error('opaque', 0) end
		debug_getContext = function() contextCalls = contextCalls + 1 end
		debug_continueFromBreakpoint = function() continueCalls = continueCalls + 1 end
		""";
}
