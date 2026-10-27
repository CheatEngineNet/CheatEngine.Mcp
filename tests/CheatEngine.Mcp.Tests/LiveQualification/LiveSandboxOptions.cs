namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Fixed, test-owned activation settings selected before a private CE copy starts.</summary>
internal sealed record LiveSandboxOptions(bool CompilerQualification, bool EnableTargetCodeExecution)
{
	internal static LiveSandboxOptions Smoke { get; } = new(false, false);

	internal bool RequiresCapabilityConfiguration => CompilerQualification || LifecycleQualification
		|| PerformanceQualification || SoakQualification || DispatchDiagnostic || WorkflowQualification || InspectionQualification || StateWorkflowsQualification || AssemblerQualification
		|| DebuggerQualification || DebuggerEventsQualification || ExecutionQualification || SymbolFixtureQualification || ProcessCreateQualification || MonoQualification
		|| KernelStatusQualification || ActivePluginLifecycleQualification || SymbolSourcesQualification;

	internal LiveCompilerVariant Variant { get; init; } = LiveCompilerVariant.Normal;
	internal CompilerTempTopology TempTopology { get; init; } = CompilerTempTopology.Isolated;
	internal bool EnableManagedInjection
	{
		get; init;
	}
	internal bool LifecycleQualification
	{
		get; init;
	}
	internal bool SeedPluginChecklistAtStartup
	{
		get; init;
	}
	internal bool ActivePluginLifecycleQualification
	{
		get; init;
	}
	internal bool WorkflowQualification
	{
		get; init;
	}
	internal bool InspectionQualification
	{
		get; init;
	}
	internal bool StateWorkflowsQualification
	{
		get; init;
	}
	internal bool AssemblerQualification
	{
		get; init;
	}
	internal bool DebuggerQualification
	{
		get; init;
	}
	internal bool DebuggerEventsQualification
	{
		get; init;
	}
	internal bool DebuggerTransitionDiagnostic
	{
		get; init;
	}
	internal bool PerformanceQualification
	{
		get; init;
	}
	internal bool SoakQualification
	{
		get; init;
	}
	internal bool UseSoakLifetime
	{
		get; init;
	}
	internal bool DispatchDiagnostic
	{
		get; init;
	}
	internal bool ExecutionQualification
	{
		get; init;
	}
	internal bool SymbolFixtureQualification
	{
		get; init;
	}
	internal bool SymbolSourcesQualification
	{
		get; init;
	}
	internal bool ProcessCreateQualification
	{
		get; init;
	}
	internal bool MonoQualification
	{
		get; init;
	}
	internal bool KernelStatusQualification
	{
		get; init;
	}
	internal bool UsesTargetFirstExecutionShutdown => ExecutionQualification || SymbolFixtureQualification || SymbolSourcesQualification || MonoQualification;
	internal string? DistributionDirectoryOverride
	{
		get; init;
	}

