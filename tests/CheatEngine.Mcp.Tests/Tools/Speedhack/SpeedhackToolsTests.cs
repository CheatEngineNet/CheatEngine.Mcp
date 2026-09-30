using System.ComponentModel;
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

	[Theory]
	[InlineData(true, false)]
	[InlineData(false, false)]
	[InlineData(true, true)]
	public void SetSpeed_One_ReturnsTheStateTheFixedBodyObservedWithoutAResource(bool hooksInstalled,
		bool firstActivation)
	{
		StateTestHarness harness = new();
		harness.Answer(_ => new LuaSpeedhackState(1, hooksInstalled, firstActivation));
		SpeedhackTools tools = new(harness.Dispatch, harness.Resources);

		SpeedhackSetResult result = tools.SetSpeed(1, Token);

		Assert.Equal(new SpeedhackSetResult(1, hooksInstalled, firstActivation), result);
		(string operation, string source) = Assert.Single(harness.LuaCalls);
		Assert.Equal(CheatEngineToolNames.SpeedhackSetSpeed, operation);
		Assert.Contains(SpeedhackScripts.SetNormal, source, StringComparison.Ordinal);
		Assert.Empty(harness.Resources.List());
	}

	[Fact]
	public void SetNormal_ChecksTheSymbolAndBothSpeedsBeforeItCallsSpeedhackSetSpeed()
	{
		string body = SpeedhackScripts.SetNormal;
		int attached = body.IndexOf("getOpenedProcessID()", StringComparison.Ordinal);
		int normal = body.IndexOf("math.abs(current - 1) <= 0.000001", StringComparison.Ordinal);
		int unhooked = body.IndexOf("if address == nil then", StringComparison.Ordinal);
		int target = body.IndexOf("pcall(readFloat, address)", StringComparison.Ordinal);
		int broken = body.IndexOf("debug_getContext(false)", StringComparison.Ordinal);
		int paused = body.IndexOf("isPaused()", StringComparison.Ordinal);
		int set = body.IndexOf("speedhack_setSpeed(1)", StringComparison.Ordinal);

		// speedhack_setSpeed(1) ticks Enable Speedhack, which hooks a process that was never sped up, and
		// speedhack_getSpeed reports 1 also after a process switch freed Cheat Engine's speedhack.
		Assert.True(attached >= 0 && attached < normal && normal < unhooked && unhooked < target && target < broken &&
					broken < paused && paused < set);
		Assert.Equal(set, body.LastIndexOf("speedhack_setSpeed(", StringComparison.Ordinal));
		Assert.DoesNotContain("firstActivation = true", body, StringComparison.Ordinal);
		Assert.DoesNotContain("firstActivation = not", body, StringComparison.Ordinal);
	}

	[Fact]
	public void SetSpeed_RestoreAction_RestoresOnlyTheProcessItSpedUpAndChecksItsOwnSpeed()
	{
		string body = SpeedhackScripts.SetSpeed;
		int record = body.IndexOf("resourceRecord(a[3], a[2], 'speedhack'", StringComparison.Ordinal);
		int check = body.IndexOf("assert(getOpenedProcessID() == pid", record, StringComparison.Ordinal);
		int target = body.IndexOf("pcall(readFloat, address)", record, StringComparison.Ordinal);
		int restore = body.IndexOf("speedhack_setSpeed(1)", record, StringComparison.Ordinal);

		Assert.True(record >= 0 && check > record && target > check && restore > target);
	}

	[Fact]
	public void RetryClaims_DependOnTheSpeedhackSymbol()
	{
		string firstActivation = typeof(SpeedhackSetResult).GetProperty(nameof(SpeedhackSetResult.FirstActivation))!
			.GetCustomAttribute<DescriptionAttribute>()!.Description;
		string getState = typeof(SpeedhackTools).GetMethod(nameof(SpeedhackTools.GetState))!
			.GetCustomAttribute<DescriptionAttribute>()!.Description;

		// SpeedhackV3.lua returns before it creates speedhack_wantedspeed when its helper library or the allocation
		// fails, so the next activation hooks again; only an existing symbol stops every later attempt.
		Assert.Contains("if hooksInstalled is still false, a later call retries", firstActivation,
			StringComparison.Ordinal);
		Assert.Contains("Speed 1 never activates the speedhack", firstActivation, StringComparison.Ordinal);
		Assert.Contains("once that symbol exists it never retries", getState, StringComparison.Ordinal);
		Assert.Contains(
			"While it is absent, the next speedhack_set_speed with a speed other than 1 attempts the hooks again",
			getState, StringComparison.Ordinal);
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
