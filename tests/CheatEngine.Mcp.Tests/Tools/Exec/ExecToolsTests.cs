using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Core;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Exec;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools.Exec;

/// <summary>Execution tools remain explicitly gated, bounded and limited to their reviewed fixed Lua bodies.</summary>
public sealed class ExecToolsTests
{
	private const string AbsentAssembly = @"C:\mods\Loader.dll";

	/// <summary>
	///     Characters that end the Auto Assembler string Cheat Engine formats a managed-injection value into, start a
	///     new Auto Assembler line, or open an Auto Assembler comment or directive.
	/// </summary>
	private static IEnumerable<char> AssemblerBreakingCharacters =>
	[
		'\'', '"', '{', '}', '\r', '\n', '\0', '\t', (char) 0x1B, (char) 0x7F, (char) 0x85, (char) 0x2028,
		(char) 0x2029
	];

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void EveryExecutionMethod_DeclaresTheTargetCodeExecutionRequirement()
	{
		MethodInfo[] methods =
		[
			Method(nameof(ExecTools.InjectLibrary)), Method(nameof(ExecTools.InjectDotNet)),
			Method(nameof(ExecTools.CallRemote)), Method(nameof(ExecTools.CallMethod)),
			Method(nameof(ExecTools.CallLocal)),
			Method(nameof(ExecTools.CompileC))
		];

		Assert.All(methods, static method => Assert.Contains(method.GetCustomAttributes<RequiresFeatureAttribute>(),
			static requirement => requirement.Feature == McpFeature.TargetCodeExecution));
	}

