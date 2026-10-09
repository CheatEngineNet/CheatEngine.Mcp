using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Lua;
using CheatEngine.Mcp.Tools.Exec;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>Native Lua coverage for the fixed <c>compileCS</c> bridge; it does not qualify Cheat Engine's compiler.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private const string CSharpCompilerStubs = """
	                                           compilerCalls = 0
	                                           compilerResult = table.pack('C:\\temp\\compiled.dll')
	                                           compileCS = function(source, references, core)
	                                             compilerCalls = compilerCalls + 1
	                                             compilerSource, compilerReferences, compilerCore = source, references, core
	                                             return table.unpack(compilerResult, 1, compilerResult.n)
	                                           end
	                                           """;

	[Fact]
	public void ExecCompileCSharp_Success_PassesSourceReferencesAndOptionalCoreWithoutInjection()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CSharpCompilerStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		ExecCSharpCompilerOutput result = dispatch.RunLua(CheatEngineToolNames.ExecCompileCSharp,
			ExecCSharpScripts.Compile, ExecCSharpJsonContext.Default.ExecCSharpCompilerOutput, Token, "class C {}",
			new[] { "C:\\refs\\one.dll", "C:\\refs\\two.dll" }, "C:\\core.dll");

		Assert.Equal("C:\\temp\\compiled.dll", result.AssemblyPath);
		Assert.True(result.CompilerAvailable);
		Assert.Equal((1L, "class C {}", "C:\\core.dll"),
			(ReadGlobal("compilerCalls"), ReadGlobal("compilerSource"), ReadGlobal("compilerCore")));
		Assert.Equal(2L, EvaluateLua("#compilerReferences"));
	}

	[Theory]
	[InlineData(false, "line 4: expected ;")]
	[InlineData(true, "compiler bridge failed")]
	public void ExecCompileCSharp_DiagnosticOrPcallFailure_ReturnsBoundedDiagnosticOutcome(bool raises,
		string expected)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(raises
			? CSharpCompilerStubs + "\ncompileCS = function() error('compiler bridge failed') end"
			: CSharpCompilerStubs + "\ncompilerResult = table.pack(nil, 'line 4: expected ;')");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		ExecCSharpCompilerOutput outcome = dispatch.RunLua(CheatEngineToolNames.ExecCompileCSharp,
			ExecCSharpScripts.Compile, ExecCSharpJsonContext.Default.ExecCSharpCompilerOutput, Token, "class C {}",
			Array.Empty<string>(), null);

		Assert.Null(outcome.AssemblyPath);
		Assert.True(outcome.CompilerAvailable);
		Assert.False(outcome.DiagnosticTruncated);
		Assert.Contains(expected, outcome.Diagnostic, StringComparison.Ordinal);
	}

	[Fact]
	public void ExecCompileCSharp_AbsentCompilerApi_ReturnsUnavailableOutcomeBeforeAnyCompilerCall()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("compileCS = nil; compilerCalls = 0");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		ExecCSharpCompilerOutput outcome = dispatch.RunLua(CheatEngineToolNames.ExecCompileCSharp,
			ExecCSharpScripts.Compile, ExecCSharpJsonContext.Default.ExecCSharpCompilerOutput, Token, "class C {}",
			Array.Empty<string>(), null);

		Assert.False(outcome.CompilerAvailable);
		Assert.Null(outcome.AssemblyPath);
		Assert.Equal(0L, ReadGlobal("compilerCalls"));
	}

	[Fact]
	public void ExecCompileCSharp_DiagnosticBeyondSixteenKiB_IsTruncatedAndMarked()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CSharpCompilerStubs + "\ncompilerResult = table.pack(nil, string.rep('d', 17000))");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		ExecCSharpCompilerOutput outcome = dispatch.RunLua(CheatEngineToolNames.ExecCompileCSharp,
			ExecCSharpScripts.Compile, ExecCSharpJsonContext.Default.ExecCSharpCompilerOutput, Token, "class C {}",
			Array.Empty<string>(), null);

		Assert.Null(outcome.AssemblyPath);
		Assert.Equal(16_384, outcome.Diagnostic!.Length);
		Assert.True(outcome.DiagnosticTruncated);
	}
}
