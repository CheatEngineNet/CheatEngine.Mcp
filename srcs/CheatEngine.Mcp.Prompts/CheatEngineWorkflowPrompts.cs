using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Prompts.Workflows;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Prompts;

/// <summary>
///     The guided workflows as prompts, one method per <see cref="CheatEngineWorkflows.All" /> entry, declared in
///     one partial file per theme. Every prompt is static (Local): it takes no <c>instanceId</c>, renders the same
///     text on every backend and on the gateway, and tells the model to pick the instance with <c>instance_list</c>
///     when the tools take one. Its text is the embedded workflow body (also served as
///     <c>cheatengine://docs/workflows/{workflow}</c>), and it links the related knowledge documents. A prompt whose
///     body cites gated tools ends its description with <see cref="WorkflowPrompt.GateLine" />.
/// </summary>
[McpServerPromptType]
public sealed partial class CheatEngineWorkflowPrompts
{
	private const string AddressHelp = "A hex address such as 7FF6A1B2C3D0 or a Cheat Engine expression such as "
									   + "game.exe+1A2B.";

	private const string NeedsAutoAssembler = " Needs Mcp:EnableAutoAssembler for its gated steps.";

	private const string NeedsTargetCodeExecution = " Needs Mcp:EnableTargetCodeExecution for its gated steps.";

	private const string NeedsUnsafeLua = " Needs Mcp:EnableUnsafeLua for its gated steps.";

