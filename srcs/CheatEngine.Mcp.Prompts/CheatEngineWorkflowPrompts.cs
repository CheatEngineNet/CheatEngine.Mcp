using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Prompts.Workflows;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using static CheatEngine.Mcp.Prompts.Workflows.CheatEngineWorkflows;

namespace CheatEngine.Mcp.Prompts;

/// <summary>
///     The 22 guided workflows as prompts. Every prompt is static (Local): it takes no <c>instanceId</c>, renders the same
///     text on every backend and on the gateway, and tells the model to pick the instance with <c>instance_list</c>. Its
///     text is the embedded workflow body (also served as <c>cheatengine://docs/workflows/{workflow}</c>), and it links
///     the related knowledge documents.
/// </summary>
[McpServerPromptType]
public sealed class CheatEngineWorkflowPrompts
{
	private const string AddressHelp = "A hex address such as 7FF6A1B2C3D0 or a Cheat Engine expression such as "
									   + "game.exe+1A2B.";

	/// <summary>Attach to a target and get oriented.</summary>
	[McpServerPrompt(Name = "attach_and_orient", Title = "Attach to a target and get oriented")]
	[Description("Start of a session: check versions and gates, attach to the authorized process, classify its "
				 + "runtime (native, Mono, IL2CPP, .NET) and suggest the next workflow.")]
	public static GetPromptResult AttachAndOrientPrompt(
		[Description("The target process name or PID; omit it to choose from the process list.")]
		string? process = null,
		[Description("What the user wants to achieve, such as infinite health.")]
		string? goal = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, AttachAndOrient, new WorkflowInput("process", process),
			new WorkflowInput("goal", goal));
	}

	/// <summary>Find a known value.</summary>
	[McpServerPrompt(Name = "find_known_value", Title = "Find a known value")]
	[Description("The value is shown as a number: exact first and next scans, verification one candidate at a time, "
				 + "then a record.")]
	public static GetPromptResult FindKnownValuePrompt(
		[Description("The number the game shows now, such as 100 or 87.5.")]
		string currentValue,
		[Description("The value type: auto (default), int8, uint8, int16, uint16, int32, uint32, int64, uint64, "
					 + "float, double, string or wstring.")]
		[AllowedValues("auto", "int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float",
			"double", "string", "wstring")]
		string? valueType = null,
		[Description("What the value is, such as player health.")]
		string? description = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, FindKnownValue,
			new WorkflowInput("currentValue", currentValue, Required: true),
			new WorkflowInput("valueType", valueType, Default: "auto", Allowed: KnownValueTypes),
			new WorkflowInput("description", description));
	}

	/// <summary>Find an unknown value.</summary>
	[McpServerPrompt(Name = "find_unknown_value", Title = "Find an unknown value")]
	[Description("Only a bar or a change is visible: an unknown first scan, then increased, decreased, changed and "
				 + "unchanged scans.")]
	public static GetPromptResult FindUnknownValuePrompt(
		[Description("The value type: auto (default), int32, float or double.")]
		[AllowedValues("auto", "int32", "float", "double")]
		string? valueType = null,
		[Description("What the value is, such as the stamina bar.")]
		string? description = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, FindUnknownValue,
			new WorkflowInput("valueType", valueType, Default: "auto", Allowed: UnknownValueTypes),
			new WorkflowInput("description", description));
	}

	/// <summary>Find a floating-point value.</summary>
	[McpServerPrompt(Name = "find_float_value", Title = "Find a floating-point value")]
	[Description("Floats, doubles, percentages and positions: range scans around the displayed value, then "
				 + "neighbouring fields.")]
	public static GetPromptResult FindFloatValuePrompt(
		[Description("The value the game shows, such as 87.3 or 75%; omit it for a bar without a number.")]
		string? displayedValue = null,
		[Description("What the value is, such as the player's X position.")]
		string? description = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, FindFloatValue, new WorkflowInput("displayedValue", displayedValue),
			new WorkflowInput("description", description));
	}

	/// <summary>Freeze a value.</summary>
	[McpServerPrompt(Name = "freeze_value", Title = "Freeze a value")]
	[Description("Hold a value constant with an address-list record, verify that the freeze holds and undo it.")]
	public static GetPromptResult FreezeValuePrompt(
		[Description("The address of the value. " + AddressHelp)]
		string address,
		[Description("The value type: int8, uint8, int16, uint16, int32, uint32, int64, uint64, float, double, "
					 + "string, wstring or bytes.")]
		[AllowedValues("int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float", "double",
			"string", "wstring", "bytes")]
		string valueType,
		[Description("The value to hold; omit it to hold the current value.")]
		string? value = null,
		[Description("The record description, such as Infinite health.")]
		string? description = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, FreezeValue,
			new WorkflowInput("address", address, Required: true),
			new WorkflowInput("valueType", valueType, Allowed: RecordValueTypes, Required: true),
			new WorkflowInput("value", value),
			new WorkflowInput("description", description));
	}

	/// <summary>Find what writes or accesses an address.</summary>
	[McpServerPrompt(Name = "find_writer", Title = "Find what writes or accesses an address")]
	[Description("Which instructions write or read an address: a debugger capture job, disassembly and module "
				 + "offsets.")]
	public static GetPromptResult FindWriterPrompt(
		[Description("The address of the value. " + AddressHelp)]
		string address,
		[Description("write (default) captures writers; access also captures readers.")]
		[AllowedValues("write", "access")]
		string? trigger = null,
		[Description("The value size in bytes: 1, 2, 4 (default) or 8.")] [AllowedValues("1", "2", "4", "8")]
		string? size = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, FindWriter, new WorkflowInput("address", address, Required: true),
			new WorkflowInput("trigger", trigger, Default: "write", Allowed: Triggers),
			new WorkflowInput("size", size, Default: "4", Allowed: ValueSizes));
	}

	/// <summary>Replace code with NOPs.</summary>
	[McpServerPrompt(Name = "nop_patch", Title = "Replace code with NOPs")]
	[Description("Disable an instruction reversibly with a tracked NOP patch, verify it and release it.")]
	public static GetPromptResult NopPatchPrompt(
		[Description("The address of the instruction. " + AddressHelp)]
		string instructionAddress,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, NopPatch,
			new WorkflowInput("instructionAddress", instructionAddress, Required: true));
	}

	/// <summary>Build a pointer chain manually.</summary>
	[McpServerPrompt(Name = "manual_pointer_chain", Title = "Build a pointer chain manually")]
	[Description("Walk back from a writer to a static base, one level at a time, and verify the chain after a "
				 + "restart.")]
	public static GetPromptResult ManualPointerChainPrompt(
		[Description("The dynamic address of the value. " + AddressHelp)]
		string address,
		[Description("The most pointer levels to walk: 1 to 8 (default 4).")]
		[AllowedValues("1", "2", "3", "4", "5", "6", "7", "8")]
		string? maxLevels = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, ManualPointerChain, new WorkflowInput("address", address, Required: true),
			new WorkflowInput("maxLevels", maxLevels, Default: "4", Allowed: PointerLevels));
	}

	/// <summary>Find a static pointer with the pointer scanner.</summary>
	[McpServerPrompt(Name = "pointer_scan", Title = "Find a static pointer with the pointer scanner")]
	[Description("Pointer map, path scan and rescans across restarts to find a static path to a value.")]
	public static GetPromptResult PointerScanPrompt(
		[Description("The dynamic address of the value. " + AddressHelp)]
		string address,
		[Description("The deepest path to search: 1 to 8 (default 5).")]
		[AllowedValues("1", "2", "3", "4", "5", "6", "7", "8")]
		string? maxDepth = null,
		[Description("The largest offset per level, as a decimal number (default 4096).")]
		string? maxOffset = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, PointerScan, new WorkflowInput("address", address, Required: true),
			new WorkflowInput("maxDepth", maxDepth, Default: "5", Allowed: PointerLevels),
			new WorkflowInput("maxOffset", maxOffset, WorkflowInputKind.PositiveInteger, "4096"));
	}

	/// <summary>Write an AOB code injection.</summary>
	[McpServerPrompt(Name = "aob_injection", Title = "Write an AOB code injection")]
	[Description("Run custom code at an instruction found by signature: template, check, apply, verify and "
				 + "release.")]
	public static GetPromptResult AobInjectionPrompt(
		[Description("The address of the instruction to hook. " + AddressHelp)]
		string instructionAddress,
		[Description("What the injected code must achieve, such as double the damage dealt.")]
		string goal,
		[Description("The script template: aob (default), full or code.")] [AllowedValues("aob", "full", "code")]
		string? template = null,
		[Description("The symbol name of the injection point, such as damageHook.")]
		string? symbolName = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, AobInjection,
			new WorkflowInput("instructionAddress", instructionAddress, Required: true),
			new WorkflowInput("goal", goal, Required: true),
			new WorkflowInput("template", template, Default: "aob", Allowed: InjectionTemplates),
			new WorkflowInput("symbolName", symbolName, WorkflowInputKind.Symbol));
	}

	/// <summary>Filter shared code by entity.</summary>
	[McpServerPrompt(Name = "shared_code_filter", Title = "Filter shared code by entity")]
	[Description("One instruction serves the player and enemies: find a field that tells them apart and filter the "
				 + "injection on it.")]
	public static GetPromptResult SharedCodeFilterPrompt(
		[Description("The address of the shared instruction. " + AddressHelp)]
		string instructionAddress,
		[Description("The player's value address, when known. " + AddressHelp)]
		string? playerAddress = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, SharedCodeFilter,
			new WorkflowInput("instructionAddress", instructionAddress, Required: true),
			new WorkflowInput("playerAddress", playerAddress));
	}

	/// <summary>Capture a base pointer by injection.</summary>
	[McpServerPrompt(Name = "injection_copy_base", Title = "Capture a base pointer by injection")]
	[Description("Copy the object base an instruction uses into a registered symbol, so records can use "
				 + "[symbol]+offset.")]
	public static GetPromptResult InjectionCopyBasePrompt(
		[Description("The address of an instruction that handles only the player. " + AddressHelp)]
		string instructionAddress,
		[Description("The symbol that receives the base (default playerBase).")]
		string? symbolName = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, InjectionCopyBase,
			new WorkflowInput("instructionAddress", instructionAddress, Required: true),
			new WorkflowInput("symbolName", symbolName, WorkflowInputKind.Symbol, "playerBase"));
	}

	/// <summary>Make a unique AOB signature.</summary>
	[McpServerPrompt(Name = "make_aob_signature", Title = "Make a unique AOB signature")]
	[Description("A byte pattern that finds an instruction exactly once in its module, with wildcards over bytes "
				 + "that change between builds.")]
	public static GetPromptResult MakeAobSignaturePrompt(
		[Description("The address of the instruction. " + AddressHelp)]
		string address,
		[Description("The module to search, such as game.exe; omit it for the module that contains the address.")]
		string? module = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, MakeAobSignature, new WorkflowInput("address", address, Required: true),
			new WorkflowInput("module", module));
	}

	/// <summary>Dissect a structure.</summary>
	[McpServerPrompt(Name = "dissect_structure", Title = "Dissect a structure")]
	[Description("Name the fields around a base address: RTTI, .NET or PDB layouts or autoguess, then comparison "
				 + "with other instances.")]
	public static GetPromptResult DissectStructurePrompt(
		[Description("The object's base address. " + AddressHelp)]
		string baseAddress,
		[Description("How many bytes to dissect, as a decimal number (default 256).")]
		string? size = null,
		[Description("Other instances' base addresses, separated by commas, to compare with.")]
		string? compareWith = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, DissectStructure,
			new WorkflowInput("baseAddress", baseAddress, Required: true),
			new WorkflowInput("size", size, WorkflowInputKind.PositiveInteger, "256"),
			new WorkflowInput("compareWith", compareWith));
	}

	/// <summary>Trace the logic of an instruction.</summary>
	[McpServerPrompt(Name = "trace_logic", Title = "Trace the logic of an instruction")]
	[Description("Find the comparison and branch that decide an outcome by reading and tracing the code.")]
	public static GetPromptResult TraceLogicPrompt(
		[Description("The address where the logic starts. " + AddressHelp)]
		string address,
		[Description("The question to answer, such as why the player dies at zero shield.")]
		string? question = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, TraceLogic, new WorkflowInput("address", address, Required: true),
			new WorkflowInput("question", question));
	}

	/// <summary>Explore a Unity (Mono) game.</summary>
	[McpServerPrompt(Name = "unity_mono_recon", Title = "Explore a Unity (Mono) game")]
	[Description("Unity Mono classes, fields, statics and methods through CE's Mono data collector, after explicit "
				 + "consent.")]
	public static GetPromptResult UnityMonoReconPrompt(
		[Description("The class to find, such as PlayerController.")]
		string? className = null,
		[Description("The field to find, such as health.")]
		string? fieldName = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, UnityMonoRecon, new WorkflowInput("className", className),
			new WorkflowInput("fieldName", fieldName));
	}

	/// <summary>Explore a .NET game.</summary>
	[McpServerPrompt(Name = "dotnet_recon", Title = "Explore a .NET game")]
	[Description(".NET types, fields, statics and instances through CE's out-of-process .NET collector.")]
	public static GetPromptResult DotNetReconPrompt(
		[Description("The type to find, such as Game.Player.")]
		string? typeName = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, DotNetRecon, new WorkflowInput("typeName", typeName));
	}

	/// <summary>Change game speed.</summary>
	[McpServerPrompt(Name = "speedhack", Title = "Change game speed")]
	[Description("Change the game speed with CE's speedhack, verify it and restore the previous speed.")]
	public static GetPromptResult SpeedhackPrompt(
		[Description("The speed multiplier between 0.01 and 1000, such as 0.5 or 2.")]
		string speed,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, Speedhack,
			new WorkflowInput("speed", speed, WorkflowInputKind.Speed, Required: true));
	}

	/// <summary>Build a robust cheat table.</summary>
	[McpServerPrompt(Name = "build_robust_table", Title = "Build a robust cheat table")]
	[Description("Turn the address list into a table that survives restarts and updates, test it and save it.")]
	public static GetPromptResult BuildRobustTablePrompt(
		[Description("The absolute .CT path to save, under an allowed table root.")]
		string? tablePath = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, BuildRobustTable, new WorkflowInput("tablePath", tablePath));
	}

	/// <summary>Repair a table after a game update.</summary>
	[McpServerPrompt(Name = "repair_after_update", Title = "Repair a table after a game update")]
	[Description("A game update broke a record: find the new code, rebuild the signature and offsets, test and save "
				 + "a new table.")]
	public static GetPromptResult RepairAfterUpdatePrompt(
		[Description("The description of the broken record, such as Infinite ammo.")]
		string? recordDescription = null,
		[Description("The absolute .CT path of the table, when it is not loaded yet.")]
		string? tablePath = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, RepairAfterUpdate,
			new WorkflowInput("recordDescription", recordDescription), new WorkflowInput("tablePath", tablePath));
	}

	/// <summary>Clean up this session.</summary>
	[McpServerPrompt(Name = "cleanup_session", Title = "Clean up this session")]
	[Description("End of session or before a target switch: stop jobs, restore and release everything in a safe "
				 + "order and report what remains.")]
	public static GetPromptResult CleanupSessionPrompt(
		[Description("yes (default) keeps the address-list records; no deletes the records this session created, "
					 + "after asking.")]
		[AllowedValues("yes", "no")]
		string? keepAddressList = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, CleanupSession,
			new WorkflowInput("keepAddressList", keepAddressList, Default: "yes", Allowed: YesNo));
	}

	/// <summary>Cheat Engine tutorial walkthrough.</summary>
	[McpServerPrompt(Name = "ce_tutorial_walkthrough", Title = "Cheat Engine tutorial walkthrough")]
	[Description("Coach one step of CE's tutorial or gtutorial with the matching workflow; a safe way to learn or to "
				 + "smoke-test a setup.")]
	public static GetPromptResult CeTutorialWalkthroughPrompt(
		[Description("The step to coach: 1 to 9 for tutorial, 1 to 3 for gtutorial.")]
		[AllowedValues("1", "2", "3", "4", "5", "6", "7", "8", "9")]
		string step,
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

		return WorkflowPrompt.Render(server, CeTutorialWalkthrough,
			new WorkflowInput("step", step, Allowed: TutorialSteps, Required: true),
			new WorkflowInput("tutorial", tutorial, Default: "tutorial", Allowed: Tutorials));
	}
}
