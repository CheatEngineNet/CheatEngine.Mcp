using System.Text.RegularExpressions;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Matches the Client's explicit, fail-fast workstation opt-in for native CE sessions.</summary>
internal static class LiveQualificationOptIn
{
	internal const string OptInVariable = "CHEATENGINE_MCP_LIVE_QUALIFICATION";
	internal const string Acknowledgement = "I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET";
	internal const string CheatEngineDirectoryVariable = "CHEATENGINE_MCP_LIVE_QUALIFICATION_CE_DIRECTORY";
	internal const string RunRootVariable = "CHEATENGINE_MCP_LIVE_QUALIFICATION_RUN_ROOT";
	internal const string FrozenInputManifestPathVariable = "CHEATENGINE_MCP_LIVE_QUALIFICATION_FROZEN_INPUT_MANIFEST";
	internal const string FrozenInputManifestSha256Variable = "CHEATENGINE_MCP_LIVE_QUALIFICATION_FROZEN_INPUT_MANIFEST_SHA256";
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
	internal const string SymbolSourcesOptInVariable = "CHEATENGINE_MCP_LIVE_SYMBOL_SOURCES_QUALIFICATION";
	internal const string SymbolSourcesAcknowledgement =
		"I_AUTHORIZE_ISOLATED_WINDOWS_SYMBOL_SOURCE_NETWORK_PROBES_ON_PRIVATE_CE";
	internal const string WfpIsolationOptInVariable = "CHEATENGINE_MCP_LIVE_WFP_ISOLATION_QUALIFICATION";
	internal const string WfpIsolationAcknowledgement =
		"I_AUTHORIZE_DYNAMIC_WFP_ISOLATION_FOR_PRIVATE_CE_SOURCE_QUALIFICATION";
	internal const string InjectionOptInVariable = "CHEATENGINE_MCP_LIVE_MANAGED_INJECTION_QUALIFICATION";
	internal const string InjectionAcknowledgement =
		"I_AUTHORIZE_FIXED_MANAGED_INJECTION_ON_OWNED_FRAMEWORK_TARGETS";
	internal const string AssemblerOptInVariable = "CHEATENGINE_MCP_LIVE_ASSEMBLER_QUALIFICATION";
	internal const string AssemblerAcknowledgement =
		"I_AUTHORIZE_FIXED_LUA_AND_REVERSIBLE_PATCHES_ON_OWNED_ALLOCATIONS";
	internal const string DebuggerOptInVariable = "CHEATENGINE_MCP_LIVE_DEBUGGER_QUALIFICATION";
	internal const string DebuggerAcknowledgement =
		"I_AUTHORIZE_WINDOWS_DEBUGGER_CHECKS_ON_OWNED_DISPOSABLE_TARGETS";
	internal const string KernelStatusOptInVariable = "CHEATENGINE_MCP_LIVE_KERNEL_STATUS_QUALIFICATION";
	internal const string KernelStatusAcknowledgement =
		"I_AUTHORIZE_READ_ONLY_KERNEL_STATUS_ON_PRIVATE_CE";
	internal const string DebuggerTransitionDiagnosticVariable = "CHEATENGINE_MCP_LIVE_DEBUGGER_TRANSITION_DIAGNOSTIC";
	internal const string MonoOptInVariable = "CHEATENGINE_MCP_LIVE_MONO_QUALIFICATION";
	internal const string MonoAcknowledgement = "I_AUTHORIZE_FIXED_MONO_COLLECTOR_AND_METHODS_ON_A_PRIVATE_OWNED_TARGET";

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
			"plugin-lifecycle" => LiveQualificationScenario.PluginLifecycle,
			"plugin-lifecycle-active" => LiveQualificationScenario.PluginLifecycleActive,
			"workflows" => LiveQualificationScenario.Workflows,
			"inspection" => LiveQualificationScenario.Inspection,
			"state-workflows" => LiveQualificationScenario.StateWorkflows,
			"assembler" => LiveQualificationScenario.Assembler,
			"debugger" => LiveQualificationScenario.Debugger,
			"debugger-basic" => LiveQualificationScenario.DebuggerBasic,
			"debugger-events" => LiveQualificationScenario.DebuggerEvents,
			"debugger-execution" => LiveQualificationScenario.DebuggerExecution,
			"kernel-status" => LiveQualificationScenario.KernelStatus,
			"execution" => LiveQualificationScenario.Execution,
			"symbol-fixture" => LiveQualificationScenario.SymbolFixture,
			"symbol-sources" => LiveQualificationScenario.SymbolSources,
			"process-create" => LiveQualificationScenario.ProcessCreate,
			"mono" => LiveQualificationScenario.Mono,
			"performance" => LiveQualificationScenario.Performance,
			"soak" => LiveQualificationScenario.Soak,
			"dispatch-diagnostic" => LiveQualificationScenario.DispatchDiagnostic,
			_ => null
		};
		if (selectedScenario is null)
		{
			return Refuse($"{ScenarioVariable} must be smoke, compiler, compiler-extended, compiler-injection, lifecycle, plugin-lifecycle, plugin-lifecycle-active, workflows, inspection, state-workflows, assembler, debugger, debugger-basic, debugger-events, debugger-execution, kernel-status, execution, symbol-fixture, process-create, mono, performance, soak, or dispatch-diagnostic.");
		}

		LiveQualificationScenario scenario = selectedScenario.Value;
		string? kernelStatusOptIn = variables(KernelStatusOptInVariable);
		if (scenario != LiveQualificationScenario.KernelStatus && kernelStatusOptIn is not null)
		{
			return Refuse($"{KernelStatusOptInVariable} is valid only in the kernel-status scenario.");
		}
		string? wfpIsolationOptIn = variables(WfpIsolationOptInVariable);
		if (scenario != LiveQualificationScenario.SymbolSources && wfpIsolationOptIn is not null)
		{
			return Refuse($"{WfpIsolationOptInVariable} is valid only in the symbol-sources scenario.");
		}
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
		if ((scenario is LiveQualificationScenario.Debugger or LiveQualificationScenario.DebuggerBasic or LiveQualificationScenario.DebuggerEvents or LiveQualificationScenario.DebuggerExecution or LiveQualificationScenario.PluginLifecycleActive) &&
			variables(DebuggerOptInVariable) != DebuggerAcknowledgement)
		{
			return Refuse($"{DebuggerOptInVariable} must contain the exact acknowledgement {DebuggerAcknowledgement}.");
		}
		if (scenario == LiveQualificationScenario.KernelStatus && kernelStatusOptIn != KernelStatusAcknowledgement)
		{
			return Refuse($"{KernelStatusOptInVariable} must contain the exact acknowledgement {KernelStatusAcknowledgement}.");
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
		if (scenario == LiveQualificationScenario.SymbolSources &&
			(variables(SymbolFixtureOptInVariable) != SymbolFixtureAcknowledgement ||
				variables(SymbolSourcesOptInVariable) != SymbolSourcesAcknowledgement ||
				wfpIsolationOptIn != WfpIsolationAcknowledgement))
		{
			return Refuse($"{SymbolFixtureOptInVariable}, {SymbolSourcesOptInVariable}, and {WfpIsolationOptInVariable} must contain their exact acknowledgements.");
		}
		if ((compiler || scenario == LiveQualificationScenario.SymbolSources) && compilerTempSettingsAreSafe is not null &&
			!compilerTempSettingsAreSafe())
		{
			return Refuse("Cheat Engine's Don't use tempdir setting is enabled; compiler and symbol-source qualification require their owned temporary/cache directories.");
		}
		if (scenario == LiveQualificationScenario.Mono && variables(MonoOptInVariable) != MonoAcknowledgement)
		{
			return Refuse($"{MonoOptInVariable} must contain the exact acknowledgement {MonoAcknowledgement}.");
		}

		string architecture = variables(TargetArchitectureVariable) ?? "x64";
		if (architecture is not ("x64" or "x86"))
		{
			return Refuse($"{TargetArchitectureVariable} must be x64 or x86.");
		}
		if ((scenario is LiveQualificationScenario.Execution or LiveQualificationScenario.SymbolFixture or LiveQualificationScenario.SymbolSources or LiveQualificationScenario.Debugger or LiveQualificationScenario.DebuggerBasic or LiveQualificationScenario.DebuggerEvents or LiveQualificationScenario.DebuggerExecution or LiveQualificationScenario.PluginLifecycleActive or LiveQualificationScenario.Mono) && architecture != "x64")
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
		string? frozenInputManifestPath = variables(FrozenInputManifestPathVariable);
		string? frozenInputManifestSha256 = variables(FrozenInputManifestSha256Variable);
		if ((frozenInputManifestPath is null) != (frozenInputManifestSha256 is null))
		{
			return Refuse($"{FrozenInputManifestPathVariable} and {FrozenInputManifestSha256Variable} must be supplied together.");
		}
		if (frozenInputManifestPath is not null &&
			(!Path.IsPathFullyQualified(frozenInputManifestPath) || !IsSameOrBelow(frozenInputManifestPath, repositoryRoot) ||
				!Regex.IsMatch(frozenInputManifestSha256!, "^[0-9A-Fa-f]{64}$", RegexOptions.CultureInvariant)))
		{
			return Refuse("The frozen input manifest must be an absolute repository path with a SHA-256 digest.");
		}

		return new LiveQualificationDecision(
			new LiveQualificationInputs(Path.GetFullPath(repositoryRoot), Path.GetFullPath(source),
				Path.GetFullPath(runRoot), architecture, scenario)
			{
				DispatchDiagnosticCase = dispatchCase,
				DebuggerTransitionDiagnostic = transitionDiagnostic == "1",
				FrozenInputManifestPath = frozenInputManifestPath is null ? null : Path.GetFullPath(frozenInputManifestPath),
				FrozenInputManifestSha256 = frozenInputManifestSha256,
				WfpIsolationAcknowledgement = scenario == LiveQualificationScenario.SymbolSources
					? WfpIsolationAcknowledgement : null
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
