namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Fixed, test-owned activation settings selected before a private CE copy starts.</summary>
internal sealed record LiveSandboxOptions(bool CompilerQualification, bool EnableTargetCodeExecution)
{
	internal static LiveSandboxOptions Smoke { get; } = new(false, false);

	internal bool RequiresCapabilityConfiguration => CompilerQualification || LifecycleQualification
		|| PerformanceQualification || SoakQualification || DispatchDiagnostic;

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
		bool valid = Enum.IsDefined(scenario) && Enum.IsDefined(Variant) && Enum.IsDefined(TempTopology)
			&& CompilerQualification == compiler
			&& EnableManagedInjection == injection
			&& (!EnableTargetCodeExecution || compiler)
			&& (!injection || EnableTargetCodeExecution)
			&& LifecycleQualification == (scenario == LiveQualificationScenario.Lifecycle)
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
