namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LiveQualificationOptInTests
{
	private const string Repository = "C:/work/CheatEngine.Mcp";
	private const string Installation = "C:/Program Files/Cheat Engine";
	private const string LocalData = "C:/Users/test/AppData/Local";

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
		Assert.False(LiveQualificationOptIn.IsSameOrBelow(decision.Inputs.RunRoot, Repository));
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
