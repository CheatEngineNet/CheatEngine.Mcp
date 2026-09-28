using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Processes;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Processes;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools.Processes;

/// <summary>
///     process_attach, process_create and process_open_file report, as table_load does, whether Cheat Engine's Mono
///     extension may inject its data collector into the process it opens, after the gate allowed it.
/// </summary>
public sealed class ProcessMonoAutoAttachTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(true, false, true)]
	[InlineData(true, true, false)]
	[InlineData(false, false, false)]
	public void Attach_ReportsWhetherTheOpenMayAutoAttachMono(bool usesMono, bool ignoreUsesMono, bool expected)
	{
		Target target = new(new MonoAutoAttachState(usesMono, ignoreUsesMono, true, true), true);

		ProcessAttachResult attached = target.Tools.Attach("game.exe", Token);

		Assert.Equal(new ProcessAttachResult(42, "game.exe", expected), attached);
		Assert.Equal([nameof(IProcessClient.AttachExactName)], target.ProcessCalls);
	}

	[Theory]
	[InlineData(true, false, true)]
	[InlineData(true, true, false)]
	[InlineData(false, false, false)]
	public void CreateAndOpenFile_ReportWhetherTheOpenMayAutoAttachMono(bool usesMono, bool ignoreUsesMono,
		bool expected)
	{
		Target target = new(new MonoAutoAttachState(usesMono, ignoreUsesMono, false, true), true);
		string input = typeof(ProcessTools).Assembly.Location;

		ProcessCreateResult created = target.Tools.Create(input, cancellationToken: Token);
		ProcessOpenFileResult opened = target.Tools.OpenFile(input, cancellationToken: Token);

		Assert.Equal(new ProcessCreateResult(88, expected), created);
		Assert.Equal((99, Path.GetFileName(input), expected),
			(opened.ObservedProcessId, opened.InputFileName, opened.MonoAutoAttach));
	}

	[Fact]
	public void Attach_MonoAutoAttachWithCodeExecutionOff_RefusesBeforeTheClientAttaches()
	{
		Target target = new(new MonoAutoAttachState(true, false, true, true), false);

		ToolError error = Assert.Throws<CheatEngineToolException>(() => target.Tools.Attach("game.exe", Token))
			.Error;

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
		Assert.Empty(target.ProcessCalls);
	}

	[Fact]
	public void MonoAutoAttach_IsFalseByDefaultForAResultReadFromCheatEngine()
	{
		Assert.False(new ProcessAttachResult(1, null).MonoAutoAttach);
		Assert.False(new ProcessCreateResult(1).MonoAutoAttach);
		Assert.False(new ProcessOpenFileResult("a.bin", true, 1, 2).MonoAutoAttach);
	}

	/// <summary>One activation over a Client double whose Lua facade answers the probe and the open scripts.</summary>
	private sealed class Target
	{
		internal Target(MonoAutoAttachState state, bool codeExecution)
		{
			ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, _) =>
			{
				Assert.Equal(nameof(ILuaClient.Execute), method.Name);
				Type result = method.GetGenericArguments()[1];
				if (result == typeof(LuaJsonResult<MonoAutoAttachState>))
				{
					return new LuaJsonResult<MonoAutoAttachState>(state, null, 0);
				}

				if (result == typeof(LuaJsonResult<ProcessCreateResult>))
				{
					return new LuaJsonResult<ProcessCreateResult>(new ProcessCreateResult(88), null, 0);
				}

				Assert.Equal(typeof(LuaJsonResult<ProcessOpenFileResult>), result);
				return new LuaJsonResult<ProcessOpenFileResult>(new ProcessOpenFileResult("ignored", true, 99, 4096),
					null, 0);
			});
			IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, _) =>
			{
				ProcessCalls.Add(method.Name);
				return new ProcessSnapshot(new TargetProcessId(42), "game.exe", null, TargetBackend.LocalProcess,
					CheatEngineArchitecture.X64, PointerSize.Bit64, 8, null, 1);
			});
			ICheatEngineClient client = ClientTestDouble.Client(new RecordingDispatcher().Dispatcher,
				CancellationToken.None, (nameof(ICheatEngineClient.Lua), lua),
				(nameof(ICheatEngineClient.Processes), processes));
			IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
			ToolDispatch dispatch = new(client,
				new McpFeatureGate(Options.Create(new McpFeatureOptions { EnableTargetCodeExecution = codeExecution })),
				execution, new DispatchStatistics(execution), TimeProvider.System, NullLogger<ToolDispatch>.Instance,
				new PluginFixedLuaExecutor(client));
			string root = Path.GetTempPath();
			Tools = new ProcessTools(dispatch, new TargetResources(), new TargetTransitionGuards([]),
				new McpFilePaths(new McpFileOptions { AllowedRoots = [root] },
					Path.Combine(root, "ce-mcp-test-registry"), Path.Combine(root, "ce-mcp-test-data")));
		}

		internal ProcessTools Tools
		{
			get;
		}

		internal List<string> ProcessCalls
		{
			get;
		} = [];
	}
}