	internal void ValidateFor(LiveQualificationScenario scenario)
	{
		bool compiler = scenario is LiveQualificationScenario.Compiler or LiveQualificationScenario.CompilerExtended
			or LiveQualificationScenario.CompilerInjection;
		bool injection = scenario == LiveQualificationScenario.CompilerInjection;
		bool extended = scenario == LiveQualificationScenario.CompilerExtended;
		bool startupSeed = scenario is LiveQualificationScenario.PluginLifecycle or LiveQualificationScenario.PluginLifecycleActive
			|| (extended && Variant == LiveCompilerVariant.HeldExportAfterB);
		bool execution = scenario == LiveQualificationScenario.Execution;
		bool symbolFixture = scenario == LiveQualificationScenario.SymbolFixture;
		bool symbolSources = scenario == LiveQualificationScenario.SymbolSources;
		bool processCreate = scenario == LiveQualificationScenario.ProcessCreate;
		bool mono = scenario == LiveQualificationScenario.Mono;
		bool activePluginLifecycle = scenario == LiveQualificationScenario.PluginLifecycleActive;
		bool debugger = scenario is LiveQualificationScenario.Debugger or LiveQualificationScenario.DebuggerBasic or LiveQualificationScenario.DebuggerEvents or LiveQualificationScenario.DebuggerExecution or LiveQualificationScenario.PluginLifecycleActive;
		bool debuggerEvents = scenario == LiveQualificationScenario.DebuggerEvents;
		bool kernelStatus = scenario == LiveQualificationScenario.KernelStatus;
		bool valid = Enum.IsDefined(scenario) && Enum.IsDefined(Variant) && Enum.IsDefined(TempTopology)
			&& CompilerQualification == compiler
			&& EnableManagedInjection == injection
			&& (!EnableTargetCodeExecution || compiler || execution || symbolFixture || symbolSources || mono)
			&& ExecutionQualification == execution
			&& SymbolFixtureQualification == symbolFixture
			&& SymbolSourcesQualification == symbolSources
			&& ProcessCreateQualification == processCreate
			&& MonoQualification == mono
			&& KernelStatusQualification == kernelStatus
			&& (!mono || (EnableTargetCodeExecution && !CompilerQualification && !EnableManagedInjection))
			&& (!execution || EnableTargetCodeExecution)
			&& (!execution || (!CompilerQualification && !EnableManagedInjection))
			&& (!symbolFixture || (EnableTargetCodeExecution && !CompilerQualification && !EnableManagedInjection))
			&& (!symbolSources || (EnableTargetCodeExecution && !CompilerQualification && !EnableManagedInjection && !SymbolFixtureQualification))
			&& (!injection || EnableTargetCodeExecution)
			&& LifecycleQualification == (scenario is LiveQualificationScenario.Lifecycle or LiveQualificationScenario.PluginLifecycle or LiveQualificationScenario.PluginLifecycleActive)
			&& SeedPluginChecklistAtStartup == startupSeed
			&& ActivePluginLifecycleQualification == activePluginLifecycle
			&& WorkflowQualification == (scenario == LiveQualificationScenario.Workflows)
			&& InspectionQualification == (scenario == LiveQualificationScenario.Inspection)
			&& StateWorkflowsQualification == (scenario == LiveQualificationScenario.StateWorkflows)
			&& AssemblerQualification == (scenario == LiveQualificationScenario.Assembler)
			&& DebuggerQualification == debugger
			&& DebuggerEventsQualification == debuggerEvents
			&& (!DebuggerTransitionDiagnostic || scenario == LiveQualificationScenario.Debugger)
			&& PerformanceQualification == (scenario == LiveQualificationScenario.Performance)
			&& SoakQualification == (scenario == LiveQualificationScenario.Soak)
			&& DispatchDiagnostic == (scenario == LiveQualificationScenario.DispatchDiagnostic)
			&& UseSoakLifetime == SoakQualification
			&& (extended || injection || (Variant == LiveCompilerVariant.Normal && TempTopology == CompilerTempTopology.Isolated))
			&& (!injection || (Variant == LiveCompilerVariant.HeldExportAfterB && TempTopology == CompilerTempTopology.Shared))
			&& (Variant != LiveCompilerVariant.HeldExportAfterB || TempTopology == CompilerTempTopology.Shared)
			&& (Variant != LiveCompilerVariant.PrivateCompilerAbsent || TempTopology == CompilerTempTopology.Isolated)
			&& (!extended || EnableTargetCodeExecution)
			&& (DistributionDirectoryOverride is null || (scenario == LiveQualificationScenario.Performance
				&& Path.IsPathFullyQualified(DistributionDirectoryOverride)));
		if (!valid)
		{
			throw new InvalidOperationException("Live sandbox options do not match the explicitly admitted scenario.");
		}
	}
}

/// <summary>Per-host compiler files, all rooted below one live run.</summary>
internal sealed record LiveCompilerRoots(string Temp, string References, string Output);
