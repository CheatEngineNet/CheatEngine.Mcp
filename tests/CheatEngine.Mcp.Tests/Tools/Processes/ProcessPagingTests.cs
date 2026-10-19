using System.Text.Json;

using CheatEngine.Client;
using CheatEngine.Client.Processes;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Processes;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools.Processes;

/// <summary>Opt-in process pages retain one bounded local-process snapshot without changing legacy list output.</summary>
public sealed class ProcessPagingTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void List_LegacyRequest_PreservesTheOriginalRequestAndJsonShape()
	{
		Target target = new()
		{
			Current = Result([Process(2, "beta.exe")], true)
		};

		ProcessListResult result = target.Tools.List(2, "beta", Token);
		string json = JsonSerializer.Serialize(result, ProcessJsonContext.Default.ProcessListResult);

		Assert.Equal(new LocalProcessEnumerationRequest(2, "beta"), Assert.Single(target.Requests));
		Assert.Equal(new ProcessSummary(2, "beta.exe"), Assert.Single(result.Processes));
		Assert.True(result.Truncated);
		Assert.DoesNotContain("nextCursor", json, StringComparison.Ordinal);
		Assert.DoesNotContain("snapshotTruncated", json, StringComparison.Ordinal);
		Assert.DoesNotContain("snapshotTotal", json, StringComparison.Ordinal);
	}

	[Fact]
	public void List_PagedSnapshot_SortsOnceAndKeepsTheOriginalProcessesAcrossPages()
	{
		Target target = new()
		{
			Current = Result([Process(7, "z.exe"), Process(3, "b.exe"), Process(3, "a.exe"), Process(9, "n.exe")],
				true)
		};

		ProcessListResult first = target.Tools.List(nameContains: "exe", cancellationToken: Token, pageSize: 2);
		target.Current = Result([Process(1, "new.exe")], false);
		ProcessListResult second = target.Tools.List(cancellationToken: Token, cursor: first.NextCursor);
		ProcessListResult replay = target.Tools.List(cancellationToken: Token, cursor: first.NextCursor);

		Assert.Equal(new LocalProcessEnumerationRequest(4096, "exe"), Assert.Single(target.Requests));
		Assert.Equal([new ProcessSummary(3, "a.exe"), new ProcessSummary(3, "b.exe")], first.Processes);
		Assert.Equal([new ProcessSummary(7, "z.exe"), new ProcessSummary(9, "n.exe")], second.Processes);
		Assert.Equal(second.Processes, replay.Processes);
		Assert.Equal((second.Truncated, second.NextCursor, second.SnapshotTruncated, second.SnapshotTotal),
			(replay.Truncated, replay.NextCursor, replay.SnapshotTruncated, replay.SnapshotTotal));
		Assert.True(first.Truncated);
		Assert.True(first.SnapshotTruncated);
		Assert.Equal(4, first.SnapshotTotal);
		Assert.Null(second.NextCursor);
		Assert.True(second.Truncated);
	}

	[Fact]
	public void List_PagedCursor_ExpiresAndIsActivationScoped()
	{
		ManualTimeProvider time = new();
		Target target = new(time)
		{
			Current = Result([Process(1, "one"), Process(2, "two")], false)
		};
		string cursor = target.Tools.List(cancellationToken: Token, pageSize: 1).NextCursor!;

		time.Advance(TimeSpan.FromSeconds(30));
		CheatEngineToolException expired = Assert.Throws<CheatEngineToolException>(() =>
			target.Tools.List(cancellationToken: Token, cursor: cursor));

		Target other = new();
		CheatEngineToolException foreign = Assert.Throws<CheatEngineToolException>(() =>
			other.Tools.List(cancellationToken: Token, cursor: cursor));

		Assert.Equal(ToolErrorKind.InvalidState, expired.Error.Kind);
		Assert.Equal(ToolErrorKind.InvalidState, foreign.Error.Kind);
		Assert.Single(target.Requests);
		Assert.Empty(other.Requests);
	}

	[Fact]
	public void List_PagedCursor_RejectsConflictsTamperingAndEvictedSnapshotsBeforeEnumeration()
	{
		Target target = new()
		{
			Current = Result([Process(1, "one"), Process(2, "two")], false)
		};
		string first = target.Tools.List(cancellationToken: Token, pageSize: 1).NextCursor!;

		AssertInvalid(() => target.Tools.List(9, cancellationToken: Token, pageSize: 1), "maximumResults");
		AssertInvalid(() => target.Tools.List(nameContains: "one", cancellationToken: Token, cursor: first), "nameContains");
		AssertInvalid(() => target.Tools.List(cancellationToken: Token, pageSize: 1, cursor: first), "pageSize");
		AssertInvalid(() => target.Tools.List(9, cancellationToken: Token, cursor: first), "maximumResults");
		string tampered = first[..^1] + (first[^1] == '0' ? "1" : "0");
		Assert.Equal(ToolErrorKind.InvalidState, Assert.Throws<CheatEngineToolException>(() =>
			target.Tools.List(cancellationToken: Token, cursor: tampered)).Error.Kind);

		for (int index = 0; index < 4; index++)
		{
			target.Tools.List(cancellationToken: Token, pageSize: 1);
		}

		Assert.Equal(ToolErrorKind.InvalidState, Assert.Throws<CheatEngineToolException>(() =>
			target.Tools.List(cancellationToken: Token, cursor: first)).Error.Kind);
		Assert.Equal(5, target.Requests.Count);
	}

	private static void AssertInvalid(Action action, string parameter)
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(action);
		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.StartsWith(parameter, exception.Error.Message, StringComparison.Ordinal);
	}

	private static LocalProcessEnumerationResult Result(LocalProcessSnapshot[] processes, bool truncated)
	{
		return new LocalProcessEnumerationResult([.. processes], truncated);
	}

	private static LocalProcessSnapshot Process(int id, string name)
	{
		return new LocalProcessSnapshot(new LocalProcessId(id), name, null);
	}

	private sealed class Target
	{
		internal Target(TimeProvider? time = null)
		{
			RecordingDispatcher dispatcher = new();
			IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, arguments) =>
			{
				Assert.Equal(nameof(IProcessClient.GetLocalProcesses), method.Name);
				Requests.Add((LocalProcessEnumerationRequest) arguments![0]!);
				return Current;
			});
			ICheatEngineClient client = ClientTestDouble.Client(dispatcher.Dispatcher, CancellationToken.None,
				(nameof(ICheatEngineClient.Processes), processes));
			IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
			ToolDispatch dispatch = new(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), execution,
				new DispatchStatistics(execution), time ?? TimeProvider.System, NullLogger<ToolDispatch>.Instance,
				new PluginFixedLuaExecutor(client));
			string root = Path.GetTempPath();
			Tools = new ProcessTools(dispatch, new TargetResources(), new TargetTransitionGuards([]),
				new McpFilePaths(new McpFileOptions { AllowedRoots = [root] }, Path.Combine(root, "ce-mcp-test-registry"),
					Path.Combine(root, "ce-mcp-test-data")), time);
		}

		internal ProcessTools Tools
		{
			get;
		}
		internal LocalProcessEnumerationResult Current { get; set; } = Result([], false);
		internal List<LocalProcessEnumerationRequest> Requests { get; } = [];
	}
}
