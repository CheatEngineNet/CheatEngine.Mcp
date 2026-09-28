using System.ComponentModel;
using System.Reflection;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Processes;

namespace CheatEngine.Mcp.Tests.Tools.Processes;

/// <summary>
///     process_set_paused tracks a pause that MCP makes as a <c>pause</c> resource: it blocks a target change, and
///     runtime_release_resources or a resume undoes it once.
/// </summary>
public sealed class ProcessPauseTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void Pause_NewPause_IsRecordedAndTrackedAsAPauseResource()
	{
		StateTestHarness harness = new();
		ProcessTools tools = CreateTools(harness);
		string id = PauseId(harness, 1);
		harness.Answer(source => new ProcessPausedResult(true, source.Contains(Quoted(id), StringComparison.Ordinal)
			? id
			: null));

		ProcessPausedResult paused = tools.SetPaused(true, Token);

		Assert.Equal(new ProcessPausedResult(true, id), paused);
		TargetResourceDescriptor resource = Assert.Single(harness.Resources.List());
		Assert.Equal((id, ProcessTools.PauseKind, TargetResourceCategory.LuaState, TargetResourceState.Active),
			(resource.Id, resource.Kind, resource.Category, resource.State));
		Assert.Equal(("pause", "Resumes the paused process."), (resource.Name, resource.Detail));
		(string operation, string source) = Assert.Single(harness.LuaCalls);
		Assert.Equal(CheatEngineToolNames.ProcessSetPaused, operation);
		Assert.Contains(ProcessPauseScripts.Pause, source, StringComparison.Ordinal);
		Assert.Contains($"[1] = {Quoted(harness.Resources.Namespace)}, [2] = {Quoted(id)}, [3] = nil", source,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Pause_TargetAlreadyPausedByTheUser_RecordsNothing()
	{
		StateTestHarness harness = new();
		ProcessTools tools = CreateTools(harness);
		harness.Answer(static _ => new ProcessPausedResult(true));

		ProcessPausedResult paused = tools.SetPaused(true, Token);

		Assert.Equal(new ProcessPausedResult(true), paused);
		Assert.Empty(harness.Resources.List());
	}

	[Fact]
	public void Pause_WhileMcpPauseIsTracked_RecordsNoSecondPauseAndReportsTheFirst()
	{
		StateTestHarness harness = new();
		ProcessTools tools = CreateTools(harness);
		string id = PauseId(harness, 1);
		harness.Answer(source => new ProcessPausedResult(true, source.Contains(Quoted(id), StringComparison.Ordinal)
			? id
			: null));
		tools.SetPaused(true, Token);

		ProcessPausedResult again = tools.SetPaused(true, Token);

		Assert.Equal(new ProcessPausedResult(true, id), again);
		Assert.Equal(id, Assert.Single(harness.Resources.List()).Id);
		// The fixed body reuses the tracked pause only while it was recorded for the opened process.
		Assert.Contains($", [2] = {Quoted(PauseId(harness, 2))}, [3] = {Quoted(id)}", harness.LuaCalls.Last().Source,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Pause_TrackedPauseOfAnotherProcess_TracksTheNewPauseAndKeepsTheOld()
	{
		StateTestHarness harness = new();
		ProcessTools tools = CreateTools(harness);
		string first = PauseId(harness, 1);
		string second = PauseId(harness, 2);
		// The body records the new id when the tracked pause covers another process than the opened one.
		harness.Answer(source => new ProcessPausedResult(true,
			source.Contains(Quoted(second), StringComparison.Ordinal) ? second :
			source.Contains(Quoted(first), StringComparison.Ordinal) ? first : null));
		tools.SetPaused(true, Token);

		ProcessPausedResult paused = tools.SetPaused(true, Token);

		Assert.Equal(new ProcessPausedResult(true, second), paused);
		Assert.Equal([second, first], harness.Resources.List().Select(static resource => resource.Id));
	}

	[Fact]
	public void Pause_UnexpectedResourceId_IsNotReported()
	{
		StateTestHarness harness = new();
		ProcessTools tools = CreateTools(harness);
		harness.Answer(static _ => new ProcessPausedResult(true, "pause-00000000-9"));

		ProcessPausedResult paused = tools.SetPaused(true, Token);

		Assert.Equal(new ProcessPausedResult(true), paused);
		Assert.Empty(harness.Resources.List());
	}

	[Fact]
	public void Pause_BlocksATargetChangeUntilTheReleaseResumesIt()
	{
		StateTestHarness harness = new();
		ProcessTools tools = CreateTools(harness);
		string id = PauseId(harness, 1);
		harness.Answer(source => new ProcessPausedResult(true, source.Contains(Quoted(id), StringComparison.Ordinal)
			? id
			: null));
		harness.Answer(static _ => new LuaResourceRelease(true, true));
		tools.SetPaused(true, Token);
		harness.Answer(_ => new LuaStateSnapshot([StateTestHarness.Entry(id, "resource", "active", true)], 1, false));

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			harness.Resources.EnsureCanChangeTarget(new TargetTransition(42, 43), Token));
		ReleaseAllResult release = harness.Resources.ReleaseAll(Token);
		harness.Answer(static _ => new LuaStateSnapshot([], 0, false));

		Assert.Equal(ToolErrorKind.Busy, refused.Error.Kind);
		Assert.Contains(id, refused.Error.Message, StringComparison.Ordinal);
		Assert.True(release.IsComplete);
		Assert.Equal(id, Assert.Single(release.Released).Resource.Id);
		harness.Resources.EnsureCanChangeTarget(new TargetTransition(42, 43), Token);
		// The released pause is no longer MCP's pause: the next one is recorded under a fresh id.
		string next = PauseId(harness, 2);
		harness.Answer(source => new ProcessPausedResult(true, source.Contains(Quoted(next), StringComparison.Ordinal)
			? next
			: null));
		Assert.Equal(next, tools.SetPaused(true, Token).ResourceId);
	}

	[Fact]
	public void Resume_ReleasesMcpPauseThenResumesTheOpenedProcess()
	{
		StateTestHarness harness = new();
		ProcessTools tools = CreateTools(harness);
		string id = PauseId(harness, 1);
		harness.Answer(source => new ProcessPausedResult(source.Contains(Quoted(id), StringComparison.Ordinal),
			source.Contains(Quoted(id), StringComparison.Ordinal) ? id : null));
		harness.Answer(static _ => new LuaResourceRelease(true, true));
		tools.SetPaused(true, Token);

		ProcessPausedResult resumed = tools.SetPaused(false, Token);

		Assert.Equal(new ProcessPausedResult(false), resumed);
		Assert.Empty(harness.Resources.List());
		Assert.Equal([CheatEngineToolNames.ProcessSetPaused, CheatEngineToolNames.ProcessSetPaused,
			"mcp_state_release_resource", CheatEngineToolNames.ProcessSetPaused],
			harness.LuaCalls.Select(static call => call.Operation));
		// The refusals run before MCP's pause is released.
		Assert.Contains(ProcessPauseScripts.ResumeCheck, harness.LuaCalls.ElementAt(1).Source,
			StringComparison.Ordinal);
		Assert.Contains(ProcessPauseScripts.Resume, harness.LuaCalls.Last().Source, StringComparison.Ordinal);
	}

	[Fact]
	public void Resume_RefusedWhileMcpPauseIsTracked_LeavesThePauseTrackedAndUnreleased()
	{
		StateTestHarness harness = new();
		ProcessTools tools = CreateTools(harness);
		string id = PauseId(harness, 1);
		harness.Answer(source => new ProcessPausedResult(true, source.Contains(Quoted(id), StringComparison.Ordinal)
			? id
			: null));
		tools.SetPaused(true, Token);
		harness.Declare<ProcessPausedResult>("invalid_state", "The debugger is stopped at a breakpoint.");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.SetPaused(false, Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		TargetResourceDescriptor kept = Assert.Single(harness.Resources.List());
		Assert.Equal((id, TargetResourceState.Active), (kept.Id, kept.State));
		Assert.DoesNotContain(harness.LuaCalls, static call => call.Operation == "mcp_state_release_resource");
		Assert.Contains(ProcessPauseScripts.ResumeCheck, harness.LuaCalls.Last().Source, StringComparison.Ordinal);
	}

	[Fact]
	public void Resume_WithoutMcpPause_OnlyResumesTheOpenedProcess()
	{
		StateTestHarness harness = new();
		ProcessTools tools = CreateTools(harness);
		harness.Answer(static _ => new ProcessPausedResult(false));

		ProcessPausedResult resumed = tools.SetPaused(false, Token);

		Assert.False(resumed.Paused);
		(string operation, string source) = Assert.Single(harness.LuaCalls);
		Assert.Equal(CheatEngineToolNames.ProcessSetPaused, operation);
		Assert.Contains(ProcessPauseScripts.Resume, source, StringComparison.Ordinal);
	}

	[Fact]
	public void Resume_FailedRelease_IsPartialEffectAndLeavesThePauseForManualRecovery()
	{
		StateTestHarness harness = new();
		ProcessTools tools = CreateTools(harness);
		string id = PauseId(harness, 1);
		harness.Answer(source => new ProcessPausedResult(true, source.Contains(Quoted(id), StringComparison.Ordinal)
			? id
			: null));
		harness.Answer(static _ => new LuaResourceRelease(true, false, "The paused process is no longer opened"));
		tools.SetPaused(true, Token);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.SetPaused(false, Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.Started, false),
			(exception.Error.Kind, exception.Error.HostEffect, exception.Error.Retryable));
		Assert.Contains("acknowledgeIds", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(id, exception.Error.Details!.Value.GetProperty("resource").GetProperty("id").GetString());
		Assert.Equal("cleanup_failed",
			exception.Error.Details!.Value.GetProperty("release").GetProperty("kind").GetString());
		// The managed handle is forgotten; the Lua entry keeps its cleanup error until it is acknowledged.
		Assert.Empty(harness.Resources.List());
		Assert.DoesNotContain(harness.LuaCalls, static call =>
			call.Source.Contains(ProcessPauseScripts.Resume, StringComparison.Ordinal));
		// The next resume no longer tries MCP's failed pause and resumes the opened process.
		harness.Answer(static _ => new ProcessPausedResult(false));
		Assert.False(tools.SetPaused(false, Token).Paused);
	}

	[Fact]
	public void PauseScripts_RecordOnlyAPauseTheyMadeAndResumeOnlyTheProcessTheyPaused()
	{
		string pause = ProcessPauseScripts.Pause;
		int reuse = pause.IndexOf("tracked.processId == pid and tracked.cleanupError == nil",
			StringComparison.Ordinal);
		int alreadyPaused = pause.IndexOf("if isPaused() then return {paused = true, resourceId = reused} end",
			StringComparison.Ordinal);
		int paused = pause.IndexOf("pause()", alreadyPaused, StringComparison.Ordinal);
		int record = pause.IndexOf("resourceRecord, a[1], a[2], 'pause'", StringComparison.Ordinal);
		int check = pause.IndexOf("assert(getOpenedProcessID() == pid", record, StringComparison.Ordinal);
		int release = pause.IndexOf("unpause()", record, StringComparison.Ordinal);

		Assert.True(reuse > 0 && alreadyPaused > reuse && paused > alreadyPaused && record > paused &&
					check > record && release > check);
		Assert.Contains("entry.processId == pid and entry.cleanupError == nil", ProcessPauseScripts.Resume,
			StringComparison.Ordinal);
		Assert.DoesNotContain("${", pause, StringComparison.Ordinal);
		// The check that precedes the release of MCP's pause has no effect of its own.
		Assert.DoesNotContain("pause()", ProcessPauseScripts.ResumeCheck, StringComparison.Ordinal);
		Assert.Contains("debug_isBroken()", ProcessPauseScripts.ResumeCheck, StringComparison.Ordinal);
	}

	[Fact]
	public void SetPaused_DescribesTheTrackedPause()
	{
		string description = typeof(ProcessTools).GetMethod(nameof(ProcessTools.SetPaused))!
			.GetCustomAttribute<DescriptionAttribute>()!.Description;

		Assert.Contains("runtime_release_resources resumes it", description, StringComparison.Ordinal);
		Assert.Contains("already paused by anything else, the user or an earlier activation, is left untracked",
			description, StringComparison.Ordinal);
		Assert.Contains("refuse with invalid_state, changing nothing", description, StringComparison.Ordinal);
	}

	private static ProcessTools CreateTools(StateTestHarness harness)
	{
		string root = Path.GetTempPath();
		McpFilePaths files = new(new McpFileOptions(), Path.Combine(root, "ce-mcp-test-registry"),
			Path.Combine(root, "ce-mcp-test-data"));
		return new ProcessTools(harness.Dispatch, harness.Resources, new TargetTransitionGuards([harness.Resources]),
			files);
	}

	private static string PauseId(StateTestHarness harness, int number)
	{
		return $"pause-{harness.Resources.Namespace}-{number}";
	}

	private static string Quoted(string text)
	{
		return "\"" + text + "\"";
	}
}
