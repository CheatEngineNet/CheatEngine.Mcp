using CheatEngine.Mcp.Core.Features;

using static CheatEngine.Mcp.Prompts.Workflows.WorkflowValues;

namespace CheatEngine.Mcp.Prompts.Workflows;

/// <summary>The workflows that start, plan, explain, report and end a session.</summary>
internal static class SessionWorkflows
{
	internal static readonly WorkflowDefinition AttachAndOrient =
		new("attach_and_orient", "attach-and-orient", ["safety", "mono-and-dotnet"])
		{
			Arguments = [new WorkflowArgument("process"), new WorkflowArgument("goal", WorkflowInputKind.Prose)]
		};

	internal static readonly WorkflowDefinition EngineTriage =
		new("engine_triage", "engine-triage", ["game-engines", "mono-and-dotnet", "safety"])
		{
			Arguments = [new WorkflowArgument("goal", WorkflowInputKind.Prose)]
		};

	internal static readonly WorkflowDefinition PlanCheat =
		new("plan_cheat", "plan-cheat", ["workflows", "cheat-recipes", "safety"])
		{
			Arguments =
			[
				new WorkflowArgument("goal", Required: true) { Allowed = CheatGoals },
				new WorkflowArgument("description", WorkflowInputKind.Prose)
			],
			Selectors = ["goal"]
		};

	internal static readonly WorkflowDefinition SessionReport =
		new("session_report", "session-report", ["workflows", "cheat-tables", "errors-and-recovery"])
		{
			Arguments =
			[
				new WorkflowArgument("detail") { Default = "summary", Allowed = ReportDetails },
				new WorkflowArgument("tablePath")
			]
		};

	internal static readonly WorkflowDefinition ExplainError =
		new("explain_error", "explain-error", ["errors-and-recovery", "workflows"])
		{
			Arguments =
			[
				new WorkflowArgument("kind", Required: true) { Allowed = ErrorKinds },
				new WorkflowArgument("hostEffect") { Allowed = HostEffects },
				new WorkflowArgument("tool", WorkflowInputKind.ToolName)
			],
			Selectors = ["kind", "hostEffect"]
		};

	internal static readonly WorkflowDefinition CleanupSession =
		new("cleanup_session", "cleanup-session", ["errors-and-recovery"])
		{
			Arguments = [new WorkflowArgument("keepAddressList") { Default = "yes", Allowed = YesNo }],
			Gates = [McpFeature.TargetCodeExecution]
		};

	internal static readonly WorkflowDefinition CeTutorialWalkthrough =
		new("ce_tutorial_walkthrough", "ce-tutorial-walkthrough", ["ce-tutorial"])
		{
			Arguments =
			[
				new WorkflowArgument("step", Required: true) { Allowed = TutorialSteps },
				new WorkflowArgument("tutorial") { Default = "tutorial", Allowed = Tutorials }
			],
			Selectors = ["step"]
		};
}
