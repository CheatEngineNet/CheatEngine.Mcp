using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using CheatEngine.Mcp.Prompts.Workflows;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Prompts;

public sealed partial class CheatEngineWorkflowPrompts
{
	/// <summary>Build a pointer chain manually.</summary>
	[McpServerPrompt(Name = "manual_pointer_chain", Title = "Build a pointer chain manually")]
	[Description("Walk back from the code that accesses a value to a static base, one level at a time, and verify "
				 + "the chain after a restart.")]
	public static GetPromptResult ManualPointerChainPrompt(
		[Display(Name = "Value address")]
		[Description("The dynamic address of the value. " + AddressHelp)]
		string address,
		[Display(Name = "Maximum levels")]
		[Description("The most pointer levels to walk: 1 to 8 (default 4).")]
		[AllowedValues("1", "2", "3", "4", "5", "6", "7", "8")]
		string? maxLevels = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, StructureWorkflows.ManualPointerChain, address, maxLevels);
	}

	/// <summary>Find a static pointer with the pointer scanner.</summary>
	[McpServerPrompt(Name = "pointer_scan", Title = "Find a static pointer with the pointer scanner")]
	[Description("Pointer map, path scan and rescans across restarts to find a static path to a value.")]
	public static GetPromptResult PointerScanPrompt(
		[Display(Name = "Value address")]
		[Description("The dynamic address of the value. " + AddressHelp)]
		string address,
		[Display(Name = "Maximum depth")]
		[Description("The deepest path to search: 1 to 8 (default 5).")]
		[AllowedValues("1", "2", "3", "4", "5", "6", "7", "8")]
		string? maxDepth = null,
		[Display(Name = "Maximum offset")]
		[Description("The largest offset per level, a decimal number from 0 to 1048576 (default 4096).")]
		string? maxOffset = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, StructureWorkflows.PointerScan, address, maxDepth, maxOffset);
	}

	/// <summary>Dissect a structure.</summary>
	[McpServerPrompt(Name = "dissect_structure", Title = "Dissect a structure")]
	[Description("Name the fields around a base address: RTTI, .NET or PDB layouts or autoguess, then comparison "
				 + "with other instances.")]
	public static GetPromptResult DissectStructurePrompt(
		[Display(Name = "Base address")]
		[Description("The object's base address. " + AddressHelp)]
		string baseAddress,
		[Display(Name = "Size in bytes")]
		[Description("How many bytes to dissect, a decimal number from 1 to 65536 (default 256).")]
		string? size = null,
		[Display(Name = "Other instances")]
		[Description("Other instances' base addresses, separated by commas, to compare with.")]
		string? compareWith = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, StructureWorkflows.DissectStructure, baseAddress, size, compareWith);
	}

	/// <summary>Find the list that holds entities.</summary>
	[McpServerPrompt(Name = "find_entity_list", Title = "Find the list that holds entities")]
	[Description("From one known entity, find the array or list that holds all of them and its count, then anchor "
				 + "it with a pointer path.")]
	public static GetPromptResult FindEntityListPrompt(
		[Display(Name = "Entity address")]
		[Description("The base address of one known entity. " + AddressHelp)]
		string entityAddress,
		[Display(Name = "Runtime")]
		[Description("The target's runtime: auto (default), native, mono or dotnet.")]
		[AllowedValues("auto", "native", "mono", "dotnet")]
		string? runtime = null,
		[Display(Name = "Entity count")]
		[Description("How many entities the game shows, when known, as a decimal number.")]
		string? expectedCount = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, StructureWorkflows.FindEntityList, entityAddress, runtime,
			expectedCount);
	}

	/// <summary>Load and use a cheat table.</summary>
	[McpServerPrompt(Name = "use_cheat_table", Title = "Load and use a cheat table")]
	[Description("Load a table the user trusts, review its scripts, activate one cheat at a time with consent, "
				 + "verify it in game and undo it." + NeedsAutoAssembler)]
	public static GetPromptResult UseCheatTablePrompt(
		[Display(Name = "Table path")]
		[Description("The absolute .CT, .XML or .CETRAINER path of the table, under an allowed table root; omit it "
					 + "to choose one with table_list_files.")]
		string? tablePath = null,
		[Display(Name = "Cheat to use")]
		[Description("The cheat to use, as the table describes it, such as Infinite health; omit it to list the "
					 + "cheats first.")]
		string? cheat = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, StructureWorkflows.UseCheatTable, tablePath, cheat);
	}

	/// <summary>Build a robust cheat table.</summary>
	[McpServerPrompt(Name = "build_robust_table", Title = "Build a robust cheat table")]
	[Description("Turn the address list into a table that survives restarts and updates, test it and save it."
				 + NeedsAutoAssembler)]
	public static GetPromptResult BuildRobustTablePrompt(
		[Display(Name = "Table path")]
		[Description("The absolute .CT path to save, under an allowed table root.")]
		string? tablePath = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, StructureWorkflows.BuildRobustTable, tablePath);
	}

	/// <summary>Repair a table after a game update.</summary>
	[McpServerPrompt(Name = "repair_after_update", Title = "Repair a table after a game update")]
	[Description("A game update broke a record: find the new code, rebuild the signature and offsets, test and save "
				 + "a new table." + NeedsAutoAssembler)]
	public static GetPromptResult RepairAfterUpdatePrompt(
		[Display(Name = "Table path")]
		[Description("The absolute .CT, .XML or .CETRAINER path of the table, under an allowed table root, when it "
					 + "is not loaded yet.")]
		string? tablePath = null,
		[Display(Name = "Broken record")]
		[Description("The description of the broken record, such as Infinite ammo.")]
		string? recordDescription = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, StructureWorkflows.RepairAfterUpdate, tablePath, recordDescription);
	}
}
