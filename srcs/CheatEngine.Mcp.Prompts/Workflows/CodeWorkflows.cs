using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;

using static CheatEngine.Mcp.Prompts.Workflows.WorkflowValues;

namespace CheatEngine.Mcp.Prompts.Workflows;

/// <summary>The workflows that find, read, patch and inject code.</summary>
internal static class CodeWorkflows
{
	internal static readonly WorkflowDefinition FindWriter =
		new("find_writer", "find-writer", ["debugger", "code-analysis"])
		{
			Arguments =
			[
				new WorkflowArgument("address", Required: true),
				new WorkflowArgument("trigger")
				{
					Default = "write",
					Allowed = Triggers,
					Target = new WorkflowTarget(CheatEngineToolNames.DebuggerStartCapture, "trigger")
				},
				new WorkflowArgument("size")
				{
					Default = "4",
					Allowed = ValueSizes,
					Target = new WorkflowTarget(CheatEngineToolNames.DebuggerStartCapture, "size")
				}
			]
		};

	internal static readonly WorkflowDefinition FindCodeByString =
		new("find_code_by_string", "find-code-by-string", ["code-analysis", "aob-signatures", "value-types"])
		{
			Arguments =
			[
				new WorkflowArgument("text", WorkflowInputKind.Prose, true),
				new WorkflowArgument("module"),
				new WorkflowArgument("encoding") { Default = "auto", Allowed = TextEncodings }
			]
		};

	internal static readonly WorkflowDefinition TraceLogic =
		new("trace_logic", "trace-logic", ["debugger", "code-analysis", "x64-injection"])
		{
			Arguments =
			[
				new WorkflowArgument("address", Required: true),
				new WorkflowArgument("question", WorkflowInputKind.Prose)
			],
			Gates = [McpFeature.AutoAssembler]
		};

	internal static readonly WorkflowDefinition PatchBranch =
		new("patch_branch", "patch-branch", ["code-analysis", "x64-injection", "auto-assembler"])
		{
			Arguments =
			[
				new WorkflowArgument("address", Required: true),
				new WorkflowArgument("mode", Required: true) { Allowed = BranchModes }
			],
			Gates = [McpFeature.AutoAssembler]
		};

	internal static readonly WorkflowDefinition NopPatch =
		new("nop_patch", "nop-patch", ["auto-assembler", "x64-injection"])
		{
			Arguments = [new WorkflowArgument("instructionAddress", Required: true)],
			Gates = [McpFeature.AutoAssembler]
		};

	internal static readonly WorkflowDefinition AobInjection =
		new("aob_injection", "aob-injection", ["auto-assembler", "x64-injection", "aob-signatures"])
		{
			Arguments =
			[
				new WorkflowArgument("instructionAddress", Required: true),
				new WorkflowArgument("goal", WorkflowInputKind.Prose, true),
				new WorkflowArgument("symbolName", WorkflowInputKind.Symbol)
			],
			Gates = [McpFeature.AutoAssembler]
		};

	internal static readonly WorkflowDefinition ReviewAaScript =
		new("review_aa_script", "review-aa-script", ["auto-assembler", "x64-injection", "aob-signatures"])
		{
			Arguments =
			[
				new WorkflowArgument("recordId", WorkflowInputKind.NonNegativeInteger), new WorkflowArgument("site")
			],
			Gates = [McpFeature.AutoAssembler]
		};

	internal static readonly WorkflowDefinition SharedCodeFilter =
		new("shared_code_filter", "shared-code-filter", ["structures", "auto-assembler", "debugger"])
		{
			Arguments =
			[
				new WorkflowArgument("instructionAddress", Required: true), new WorkflowArgument("playerAddress")
			]
		};

	internal static readonly WorkflowDefinition InjectionCopyBase =
		new("injection_copy_base", "injection-copy-base", ["auto-assembler", "pointers"])
		{
			Arguments =
			[
				new WorkflowArgument("instructionAddress", Required: true),
				// The body passes asm_generate_injection(symbolName="{symbolName}Hook"), which takes 64 characters.
				new WorkflowArgument("symbolName", WorkflowInputKind.Symbol)
				{
					Default = "playerBase",
					MaximumLength = WorkflowPrompt.MaximumSymbolLength - "Hook".Length
				}
			],
			Gates = [McpFeature.AutoAssembler]
		};

	internal static readonly WorkflowDefinition MakeAobSignature =
		new("make_aob_signature", "make-aob-signature", ["aob-signatures"])
		{
			Arguments = [new WorkflowArgument("address", Required: true), new WorkflowArgument("module")]
		};

	// The body passes {functionAddress} to exec_call_remote or exec_call_method and {instanceAddress} to
	// exec_call_method(classInstance=...); both tools require target code execution.
	internal static readonly WorkflowDefinition CallGameFunction =
		new("call_game_function", "call-game-function", ["x64-injection", "code-analysis", "safety"])
		{
			Arguments =
			[
				new WorkflowArgument("functionAddress", Required: true),
				new WorkflowArgument("purpose", WorkflowInputKind.Prose, true),
				new WorkflowArgument("instanceAddress")
			],
			Gates = [McpFeature.TargetCodeExecution]
		};
}
