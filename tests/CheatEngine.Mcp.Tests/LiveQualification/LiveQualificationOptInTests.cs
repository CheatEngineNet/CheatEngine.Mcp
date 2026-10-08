namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LiveQualificationOptInTests
{
	private const string Repository = "C:/work/CheatEngine.Mcp";
	private const string Installation = "C:/Program Files/Cheat Engine";
	private const string LocalData = "C:/Users/test/AppData/Local";

	[Fact]
	public void CompilerPayload_ReviewedUtf8LfSource_MatchesExpectedHash()
	{
		Assert.DoesNotContain("\r", LiveCompilerPayload.ValidSource, StringComparison.Ordinal);
		Assert.Equal(LiveCompilerPayload.ValidSha256, LiveCompilerPayload.Sha256(LiveCompilerPayload.ValidSource));
		Assert.Equal(LiveCompilerPayload.InvalidSourceSha256, LiveCompilerPayload.Sha256(LiveCompilerPayload.InvalidSource));
		Assert.Equal(LiveCompilerPayload.InvalidReferenceSha256, LiveCompilerPayload.Sha256(LiveCompilerPayload.InvalidReference));
		LiveCompilerPayload.RequireReviewedHashes();
		LiveCompilerBridge.RequireReviewedHash();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("1")]
	[InlineData("true")]
	public void Evaluate_MissingExactAcknowledgement_RefusesWithRunInstructions(string? acknowledgement)
	{
		LiveQualificationDecision decision = Evaluate(new Dictionary<string, string?>
		{
			[LiveQualificationOptIn.OptInVariable] = acknowledgement
		});
		Assert.False(decision.IsAuthorized);
		Assert.Null(decision.Inputs);
		Assert.Contains(LiveQualificationOptIn.LocalCommand, decision.Refusal, StringComparison.Ordinal);
		Assert.Contains(LiveQualificationOptIn.Acknowledgement, decision.Refusal, StringComparison.Ordinal);
	}

	[Fact]
	public void Evaluate_CiEvenWithAcknowledgement_RefusesBeforeInspectingDirectories()
	{
		Dictionary<string, string?> variables = Authorized();
		variables["CI"] = "true";
		LiveQualificationDecision decision = LiveQualificationOptIn.Evaluate(variables.GetValueOrDefault,
			Repository, LocalData, Installation,
			_ => throw new InvalidOperationException("No directory inspection expected."));
		Assert.False(decision.IsAuthorized);
		Assert.Contains("CI", decision.Refusal, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("runs")]
	[InlineData(Repository)]
	[InlineData(Repository + "/artifacts/live")]
	[InlineData(Installation)]
	[InlineData(Installation + "/runs")]
	[InlineData("C:/Program Files")]
	public void Evaluate_UnsafeRunRoot_Refuses(string runRoot)
	{
		Dictionary<string, string?> variables = Authorized();
		variables[LiveQualificationOptIn.RunRootVariable] = runRoot;
		Assert.False(Evaluate(variables).IsAuthorized);
	}

	[Fact]
	public void Evaluate_AcknowledgedWorkstation_UsesExternalRunRoot()
	{
		LiveQualificationDecision decision = Evaluate(Authorized());
		Assert.True(decision.IsAuthorized, decision.Refusal);
		Assert.Equal(Path.GetFullPath(Installation), decision.Inputs!.CheatEngineDirectory);
		Assert.Equal(Path.GetFullPath(LocalData + "/CheatEngine.Mcp.LiveQualification/runs"), decision.Inputs.RunRoot);
		Assert.Equal("x64", decision.Inputs.TargetArchitecture);
		Assert.Equal(LiveQualificationScenario.Smoke, decision.Inputs.Scenario);
		Assert.False(LiveQualificationOptIn.IsSameOrBelow(decision.Inputs.RunRoot, Repository));
	}

	[Fact]
	public void Evaluate_CompilerScenarioWithSecondAcknowledgement_Authorizes()
	{
		Dictionary<string, string?> variables = Authorized();
		variables[LiveQualificationOptIn.ScenarioVariable] = "compiler";
		variables[LiveQualificationOptIn.CodeExecutionOptInVariable] = LiveQualificationOptIn.CodeExecutionAcknowledgement;

		LiveQualificationDecision decision = Evaluate(variables);

		Assert.True(decision.IsAuthorized, decision.Refusal);
		Assert.Equal(LiveQualificationScenario.Compiler, decision.Inputs!.Scenario);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("I_AUTHORIZE_SOMETHING_ELSE")]
	public void Evaluate_CompilerScenarioWithoutExactSecondAcknowledgement_RefusesBeforeDirectoryInspection(
		string? acknowledgement)
	{
		Dictionary<string, string?> variables = Authorized();
		variables[LiveQualificationOptIn.ScenarioVariable] = "compiler";
		variables[LiveQualificationOptIn.CodeExecutionOptInVariable] = acknowledgement;

		LiveQualificationDecision decision = LiveQualificationOptIn.Evaluate(variables.GetValueOrDefault,
			Repository, LocalData, Installation, _ => throw new InvalidOperationException("No directory inspection expected."));

		Assert.False(decision.IsAuthorized);
		Assert.Contains(LiveQualificationOptIn.CodeExecutionOptInVariable, decision.Refusal, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("COMpiler")]
	[InlineData("inject")]
	[InlineData("compiler ")]
	public void Evaluate_UnknownScenario_RefusesBeforeDirectoryInspection(string scenario)
	{
		Dictionary<string, string?> variables = Authorized();
		variables[LiveQualificationOptIn.ScenarioVariable] = scenario;

		LiveQualificationDecision decision = LiveQualificationOptIn.Evaluate(variables.GetValueOrDefault,
			Repository, LocalData, Installation, _ => throw new InvalidOperationException("No directory inspection expected."));

		Assert.False(decision.IsAuthorized);
		Assert.Contains(LiveQualificationOptIn.ScenarioVariable, decision.Refusal, StringComparison.Ordinal);
	}

	[Fact]
	public void Evaluate_CompilerScenarioWithTempdirDisabled_RefusesBeforeDirectoryInspection()
	{
		Dictionary<string, string?> variables = Authorized();
		variables[LiveQualificationOptIn.ScenarioVariable] = "compiler";
		variables[LiveQualificationOptIn.CodeExecutionOptInVariable] = LiveQualificationOptIn.CodeExecutionAcknowledgement;

		LiveQualificationDecision decision = LiveQualificationOptIn.Evaluate(variables.GetValueOrDefault,
			Repository, LocalData, Installation, _ => throw new InvalidOperationException("No directory inspection expected."),
			static () => false);

		Assert.False(decision.IsAuthorized);
		Assert.Contains("Don't use tempdir", decision.Refusal, StringComparison.Ordinal);
	}

	[Fact]
	public void Evaluate_X86TargetArchitecture_AuthorizesTheBoundedFixtureChoice()
	{
		Dictionary<string, string?> variables = Authorized();
		variables[LiveQualificationOptIn.TargetArchitectureVariable] = "x86";

		LiveQualificationDecision decision = Evaluate(variables);

		Assert.True(decision.IsAuthorized, decision.Refusal);
		Assert.Equal("x86", decision.Inputs!.TargetArchitecture);
	}

	[Theory]
	[InlineData("X86")]
	[InlineData("arm64")]
	[InlineData("C:/other.exe")]
	public void Evaluate_UnsupportedTargetArchitecture_Refuses(string architecture)
	{
		Dictionary<string, string?> variables = Authorized();
		variables[LiveQualificationOptIn.TargetArchitectureVariable] = architecture;

		Assert.False(Evaluate(variables).IsAuthorized);
	}

	[Theory]
	[InlineData("relative")]
	[InlineData("C:/missing")]
	public void Evaluate_MissingOrRelativeInstallation_Refuses(string installation)
	{
		Dictionary<string, string?> variables = Authorized();
		variables[LiveQualificationOptIn.CheatEngineDirectoryVariable] = installation;
		Assert.False(Evaluate(variables).IsAuthorized);
	}

	[Theory]
	[InlineData("lifecycle", LiveQualificationScenario.Lifecycle)]
	[InlineData("performance", LiveQualificationScenario.Performance)]
	[InlineData("soak", LiveQualificationScenario.Soak)]
	[InlineData("dispatch-diagnostic", LiveQualificationScenario.DispatchDiagnostic)]
	public void Evaluate_StandardQualificationScenario_UsesOnlyStandardAcknowledgement(string name, int expected)
	{
		Dictionary<string, string?> variables = Authorized();
		variables[LiveQualificationOptIn.ScenarioVariable] = name;
		LiveQualificationDecision decision = LiveQualificationOptIn.Evaluate(variables.GetValueOrDefault,
			Repository, LocalData, Installation, static _ => true,
			static () => throw new InvalidOperationException("A standard scenario must not inspect compiler settings."));
		Assert.True(decision.IsAuthorized, decision.Refusal);
		Assert.Equal((LiveQualificationScenario) expected, decision.Inputs!.Scenario);
	}

	[Theory]
	[InlineData("compiler-extended")]
	[InlineData("compiler-injection")]
	public void Evaluate_CompilerVariantsWithoutCompilerAcknowledgement_RefuseBeforeInspection(string name)
	{
		Dictionary<string, string?> variables = Authorized();
		variables[LiveQualificationOptIn.ScenarioVariable] = name;
		variables[LiveQualificationOptIn.InjectionOptInVariable] = LiveQualificationOptIn.InjectionAcknowledgement;
		LiveQualificationDecision decision = LiveQualificationOptIn.Evaluate(variables.GetValueOrDefault,
			Repository, LocalData, Installation, static _ => throw new InvalidOperationException("Must refuse first."));
		Assert.False(decision.IsAuthorized);
		Assert.Contains(LiveQualificationOptIn.CodeExecutionOptInVariable, decision.Refusal, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("true")]
	[InlineData(LiveQualificationOptIn.CodeExecutionAcknowledgement)]
	public void Evaluate_InjectionWithoutExactThirdAcknowledgement_RefusesBeforeInspection(string? acknowledgement)
	{
		Dictionary<string, string?> variables = Authorized();
		variables[LiveQualificationOptIn.ScenarioVariable] = "compiler-injection";
		variables[LiveQualificationOptIn.CodeExecutionOptInVariable] = LiveQualificationOptIn.CodeExecutionAcknowledgement;
		variables[LiveQualificationOptIn.InjectionOptInVariable] = acknowledgement;
		LiveQualificationDecision decision = LiveQualificationOptIn.Evaluate(variables.GetValueOrDefault,
			Repository, LocalData, Installation, static _ => throw new InvalidOperationException("Must refuse first."),
			static () => throw new InvalidOperationException("Must refuse first."));
		Assert.False(decision.IsAuthorized);
		Assert.Contains(LiveQualificationOptIn.InjectionOptInVariable, decision.Refusal, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("compiler-extended", LiveQualificationScenario.CompilerExtended)]
	[InlineData("compiler-injection", LiveQualificationScenario.CompilerInjection)]
	public void Evaluate_CompilerVariantsWithRequiredAcknowledgements_Authorize(string name, int expected)
	{
		Dictionary<string, string?> variables = Authorized();
		variables[LiveQualificationOptIn.ScenarioVariable] = name;
		variables[LiveQualificationOptIn.CodeExecutionOptInVariable] = LiveQualificationOptIn.CodeExecutionAcknowledgement;
		if (name == "compiler-injection")
		{
			variables[LiveQualificationOptIn.InjectionOptInVariable] = LiveQualificationOptIn.InjectionAcknowledgement;
		}
		LiveQualificationDecision decision = Evaluate(variables);
		Assert.True(decision.IsAuthorized, decision.Refusal);
		Assert.Equal((LiveQualificationScenario) expected, decision.Inputs!.Scenario);
	}

	private static Dictionary<string, string?> Authorized()
	{
		return new Dictionary<string, string?>
		{
			[LiveQualificationOptIn.OptInVariable] = LiveQualificationOptIn.Acknowledgement
		};
	}

	private static LiveQualificationDecision Evaluate(Dictionary<string, string?> variables)
	{
		return LiveQualificationOptIn.Evaluate(variables.GetValueOrDefault, Repository, LocalData, Installation,
			path => string.Equals(path, Installation, StringComparison.OrdinalIgnoreCase));
	}
}
