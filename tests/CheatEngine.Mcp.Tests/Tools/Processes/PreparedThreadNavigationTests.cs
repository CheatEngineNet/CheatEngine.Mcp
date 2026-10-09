using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
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

/// <summary>Prepared target-thread navigation is bounded to one target observation and never repeats Lua collection.</summary>
public sealed class PreparedThreadNavigationTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void PreparedThreads_CopyTheExplicitResultWithoutAnotherLuaEnumerationOrArrayAliasing()
	{
		ManualTimeProvider time = new();
		Target target = new(time);

		ProcessThreadListResult explicitResult = target.Tools.ListThreads(Token);
		explicitResult.Threads[0] = 99;
		ProcessThreadListResult prepared = target.Tools.ListPreparedThreads(Token);

		Assert.Equal(new long[] { 0x64, 0xC8, 0x12C }, prepared.Threads);
		prepared.Threads[1] = 99;
		ProcessThreadListResult repeated = target.Tools.ListPreparedThreads(Token);
		Assert.Equal(new long[] { 0x64, 0xC8, 0x12C }, repeated.Threads);
		Assert.False(prepared.Truncated);
		Assert.Equal(1, target.LuaCalls);
		Assert.Equal(4, target.ProcessCalls.Count);
	}

	[Fact]
	public void PreparedThreads_ColdStaleAndChangedTarget_RefuseWithoutLuaEnumeration()
	{
		ManualTimeProvider time = new();
		Target target = new(time);

		ToolError cold = Assert.Throws<CheatEngineToolException>(() => target.Tools.ListPreparedThreads(Token)).Error;
		Assert.Equal(ToolErrorKind.InvalidState, cold.Kind);
		Assert.Contains(CheatEngineToolNames.ProcessListThreads, cold.Hint, StringComparison.Ordinal);
		Assert.Equal(0, target.LuaCalls);

		target.Tools.ListThreads(Token);
		time.Advance(TimeSpan.FromSeconds(5));
		ToolError stale = Assert.Throws<CheatEngineToolException>(() => target.Tools.ListPreparedThreads(Token)).Error;
		Assert.Equal(ToolErrorKind.InvalidState, stale.Kind);
		Assert.Contains("5 seconds", stale.Hint, StringComparison.Ordinal);
		Assert.Equal(1, target.LuaCalls);

		target.Observations.Enqueue(Process(42, 2));
		ToolError changed = Assert.Throws<CheatEngineToolException>(() => target.Tools.ListPreparedThreads(Token)).Error;
		Assert.Equal(ToolErrorKind.InvalidState, changed.Kind);
		Assert.Contains(CheatEngineToolNames.ProcessListThreads, changed.Hint, StringComparison.Ordinal);
		Assert.Equal(1, target.LuaCalls);
	}

	[Fact]
	public void ListThreads_TargetChangeDuringEnumeration_DiscardsTheUnpublishedResult()
	{
		ManualTimeProvider time = new();
		Target target = new(time);
		target.Observations.Enqueue(Process(42, 1));
		target.Observations.Enqueue(Process(77, 2));

		ToolError changed = Assert.Throws<CheatEngineToolException>(() => target.Tools.ListThreads(Token)).Error;

		Assert.Equal((ToolErrorKind.TargetChanged, ToolHostEffect.Completed), (changed.Kind, changed.HostEffect));
		Assert.Contains(CheatEngineToolNames.ProcessListThreads, changed.Hint, StringComparison.Ordinal);
		Assert.Equal(1, target.LuaCalls);
		Assert.Equal(ToolErrorKind.InvalidState,
			Assert.Throws<CheatEngineToolException>(() => target.Tools.ListPreparedThreads(Token)).Error.Kind);
	}

	private static ProcessSnapshot Process(int processId, long epoch)
	{
		return new ProcessSnapshot(new TargetProcessId(processId), "game.exe", null, TargetBackend.LocalProcess,
			CheatEngineArchitecture.X64, PointerSize.Bit64, 8, null, epoch);
	}

	private sealed class Target
	{
		private readonly ProcessSnapshot _default = Process(42, 1);

		internal Target(ManualTimeProvider time)
		{
			RecordingDispatcher dispatcher = new();
			ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, _) =>
			{
				Assert.Equal(nameof(ILuaClient.Execute), method.Name);
				Assert.Equal(typeof(LuaJsonResult<ProcessThreadListResult>), method.GetGenericArguments()[1]);
				LuaCalls++;
				return new LuaJsonResult<ProcessThreadListResult>(
					new ProcessThreadListResult([0x64, 0xC8, 0x12C], false), null, 0);
			});
			IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, _) =>
			{
				Assert.Equal(nameof(IProcessClient.GetCurrentProcess), method.Name);
				ProcessCalls.Add(method.Name);
				return Observations.Count > 1 ? Observations.Dequeue() :
					Observations.TryPeek(out ProcessSnapshot observed) ? observed : _default;
			});
			ICheatEngineClient client = ClientTestDouble.Client(dispatcher.Dispatcher, CancellationToken.None,
				(nameof(ICheatEngineClient.Lua), lua), (nameof(ICheatEngineClient.Processes), processes));
			IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
			ToolDispatch dispatch = new(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), execution,
				new DispatchStatistics(execution), time, NullLogger<ToolDispatch>.Instance, new PluginFixedLuaExecutor(client));
			string root = Path.GetTempPath();
			Tools = new ProcessTools(dispatch, new TargetResources(), new TargetTransitionGuards([]),
				new McpFilePaths(new McpFileOptions { AllowedRoots = [root] }, Path.Combine(root, "ce-mcp-test-registry"),
					Path.Combine(root, "ce-mcp-test-data")), time);
		}

		internal ProcessTools Tools
		{
			get;
		}
		internal Queue<ProcessSnapshot> Observations { get; } = [];
		internal List<string> ProcessCalls { get; } = [];
		internal int LuaCalls
		{
			get; private set;
		}
	}
}
