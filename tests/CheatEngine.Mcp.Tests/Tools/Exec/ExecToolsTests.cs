using System.Reflection;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Exec;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools.Exec;

/// <summary>Execution tools remain explicitly gated, bounded and limited to their reviewed fixed Lua bodies.</summary>
public sealed class ExecToolsTests
{
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
		ExecCallArgument[] arguments = [new ExecCallArgument((ExecArgumentType) 3, "temporary target buffer")];

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			CreateTools(harness).CallRemote("401000", arguments: arguments, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("Allocate and write strings or buffers separately", exception.Error.Message,
			StringComparison.Ordinal);
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
	public void CompileC_KernelModeWithoutKernelAccess_RefusesBeforeDispatch()
	{
		DispatchHarness harness = new(new McpFeatureOptions { EnableKernelAccess = false });

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			CreateTools(harness).CompileC("int main(void) { return 0; }", targetSelf: true, kernelMode: true,
				cancellationToken: Token));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
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

	public static IEnumerable<object[]> FixedLuaBodies()
	{
		yield return [ExecScripts.InjectLibrary, "injectLibrary"];
		yield return [ExecScripts.InjectDotNet, "injectDotNetDLL"];
		yield return [ExecScripts.CallRemote, "executeCodeEx"];
		yield return [ExecScripts.CallMethod, "executeMethod"];
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
		internal DispatchHarness(McpFeatureOptions? features = null)
		{
			IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
			Dispatch = new ToolDispatch(ClientTestDouble.Client(Dispatcher.Dispatcher, CancellationToken.None),
				new McpFeatureGate(Options.Create(features ?? new McpFeatureOptions())), execution,
				new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>());
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
}
