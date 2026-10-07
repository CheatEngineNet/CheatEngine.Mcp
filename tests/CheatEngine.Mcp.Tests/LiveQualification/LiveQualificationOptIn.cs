namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Matches the Client's explicit, fail-fast workstation opt-in for native CE sessions.</summary>
internal static class LiveQualificationOptIn
{
	internal const string OptInVariable = "CHEATENGINE_MCP_LIVE_QUALIFICATION";
	internal const string Acknowledgement = "I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET";
	internal const string CheatEngineDirectoryVariable = "CHEATENGINE_MCP_LIVE_QUALIFICATION_CE_DIRECTORY";
	internal const string RunRootVariable = "CHEATENGINE_MCP_LIVE_QUALIFICATION_RUN_ROOT";
	internal const string TargetArchitectureVariable = "CHEATENGINE_MCP_LIVE_QUALIFICATION_TARGET_ARCHITECTURE";
	internal const string ScenarioVariable = "CHEATENGINE_MCP_LIVE_QUALIFICATION_SCENARIO";
	internal const string CodeExecutionOptInVariable = "CHEATENGINE_MCP_LIVE_CODE_EXECUTION_QUALIFICATION";
	internal const string CodeExecutionAcknowledgement =
		"I_AUTHORIZE_FIXED_COMPILECS_PROBES_ON_PRIVATE_CE_AND_OWNED_TARGETS";

	internal const string LocalCommand =
		"dotnet test --project tests/CheatEngine.Mcp.Tests -c Release --filter-trait Category=LiveQualification --fail-skips on";

	internal static string Instructions =>
		"Close Cheat Engine and DebugView. From the repository root, run:" + Environment.NewLine +
		$"$env:{OptInVariable} = '{Acknowledgement}'" + Environment.NewLine + LocalCommand + Environment.NewLine +
		$"Remove-Item Env:{OptInVariable}";

	internal static LiveQualificationDecision ResolveFromEnvironment(string repositoryRoot)
	{
		return Evaluate(Environment.GetEnvironmentVariable, repositoryRoot,
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cheat Engine"),
			Directory.Exists, CompilerTempSettingsAreSafe);
	}

	internal static LiveQualificationDecision Evaluate(Func<string, string?> variables, string repositoryRoot,
		string localApplicationData, string defaultCheatEngineDirectory, Func<string, bool> directoryExists,
		Func<bool>? compilerTempSettingsAreSafe = null)
	{
		if (string.Equals(variables("CI"), "true", StringComparison.OrdinalIgnoreCase))
		{
			return Refuse("Live qualification never starts Cheat Engine in CI. Exclude Category=LiveQualification.");
		}

		if (variables(OptInVariable) != Acknowledgement)
		{
			return Refuse($"{OptInVariable} must contain the exact acknowledgement {Acknowledgement}.");
		}

		string scenarioValue = variables(ScenarioVariable) ?? "smoke";
		if (scenarioValue is not ("smoke" or "compiler"))
		{
			return Refuse($"{ScenarioVariable} must be smoke or compiler.");
		}

		LiveQualificationScenario scenario = scenarioValue == "compiler"
			? LiveQualificationScenario.Compiler
			: LiveQualificationScenario.Smoke;

		if (scenario == LiveQualificationScenario.Compiler &&
			variables(CodeExecutionOptInVariable) != CodeExecutionAcknowledgement)
		{
			return Refuse($"{CodeExecutionOptInVariable} must contain the exact acknowledgement {CodeExecutionAcknowledgement}.");
		}
		if (scenario == LiveQualificationScenario.Compiler && compilerTempSettingsAreSafe is not null &&
			!compilerTempSettingsAreSafe())
		{
			return Refuse("Cheat Engine's Don't use tempdir setting is enabled; compiler qualification requires the owned TEMP directory.");
		}

		string source = variables(CheatEngineDirectoryVariable) is { Length: > 0 } configuredSource
			? configuredSource
			: defaultCheatEngineDirectory;
		if (!Path.IsPathFullyQualified(source) || !directoryExists(source))
		{
			return Refuse($"{CheatEngineDirectoryVariable} must identify an existing absolute Cheat Engine directory.");
		}

		string runRoot = variables(RunRootVariable) is { Length: > 0 } configuredRunRoot
			? configuredRunRoot
			: Path.Combine(localApplicationData, "CheatEngine.Mcp.LiveQualification", "runs");
		if (!Path.IsPathFullyQualified(runRoot))
		{
			return Refuse($"{RunRootVariable} must be an absolute directory.");
		}

		if (IsSameOrBelow(runRoot, repositoryRoot) || IsSameOrBelow(runRoot, source) || IsSameOrBelow(source, runRoot))
		{
			return Refuse(
				"The run root must lie outside the repository and apart from the installed Cheat Engine directory.");
		}
		string architecture = variables(TargetArchitectureVariable) ?? "x64";
		if (architecture is not ("x64" or "x86"))
		{
			return Refuse($"{TargetArchitectureVariable} must be x64 or x86.");
		}

		return new LiveQualificationDecision(
			new LiveQualificationInputs(Path.GetFullPath(repositoryRoot), Path.GetFullPath(source),
				Path.GetFullPath(runRoot), architecture, scenario), null);
	}

	internal static bool IsSameOrBelow(string path, string directory)
	{
		string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
		string fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
		return string.Equals(fullPath, fullDirectory, StringComparison.OrdinalIgnoreCase)
			   || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	private static LiveQualificationDecision Refuse(string reason)
	{
		return new LiveQualificationDecision(null, reason + Environment.NewLine + Instructions);
	}

	private static bool CompilerTempSettingsAreSafe()
	{
		if (!OperatingSystem.IsWindows())
		{
			return false;
		}
		using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Cheat Engine");
		object? value = key?.GetValue("Don't use tempdir");
		return value switch
		{
			null => true,
			int number => number == 0,
			string text when bool.TryParse(text, out bool enabled) => !enabled,
			string text when int.TryParse(text, out int number) => number == 0,
			_ => false
		};
	}
}
