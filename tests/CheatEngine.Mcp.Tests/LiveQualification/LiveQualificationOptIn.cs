using ClientLivePaths = CheatEngine.Client.Tests.LiveQualification.LiveQualificationOptIn;

namespace CheatEngine.Mcp.Tests.LiveQualification;

internal sealed record LiveQualificationInputs(string RepositoryRoot, string CheatEngineDirectory, string RunRoot);

internal sealed record LiveQualificationDecision(LiveQualificationInputs? Inputs, string? Refusal)
{
	internal bool IsAuthorized => Inputs is not null;
}

/// <summary>Matches the Client's explicit, fail-fast workstation opt-in for native CE sessions.</summary>
internal static class LiveQualificationOptIn
{
	internal const string OptInVariable = "CHEATENGINE_MCP_LIVE_QUALIFICATION";
	internal const string Acknowledgement = "I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET";
	internal const string CheatEngineDirectoryVariable = "CHEATENGINE_MCP_LIVE_QUALIFICATION_CE_DIRECTORY";
	internal const string RunRootVariable = "CHEATENGINE_MCP_LIVE_QUALIFICATION_RUN_ROOT";
	internal const string LocalCommand = "dotnet test --project tests/CheatEngine.Mcp.Tests -c Release --filter-trait Category=LiveQualification --fail-skips on";

	internal static string Instructions =>
		"Close Cheat Engine and DebugView. From the repository root, run:" + Environment.NewLine +
		$"$env:{OptInVariable} = '{Acknowledgement}'" + Environment.NewLine + LocalCommand + Environment.NewLine +
		$"Remove-Item Env:{OptInVariable}";

	internal static LiveQualificationDecision ResolveFromEnvironment(string repositoryRoot) =>
		Evaluate(Environment.GetEnvironmentVariable, repositoryRoot,
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cheat Engine"), Directory.Exists);

	internal static LiveQualificationDecision Evaluate(Func<string, string?> variables, string repositoryRoot,
		string localApplicationData, string defaultCheatEngineDirectory, Func<string, bool> directoryExists)
	{
		if (string.Equals(variables("CI"), "true", StringComparison.OrdinalIgnoreCase))
		{
			return Refuse("Live qualification never starts Cheat Engine in CI. Exclude Category=LiveQualification.");
		}
		if (variables(OptInVariable) != Acknowledgement)
		{
			return Refuse($"{OptInVariable} must contain the exact acknowledgement {Acknowledgement}.");
		}
		string source = variables(CheatEngineDirectoryVariable) is { Length: > 0 } configuredSource
			? configuredSource : defaultCheatEngineDirectory;
		if (!Path.IsPathFullyQualified(source) || !directoryExists(source))
		{
			return Refuse($"{CheatEngineDirectoryVariable} must identify an existing absolute Cheat Engine directory.");
		}
		string runRoot = variables(RunRootVariable) is { Length: > 0 } configuredRunRoot
			? configuredRunRoot : Path.Combine(localApplicationData, "CheatEngine.Mcp.LiveQualification", "runs");
		if (!Path.IsPathFullyQualified(runRoot))
		{
			return Refuse($"{RunRootVariable} must be an absolute directory.");
		}
		if (IsSameOrBelow(runRoot, repositoryRoot) || IsSameOrBelow(runRoot, source) || IsSameOrBelow(source, runRoot))
		{
			return Refuse("The run root must lie outside the repository and apart from the installed Cheat Engine directory.");
		}
		return new(new(Path.GetFullPath(repositoryRoot), Path.GetFullPath(source), Path.GetFullPath(runRoot)), null);
	}

	internal static bool IsSameOrBelow(string path, string directory) => ClientLivePaths.IsSameOrBelow(path, directory);

	private static LiveQualificationDecision Refuse(string reason) => new(null, reason + Environment.NewLine + Instructions);
}
