using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveSpecializedQualificationTests
{
	[Theory]
	[InlineData((int) LiveQualificationScenario.Assembler, true)]
	[InlineData((int) LiveQualificationScenario.Debugger, true)]
	[InlineData((int) LiveQualificationScenario.DebuggerBasic, true)]
	[InlineData((int) LiveQualificationScenario.DebuggerEvents, true)]
	[InlineData((int) LiveQualificationScenario.DebuggerExecution, true)]
	[InlineData((int) LiveQualificationScenario.Smoke, false)]
	[InlineData((int) LiveQualificationScenario.KernelStatus, false)]
	public void Handles_OnlyAdmittedSpecializedScenarios(int scenario, bool expected)
	{
		Assert.Equal(expected, LiveSpecializedQualification.Handles((LiveQualificationScenario) scenario));
	}

	[Fact]
	public async Task RunDebuggerPlanAsync_DiagnosticRunsOnlyTheCallbackGatedProbe()
	{
		List<string> calls = [];
		await LiveSpecializedQualification.RunDebuggerPlanAsync(true,
			() => throw new InvalidOperationException("Ordinary break/context must not run before the diagnostic."),
			() => { calls.Add("transition"); return Task.CompletedTask; },
			() => throw new InvalidOperationException("Execution probes must not run during the diagnostic."));
		Assert.Equal(["transition"], calls);
	}

	[Fact]
	public async Task RunDebuggerPlanAsync_NormalRunsBasicThenExecution()
	{
		List<string> calls = [];
		await LiveSpecializedQualification.RunDebuggerPlanAsync(false,
			() => { calls.Add("basic"); return Task.CompletedTask; },
			() => throw new InvalidOperationException("The diagnostic must remain separately selected."),
			() => { calls.Add("execution"); return Task.CompletedTask; });
		Assert.Equal(["basic", "execution"], calls);
	}

	[Fact]
	public async Task RunDebuggerPlanAsync_BasicOnlyDoesNotEnterExecutionScenario()
	{
		List<string> calls = [];
		await LiveSpecializedQualification.RunDebuggerPlanAsync(false,
			() => { calls.Add("basic"); return Task.CompletedTask; },
			() => throw new InvalidOperationException("The diagnostic must remain separately selected."),
			() => throw new InvalidOperationException("Basic-only qualification must not run the failing execution scenario."),
			basicOnly: true);
		Assert.Equal(["basic"], calls);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task RunDebuggerPlanAsync_FailureDoesNotStartAnotherProbe(bool diagnostic)
	{
		List<string> calls = [];
		InvalidOperationException failure = new("Selected probe failed.");
		InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			LiveSpecializedQualification.RunDebuggerPlanAsync(diagnostic,
				() => { calls.Add("basic"); return Task.FromException(failure); },
				() => { calls.Add("transition"); return Task.FromException(failure); },
				() => { calls.Add("execution"); return Task.CompletedTask; }));
		Assert.Same(failure, actual);
		Assert.Equal([diagnostic ? "transition" : "basic"], calls);
	}

	[Fact]
	public async Task RunDebuggerScenarioPlanAsync_EventsOnlySkipsTheKnownFailingExecutionGroup()
	{
		List<string> calls = [];
		await LiveSpecializedQualification.RunDebuggerScenarioPlanAsync(eventsOnly: true, executionOnly: false, transitionDiagnostic: false, basicOnly: false,
			() => { calls.Add("basic"); return Task.CompletedTask; },
			() => { calls.Add("transition"); return Task.CompletedTask; },
			() => { calls.Add("execution"); return Task.CompletedTask; },
			() => { calls.Add("execution-only"); return Task.CompletedTask; },
			() => { calls.Add("events"); return Task.CompletedTask; });
		Assert.Equal(["events"], calls);
	}

	[Fact]
	public async Task RunDebuggerScenarioPlanAsync_EventFailureDoesNotFallThroughToAnotherProbe()
	{
		List<string> calls = [];
		InvalidOperationException failure = new("Event qualification failed.");
		InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			LiveSpecializedQualification.RunDebuggerScenarioPlanAsync(eventsOnly: true, executionOnly: false, transitionDiagnostic: false, basicOnly: false,
				() => { calls.Add("basic"); return Task.CompletedTask; },
				() => { calls.Add("transition"); return Task.CompletedTask; },
				() => { calls.Add("execution"); return Task.CompletedTask; },
				() => { calls.Add("execution-only"); return Task.CompletedTask; },
				() => { calls.Add("events"); return Task.FromException(failure); }));
		Assert.Same(failure, actual);
		Assert.Equal(["events"], calls);
	}

	[Fact]
	public async Task RunDebuggerScenarioPlanAsync_ExecutionOnlyRunsOnlyTheRunToGroup()
	{
		List<string> calls = [];
		await LiveSpecializedQualification.RunDebuggerScenarioPlanAsync(eventsOnly: false, executionOnly: true, transitionDiagnostic: false, basicOnly: false,
			() => { calls.Add("basic"); return Task.CompletedTask; },
			() => { calls.Add("transition"); return Task.CompletedTask; },
			() => throw new InvalidOperationException("The combined execution and event group must not run."),
			() => { calls.Add("execution-only"); return Task.CompletedTask; },
			() => throw new InvalidOperationException("Events must remain separately admitted."));
		Assert.Equal(["execution-only"], calls);
	}

	[Fact]
	public async Task RunDebuggerScenarioPlanAsync_ExecutionOnlyFailureDoesNotStartAnotherProbe()
	{
		List<string> calls = [];
		InvalidOperationException failure = new("Run-to failed.");
		InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			LiveSpecializedQualification.RunDebuggerScenarioPlanAsync(eventsOnly: false, executionOnly: true, transitionDiagnostic: false, basicOnly: false,
				() => { calls.Add("basic"); return Task.CompletedTask; },
				() => { calls.Add("transition"); return Task.CompletedTask; },
				() => { calls.Add("combined"); return Task.CompletedTask; },
				() => { calls.Add("execution-only"); return Task.FromException(failure); },
				() => { calls.Add("events"); return Task.CompletedTask; }));
		Assert.Same(failure, actual);
		Assert.Equal(["execution-only"], calls);
	}
}
