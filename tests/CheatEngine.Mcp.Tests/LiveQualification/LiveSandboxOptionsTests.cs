namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LiveSandboxOptionsTests
{
	[Fact]
	public void ValidateFor_ExactScenarioOptions_Accept()
	{
		LiveSandboxOptions.Smoke.ValidateFor(LiveQualificationScenario.Smoke);
		new LiveSandboxOptions(true, false).ValidateFor(LiveQualificationScenario.Compiler);
		new LiveSandboxOptions(true, true).ValidateFor(LiveQualificationScenario.Compiler);
		new LiveSandboxOptions(true, true) { Variant = LiveCompilerVariant.PrivateCompilerAbsent }
			.ValidateFor(LiveQualificationScenario.CompilerExtended);
		new LiveSandboxOptions(true, true)
		{
			EnableManagedInjection = true,
			Variant = LiveCompilerVariant.HeldExportAfterB,
			TempTopology = CompilerTempTopology.Shared
		}
			.ValidateFor(LiveQualificationScenario.CompilerInjection);
		(LiveSandboxOptions.Smoke with
		{
			LifecycleQualification = true
		}).ValidateFor(LiveQualificationScenario.Lifecycle);
		(LiveSandboxOptions.Smoke with
		{
			PerformanceQualification = true
		}).ValidateFor(LiveQualificationScenario.Performance);
		(LiveSandboxOptions.Smoke with
		{
			SoakQualification = true,
			UseSoakLifetime = true
		}).ValidateFor(LiveQualificationScenario.Soak);
	}

	[Fact]
	public void ValidateFor_SmokeRejectsEveryExtraCapabilityAndLifetime()
	{
		LiveSandboxOptions[] invalid =
		[
			new(true, false), new(false, true),
			LiveSandboxOptions.Smoke with { Variant = LiveCompilerVariant.PrivateCompilerAbsent },
			LiveSandboxOptions.Smoke with { TempTopology = CompilerTempTopology.Shared },
			LiveSandboxOptions.Smoke with { EnableManagedInjection = true },
			LiveSandboxOptions.Smoke with { LifecycleQualification = true },
			LiveSandboxOptions.Smoke with { PerformanceQualification = true },
			LiveSandboxOptions.Smoke with { SoakQualification = true },
			LiveSandboxOptions.Smoke with { UseSoakLifetime = true }
		];
		foreach (LiveSandboxOptions options in invalid)
		{
			Assert.Throws<InvalidOperationException>(() => options.ValidateFor(LiveQualificationScenario.Smoke));
		}
	}

	[Fact]
	public void ValidateFor_MissingScenarioFlagsAndUnknownValues_Refuse()
	{
		foreach (LiveQualificationScenario scenario in Enum.GetValues<LiveQualificationScenario>()
			.Where(value => value != LiveQualificationScenario.Smoke))
		{
			Assert.Throws<InvalidOperationException>(() => LiveSandboxOptions.Smoke.ValidateFor(scenario));
		}
		Assert.Throws<InvalidOperationException>(() => LiveSandboxOptions.Smoke.ValidateFor((LiveQualificationScenario) 999));
		Assert.Throws<InvalidOperationException>(() => new LiveSandboxOptions(true, true) { Variant = (LiveCompilerVariant) 999 }
			.ValidateFor(LiveQualificationScenario.CompilerExtended));
		Assert.Throws<InvalidOperationException>(() => new LiveSandboxOptions(true, true) { TempTopology = (CompilerTempTopology) 999 }
			.ValidateFor(LiveQualificationScenario.CompilerExtended));
	}

	[Fact]
	public void ValidateFor_SoakRequiresItsLifetimeAndProhibitsExecution()
	{
		LiveSandboxOptions soak = LiveSandboxOptions.Smoke with
		{
			SoakQualification = true,
			UseSoakLifetime = true
		};
		Assert.Throws<InvalidOperationException>(() => (soak with { UseSoakLifetime = false }).ValidateFor(LiveQualificationScenario.Soak));
		Assert.Throws<InvalidOperationException>(() => (soak with { EnableTargetCodeExecution = true }).ValidateFor(LiveQualificationScenario.Soak));
		Assert.Throws<InvalidOperationException>(() => (soak with { LifecycleQualification = true }).ValidateFor(LiveQualificationScenario.Soak));
	}

	[Fact]
	public void ValidateFor_ExtendedCompilerRequiresTheVariantTopology()
	{
		LiveSandboxOptions held = new(true, true)
		{
			Variant = LiveCompilerVariant.HeldExportAfterB,
			TempTopology = CompilerTempTopology.Shared
		};
		held.ValidateFor(LiveQualificationScenario.CompilerExtended);
		Assert.Throws<InvalidOperationException>(() => (held with
		{
			TempTopology = CompilerTempTopology.Isolated
		})
			.ValidateFor(LiveQualificationScenario.CompilerExtended));
		Assert.Throws<InvalidOperationException>(() => (held with
		{
			Variant = LiveCompilerVariant.PrivateCompilerAbsent
		})
			.ValidateFor(LiveQualificationScenario.CompilerExtended));
	}

	[Fact]
	public void ValidateFor_OnlyPerformanceAcceptsAnAbsoluteDistributionOverride()
	{
		string distribution = Path.Combine(Path.GetTempPath(), "reviewed-qualification-package");
		LiveSandboxOptions performance = LiveSandboxOptions.Smoke with
		{
			PerformanceQualification = true,
			DistributionDirectoryOverride = distribution
		};
		performance.ValidateFor(LiveQualificationScenario.Performance);
		Assert.Throws<InvalidOperationException>(() => (performance with
		{
			DistributionDirectoryOverride = "relative"
		})
			.ValidateFor(LiveQualificationScenario.Performance));
		Assert.Throws<InvalidOperationException>(() => new LiveSandboxOptions(true, true)
		{
			DistributionDirectoryOverride = distribution
		}.ValidateFor(LiveQualificationScenario.Compiler));
	}

	[Fact]
	public void ValidateFor_InjectionRequiresBothExecutionFlagsAndReviewedHeldTopology()
	{
		LiveSandboxOptions injection = new(true, true)
		{
			EnableManagedInjection = true,
			Variant = LiveCompilerVariant.HeldExportAfterB,
			TempTopology = CompilerTempTopology.Shared
		};
		Assert.Throws<InvalidOperationException>(() => (injection with { EnableTargetCodeExecution = false }).ValidateFor(LiveQualificationScenario.CompilerInjection));
		Assert.Throws<InvalidOperationException>(() => (injection with { EnableManagedInjection = false }).ValidateFor(LiveQualificationScenario.CompilerInjection));
		Assert.Throws<InvalidOperationException>(() => (injection with { TempTopology = CompilerTempTopology.Isolated }).ValidateFor(LiveQualificationScenario.CompilerInjection));
		Assert.Throws<InvalidOperationException>(() => (injection with { Variant = LiveCompilerVariant.Normal }).ValidateFor(LiveQualificationScenario.CompilerInjection));
		Assert.Throws<InvalidOperationException>(() => injection.ValidateFor(LiveQualificationScenario.Compiler));
	}
}
