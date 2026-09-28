using CheatEngine.Client;
using CheatEngine.Client.Processes;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Processes;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed partial class NativeLuaToolRuntimeTests
{
	private const string ProcessOpenStubs = """
	                                        pid = 77; created = 0; opened = 0
	                                        getSettingsOption = function(name) assert(name == 'IgnoreUsesMono') return false end
	                                        createProcess = function() created = created + 1; pid = 88 end
	                                        openFileAsProcess = function() opened = opened + 1; pid = 99 end
	                                        getOpenedProcessID = function() return pid end
	                                        getOpenedFileSize = function() return 4096 end
	                                        """;

	[Theory]
	[InlineData(
		"getTableOption = function(n) assert(n == 'UsesMono') return true end; getSettingsOption = function(n) assert(n == 'IgnoreUsesMono') return false end; process = 'game.exe'",
		true, false, true, true)]
	[InlineData(
		"getTableOption = function() return false end; getSettingsOption = function() return false end",
		false, false, false, true)]
	[InlineData(
		"getTableOption = function() return nil end; getSettingsOption = function() return true end; process = 'x'",
		false, true, true, true)]
	// Lua truth: an option read as '0' is set; the process-open hook attaches only when the setting reads exactly false.
	[InlineData("getTableOption = function() return '0' end; getSettingsOption = function() return nil end",
		true, true, false, true)]
	[InlineData("process = false", true, false, false, false)]
	[InlineData(
		"getTableOption = function() error('no option') end; getSettingsOption = function() error('no setting') end",
		true, false, false, false)]
	public void MonoAutoAttachProbe_StubbedCheatEngine_ReportsWhatTheMonoHooksWouldDo(string stubs, bool usesMono,
		bool ignore, bool processOpen, bool determined)
	{
		LuaFixedScriptAssert.NeverLoadsCode(MonoAutoAttachProbe.Script);
		using RuntimeScope scope = CreateScope();
		InstallStubs(stubs);

		LuaJsonResult<MonoAutoAttachState> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoAutoAttachProbe.Script, 100, []),
			FeaturesJsonContext.Default.MonoAutoAttachState);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(new MonoAutoAttachState(usesMono, ignore, processOpen, determined), result.Value);
	}

	[Fact]
	public void ProcessOpen_TableUsesMonoWithCodeExecutionOff_RefusesBeforeCheatEngineOpensTheProcess()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(ProcessOpenStubs);
		InstallStubs("getTableOption = function(name) assert(name == 'UsesMono') return true end");
		string input = typeof(ProcessTools).Assembly.Location;
		ProcessTools tools = new(CreateNativeDispatch(new McpFeatureOptions { EnableTargetCodeExecution = false }),
			new TargetResources(), new TargetTransitionGuards([]), CreateProcessFiles());

		CheatEngineToolException create = Assert.Throws<CheatEngineToolException>(() =>
			tools.Create(input, cancellationToken: Token));
		CheatEngineToolException open = Assert.Throws<CheatEngineToolException>(() =>
			tools.OpenFile(input, cancellationToken: Token));

		foreach (CheatEngineToolException refusal in new[] { create, open })
		{
			Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
				(refusal.Error.Kind, refusal.Error.HostEffect));
			Assert.Contains("UsesMono", refusal.Error.Message, StringComparison.Ordinal);
		}

		Assert.Equal((0L, 0L, 77L), (ReadGlobal("created"), ReadGlobal("opened"), ReadGlobal("pid")));
	}

	[Theory]
	[InlineData("getTableOption = function(name) assert(name == 'UsesMono') return false end", false, false)]
	[InlineData("getTableOption = function(name) return true end; getSettingsOption = function() return true end",
		false, false)]
	[InlineData("getTableOption = function(name) return true end", true, true)]
	public void ProcessOpen_NoMonoAutoAttachOrCodeExecutionOn_OpensTheProcessAndReportsTheAutoAttach(string stubs,
		bool codeExecution, bool monoAutoAttach)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(ProcessOpenStubs);
		InstallStubs(stubs);
		string input = typeof(ProcessTools).Assembly.Location;
		ProcessTools tools = new(
			CreateNativeDispatch(new McpFeatureOptions { EnableTargetCodeExecution = codeExecution }),
			new TargetResources(), new TargetTransitionGuards([]), CreateProcessFiles());

		ProcessCreateResult created = tools.Create(input, cancellationToken: Token);
		ProcessOpenFileResult opened = tools.OpenFile(input, cancellationToken: Token);

		Assert.Equal((88, 99), (created.ProcessId, opened.ObservedProcessId));
		Assert.Equal((monoAutoAttach, monoAutoAttach), (created.MonoAutoAttach, opened.MonoAutoAttach));
		Assert.Equal((1L, 1L), (ReadGlobal("created"), ReadGlobal("opened")));
	}

	[Fact]
	public void ProcessAttach_TableUsesMonoWithCodeExecutionOff_RefusesBeforeTheClientAttaches()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(ProcessOpenStubs);
		InstallStubs("getTableOption = function(name) assert(name == 'UsesMono') return true end");
		int processCalls = 0;
		IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, _) =>
		{
			processCalls++;
			throw new NotSupportedException(method.Name);
		});
		ICheatEngineClient client = ClientTestDouble.Client(
			(nameof(ICheatEngineClient.Lua), CreateJsonLuaClient().Lua),
			(nameof(ICheatEngineClient.Processes), processes));
		IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
		ToolDispatch dispatch = new(client,
			new McpFeatureGate(Options.Create(new McpFeatureOptions { EnableTargetCodeExecution = false })),
			options, new DispatchStatistics(options), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
			new PluginFixedLuaExecutor(client));
		ProcessTools tools = new(dispatch, new TargetResources(), new TargetTransitionGuards([]),
			CreateProcessFiles());

		CheatEngineToolException refusal = Assert.Throws<CheatEngineToolException>(() =>
			tools.Attach("game.exe", Token));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(refusal.Error.Kind, refusal.Error.HostEffect));
		Assert.StartsWith($"{CheatEngineToolNames.ProcessAttach} is disabled", refusal.Error.Message,
			StringComparison.Ordinal);
		Assert.Contains("UsesMono", refusal.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, processCalls);
	}

	[Theory]
	[InlineData("getTableOption = function(name) assert(name == 'UsesMono') return true end", true)]
	[InlineData("getTableOption = function(name) assert(name == 'UsesMono') return false end", false)]
	public void ProcessAttach_CodeExecutionOn_ReportsWhetherCheatEngineMayAutoAttachMono(string stubs,
		bool monoAutoAttach)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(ProcessOpenStubs);
		InstallStubs(stubs);
		IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, arguments) =>
		{
			Assert.Equal(nameof(IProcessClient.AttachExactName), method.Name);
			Assert.Equal("game.exe", arguments![0]);
			return new ProcessSnapshot(new TargetProcessId(77), "game.exe", null, TargetBackend.LocalProcess,
				CheatEngineArchitecture.X64, PointerSize.Bit64, 8, null, 1);
		});
		ICheatEngineClient client = ClientTestDouble.Client(
			(nameof(ICheatEngineClient.Lua), CreateJsonLuaClient().Lua),
			(nameof(ICheatEngineClient.Processes), processes));
		IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
		ToolDispatch dispatch = new(client,
			new McpFeatureGate(Options.Create(new McpFeatureOptions { EnableTargetCodeExecution = true })),
			options, new DispatchStatistics(options), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
			new PluginFixedLuaExecutor(client));
		ProcessTools tools = new(dispatch, new TargetResources(), new TargetTransitionGuards([]),
			CreateProcessFiles());

		ProcessAttachResult attached = tools.Attach("game.exe", Token);

		Assert.Equal(new ProcessAttachResult(77, "game.exe", monoAutoAttach), attached);
	}
}