	[Fact]
	public void CallLocal_DisabledTargetExecution_RefusesBeforeDispatch()
	{
		DispatchHarness harness = new(new McpFeatureOptions { EnableTargetCodeExecution = false });

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			CreateTools(harness).CallLocal("401000", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void CallRemote_MoreThanSixteenArguments_RefusesBeforeAddressResolutionOrDispatch()
	{
		DispatchHarness harness = new();
		ExecCallArgument[] arguments = Enumerable.Range(0, ExecSupport.MaximumArguments + 1)
			.Select(static _ => new ExecCallArgument(ExecArgumentType.Integral, "1")).ToArray();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			CreateTools(harness).CallRemote("401000", arguments: arguments, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void CallRemote_FormerStringArgumentCode_RefusesBeforeAddressResolutionOrDispatch()
	{
		DispatchHarness harness = new();
		ExecCallArgument[] arguments = [new((ExecArgumentType) 3, "temporary target buffer")];

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			CreateTools(harness).CallRemote("401000", arguments: arguments, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("Allocate and write strings or buffers separately", exception.Error.Message,
			StringComparison.Ordinal);
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void CallArgument_ValueIsAJsonStringEvenForAnInteger()
	{
		ExecCallArgument[]? quoted = JsonSerializer.Deserialize("""[{"type":"integer","value":"10"}]""",
			ExecJsonContext.Default.ExecCallArgumentArray);

		Assert.Equal(new ExecCallArgument(ExecArgumentType.Integral, "10"), Assert.Single(quoted!));
		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""[{"type":"integer","value":10}]""",
			ExecJsonContext.Default.ExecCallArgumentArray));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(4)]
	[InlineData(16)]
	public void CallMethod_StackPointerOrUnknownClassRegister_RefusesBeforeAddressResolutionOrDispatch(int register)
	{
		DispatchHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			CreateTools(harness).CallMethod("401000", "500000", register, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.StartsWith("classRegister: ", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void CompileC_SourceBeyondTheBound_RefusesBeforeDispatch()
	{
		DispatchHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			CreateTools(harness).CompileC(new string('x', 131_073), targetSelf: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void CompileC_KernelModeWithoutKernelAccess_RefusesLikeTheGateFilterBeforeDispatch()
	{
		DispatchHarness harness = new(new McpFeatureOptions { EnableKernelAccess = false });
		ToolError filterRefusal = CheatEngineToolException.CapabilityDisabled(
			McpFeatureGate.SettingName(McpFeature.KernelAccess), CheatEngineToolNames.ExecCompileC).Error;

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			CreateTools(harness).CompileC("int main(void) { return 0; }", targetSelf: true, kernelMode: true,
				cancellationToken: Token));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal((filterRefusal.Message, filterRefusal.Hint), (exception.Error.Message, exception.Error.Hint));
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void CompileC_KernelModeWithAWriteAddress_RefusesBeforeTheGateOrDispatch(bool kernelAccess)
	{
		DispatchHarness harness = new(new McpFeatureOptions { EnableKernelAccess = kernelAccess });

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			CreateTools(harness).CompileC("int main(void) { return 0; }", "7FF600001000", kernelMode: true,
				cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.StartsWith("kernelMode: applies only when address is omitted", exception.Error.Message,
			StringComparison.Ordinal);
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Theory]
	[InlineData("", ToolErrorKind.InvalidArgument)]
	[InlineData("401000\n", ToolErrorKind.InvalidArgument)]
	public void CallLocal_InvalidExpression_RefusesBeforeDispatch(string address, ToolErrorKind expected)
	{
		DispatchHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			CreateTools(harness).CallLocal(address, cancellationToken: Token));

		Assert.Equal((expected, ToolHostEffect.NotStarted), (exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void CallLocal_ExpressionBeyondTheBound_RefusesBeforeDispatch()
	{
		DispatchHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			CreateTools(harness)
				.CallLocal(new string('a', ExecSupport.MaximumExpression + 1), cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void InjectDotNet_TakesNoTimeoutBecauseCheatEngineIgnoresIt()
	{
		Assert.DoesNotContain(Method(nameof(ExecTools.InjectDotNet)).GetParameters(),
			static parameter => parameter.Name == "timeoutMilliseconds");
	}

	public static IEnumerable<object[]> AssemblerBreakingArguments()
	{
		foreach (string parameter in (string[]) ["assemblyPath", "className", "methodName", "parameter"])
		{
			foreach (char character in AssemblerBreakingCharacters)
			{
				yield return [parameter, (int) character];
			}
		}
	}

	[Theory]
	[MemberData(nameof(AssemblerBreakingArguments))]
	public void InjectDotNet_CharacterThatLeavesTheAutoAssemblerString_RefusesBeforeFileAccessOrDispatch(
		string parameter, int character)
	{
		DispatchHarness harness = new();
		string text = $"Mod{(char) character}Name";

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			InjectDotNet(harness, parameter, text));

		AssertManagedTextRefusal(exception, parameter);
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Theory]
	[InlineData("assemblyPath", "C:\\mods\\x.dll',0\r\n{$lua}\r\nprint('injected')\r\n{$asm}\r\ndw 'y")]
	[InlineData("className", "Loader',0\n[DISABLE]\ncreatethread(0)\ndw 'y")]
	[InlineData("methodName", "Init',0\r{$lua}\rreturn ''\r{$asm}")]
	[InlineData("parameter", "x',0\r\n{$lua}\r\nshellExecute('calc.exe')\r\n{$asm}\r\ndw 'y")]
	public void InjectDotNet_AutoAssemblerDirectiveInjection_RefusesBeforeFileAccessOrDispatch(string parameter,
		string payload)
	{
		DispatchHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			InjectDotNet(harness, parameter, payload));

		AssertManagedTextRefusal(exception, parameter);
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	public static IEnumerable<object[]> OversizedOrEmptyManagedText()
	{
		yield return ["assemblyPath", @"C:\" + new string('a', ExecSupport.MaximumManagedPathBytes - 6) + ".dll"];
		yield return ["className", new string('C', ExecSupport.MaximumManagedTextBytes + 1)];
		yield return ["methodName", new string('M', ExecSupport.MaximumManagedTextBytes + 1)];
		yield return ["parameter", new string('p', ExecSupport.MaximumManagedTextBytes + 1)];
		// 64 characters, but 128 UTF-8 bytes: the bound holds even if every byte became one UTF-16 character.
		yield return ["parameter", new string('\u00E9', 64)];
		yield return ["className", ""];
		yield return ["methodName", "   "];
	}

	[Theory]
	[MemberData(nameof(OversizedOrEmptyManagedText))]
	public void InjectDotNet_TextOutsideItsAutoAssemblerBuffer_RefusesBeforeFileAccessOrDispatch(string parameter,
		string value)
	{
		DispatchHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			InjectDotNet(harness, parameter, value));

		AssertManagedTextRefusal(exception, parameter);
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	public static IEnumerable<object[]> PlainManagedNames()
	{
		yield return
		[
			"MyMod.Loader+Entry", "Init_2",
			@"key=value; mode=fast, 100% C:\data/x.txt // not a comment /* nor this */ [ENABLE] $x @f %s"
		];
		yield return
		[
			new string('C', ExecSupport.MaximumManagedTextBytes), new string('M', ExecSupport.MaximumManagedTextBytes),
			new string('p', ExecSupport.MaximumManagedTextBytes)
		];
		yield return ["Spiel.\u00DCberladung", "Start", ""];
	}

	[Theory]
	[MemberData(nameof(PlainManagedNames))]
	public void InjectDotNet_PlainNamesAndPath_ReachTheFixedBodyUnchangedWithoutATimeout(string className,
		string methodName, string parameter)
	{
		using McpFilePathsTests.Scratch scratch = new();
		string assembly = Path.Combine(scratch.CreateFolder("Mod Loader (x64) \u00E9"), "My.Mod-1.0.dll");
		File.WriteAllBytes(assembly, [0x4D, 0x5A]);
		RecordingFixedLua lua = new();
		DispatchHarness harness = new(fixedLua: lua);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			CreateTools(harness).InjectDotNet(assembly, className, methodName, parameter, Token));

		Assert.Equal(RecordingFixedLua.Message, exception.Error.Message);
		Assert.Equal(1, harness.Dispatcher.Calls);
		string expected = LuaToolRuntime.BuildSource(ExecScripts.InjectDotNet,
			new McpExecutionOptions().DispatchBudgetMilliseconds, [assembly, className, methodName, parameter]);
		Assert.Equal(expected, Assert.Single(lua.Sources));
	}

	public static IEnumerable<object[]> FixedLuaBodies()
	{
		yield return [ExecScripts.InjectLibrary, "injectLibrary"];
		yield return [ExecScripts.InjectDotNet, "injectDotNetDLL"];
		yield return [ExecScripts.CallRemote, "executeCodeEx"];
		yield return [ExecScripts.CallMethod, "executeMethod"];
		// On a 64-bit target an RCX instance goes to executeCodeEx as the first argument.
		yield return [ExecScripts.CallMethod, "executeCodeEx"];
		yield return [ExecScripts.CallLocal, "executeCodeLocal"];
		yield return [ExecScripts.CompileC, "compile"];
	}

	[Theory]
	[MemberData(nameof(FixedLuaBodies))]
	public void FixedLuaBody_UsesTheReviewedTargetExecutionApiAndCannotLoadCallerCode(string script, string api)
	{
		Assert.Contains(api, script, StringComparison.Ordinal);
		Assert.Contains("mcp.err", script, StringComparison.Ordinal);
		LuaFixedScriptAssert.NeverLoadsCode(script);
		Assert.Equal([McpFeature.TargetCodeExecution], LuaFeatureScan.Scan(script));
	}

	private static ExecDotNetInjection InjectDotNet(DispatchHarness harness, string parameter, string value)
	{
		return CreateTools(harness).InjectDotNet(parameter == "assemblyPath" ? value : AbsentAssembly,
			parameter == "className" ? value : "MyMod.Loader", parameter == "methodName" ? value : "Init",
			parameter == "parameter" ? value : "", Token);
	}

	private static void AssertManagedTextRefusal(CheatEngineToolException exception, string parameter)
	{
		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.StartsWith($"{parameter}: must contain ", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(ExecSupport.ManagedTextHint, exception.Error.Hint);
	}

	private static MethodInfo Method(string name)
	{
		return typeof(ExecTools).GetMethod(name, BindingFlags.Instance | BindingFlags.Public)
			   ?? throw new InvalidOperationException($"Missing {name}.");
	}

	private static ExecTools CreateTools(DispatchHarness harness)
	{
		return new ExecTools(harness.Dispatch,
			new McpFilePaths(new McpFileOptions(), AppContext.BaseDirectory, AppContext.BaseDirectory));
	}

	private sealed class DispatchHarness
	{
		internal DispatchHarness(McpFeatureOptions? features = null, IFixedLuaExecutor? fixedLua = null)
		{
			IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
			Dispatch = new ToolDispatch(ClientTestDouble.Client(Dispatcher.Dispatcher, CancellationToken.None),
				new McpFeatureGate(Options.Create(features ?? new McpFeatureOptions())), execution,
				new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
				fixedLua ?? UnavailableFixedLuaExecutor.Instance);
		}

		internal RecordingDispatcher Dispatcher
		{
			get;
		} = new();

		internal ToolDispatch Dispatch
		{
			get;
		}
	}

	/// <summary>Records each fixed Lua source and declares a failure instead of running it.</summary>
	private sealed class RecordingFixedLua : IFixedLuaExecutor
	{
		internal const string Message = "The recording executor does not run Lua.";

		internal List<string> Sources
		{
			get;
		} = [];

		public LuaJsonResult<T> Execute<T>(string operation, string source, JsonTypeInfo<T> resultType,
			LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque, CancellationToken cancellationToken)
		{
			Sources.Add(source);
			return new LuaJsonResult<T>(default, new LuaScriptError("host_refused", Message, "unknown", null), 0);
		}
	}
}
