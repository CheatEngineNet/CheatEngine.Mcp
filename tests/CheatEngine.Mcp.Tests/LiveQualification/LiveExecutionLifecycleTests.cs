namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LiveExecutionLifecycleTests
{
	private const string Repository = "C:/work/CheatEngine.Mcp";
	private const string Installation = "C:/Program Files/Cheat Engine";
	private const string LocalData = "C:/Users/test/AppData/Local";

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("I_AUTHORIZE_FIXED_COMPILECS_PROBES_ON_PRIVATE_CE_AND_OWNED_TARGETS")]
	[InlineData("wrong")]
	public void Evaluate_ExecutionWithoutDedicatedAcknowledgement_RefusesBeforeDirectoryInspection(string? acknowledgement)
	{
		Dictionary<string, string?> variables = BaseExecutionVariables();
		variables[LiveQualificationOptIn.ExecutionOptInVariable] = acknowledgement;

		LiveQualificationDecision decision = LiveQualificationOptIn.Evaluate(variables.GetValueOrDefault,
			Repository, LocalData, Installation,
			static _ => throw new InvalidOperationException("Execution acknowledgement must be checked before directories."));

		Assert.False(decision.IsAuthorized);
		Assert.Contains(LiveQualificationOptIn.ExecutionOptInVariable, decision.Refusal, StringComparison.Ordinal);
	}

	[Fact]
	public void Evaluate_ExecutionWithDedicatedAcknowledgement_Authorizes()
	{
		Dictionary<string, string?> variables = BaseExecutionVariables();
		variables[LiveQualificationOptIn.ExecutionOptInVariable] = LiveQualificationOptIn.ExecutionAcknowledgement;

		LiveQualificationDecision decision = LiveQualificationOptIn.Evaluate(variables.GetValueOrDefault,
			Repository, LocalData, Installation, static _ => true);

		Assert.True(decision.IsAuthorized, decision.Refusal);
		Assert.Equal(LiveQualificationScenario.Execution, decision.Inputs!.Scenario);
	}

	[Fact]
	public void Evaluate_ExecutionX86_RefusesBeforeDirectoryInspection()
	{
		Dictionary<string, string?> variables = BaseExecutionVariables();
		variables[LiveQualificationOptIn.ExecutionOptInVariable] = LiveQualificationOptIn.ExecutionAcknowledgement;
		variables[LiveQualificationOptIn.TargetArchitectureVariable] = "x86";

		LiveQualificationDecision decision = LiveQualificationOptIn.Evaluate(variables.GetValueOrDefault,
			Repository, LocalData, Installation,
			static _ => throw new InvalidOperationException("x86 execution must be refused before directories."));

		Assert.False(decision.IsAuthorized);
		Assert.Contains("x64", decision.Refusal, StringComparison.Ordinal);
	}

	[Fact]
	public void ValidateFor_ExecutionRequiresOnlyItsExecutionGate()
	{
		LiveSandboxOptions execution = LiveSandboxOptions.Smoke with
		{
			ExecutionQualification = true,
			EnableTargetCodeExecution = true
		};

		execution.ValidateFor(LiveQualificationScenario.Execution);
		Assert.True(execution.RequiresCapabilityConfiguration);
		Assert.Throws<InvalidOperationException>(() => (execution with { EnableTargetCodeExecution = false }).ValidateFor(LiveQualificationScenario.Execution));
		Assert.Throws<InvalidOperationException>(() => (execution with { CompilerQualification = true }).ValidateFor(LiveQualificationScenario.Execution));
		Assert.Throws<InvalidOperationException>(() => (execution with { EnableManagedInjection = true }).ValidateFor(LiveQualificationScenario.Execution));
		Assert.Throws<InvalidOperationException>(() => (execution with { AssemblerQualification = true }).ValidateFor(LiveQualificationScenario.Execution));
		Assert.Throws<InvalidOperationException>(() => (execution with { DebuggerQualification = true }).ValidateFor(LiveQualificationScenario.Execution));
		Assert.Throws<InvalidOperationException>(() => (execution with { LifecycleQualification = true }).ValidateFor(LiveQualificationScenario.Execution));
	}

	[Fact]
	public void Render_ExecutionLifetimeWritesOneShotExpiryAndKeepsTimerAliveForExplicitStop()
	{
		string lua = LiveExecutionDriverLua.RenderPreamble() + "\n" +
			"timer.OnTimer=function()\n" + LiveExecutionDriverLua.RenderTimerBody(
				"C:/run/stop", "C:/run/target.exited", "C:/run/expiry", 300_000, true) + "\nend";

		int preamble = lua.IndexOf("local expirySignaled=false", StringComparison.Ordinal);
		int callback = lua.IndexOf("timer.OnTimer=function()", StringComparison.Ordinal);
		Assert.True(preamble >= 0 && preamble < callback);
		Assert.Equal(1, lua.Split("local expirySignaled=false", StringSplitOptions.None).Length - 1);
		Assert.True(lua.IndexOf("local stop=io.open", StringComparison.Ordinal) < lua.IndexOf("local expired=", StringComparison.Ordinal));
		Assert.Contains("> 300000", lua, StringComparison.Ordinal);
		Assert.Contains("if expired and not expirySignaled", lua, StringComparison.Ordinal);
		Assert.Contains("expirySignaled=true", lua, StringComparison.Ordinal);
		string expiryBranch = lua[lua.IndexOf("if expired and not expirySignaled", StringComparison.Ordinal)..];
		Assert.DoesNotContain("getAddressList().clear", expiryBranch, StringComparison.Ordinal);
		Assert.DoesNotContain("closeCE()", expiryBranch, StringComparison.Ordinal);
		Assert.DoesNotContain("timer.Enabled=false", expiryBranch, StringComparison.Ordinal);
		Assert.Contains("target exit was not confirmed before CE shutdown", lua, StringComparison.Ordinal);
	}

	[Fact]
	public void Render_NonExecutionLifetimeRetainsAddressListClearAndClose()
	{
		string lua = LiveExecutionDriverLua.RenderTimerBody("C:/run/stop", "C:/run/target.exited", "C:/run/expiry", 300_000, false);

		Assert.Contains("if expired then log('closing'); timer.Enabled=false; pcall(function() getAddressList().clear() end); closeCE(); return end", lua, StringComparison.Ordinal);
		Assert.DoesNotContain("execution-expired", lua, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AttemptAllAsync_AttemptsLaterTargetWhenEarlierTerminationFails()
	{
		List<string> attempted = [];
		List<Exception> failures = [];

		await LiveExecutionCleanup.AttemptAllAsync(["A", "B"], name =>
		{
			attempted.Add(name);
			return name == "A" ? Task.FromException(new InvalidOperationException("A failed")) : Task.CompletedTask;
		}, failures);

		Assert.Equal(["A", "B"], attempted);
		InvalidOperationException failure = Assert.IsType<InvalidOperationException>(Assert.Single(failures));
		Assert.Equal("A failed", failure.Message);
	}

	[Fact]
	public async Task ConfirmTargetExitAsync_MarkerWriteFailureStillForcesAndConfirmsExit()
	{
		bool forced = false;
		bool exited = false;
		int waits = 0;
		List<Exception> markerFailures = [];

		await LiveExecutionCleanup.ConfirmTargetExitAsync(
			static () => throw new IOException("marker write failed"),
			() => ++waits == 1 ? Task.FromException(new TimeoutException()) : Task.CompletedTask,
			() => { forced = true; exited = true; }, () => exited, markerFailures);

		Assert.True(forced);
		Assert.True(exited);
		Assert.Equal(2, waits);
		IOException failure = Assert.IsType<IOException>(Assert.Single(markerFailures));
		Assert.Equal("marker write failed", failure.Message);
	}

	private static Dictionary<string, string?> BaseExecutionVariables() => new()
	{
		[LiveQualificationOptIn.OptInVariable] = LiveQualificationOptIn.Acknowledgement,
		[LiveQualificationOptIn.ScenarioVariable] = "execution"
	};
}
