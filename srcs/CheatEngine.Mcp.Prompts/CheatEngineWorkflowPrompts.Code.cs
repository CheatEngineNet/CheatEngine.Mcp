using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using CheatEngine.Mcp.Prompts.Workflows;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Prompts;

public sealed partial class CheatEngineWorkflowPrompts
{
	/// <summary>Find what writes or accesses an address.</summary>
	[McpServerPrompt(Name = "find_writer", Title = "Find what writes or accesses an address")]
	[Description("Which instructions write or read an address: a debugger capture job, disassembly and module "
				 + "offsets.")]
	public static GetPromptResult FindWriterPrompt(
		[Display(Name = "Value address")]
		[Description("The address of the value. " + AddressHelp)]
		string address,
		[Display(Name = "Trigger")]
		[Description("write (default) captures writers; access also captures readers.")]
		[AllowedValues("write", "access")]
		string? trigger = null,
		[Display(Name = "Value size")]
		[Description("The value size in bytes: 1, 2, 4 (default) or 8.")] [AllowedValues("1", "2", "4", "8")]
		string? size = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, CodeWorkflows.FindWriter, address, trigger, size);
	}

	/// <summary>Find the code that uses a text.</summary>
	[McpServerPrompt(Name = "find_code_by_string", Title = "Find the code that uses a text")]
	[Description("A visible message or log line leads to the logic: find the text in the module, the code that "
				 + "references it and the function around that code.")]
	public static GetPromptResult FindCodeByStringPrompt(
		[Display(Name = "Visible text")]
		[Description("The exact visible text, case preserved, such as Not enough gold.")]
		string text,
		[Display(Name = "Module")]
		[Description("The module to search, such as game.exe; omit it for the main module.")]
		string? module = null,
		[Display(Name = "Encoding")]
		[Description("auto (default) tries UTF-8 and UTF-16; utf8 or utf16 searches one encoding.")]
		[AllowedValues("auto", "utf8", "utf16")]
		string? encoding = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, CodeWorkflows.FindCodeByString, text, module, encoding);
	}

	/// <summary>Trace the logic of an instruction.</summary>
	[McpServerPrompt(Name = "trace_logic", Title = "Trace the logic of an instruction")]
	[Description("Find the comparison and branch that decide an outcome by reading and tracing the code."
				 + NeedsAutoAssembler)]
	public static GetPromptResult TraceLogicPrompt(
		[Display(Name = "Start address")]
		[Description("The address where the logic starts. " + AddressHelp)]
		string address,
		[Display(Name = "Question")]
		[Description("The question to answer, such as why the player dies at zero shield.")]
		string? question = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, CodeWorkflows.TraceLogic, address, question);
	}

	/// <summary>Force or invert a conditional branch.</summary>
	[McpServerPrompt(Name = "patch_branch", Title = "Force or invert a conditional branch")]
	[Description("A comparison and a conditional jump decide an outcome: force the jump, remove it or invert it with "
				 + "a tracked code patch of the same length." + NeedsAutoAssembler)]
	public static GetPromptResult PatchBranchPrompt(
		[Display(Name = "Jump address")]
		[Description("The address of the conditional jump. " + AddressHelp)]
		string address,
		[Display(Name = "Mode")]
		[Description("always makes the jump unconditional, never removes it, invert reverses its condition.")]
		[AllowedValues("always", "never", "invert")]
		string mode,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, CodeWorkflows.PatchBranch, address, mode);
	}

	/// <summary>Replace code with NOPs.</summary>
	[McpServerPrompt(Name = "nop_patch", Title = "Replace code with NOPs")]
	[Description("Disable an instruction reversibly with a tracked NOP patch, verify it and release it."
				 + NeedsAutoAssembler)]
	public static GetPromptResult NopPatchPrompt(
		[Display(Name = "Instruction address")]
		[Description("The address of the instruction. " + AddressHelp)]
		string instructionAddress,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, CodeWorkflows.NopPatch, instructionAddress);
	}

	/// <summary>Write an AOB code injection.</summary>
	[McpServerPrompt(Name = "aob_injection", Title = "Write an AOB code injection")]
	[Description("Run custom code at an instruction found by signature: generate, check, apply, verify and release."
				 + NeedsAutoAssembler)]
	public static GetPromptResult AobInjectionPrompt(
		[Display(Name = "Instruction address")]
		[Description("The address of the instruction to hook. " + AddressHelp)]
		string instructionAddress,
		[Display(Name = "Goal")]
		[Description("What the injected code must achieve, such as double the damage dealt.")]
		string goal,
		[Display(Name = "Symbol name")]
		[Description("The symbol name of the injection point, such as damageHook: a letter or underscore, then "
					 + "letters, digits or underscores, 64 characters at most.")]
		string? symbolName = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, CodeWorkflows.AobInjection, instructionAddress, goal, symbolName);
	}

	/// <summary>Review an Auto Assembler script before applying it.</summary>
	[McpServerPrompt(Name = "review_aa_script", Title = "Review an Auto Assembler script before applying it")]
	[Description("Before applying an Auto Assembler script or enabling a script record: check its syntax, signatures, "
				 + "original bytes and cleanup, without applying it." + NeedsAutoAssembler)]
	public static GetPromptResult ReviewAaScriptPrompt(
		[Display(Name = "Record ID")]
		[Description("The id of the script record to review; omit it to review the script in the conversation.")]
		string? recordId = null,
		[Display(Name = "Injection address")]
		[Description("The injection address to compare with. " + AddressHelp)]
		string? site = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, CodeWorkflows.ReviewAaScript, recordId, site);
	}

	/// <summary>Filter shared code by entity.</summary>
	[McpServerPrompt(Name = "shared_code_filter", Title = "Filter shared code by entity")]
	[Description("One instruction serves the player and enemies: find a field that tells them apart and filter the "
				 + "injection on it.")]
	public static GetPromptResult SharedCodeFilterPrompt(
		[Display(Name = "Instruction address")]
		[Description("The address of the shared instruction. " + AddressHelp)]
		string instructionAddress,
		[Display(Name = "Player value address")]
		[Description("The player's value address, when known. " + AddressHelp)]
		string? playerAddress = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, CodeWorkflows.SharedCodeFilter, instructionAddress, playerAddress);
	}

	/// <summary>Capture a base pointer by injection.</summary>
	[McpServerPrompt(Name = "injection_copy_base", Title = "Capture a base pointer by injection")]
	[Description("Copy the object base an instruction uses into a registered symbol, so records can use "
				 + "[symbol]+offset." + NeedsAutoAssembler)]
	public static GetPromptResult InjectionCopyBasePrompt(
		[Display(Name = "Instruction address")]
		[Description("The address of an instruction that handles only the player. " + AddressHelp)]
		string instructionAddress,
		[Display(Name = "Base symbol")]
		[Description("The symbol that receives the base (default playerBase): a letter or underscore, then letters, "
					 + "digits or underscores, 60 characters at most, because the injection point is named after it "
					 + "plus Hook.")]
		string? symbolName = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, CodeWorkflows.InjectionCopyBase, instructionAddress, symbolName);
	}

	/// <summary>Make a unique AOB signature.</summary>
	[McpServerPrompt(Name = "make_aob_signature", Title = "Make a unique AOB signature")]
	[Description("A byte pattern that finds an instruction exactly once in its module, with wildcards over bytes "
				 + "that change between builds.")]
	public static GetPromptResult MakeAobSignaturePrompt(
		[Display(Name = "Instruction address")]
		[Description("The address of the instruction. " + AddressHelp)]
		string address,
		[Display(Name = "Module")]
		[Description("The module to search, such as game.exe; omit it for the module that contains the address.")]
		string? module = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, CodeWorkflows.MakeAobSignature, address, module);
	}

	/// <summary>Call a game function once.</summary>
	[McpServerPrompt(Name = "call_game_function", Title = "Call a game function once")]
	[Description("Run one game function once, such as one that adds gold: find its arguments at a call site, pass "
				 + "numbers or a buffer, call it with consent and verify the effect." + NeedsTargetCodeExecution)]
	public static GetPromptResult CallGameFunctionPrompt(
		[Display(Name = "Function address")]
		[Description("The entry address of the function. " + AddressHelp)]
		string functionAddress,
		[Display(Name = "Purpose")]
		[Description("What the call must achieve, such as add 1000 gold.")]
		string purpose,
		[Display(Name = "Instance address")]
		[Description("The object whose method it is (this); omit it for a free or static function. " + AddressHelp)]
		string? instanceAddress = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, CodeWorkflows.CallGameFunction, functionAddress, purpose,
			instanceAddress);
	}
}
