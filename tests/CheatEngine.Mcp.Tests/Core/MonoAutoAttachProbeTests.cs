using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class MonoAutoAttachProbeTests
{
	[Fact]
	public void Script_NamesNoGatedApiAndLoadsNoCode()
	{
		Assert.Empty(LuaFeatureScan.Scan(MonoAutoAttachProbe.Script));
		LuaFixedScriptAssert.NeverLoadsCode(MonoAutoAttachProbe.Script);
		Assert.Equal("mono_auto_attach", MonoAutoAttachProbe.HostEffect);
	}

	[Theory]
	[InlineData(true, false, true)]
	[InlineData(true, true, false)]
	[InlineData(false, false, false)]
	[InlineData(false, true, false)]
	public void WouldAttachOnProcessOpen_FollowsTheOptionAndTheIgnoreSetting(bool usesMono, bool ignore,
		bool expected)
	{
		MonoAutoAttachState state = new(usesMono, ignore, false, true);

		Assert.Equal(expected, state.WouldAttachOnProcessOpen);
	}

	[Theory]
	[InlineData(true, false, true, true)]
	[InlineData(false, true, true, true)]
	[InlineData(false, false, true, false)]
	[InlineData(true, true, false, false)]
	public void WouldAttachOnTableLoad_NeedsAnOpenProcessAndIgnoresTheIgnoreSetting(bool current, bool loaded,
		bool processOpen, bool expected)
	{
		MonoAutoAttachState state = new(current, true, processOpen, true);

		Assert.Equal(expected, state.WouldAttachOnTableLoad(loaded));
	}

	[Fact]
	public void RequireForProcessOpen_WouldAttachWithCodeExecutionOff_IsCapabilityDisabled()
	{
		MonoAutoAttachState state = new(true, false, false, true);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			state.RequireForProcessOpen(Gate(false), "process_attach"));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("Mcp:EnableTargetCodeExecution", exception.Error.Message, StringComparison.Ordinal);
		Assert.Contains("UsesMono", exception.Error.Message, StringComparison.Ordinal);
		Assert.True(state.RequireForProcessOpen(Gate(true), "process_attach"));
		Assert.False(new MonoAutoAttachState(false, false, true, true).RequireForProcessOpen(Gate(false),
			"process_attach"));
	}

	[Fact]
	public void RequireForTableLoad_LoadedTableUsesMonoWithCodeExecutionOff_IsCapabilityDisabled()
	{
		MonoAutoAttachState state = new(false, true, true, true);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			state.RequireForTableLoad(Gate(false), "table_load", true));

		Assert.Equal(ToolErrorKind.CapabilityDisabled, exception.Error.Kind);
		Assert.True(state.RequireForTableLoad(Gate(true), "table_load", true));
		Assert.False(state.RequireForTableLoad(Gate(false), "table_load", false));
	}

	[Fact]
	public void Read_EverySwitchOff_StillDispatchesTheUngatedProbe()
	{
		MonoAutoAttachState expected = new(true, false, true, true);
		int calls = 0;
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, _) =>
		{
			Assert.Equal(nameof(ILuaClient.Execute), method.Name);
			calls++;
			return new LuaJsonResult<MonoAutoAttachState>(expected, null, 0);
		});
		RecordingDispatcher dispatcher = new();
		ICheatEngineClient client = ClientTestDouble.Client(dispatcher.Dispatcher, CancellationToken.None,
			(nameof(ICheatEngineClient.Lua), lua));
		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		ToolDispatch dispatch = new(client,
			new McpFeatureGate(Options.Create(new McpFeatureOptions
			{
				EnableUnsafeLua = false,
				EnableAutoAssembler = false,
				EnableTargetCodeExecution = false,
				EnableKernelAccess = false
			})), execution, new DispatchStatistics(execution), TimeProvider.System,
			NullLogger<ToolDispatch>.Instance, new PluginFixedLuaExecutor(client));

		MonoAutoAttachState own = MonoAutoAttachProbe.Read(dispatch, "process_attach",
			TestContext.Current.CancellationToken);
		MonoAutoAttachState inside = dispatch.Run("process_attach",
			token => MonoAutoAttachProbe.ReadInDispatch(dispatch, "process_attach", token),
			TestContext.Current.CancellationToken);

		Assert.Equal(expected, own);
		Assert.Equal(expected, inside);
		Assert.Equal(2, calls);
		Assert.Equal(2, dispatcher.Calls);
	}

	private static McpFeatureGate Gate(bool codeExecution)
	{
		return new McpFeatureGate(Options.Create(new McpFeatureOptions { EnableTargetCodeExecution = codeExecution }));
	}
}
