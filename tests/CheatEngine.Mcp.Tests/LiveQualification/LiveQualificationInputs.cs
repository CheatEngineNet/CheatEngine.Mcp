namespace CheatEngine.Mcp.Tests.LiveQualification;

internal sealed record LiveQualificationInputs(string RepositoryRoot, string CheatEngineDirectory, string RunRoot,
	string TargetArchitecture, LiveQualificationScenario Scenario);
