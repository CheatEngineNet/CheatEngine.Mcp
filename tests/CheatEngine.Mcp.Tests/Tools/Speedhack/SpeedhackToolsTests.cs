using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Speedhack;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools.Speedhack;

/// <summary>The gate, bounds and fixed Lua ownership contract of <c>speedhack_*</c>.</summary>
public sealed class SpeedhackToolsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void SetSpeed_DisabledTargetCodeExecution_IsRefusedBeforeDispatch()
	{
		RecordingDispatcher dispatcher = new();
		ToolDispatch dispatch = CreateDispatch(dispatcher, false);
		SpeedhackTools tools = new(dispatch, new TargetResources());

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => tools.SetSpeed(1, Token));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, dispatcher.Calls);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(0.009)]
	[InlineData(1000.001)]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	public void SetSpeed_InvalidMultiplier_IsRefusedBeforeLua(double speed)
	{
		RecordingDispatcher dispatcher = new();
		SpeedhackTools tools = new(CreateDispatch(dispatcher, true), new TargetResources());

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => tools.SetSpeed(speed, Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal(0, dispatcher.Calls);
	}

	[Fact]
	public void SetSpeed_DeclaresTheTargetCodeExecutionGate()
	{
		MethodInfo method = typeof(SpeedhackTools).GetMethod(nameof(SpeedhackTools.SetSpeed))!;

		RequiresFeatureAttribute gate = Assert.Single(method.GetCustomAttributes<RequiresFeatureAttribute>());
		Assert.Equal(McpFeature.TargetCodeExecution, gate.Feature);
	}

	[Fact]
	public void FixedScripts_BoundTheStateAndRetainAOneTimeRestoreAction()
	{
		Assert.Contains("speedhack_getSpeed()", SpeedhackScripts.GetState, StringComparison.Ordinal);
		Assert.Contains("getAddressSafe", SpeedhackScripts.GetState, StringComparison.Ordinal);
		Assert.Contains("speedhack_setSpeed(a[1])", SpeedhackScripts.SetSpeed, StringComparison.Ordinal);
		Assert.Contains("resourceRecord(a[3], a[2], 'speedhack'", SpeedhackScripts.SetSpeed,
			StringComparison.Ordinal);
		Assert.Contains("speedhack_setSpeed(1)", SpeedhackScripts.SetSpeed, StringComparison.Ordinal);
		Assert.Contains("mcp.err('invalid_state'", SpeedhackScripts.SetSpeed, StringComparison.Ordinal);
		Assert.DoesNotContain("${", SpeedhackScripts.SetSpeed, StringComparison.Ordinal);
	}

	private static ToolDispatch CreateDispatch(RecordingDispatcher dispatcher, bool enabled)
	{
		ICheatEngineClient client = ClientTestDouble.Client(dispatcher.Dispatcher, CancellationToken.None);
		McpFeatureOptions features = new()
		{
			EnableTargetCodeExecution = enabled
		};
		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		return new ToolDispatch(client, new McpFeatureGate(Options.Create(features)), execution,
			new DispatchStatistics(execution), TimeProvider.System, NullLogger<ToolDispatch>.Instance);
	}
}