	/// <summary>Attach to a target and get oriented.</summary>
	[McpServerPrompt(Name = "attach_and_orient", Title = "Attach to a target and get oriented")]
	[Description("Start of a session: check versions and capabilities, attach to the authorized process, classify its "
				 + "runtime (native, Mono, IL2CPP, .NET) and suggest the next workflow.")]
	public static GetPromptResult AttachAndOrientPrompt(
		[Display(Name = "Process")]
		[Description("The target process name or PID; omit it to choose from the process list.")]
		string? process = null,
		[Display(Name = "Goal")]
		[Description("What the user wants to achieve, such as infinite health.")]
		string? goal = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, SessionWorkflows.AttachAndOrient, process, goal);
	}

	/// <summary>Identify the game engine and how it stores values.</summary>
	[McpServerPrompt(Name = "engine_triage", Title = "Identify the game engine and how it stores values")]
	[Description("Right after attaching, before choosing value types: identify the engine or runtime from modules, "
				 + "exports and version strings, and predict how it stores values.")]
	public static GetPromptResult EngineTriagePrompt(
		[Display(Name = "Goal")]
		[Description("What the user wants to find or change, such as the player's gold.")]
		string? goal = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, SessionWorkflows.EngineTriage, goal);
	}

	/// <summary>Plan a cheat from a goal.</summary>
	[McpServerPrompt(Name = "plan_cheat", Title = "Plan a cheat from a goal")]
	[Description("The user states a goal but not a technique: choose the data or code route for that goal, the "
				 + "workflows to run, how to verify the result and the scope limits.")]
	public static GetPromptResult PlanCheatPrompt(
		[Display(Name = "Cheat goal")]
		[Description("The goal: infinite_health, infinite_ammo, currency, one_hit_kill, no_cooldown, teleport, "
					 + "game_speed, toggle_feature, unlock_items or other.")]
		[AllowedValues("infinite_health", "infinite_ammo", "currency", "one_hit_kill", "no_cooldown", "teleport",
			"game_speed", "toggle_feature", "unlock_items", "other")]
		string goal,
		[Display(Name = "Goal in your words")]
		[Description("The goal in the user's words, such as never run out of arrows.")]
		string? description = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, SessionWorkflows.PlanCheat, goal, description);
	}

	/// <summary>Report what this session found and changed.</summary>
	[McpServerPrompt(Name = "session_report", Title = "Report what this session found and changed")]
	[Description("End of a session or a hand-off, before cleanup: a read-only report of the target, the findings, the "
				 + "changes still applied and what cleanup would release.")]
	public static GetPromptResult SessionReportPrompt(
		[Display(Name = "Detail")]
		[Description("summary (default) or full, which also lists every record, structure and scanner.")]
		[AllowedValues("summary", "full")]
		string? detail = null,
		[Display(Name = "Table path")]
		[Description("The absolute .CT, .XML or .CETRAINER path to save the table to, under an allowed table root; "
					 + "omit it to save nothing.")]
		string? tablePath = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, SessionWorkflows.SessionReport, detail, tablePath);
	}

	/// <summary>Explain a failure and recover safely.</summary>
	[McpServerPrompt(Name = "explain_error", Title = "Explain a failure and recover safely")]
	[Description("After a failed call: explain its error kind and host effect, inspect the state it touched and "
				 + "decide whether a single retry is safe.")]
	public static GetPromptResult ExplainErrorPrompt(
		[Display(Name = "Error kind")]
		[Description("The error.kind of the failure, such as busy, timeout or capability_disabled.")]
		[AllowedValues("invalid_argument", "invalid_state", "not_found", "not_attached", "busy", "timeout",
			"cancelled", "capability_disabled", "unsupported", "target_changed", "host_refused", "memory_read_failed",
			"memory_write_failed", "limit_exceeded", "partial_effect", "stopping", "instance_unavailable", "internal")]
		string kind,
		[Display(Name = "Host effect")]
		[Description("The failure's hostEffect: not_started, not_applied, started, completed, cleanup_unconfirmed or "
					 + "unknown.")]
		[AllowedValues("not_started", "not_applied", "started", "completed", "cleanup_unconfirmed", "unknown")]
		string? hostEffect = null,
		[Display(Name = "Failed tool")]
		[Description("The tool that failed, such as " + CheatEngineToolNames.MemoryWrite + ".")]
		[ToolNameValues]
		string? tool = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, SessionWorkflows.ExplainError, kind, hostEffect, tool);
	}

	/// <summary>Clean up this session.</summary>
	[McpServerPrompt(Name = "cleanup_session", Title = "Clean up this session")]
	[Description("End of session or before a target switch: stop jobs, restore and release everything in a safe "
				 + "order and report what remains." + NeedsTargetCodeExecution)]
	public static GetPromptResult CleanupSessionPrompt(
		[Display(Name = "Keep address list")]
		[Description("yes (default) keeps the address-list records; no deletes the records this session created, "
					 + "after asking.")]
		[AllowedValues("yes", "no")]
		string? keepAddressList = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, SessionWorkflows.CleanupSession, keepAddressList);
	}

	/// <summary>Cheat Engine tutorial walkthrough.</summary>
	[McpServerPrompt(Name = "ce_tutorial_walkthrough", Title = "Cheat Engine tutorial walkthrough")]
	[Description("Coach one step of CE's tutorial or gtutorial with the matching workflow; a safe way to learn or to "
				 + "smoke-test a setup.")]
	public static GetPromptResult CeTutorialWalkthroughPrompt(
		[Display(Name = "Step")]
		[Description("The step to coach: 1 to 9 for tutorial, 1 to 3 for gtutorial.")]
		[AllowedValues("1", "2", "3", "4", "5", "6", "7", "8", "9")]
		string step,
		[Display(Name = "Tutorial")]
		[Description("tutorial (default, Tutorial-x86_64.exe) or gtutorial (gtutorial-x86_64.exe).")]
		[AllowedValues("tutorial", "gtutorial")]
		string? tutorial = null,
		McpServer? server = null)
	{
		if (string.Equals(tutorial?.Trim(), "gtutorial", StringComparison.OrdinalIgnoreCase) &&
			step?.Trim() is not ("1" or "2" or "3"))
		{
			throw CheatEngineToolException.InvalidArgument(nameof(step),
				"must be 1, 2 or 3 for gtutorial.");
		}

		return WorkflowPrompt.Render(server, SessionWorkflows.CeTutorialWalkthrough, step, tutorial);
	}
}
