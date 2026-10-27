using CheatEngine.Client;
using CheatEngine.Client.Inspection;

using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Core.Targets;
using CheatEngine.Mcp.Tests.Core;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Exec;
using CheatEngine.Mcp.Tools.Kernel;

using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     Exercises the public execution and kernel adapters through the real Native Lua bridge. The Lua functions below
///     are inert doubles: they record arguments and return deterministic values without compiling, injecting, calling
///     a target, or accessing DBK/DBVM.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void ExecutionAdapter_CallLocal_EnablesTheFeatureAndForwardsTheKnownResult()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CallLocalStubs);
		ExecTools tools = new(CreateNativeDispatch(new McpFeatureOptions { EnableTargetCodeExecution = true }),
			CreateFiles());

		ExecCallResult result = tools.CallLocal("localcall", -1, Token);

		Assert.Equal(new ExecCallResult("1000", "-1"), result);
		Assert.Equal("FFFFFFFFFFFFFFFF", ReadGlobal("localParameter"));
	}

	[Fact]
	public void ExecutionAdapter_CompileC_TargetSelfForwardsFlagsAndSortsTheKnownSymbols()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             compile = function(source, address, targetSelf, kernelMode, noDebug)
		              observedSource, observedAddress, observedTargetSelf, observedKernelMode, observedNoDebug =
		                source, address, targetSelf, kernelMode, noDebug
		              return {zeta = 0x2000, alpha = 0x1000, ignored = 'not an address'}
		             end
		             """);
		ExecTools tools = new(CreateNativeDispatch(new McpFeatureOptions { EnableTargetCodeExecution = true }),
			CreateFiles());

		ExecCompileResult result = tools.CompileC("int entry(void) { return 7; }", targetSelf: true, noDebug: false,
			cancellationToken: Token);

		Assert.Equal(new[] { new ExecCompiledSymbol("alpha", "1000"), new ExecCompiledSymbol("zeta", "2000") },
			result.Symbols);
		Assert.False(result.SymbolsTruncated);
		Assert.True(result.TargetSelf);
		Assert.False(result.KernelMode);
		Assert.Equal("int entry(void) { return 7; }", ReadGlobal("observedSource"));
		Assert.Null(ReadGlobal("observedAddress"));
		Assert.Equal((true, false, false), (ReadGlobal("observedTargetSelf"), ReadGlobal("observedKernelMode"),
			ReadGlobal("observedNoDebug")));
	}

	[Fact]
	public void ExecutionAdapter_CompileCSharp_ExportsTheNativeCompilerArtifact()
	{
		using RuntimeScope scope = CreateScope();
		using McpFileScratch scratch = new();
		string generated = Path.Combine(scratch.Root, "ce-generated.dll");
		string output = Path.Combine(scratch.Root, "exported.dll");
		byte[] bytes = [0x4D, 0x5A, 0x90, 0x00];
		File.WriteAllBytes(generated, bytes);
		InstallStubs($"""
		              compileCS = function(source, references, coreAssembly)
		               observedCSharpSource, observedReferenceCount, observedCoreAssembly = source, #references, coreAssembly
		               return '{generated.Replace("\\", "\\\\", StringComparison.Ordinal)}'
		              end
		              """);
		McpFilePaths files = new(new McpFileOptions { AllowedRoots = [scratch.Root] },
			Path.Combine(scratch.Root, "registry"), Path.Combine(scratch.Root, "data"));
		ExecCSharpTools tools = new(CreateNativeDispatch(new McpFeatureOptions { EnableTargetCodeExecution = true }),
			files);

		ExecCSharpCompileResult result = tools.CompileCSharp("public static class Entry {}", output,
			cancellationToken: Token);

		Assert.Equal(output, result.OutputPath);
		Assert.Equal(bytes.Length, result.Length);
		Assert.Equal(bytes, File.ReadAllBytes(output));
		Assert.Equal(("public static class Entry {}", 0L),
			(ReadGlobal("observedCSharpSource"), ReadGlobal("observedReferenceCount")));
		Assert.Null(ReadGlobal("observedCoreAssembly"));
	}

	[Fact]
	public void ExecutionAdapter_CallRemote_ResolvesTheFunctionAndForwardsTypedArguments()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CallStubs + "\nwideTarget = true");
		ExecTools tools = new(CreateResolvedNativeDispatch(), CreateFiles());

		ExecCallResult result = tools.CallRemote("function", ExecCallingConvention.Cdecl, 123,
			[new ExecCallArgument(ExecArgumentType.Integral, "7")], Token);

		Assert.Equal(new ExecCallResult("1234", "5"), result);
		Assert.Equal((1L, "rcx=7"), (ReadGlobal("remoteCalls"), ReadGlobal("lastCall")));
	}

	[Fact]
	public void ExecutionAdapter_CallMethod_ResolvesMethodAndInstanceBeforeCalling()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CallStubs);
		ExecTools tools = new(CreateResolvedNativeDispatch(), CreateFiles());

		ExecCallResult result = tools.CallMethod("function", "instance", 1, cancellationToken: Token);

		Assert.Equal(new ExecCallResult("1234", "5"), result);
		Assert.Equal((1L, 1L, 0x5678L),
			(ReadGlobal("methodCalls"), ReadGlobal("methodRegister"), ReadGlobal("methodInstance")));
	}

	[Fact]
	public void ExecutionAdapter_InjectLibrary_HoldsTheApprovedArtifactAndForwardsTheWaitFlag()
	{
		using RuntimeScope scope = CreateScope();
		using McpFileScratch scratch = new();
		string library = Path.Combine(scratch.Root, "fixture.dll");
		File.WriteAllBytes(library, [0x4D, 0x5A]);
		InstallStubs("""
		             injectLibrary = function(path, skipWait)
		              observedLibrary, observedSkipWait = path, skipWait
		              return true
		             end
		             """);
		ExecTools tools = new(CreateNativeDispatch(new McpFeatureOptions { EnableTargetCodeExecution = true }),
			CreateFiles(scratch.Root));

		ExecLibraryInjection result = tools.InjectLibrary(library, true, Token);

		Assert.Equal(new ExecLibraryInjection(library, true), result);
		Assert.Equal((library, true), (ReadGlobal("observedLibrary"), ReadGlobal("observedSkipWait")));
		File.AppendAllText(library, "released");
	}

	[Fact]
	public void ExecutionAdapter_InjectDotNet_HoldsTheApprovedAssemblyAndForwardsEveryArgument()
	{
		using RuntimeScope scope = CreateScope();
		using McpFileScratch scratch = new();
		string assembly = Path.Combine(scratch.Root, "fixture.dll");
		File.WriteAllBytes(assembly, [0x4D, 0x5A]);
		InstallStubs("""
		             injectDotNetDLL = function(path, className, methodName, parameter)
		              observedAssembly, observedClass, observedMethod, observedParameter = path, className, methodName, parameter
		              return 42
		             end
		             """);
		ExecTools tools = new(CreateNativeDispatch(new McpFeatureOptions { EnableTargetCodeExecution = true }),
			CreateFiles(scratch.Root));

		ExecDotNetInjection result = tools.InjectDotNet(assembly, "Fixture.Entry", "Run", "hello", Token);

		Assert.Equal(new ExecDotNetInjection(assembly, "Fixture.Entry", "Run", "42"), result);
		Assert.Equal((assembly, "Fixture.Entry", "Run", "hello"),
			(ReadGlobal("observedAssembly"), ReadGlobal("observedClass"), ReadGlobal("observedMethod"),
				ReadGlobal("observedParameter")));
		File.AppendAllText(assembly, "released");
	}

	[Fact]
	public void KernelAdapter_GetStatus_EnablesKernelFeatureAndCopiesKnownState()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             dbk_initialized = function() return true end
		             dbvm_initialized = function() return false end
		             dbk_getCR0 = function() return 0x80050033 end
		             dbk_getCR3 = function() return 0x1AB000 end
		             dbk_getCR4 = function() return 0x6F0 end
		             dbvm_getCR4 = function() return 0x690 end
		             """);

		KernelStatus result = CreateKernelTools().GetStatus(Token);

		Assert.Equal(new KernelStatus(true, false, "80050033", "1AB000", "6F0", "690"), result);
	}

	[Fact]
	public void KernelAdapter_ReadPhysical_EnablesKernelFeatureAndReturnsTheRequestedByteRange()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             dbvm_initialized = function() return true end
		             dbvm_readPhysicalMemory = function(address, size)
		              observedAddress, observedSize = address, size
		              return {0x48, 0x8B, 0x05}
		             end
		             """);

		KernelPhysicalRead result = CreateKernelTools().ReadPhysical("1ab000", 3, Token);

		Assert.Equal(new KernelPhysicalRead("1AB000", "48 8B 05"), result);
		Assert.Equal((0x1AB000L, 3L), (ReadGlobal("observedAddress"), ReadGlobal("observedSize")));
	}

	[Fact]
	public void KernelAdapter_WritePhysical_EnablesKernelFeatureAndForwardsEachByte()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             dbvm_initialized = function() return true end
		             dbvm_writePhysicalMemory = function(address, bytes)
		              observedAddress, observedFirst, observedCount = address, bytes[1], #bytes
		              return true
		             end
		             """);

		KernelPhysicalWrite result = CreateKernelTools().WritePhysical("1AB000", "48 8B 05", Token);

		Assert.Equal(new KernelPhysicalWrite("1AB000", 3), result);
		Assert.Equal((0x1AB000L, 0x48L, 3L), (ReadGlobal("observedAddress"), ReadGlobal("observedFirst"),
			ReadGlobal("observedCount")));
	}

	[Fact]
	public void KernelAdapter_TranslateAddress_ResolvesTheVirtualAddressBeforeTheDbkCall()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             dbk_getPhysicalAddress = function(address)
		              observedVirtualAddress = address
		              return 0x1AB000
		             end
		             """);
		IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
		ToolDispatch dispatch = CreateResolvedNativeDispatch(new McpFeatureOptions { EnableKernelAccess = true });
		KernelTools tools = new(dispatch, new JobRegistry(dispatch, new TargetResources(), options, TimeProvider.System));

		KernelAddressTranslation result = tools.TranslateAddress("function", Token);

		Assert.Equal(new KernelAddressTranslation("1234", "1AB000"), result);
		Assert.Equal(0x1234L, ReadGlobal("observedVirtualAddress"));
	}

	private static McpFilePaths CreateFiles(string? allowedRoot = null)
	{
		return allowedRoot is null
			? new McpFilePaths(new McpFileOptions(), AppContext.BaseDirectory, AppContext.BaseDirectory)
			: new McpFilePaths(new McpFileOptions { AllowedRoots = [allowedRoot] }, Path.Combine(allowedRoot, "registry"),
				Path.Combine(allowedRoot, "data"));
	}

	private static KernelTools CreateKernelTools()
	{
		IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions { EnableKernelAccess = true });
		return new KernelTools(dispatch, new JobRegistry(dispatch, new TargetResources(), options, TimeProvider.System));
	}

	private static ToolDispatch CreateResolvedNativeDispatch(McpFeatureOptions? features = null)
	{
		IInspectionClient inspection = ClientTestDouble.Create<IInspectionClient>((method, arguments) =>
		{
			Assert.Equal(nameof(IInspectionClient.TryResolveAddress), method.Name);
			SymbolExpression expression = Assert.IsType<SymbolExpression>(arguments![0]);
			arguments[2] = new Address(expression.Value == "instance" ? 0x5678UL : 0x1234UL);
			arguments[3] = default(CheatEngine.Client.Results.CheatEngineFailure);
			return true;
		});
		ICheatEngineClient client = ClientTestDouble.Client(
			(nameof(ICheatEngineClient.Lua), CreateJsonLuaClient().Lua),
			(nameof(ICheatEngineClient.Inspection), inspection));
		IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
		return new ToolDispatch(client, new McpFeatureGate(Options.Create(features ??
			new McpFeatureOptions { EnableTargetCodeExecution = true })), options, new DispatchStatistics(options),
			TimeProvider.System, new RecordingLogger<ToolDispatch>(), new PluginFixedLuaExecutor(client));
	}
}
