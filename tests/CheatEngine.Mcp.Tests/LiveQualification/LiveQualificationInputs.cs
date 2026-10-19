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
}
