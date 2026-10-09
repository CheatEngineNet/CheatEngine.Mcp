using CheatEngine.Mcp.Tests.LiveQualification;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>Real-Lua coverage for the qualification-only fixed compiler bridge; all file and compiler operations are stubbed.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private const string CompilerBridgeStubs = """
		compilerCalls = 0
		removeCalls = 0
		closeCalls = 0
		removeOk = true
		getTickCount = function() return 0 end
		sleep = function() end
		command = 'status'
		response = ''
		seen = {}
		compileCS = function(...)
		  compilerCalls = compilerCalls + 1
		  seen = table.pack(...)
		  return 'C:\\owned\\ce-cscode.dll', nil, 'third'
		end
		io = { open = function(path, mode)
		  if mode == 'r' then
		    return { read = function() return command end, close = function() closeCalls = closeCalls + 1 end }
		  end
		  return { write = function(_, value) response = response .. value end, close = function() closeCalls = closeCalls + 1 end }
		end }
		os = { remove = function() removeCalls = removeCalls + 1; return removeOk end,
		       rename = function() return true end }
		""";

	[Fact]
	public void LiveCompilerBridge_Status_DoesNotCallCompiler()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CompilerBridgeStubs);

		RunBridge("compilerProbe()");

		Assert.Equal(0L, ReadGlobal("compilerCalls"));
		Assert.Equal(1L, ReadGlobal("removeCalls"));
		Assert.Contains("ok\ntrue\n0", (string) ReadGlobal("response")!, StringComparison.Ordinal);
	}

	[Fact]
	public void LiveCompilerBridge_Compile_UsesOnlyFixedPayloadAndEmptyReferences()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CompilerBridgeStubs + "\ncommand = 'compile'");

		RunBridge("compilerProbe()");

		Assert.Equal(1L, ReadGlobal("compilerCalls"));
		Assert.Equal(LiveCompilerPayload.ValidSource, EvaluateLua("seen[1]"));
		Assert.Equal(0L, EvaluateLua("#seen[2]"));
		Assert.Null(EvaluateLua("seen[3]"));
		Assert.Contains("C:\\owned\\ce-cscode.dll", (string) ReadGlobal("response")!, StringComparison.Ordinal);
	}

	[Fact]
	public void LiveCompilerBridge_Wrapper_PreservesNilAndMultipleOriginalReturns()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CompilerBridgeStubs + "\ncommand = 'ignored'");

		RunBridge("local a,b,c=compileCS('sentinel', nil, 'core'); wrapperReturns=table.pack(a,b,c)");

		Assert.Equal(1L, ReadGlobal("compilerCalls"));
		Assert.Equal("sentinel", EvaluateLua("seen[1]"));
		Assert.Null(EvaluateLua("seen[2]"));
		Assert.Equal("core", EvaluateLua("seen[3]"));
		Assert.Equal((3L, "C:\\owned\\ce-cscode.dll", "third"),
			((long) EvaluateLua("wrapperReturns.n")!, (string) EvaluateLua("wrapperReturns[1]")!,
				(string) EvaluateLua("wrapperReturns[3]")!));
		Assert.Null(EvaluateLua("wrapperReturns[2]"));
	}

	[Fact]
	public void LiveCompilerBridge_RequestDequeueFailure_DoesNotCallCompiler()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CompilerBridgeStubs + "\ncommand = 'compile'; removeOk = false");

		RunBridge("probeOk, probeError = pcall(compilerProbe)");

		Assert.Equal(0L, ReadGlobal("compilerCalls"));
		Assert.False((bool) ReadGlobal("probeOk")!);
		Assert.Equal(1L, ReadGlobal("removeCalls"));
	}

	[Fact]
	public void LiveCompilerBridge_ArmHold_DoesNotCompile_AndNextSuccessfulCompilePreservesReturns()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CompilerBridgeStubs + "\ncommand = 'armHold'");

		RunBridge("compilerProbe(); holdReturns=table.pack(compileCS('sentinel', {}, nil))");

		Assert.Equal(1L, ReadGlobal("compilerCalls"));
		Assert.Equal("sentinel", EvaluateLua("seen[1]"));
		Assert.Equal(3L, EvaluateLua("holdReturns.n"));
		Assert.Equal("C:\\owned\\ce-cscode.dll", EvaluateLua("holdReturns[1]"));
		Assert.Null(EvaluateLua("holdReturns[2]"));
		Assert.Equal("third", EvaluateLua("holdReturns[3]"));
		Assert.True((long) ReadGlobal("closeCalls")! >= 4, "The hold release handle must be closed after observation.");
	}

	private static void RunBridge(string tail)
	{
		string bridge = LiveCompilerBridge.Render("request", "response", LiveCompilerPayload.ValidSource,
			static value => string.Concat('"', value.Replace("\\", "\\\\", StringComparison.Ordinal)
				.Replace("\"", "\\\"", StringComparison.Ordinal)
				.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal), '"'));
		InstallStubs(bridge + "\n" + tail);
	}
}
