namespace CheatEngine.Mcp.Tests.LiveQualification;

internal sealed record LiveQualificationInputs(string RepositoryRoot, string CheatEngineDirectory, string RunRoot,
	string TargetArchitecture, LiveQualificationScenario Scenario)
{
	internal bool DebuggerTransitionDiagnostic
	{
		get; init;
	}

	internal LiveDispatchDiagnosticCase? DispatchDiagnosticCase
	{
		get; init;
	}

	internal string? FrozenInputManifestPath
	{
		get; init;
	}

	internal string? FrozenInputManifestSha256
	{
		get; init;
	}

	/// <summary>Exact acknowledgement carried from admission to the one native WFP mutation boundary.</summary>
	internal string? WfpIsolationAcknowledgement
	{
		get; init;
	}
}
