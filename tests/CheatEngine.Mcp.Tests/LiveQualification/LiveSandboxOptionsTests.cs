namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LiveSandboxOptionsTests
{
	[Fact]
	public void ValidateFor_StateWorkflowsRequiresItsDedicatedDisabledCapabilityConfiguration()
	{
		LiveSandboxOptions state = LiveSandboxOptions.Smoke with
		{
			StateWorkflowsQualification = true
		};
		state.ValidateFor(LiveQualificationScenario.StateWorkflows);
		Assert.True(state.RequiresCapabilityConfiguration);
		Assert.False(state.WorkflowQualification);
		Assert.False(state.UsesTargetFirstExecutionShutdown);
		Assert.Throws<InvalidOperationException>(() => state.ValidateFor(LiveQualificationScenario.Workflows));
		Assert.Throws<InvalidOperationException>(() => state.ValidateFor(LiveQualificationScenario.Inspection));
		Assert.Throws<InvalidOperationException>(() => (state with { WorkflowQualification = true }).ValidateFor(LiveQualificationScenario.StateWorkflows));
		Assert.Throws<InvalidOperationException>(() => (state with { InspectionQualification = true }).ValidateFor(LiveQualificationScenario.StateWorkflows));
		Assert.Throws<InvalidOperationException>(() => (state with { EnableTargetCodeExecution = true }).ValidateFor(LiveQualificationScenario.StateWorkflows));
		Assert.Throws<InvalidOperationException>(() => (state with { AssemblerQualification = true }).ValidateFor(LiveQualificationScenario.StateWorkflows));
		Assert.Throws<InvalidOperationException>(() => (state with { DebuggerQualification = true }).ValidateFor(LiveQualificationScenario.StateWorkflows));
	}

	[Fact]
	public void ValidateFor_InspectionRequiresItsDedicatedDisabledCapabilityConfiguration()
	{
		LiveSandboxOptions inspection = LiveSandboxOptions.Smoke with
		{
			InspectionQualification = true
		};
		inspection.ValidateFor(LiveQualificationScenario.Inspection);
		Assert.True(inspection.RequiresCapabilityConfiguration);
		Assert.False(inspection.UsesTargetFirstExecutionShutdown);
		Assert.False(inspection.WorkflowQualification);
		Assert.Throws<InvalidOperationException>(() => inspection.ValidateFor(LiveQualificationScenario.Workflows));
		Assert.Throws<InvalidOperationException>(() => (inspection with { WorkflowQualification = true }).ValidateFor(LiveQualificationScenario.Inspection));
		Assert.Throws<InvalidOperationException>(() => LiveSandboxOptions.Smoke.ValidateFor(LiveQualificationScenario.Inspection));
		Assert.Throws<InvalidOperationException>(() => (inspection with { EnableTargetCodeExecution = true }).ValidateFor(LiveQualificationScenario.Inspection));
		Assert.Throws<InvalidOperationException>(() => (inspection with { AssemblerQualification = true }).ValidateFor(LiveQualificationScenario.Inspection));
		Assert.Throws<InvalidOperationException>(() => (inspection with { DebuggerQualification = true }).ValidateFor(LiveQualificationScenario.Inspection));
		Assert.Throws<InvalidOperationException>(() => (inspection with { LifecycleQualification = true }).ValidateFor(LiveQualificationScenario.Inspection));
	}

	[Fact]
	public void ValidateFor_MonoRequiresOnlyItsOwnedTargetExecutionConfiguration()
	{
		LiveSandboxOptions mono = LiveSandboxOptions.Smoke with
		{
			MonoQualification = true,
			EnableTargetCodeExecution = true
		};
		mono.ValidateFor(LiveQualificationScenario.Mono);
		Assert.True(mono.RequiresCapabilityConfiguration);
		Assert.True(mono.UsesTargetFirstExecutionShutdown);
		Assert.Throws<InvalidOperationException>(() => (mono with { EnableTargetCodeExecution = false }).ValidateFor(LiveQualificationScenario.Mono));
		Assert.Throws<InvalidOperationException>(() => (mono with { CompilerQualification = true }).ValidateFor(LiveQualificationScenario.Mono));
		Assert.Throws<InvalidOperationException>(() => (mono with { EnableManagedInjection = true }).ValidateFor(LiveQualificationScenario.Mono));
		Assert.Throws<InvalidOperationException>(() => (mono with { AssemblerQualification = true }).ValidateFor(LiveQualificationScenario.Mono));
		Assert.Throws<InvalidOperationException>(() => (mono with { DebuggerQualification = true }).ValidateFor(LiveQualificationScenario.Mono));
		Assert.Throws<InvalidOperationException>(() => (mono with { ProcessCreateQualification = true }).ValidateFor(LiveQualificationScenario.Mono));
		Assert.Throws<InvalidOperationException>(() => (mono with { TempTopology = CompilerTempTopology.Shared }).ValidateFor(LiveQualificationScenario.Mono));
		Assert.Throws<InvalidOperationException>(() => mono.ValidateFor(LiveQualificationScenario.Smoke));
	}
	[Fact]
	public void ValidateFor_SymbolFixtureRequiresOnlyItsIsolatedExecutionConfiguration()
	{
		LiveSandboxOptions symbols = LiveSandboxOptions.Smoke with
		{
			SymbolFixtureQualification = true,
			EnableTargetCodeExecution = true
		};
		symbols.ValidateFor(LiveQualificationScenario.SymbolFixture);
		Assert.True(symbols.RequiresCapabilityConfiguration);
		Assert.True(symbols.UsesTargetFirstExecutionShutdown);
		Assert.Throws<InvalidOperationException>(() => (symbols with { EnableTargetCodeExecution = false }).ValidateFor(LiveQualificationScenario.SymbolFixture));
		Assert.Throws<InvalidOperationException>(() => (symbols with { CompilerQualification = true }).ValidateFor(LiveQualificationScenario.SymbolFixture));
		Assert.Throws<InvalidOperationException>(() => (symbols with { EnableManagedInjection = true }).ValidateFor(LiveQualificationScenario.SymbolFixture));
		Assert.Throws<InvalidOperationException>(() => (symbols with { AssemblerQualification = true }).ValidateFor(LiveQualificationScenario.SymbolFixture));
		Assert.Throws<InvalidOperationException>(() => (symbols with { DebuggerQualification = true }).ValidateFor(LiveQualificationScenario.SymbolFixture));
		Assert.Throws<InvalidOperationException>(() => (symbols with { TempTopology = CompilerTempTopology.Shared }).ValidateFor(LiveQualificationScenario.SymbolFixture));
	}

	[Fact]
	public void ValidateFor_SymbolSourcesRequiresItsOwnIsolatedExecutionConfiguration()
	{
		LiveSandboxOptions sources = LiveSandboxOptions.Smoke with
		{
			SymbolSourcesQualification = true,
			EnableTargetCodeExecution = true
		};
		sources.ValidateFor(LiveQualificationScenario.SymbolSources);
		Assert.True(sources.RequiresCapabilityConfiguration);
		Assert.True(sources.UsesTargetFirstExecutionShutdown);
		Assert.Throws<InvalidOperationException>(() => (sources with { EnableTargetCodeExecution = false }).ValidateFor(LiveQualificationScenario.SymbolSources));
		Assert.Throws<InvalidOperationException>(() => (sources with { SymbolFixtureQualification = true }).ValidateFor(LiveQualificationScenario.SymbolSources));
	}

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
			LifecycleQualification = true,
			SeedPluginChecklistAtStartup = true
		}).ValidateFor(LiveQualificationScenario.PluginLifecycle);
		(LiveSandboxOptions.Smoke with
		{
			LifecycleQualification = true,
			SeedPluginChecklistAtStartup = true,
			ActivePluginLifecycleQualification = true,
			DebuggerQualification = true
		}).ValidateFor(LiveQualificationScenario.PluginLifecycleActive);
		Assert.Throws<InvalidOperationException>(() => (LiveSandboxOptions.Smoke with
		{
			LifecycleQualification = true
		}).ValidateFor(LiveQualificationScenario.PluginLifecycle));
		Assert.Throws<InvalidOperationException>(() => (LiveSandboxOptions.Smoke with
		{
			LifecycleQualification = true,
			SeedPluginChecklistAtStartup = true
		}).ValidateFor(LiveQualificationScenario.Lifecycle));
		Assert.Throws<InvalidOperationException>(() => LiveSandboxOptions.Smoke.ValidateFor(LiveQualificationScenario.PluginLifecycle));
		Assert.Throws<InvalidOperationException>(() => (LiveSandboxOptions.Smoke with
		{
			LifecycleQualification = true,
			WorkflowQualification = true
		}).ValidateFor(LiveQualificationScenario.PluginLifecycle));
		Assert.Throws<InvalidOperationException>(() => (LiveSandboxOptions.Smoke with
		{
			LifecycleQualification = true,
			SeedPluginChecklistAtStartup = true,
			ActivePluginLifecycleQualification = true
		}).ValidateFor(LiveQualificationScenario.PluginLifecycleActive));
		Assert.Throws<InvalidOperationException>(() => (LiveSandboxOptions.Smoke with
		{
			LifecycleQualification = true,
			DebuggerQualification = true,
			ActivePluginLifecycleQualification = true
		}).ValidateFor(LiveQualificationScenario.PluginLifecycleActive));
		Assert.Throws<InvalidOperationException>(() => (LiveSandboxOptions.Smoke with
		{
			LifecycleQualification = true,
			SeedPluginChecklistAtStartup = true,
			DebuggerQualification = true
		}).ValidateFor(LiveQualificationScenario.PluginLifecycleActive));
		(LiveSandboxOptions.Smoke with
		{
			WorkflowQualification = true
		}).ValidateFor(LiveQualificationScenario.Workflows);
		(LiveSandboxOptions.Smoke with
		{
			ProcessCreateQualification = true
		}).ValidateFor(LiveQualificationScenario.ProcessCreate);
		(LiveSandboxOptions.Smoke with
		{
			PerformanceQualification = true
		}).ValidateFor(LiveQualificationScenario.Performance);
		(LiveSandboxOptions.Smoke with
		{
			SoakQualification = true,
			UseSoakLifetime = true
		}).ValidateFor(LiveQualificationScenario.Soak);
		(LiveSandboxOptions.Smoke with
		{
			DispatchDiagnostic = true
		}).ValidateFor(LiveQualificationScenario.DispatchDiagnostic);
		(LiveSandboxOptions.Smoke with
		{
			AssemblerQualification = true
		}).ValidateFor(LiveQualificationScenario.Assembler);
		(LiveSandboxOptions.Smoke with
		{
			DebuggerQualification = true
		}).ValidateFor(LiveQualificationScenario.Debugger);
		(LiveSandboxOptions.Smoke with
		{
			DebuggerQualification = true
		}).ValidateFor(LiveQualificationScenario.DebuggerBasic);
		(LiveSandboxOptions.Smoke with
		{
			DebuggerQualification = true,
			DebuggerEventsQualification = true
		}).ValidateFor(LiveQualificationScenario.DebuggerEvents);
		(LiveSandboxOptions.Smoke with
		{
			DebuggerQualification = true
		}).ValidateFor(LiveQualificationScenario.DebuggerExecution);
		(LiveSandboxOptions.Smoke with
		{
			KernelStatusQualification = true
		}).ValidateFor(LiveQualificationScenario.KernelStatus);
		(LiveSandboxOptions.Smoke with
		{
			SymbolFixtureQualification = true,
			EnableTargetCodeExecution = true
		})
			.ValidateFor(LiveQualificationScenario.SymbolFixture);
	}

	[Fact]
	public void ValidateFor_KernelStatusAdmitsOnlyItsReadOnlyKernelGate()
	{
		LiveSandboxOptions status = LiveSandboxOptions.Smoke with
		{
			KernelStatusQualification = true
		};
		status.ValidateFor(LiveQualificationScenario.KernelStatus);
		Assert.True(status.RequiresCapabilityConfiguration);
		Assert.False(status.EnableTargetCodeExecution);
		Assert.Throws<InvalidOperationException>(() => status.ValidateFor(LiveQualificationScenario.Smoke));
		Assert.Throws<InvalidOperationException>(() => (status with { EnableTargetCodeExecution = true }).ValidateFor(LiveQualificationScenario.KernelStatus));
		Assert.Throws<InvalidOperationException>(() => (status with { AssemblerQualification = true }).ValidateFor(LiveQualificationScenario.KernelStatus));
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
			LiveSandboxOptions.Smoke with { WorkflowQualification = true },
			LiveSandboxOptions.Smoke with { PerformanceQualification = true },
			LiveSandboxOptions.Smoke with { SoakQualification = true },
			LiveSandboxOptions.Smoke with { DispatchDiagnostic = true },
			LiveSandboxOptions.Smoke with { AssemblerQualification = true },
			LiveSandboxOptions.Smoke with { DebuggerQualification = true },
			LiveSandboxOptions.Smoke with { DebuggerEventsQualification = true },
			LiveSandboxOptions.Smoke with { DebuggerTransitionDiagnostic = true },
			LiveSandboxOptions.Smoke with { ActivePluginLifecycleQualification = true },
			LiveSandboxOptions.Smoke with { KernelStatusQualification = true },
			LiveSandboxOptions.Smoke with { ProcessCreateQualification = true },
			LiveSandboxOptions.Smoke with { MonoQualification = true },
			LiveSandboxOptions.Smoke with { UseSoakLifetime = true }
		];
		foreach (LiveSandboxOptions options in invalid)
		{
			Assert.Throws<InvalidOperationException>(() => options.ValidateFor(LiveQualificationScenario.Smoke));
		}
	}

	[Fact]
	public void ValidateFor_DebuggerEventsRequiresItsDedicatedDebuggerConfiguration()
	{
		LiveSandboxOptions events = LiveSandboxOptions.Smoke with
		{
			DebuggerQualification = true,
			DebuggerEventsQualification = true
		};
		events.ValidateFor(LiveQualificationScenario.DebuggerEvents);
		Assert.True(events.RequiresCapabilityConfiguration);
		Assert.False(events.EnableTargetCodeExecution);
		Assert.Throws<InvalidOperationException>(() => (events with { DebuggerEventsQualification = false }).ValidateFor(LiveQualificationScenario.DebuggerEvents));
		Assert.Throws<InvalidOperationException>(() => (events with { DebuggerQualification = false }).ValidateFor(LiveQualificationScenario.DebuggerEvents));
		Assert.Throws<InvalidOperationException>(() => (events with { DebuggerTransitionDiagnostic = true }).ValidateFor(LiveQualificationScenario.DebuggerEvents));
		Assert.Throws<InvalidOperationException>(() => (events with { EnableTargetCodeExecution = true }).ValidateFor(LiveQualificationScenario.DebuggerEvents));
		Assert.Throws<InvalidOperationException>(() => events.ValidateFor(LiveQualificationScenario.Debugger));
		LiveSandboxOptions execution = LiveSandboxOptions.Smoke with
		{
			DebuggerQualification = true
		};
		execution.ValidateFor(LiveQualificationScenario.DebuggerExecution);
		Assert.True(execution.RequiresCapabilityConfiguration);
		Assert.False(execution.DebuggerEventsQualification);
		Assert.Throws<InvalidOperationException>(() => (execution with { DebuggerQualification = false }).ValidateFor(LiveQualificationScenario.DebuggerExecution));
		Assert.Throws<InvalidOperationException>(() => (execution with { DebuggerEventsQualification = true }).ValidateFor(LiveQualificationScenario.DebuggerExecution));
		Assert.Throws<InvalidOperationException>(() => execution.ValidateFor(LiveQualificationScenario.DebuggerEvents));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void ValidateFor_SpecializedScenariosRequireExactFlagsAndDisabledTargetExecution(bool assembler)
	{
		LiveQualificationScenario scenario = assembler ? LiveQualificationScenario.Assembler : LiveQualificationScenario.Debugger;
		LiveSandboxOptions options = LiveSandboxOptions.Smoke with
		{
			AssemblerQualification = assembler,
			DebuggerQualification = !assembler
		};
		Assert.True(options.RequiresCapabilityConfiguration);
		options.ValidateFor(scenario);
		Assert.Throws<InvalidOperationException>(() => (options with { EnableTargetCodeExecution = true }).ValidateFor(scenario));
		Assert.Throws<InvalidOperationException>(() => (options with { CompilerQualification = true }).ValidateFor(scenario));
		Assert.Throws<InvalidOperationException>(() => (options with { WorkflowQualification = true }).ValidateFor(scenario));
		Assert.Throws<InvalidOperationException>(() => (options with { AssemblerQualification = true, DebuggerQualification = true }).ValidateFor(scenario));
		Assert.Throws<InvalidOperationException>(() => options.ValidateFor(LiveQualificationScenario.Smoke));
	}

	[Fact]
	public void ValidateFor_TransitionDiagnostic_RequiresDebuggerWithDisabledProductGates()
	{
		LiveSandboxOptions diagnostic = LiveSandboxOptions.Smoke with
		{
			DebuggerQualification = true,
			DebuggerTransitionDiagnostic = true
		};
		diagnostic.ValidateFor(LiveQualificationScenario.Debugger);
		Assert.Throws<InvalidOperationException>(() => (diagnostic with
		{
			DebuggerQualification = false
		})
			.ValidateFor(LiveQualificationScenario.Smoke));
		Assert.Throws<InvalidOperationException>(() => (diagnostic with
		{
			EnableTargetCodeExecution = true
		})
			.ValidateFor(LiveQualificationScenario.Debugger));
	}

	[Fact]
	public void ValidateFor_WorkflowsRequireDisabledExecutionAndDedicatedConfiguration()
	{
		LiveSandboxOptions workflows = LiveSandboxOptions.Smoke with
		{
			WorkflowQualification = true
		};
		Assert.True(workflows.RequiresCapabilityConfiguration);
		Assert.False(workflows.EnableTargetCodeExecution);
		workflows.ValidateFor(LiveQualificationScenario.Workflows);
		Assert.Throws<InvalidOperationException>(() => (workflows with { EnableTargetCodeExecution = true }).ValidateFor(LiveQualificationScenario.Workflows));
		Assert.Throws<InvalidOperationException>(() => (workflows with { LifecycleQualification = true }).ValidateFor(LiveQualificationScenario.Workflows));
	}

	[Fact]
	public void ValidateFor_ProcessCreateRequiresItsDedicatedAllGatesDisabledConfiguration()
	{
		LiveSandboxOptions processCreate = LiveSandboxOptions.Smoke with
		{
			ProcessCreateQualification = true
		};
		processCreate.ValidateFor(LiveQualificationScenario.ProcessCreate);
		Assert.True(processCreate.RequiresCapabilityConfiguration);
		Assert.False(processCreate.EnableTargetCodeExecution);
		Assert.False(processCreate.AssemblerQualification);
		Assert.False(processCreate.CompilerQualification);
		Assert.False(processCreate.EnableManagedInjection);
		Assert.Throws<InvalidOperationException>(() => (processCreate with { EnableTargetCodeExecution = true }).ValidateFor(LiveQualificationScenario.ProcessCreate));
		Assert.Throws<InvalidOperationException>(() => (processCreate with { AssemblerQualification = true }).ValidateFor(LiveQualificationScenario.ProcessCreate));
		Assert.Throws<InvalidOperationException>(() => (processCreate with { CompilerQualification = true }).ValidateFor(LiveQualificationScenario.ProcessCreate));
		Assert.Throws<InvalidOperationException>(() => (processCreate with { EnableManagedInjection = true }).ValidateFor(LiveQualificationScenario.ProcessCreate));
		Assert.Throws<InvalidOperationException>(() => (processCreate with { WorkflowQualification = true }).ValidateFor(LiveQualificationScenario.ProcessCreate));
	}

	[Fact]
	public void ValidateFor_DispatchDiagnosticRejectsExecutionAndUnrelatedScenarios()
	{
		LiveSandboxOptions diagnostic = LiveSandboxOptions.Smoke with
		{
			DispatchDiagnostic = true
		};
		LiveSandboxOptions[] invalid =
		[
			diagnostic with { EnableTargetCodeExecution = true },
			diagnostic with { EnableManagedInjection = true },
			diagnostic with { CompilerQualification = true },
			diagnostic with { LifecycleQualification = true },
			diagnostic with { PerformanceQualification = true },
			diagnostic with { SoakQualification = true, UseSoakLifetime = true },
			diagnostic with { DistributionDirectoryOverride = Path.GetTempPath() }
		];
		foreach (LiveSandboxOptions options in invalid)
		{
			Assert.Throws<InvalidOperationException>(() => options.ValidateFor(LiveQualificationScenario.DispatchDiagnostic));
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
			TempTopology = CompilerTempTopology.Shared,
			SeedPluginChecklistAtStartup = true
		};
		held.ValidateFor(LiveQualificationScenario.CompilerExtended);
		new LiveSandboxOptions(true, true)
		{
			Variant = LiveCompilerVariant.PrivateCompilerAbsent
		}
			.ValidateFor(LiveQualificationScenario.CompilerExtended);
		Assert.Throws<InvalidOperationException>(() => (held with
		{
			SeedPluginChecklistAtStartup = false
		})
			.ValidateFor(LiveQualificationScenario.CompilerExtended));
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
