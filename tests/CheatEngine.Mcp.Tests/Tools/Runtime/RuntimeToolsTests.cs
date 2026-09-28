using System.Text.Json;

using CheatEngine.Client;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Resources.Live;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Runtime;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Tools.Runtime;

/// <summary>
///     The runtime tools report the activation's <c>Mcp:Enable*</c> switches next to the Client capabilities, so an
///     assistant can pick a route before a gated tool refuses it with capability_disabled.
/// </summary>
public sealed class RuntimeToolsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(true, true, true, true)]
	[InlineData(false, true, false, true)]
	[InlineData(true, false, true, false)]
	[InlineData(false, false, false, false)]
	public void GetInfo_ReportsEveryGateOfTheActivation(bool autoAssembler, bool unsafeLua,
		bool targetCodeExecution, bool kernelAccess)
	{
		RuntimeTools tools = CreateTools(new McpFeatureOptions
		{
			EnableAutoAssembler = autoAssembler,
			EnableUnsafeLua = unsafeLua,
			EnableTargetCodeExecution = targetCodeExecution,
			EnableKernelAccess = kernelAccess
		});

		RuntimeInfoResult info = tools.GetInfo(Token);

		Assert.Equal(new RuntimeGates(autoAssembler, unsafeLua, targetCodeExecution, kernelAccess), info.Gates);
	}

	[Fact]
	public void GetOverview_CarriesTheGatesInItsRuntimeSection()
	{
		RuntimeTools tools = CreateTools(new McpFeatureOptions
		{
			EnableAutoAssembler = false,
			EnableKernelAccess = false
		});

		RuntimeOverviewResult overview = tools.GetOverview(Token);

		Assert.Equal(new RuntimeGates(false, true, true, false), overview.Runtime.Gates);
		Assert.Equal((true, 42), (overview.Process.IsOpen, overview.Process.ProcessId));
	}

	[Fact]
	public void Gates_SerializeAsCamelCaseBooleansWithoutSettingNames()
	{
		RuntimeTools tools = CreateTools(new McpFeatureOptions
		{
			EnableUnsafeLua = false
		});

		string json = JsonSerializer.Serialize(tools.GetInfo(Token), RuntimeJsonContext.Default.RuntimeInfoResult);

		Assert.Contains(
			"\"gates\":{\"autoAssembler\":true,\"unsafeLua\":false,\"targetCodeExecution\":true,\"kernelAccess\":true}",
			json, StringComparison.Ordinal);
	}

	[Fact]
	public void RuntimeLiveResource_ProjectsTheGates()
	{
		RuntimeTools tools = CreateTools(new McpFeatureOptions
		{
			EnableTargetCodeExecution = false
		});

		ReadResourceResult result = new RuntimeLiveResources(tools).Runtime(Token);

		string text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text;
		using JsonDocument document = JsonDocument.Parse(text);
		JsonElement gates = document.RootElement.GetProperty("runtime").GetProperty("gates");
		Assert.False(gates.GetProperty("targetCodeExecution").GetBoolean());
		Assert.True(gates.GetProperty("autoAssembler").GetBoolean());
	}

	private static RuntimeTools CreateTools(McpFeatureOptions features)
	{
		CheatEngineVersion version = new(7, 7, 0, 0);
		CheatEngineRuntimeSnapshot snapshot = new(7,
			new CheatEngineRuntimeVersionInfo(version, version, new Version(1, 0), new Version(2, 0), "2.0.0", true),
			new CheatEngineRuntimePlatformInfo(default, CheatEngineArchitecture.X64, PointerSize.Bit64,
				TargetBackend.LocalProcess, CheatEngineArchitecture.X64, PointerSize.Bit64, default, false, 8),
			CheatEngine.Client.Runtime.ClientCapabilities.Empty);
		ICheatEngineRuntime runtime = ClientTestDouble.Create<ICheatEngineRuntime>((method, _) =>
			method.Name == nameof(ICheatEngineRuntime.GetSnapshot)
				? snapshot
				: throw new NotSupportedException($"Unexpected runtime call {method.Name}."));
		IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, arguments) =>
		{
			Assert.Equal(nameof(IProcessClient.TryGetCurrentProcess), method.Name);
			arguments![0] = new ProcessSnapshot(new TargetProcessId(42), "game.exe", null,
				TargetBackend.LocalProcess, CheatEngineArchitecture.X64, PointerSize.Bit64, 8, null, 3);
			arguments[1] = default(CheatEngineFailure);
			return true;
		});
		ICheatEngineClient client = ClientTestDouble.Client(new RecordingDispatcher().Dispatcher,
			CancellationToken.None, (nameof(ICheatEngineClient.Runtime), runtime),
			(nameof(ICheatEngineClient.Processes), processes));
		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		ToolDispatch dispatch = new(client, new McpFeatureGate(Options.Create(features)), execution,
			new DispatchStatistics(execution), TimeProvider.System, NullLogger<ToolDispatch>.Instance);
		TargetResources resources = new();
		return new RuntimeTools(dispatch, TestRuntime.Info, resources,
			new JobRegistry(dispatch, resources, execution, TimeProvider.System));
	}
}
