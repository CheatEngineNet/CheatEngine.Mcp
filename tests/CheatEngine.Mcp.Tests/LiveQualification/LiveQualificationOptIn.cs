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
	internal const string DispatchDiagnosticCaseVariable = "CHEATENGINE_MCP_LIVE_DISPATCH_DIAGNOSTIC_CASE";
	internal const string CodeExecutionOptInVariable = "CHEATENGINE_MCP_LIVE_CODE_EXECUTION_QUALIFICATION";
	internal const string CodeExecutionAcknowledgement =
		"I_AUTHORIZE_FIXED_COMPILECS_PROBES_ON_PRIVATE_CE_AND_OWNED_TARGETS";
	internal const string ExecutionOptInVariable = "CHEATENGINE_MCP_LIVE_EXECUTION_QUALIFICATION";
	internal const string ExecutionAcknowledgement =
		"I_AUTHORIZE_FIXED_C_AND_CALL_PROBES_ON_PRIVATE_CE_AND_OWNED_TARGETS";
	internal const string SymbolFixtureOptInVariable = "CHEATENGINE_MCP_LIVE_SYMBOL_FIXTURE_QUALIFICATION";
	internal const string SymbolFixtureAcknowledgement =
		"I_AUTHORIZE_FIXED_NATIVE_DLL_AND_LOCAL_PDB_ON_PRIVATE_CE_AND_OWNED_TARGETS";
	internal const string InjectionOptInVariable = "CHEATENGINE_MCP_LIVE_MANAGED_INJECTION_QUALIFICATION";
	internal const string InjectionAcknowledgement =
		"I_AUTHORIZE_FIXED_MANAGED_INJECTION_ON_OWNED_FRAMEWORK_TARGETS";
	internal const string AssemblerOptInVariable = "CHEATENGINE_MCP_LIVE_ASSEMBLER_QUALIFICATION";
	internal const string AssemblerAcknowledgement =
		"I_AUTHORIZE_FIXED_LUA_AND_REVERSIBLE_PATCHES_ON_OWNED_ALLOCATIONS";
	internal const string DebuggerOptInVariable = "CHEATENGINE_MCP_LIVE_DEBUGGER_QUALIFICATION";
	internal const string DebuggerAcknowledgement =
		"I_AUTHORIZE_WINDOWS_DEBUGGER_CHECKS_ON_OWNED_DISPOSABLE_TARGETS";
	internal const string DebuggerTransitionDiagnosticVariable = "CHEATENGINE_MCP_LIVE_DEBUGGER_TRANSITION_DIAGNOSTIC";

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
		LiveQualificationScenario? selectedScenario = scenarioValue switch
		{
			"smoke" => LiveQualificationScenario.Smoke,
			"compiler" => LiveQualificationScenario.Compiler,
			"compiler-extended" => LiveQualificationScenario.CompilerExtended,
			"compiler-injection" => LiveQualificationScenario.CompilerInjection,
			"lifecycle" => LiveQualificationScenario.Lifecycle,
			"workflows" => LiveQualificationScenario.Workflows,
			"assembler" => LiveQualificationScenario.Assembler,
			"debugger" => LiveQualificationScenario.Debugger,
			"execution" => LiveQualificationScenario.Execution,
			"symbol-fixture" => LiveQualificationScenario.SymbolFixture,
			"performance" => LiveQualificationScenario.Performance,
			"soak" => LiveQualificationScenario.Soak,
			"dispatch-diagnostic" => LiveQualificationScenario.DispatchDiagnostic,
			_ => null
		};
		if (selectedScenario is null)
		{
			return Refuse($"{ScenarioVariable} must be smoke, compiler, compiler-extended, compiler-injection, lifecycle, workflows, assembler, debugger, execution, symbol-fixture, performance, soak, or dispatch-diagnostic.");
		}

		LiveQualificationScenario scenario = selectedScenario.Value;
		string? transitionDiagnostic = variables(DebuggerTransitionDiagnosticVariable);
		if (transitionDiagnostic is not null && (transitionDiagnostic != "1" || scenario != LiveQualificationScenario.Debugger))
		{
			return Refuse($"{DebuggerTransitionDiagnosticVariable} accepts only 1 in the debugger scenario.");
		}
		string? dispatchCaseValue = variables(DispatchDiagnosticCaseVariable);
		if (scenario != LiveQualificationScenario.DispatchDiagnostic && dispatchCaseValue is not null)
		{
			return Refuse($"{DispatchDiagnosticCaseVariable} is valid only when {ScenarioVariable} is dispatch-diagnostic.");
		}
		LiveDispatchDiagnosticCase? dispatchCase = dispatchCaseValue switch
		{
			null => null,
			"AobOnly" => LiveDispatchDiagnosticCase.AobOnly,
			"NamedScanThenAob" => LiveDispatchDiagnosticCase.NamedScanThenAob,
			"MemoryNamedScanThenAob" => LiveDispatchDiagnosticCase.MemoryNamedScanThenAob,
			"ResourcePreludeMemoryNamedScanThenAob" => LiveDispatchDiagnosticCase.ResourcePreludeMemoryNamedScanThenAob,
			_ => (LiveDispatchDiagnosticCase?) null
		};
		if (scenario == LiveQualificationScenario.DispatchDiagnostic && dispatchCaseValue is not null && dispatchCase is null)
		{
			return Refuse($"{DispatchDiagnosticCaseVariable} must be AobOnly, NamedScanThenAob, MemoryNamedScanThenAob, or ResourcePreludeMemoryNamedScanThenAob.");
		}
		bool compiler = scenario is LiveQualificationScenario.Compiler or LiveQualificationScenario.CompilerExtended
			or LiveQualificationScenario.CompilerInjection;

		if (compiler &&
			variables(CodeExecutionOptInVariable) != CodeExecutionAcknowledgement)
		{
			return Refuse($"{CodeExecutionOptInVariable} must contain the exact acknowledgement {CodeExecutionAcknowledgement}.");
		}
		if (scenario == LiveQualificationScenario.CompilerInjection &&
			variables(InjectionOptInVariable) != InjectionAcknowledgement)
		{
			return Refuse($"{InjectionOptInVariable} must contain the exact acknowledgement {InjectionAcknowledgement}.");
		}
		if (scenario == LiveQualificationScenario.Assembler &&
			variables(AssemblerOptInVariable) != AssemblerAcknowledgement)
		{
			return Refuse($"{AssemblerOptInVariable} must contain the exact acknowledgement {AssemblerAcknowledgement}.");
		}
		if (scenario == LiveQualificationScenario.Debugger &&
			variables(DebuggerOptInVariable) != DebuggerAcknowledgement)
		{
			return Refuse($"{DebuggerOptInVariable} must contain the exact acknowledgement {DebuggerAcknowledgement}.");
		}
		if (scenario == LiveQualificationScenario.Execution &&
			variables(ExecutionOptInVariable) != ExecutionAcknowledgement)
		{
			return Refuse($"{ExecutionOptInVariable} must contain the exact acknowledgement {ExecutionAcknowledgement}.");
		}
		if (scenario == LiveQualificationScenario.SymbolFixture &&
			variables(SymbolFixtureOptInVariable) != SymbolFixtureAcknowledgement)
		{
			return Refuse($"{SymbolFixtureOptInVariable} must contain the exact acknowledgement {SymbolFixtureAcknowledgement}.");
		}
		if (compiler && compilerTempSettingsAreSafe is not null &&
			!compilerTempSettingsAreSafe())
		{
			return Refuse("Cheat Engine's Don't use tempdir setting is enabled; compiler qualification requires the owned TEMP directory.");
		}

		string architecture = variables(TargetArchitectureVariable) ?? "x64";
		if (architecture is not ("x64" or "x86"))
		{
			return Refuse($"{TargetArchitectureVariable} must be x64 or x86.");
		}
		if ((scenario is LiveQualificationScenario.Execution or LiveQualificationScenario.SymbolFixture or LiveQualificationScenario.Debugger) && architecture != "x64")
		{
			return Refuse("The fixed execution qualification supports only the owned x64 target.");
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
		return new LiveQualificationDecision(
			new LiveQualificationInputs(Path.GetFullPath(repositoryRoot), Path.GetFullPath(source),
				Path.GetFullPath(runRoot), architecture, scenario)
			{
				DispatchDiagnosticCase = dispatchCase,
				DebuggerTransitionDiagnostic = transitionDiagnostic == "1"
			}, null);
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
