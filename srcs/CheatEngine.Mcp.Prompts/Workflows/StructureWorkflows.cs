using CheatEngine.Mcp.Core.Features;

using static CheatEngine.Mcp.Prompts.Workflows.WorkflowValues;

namespace CheatEngine.Mcp.Prompts.Workflows;

/// <summary>The workflows that anchor values with pointers, name structures and keep them in cheat tables.</summary>
internal static class StructureWorkflows
{
	internal static readonly WorkflowDefinition ManualPointerChain =
		new("manual_pointer_chain", "manual-pointer-chain", ["pointers", "debugger"])
		{
			Arguments =
			[
				new WorkflowArgument("address", Required: true),
				new WorkflowArgument("maxLevels") { Default = "4", Allowed = PointerLevels }
			]
		};

	internal static readonly WorkflowDefinition PointerScan = new("pointer_scan", "pointer-scan", ["pointers"])
	{
		Arguments =
		[
			new WorkflowArgument("address", Required: true),
			new WorkflowArgument("maxDepth") { Default = "5", Allowed = PointerLevels },
			// pointer_find_paths accepts a maxOffset of 0 (exact pointers only) to 1 MiB.
			new WorkflowArgument("maxOffset", WorkflowInputKind.NonNegativeInteger)
			{
				Default = "4096",
				Maximum = 1024 * 1024
			}
		]
	};

	internal static readonly WorkflowDefinition DissectStructure =
		new("dissect_structure", "dissect-structure", ["structures", "mono-and-dotnet"])
		{
			Arguments =
			[
				new WorkflowArgument("baseAddress", Required: true),
				// structure_autoguess guesses from at most 64 KiB, below memory_create_snapshot's 16 MiB.
				new WorkflowArgument("size", WorkflowInputKind.PositiveInteger) { Default = "256", Maximum = 65536 },
				new WorkflowArgument("compareWith")
			]
		};

	internal static readonly WorkflowDefinition FindEntityList =
		new("find_entity_list", "find-entity-list", ["structures", "pointers", "memory-model", "game-engines"])
		{
			Arguments =
			[
				new WorkflowArgument("entityAddress", Required: true),
				new WorkflowArgument("runtime") { Default = "auto", Allowed = EntityRuntimes },
				new WorkflowArgument("expectedCount", WorkflowInputKind.PositiveInteger)
			]
		};

	// Only asm_check is gated statically; the gates table_load enforces depend on the table's content, which the body
	// explains instead of listing them here.
	internal static readonly WorkflowDefinition UseCheatTable =
		new("use_cheat_table", "use-cheat-table", ["cheat-tables", "safety", "auto-assembler"])
		{
			Arguments = [new WorkflowArgument("tablePath"), new WorkflowArgument("cheat", WorkflowInputKind.Prose)],
			Gates = [McpFeature.AutoAssembler]
		};

	internal static readonly WorkflowDefinition BuildRobustTable =
		new("build_robust_table", "build-robust-table", ["cheat-tables", "aob-signatures", "auto-assembler"])
		{
			Arguments = [new WorkflowArgument("tablePath")],
			Gates = [McpFeature.AutoAssembler]
		};

	internal static readonly WorkflowDefinition RepairAfterUpdate =
		new("repair_after_update", "repair-after-update", ["aob-signatures", "cheat-tables"])
		{
			Arguments =
			[
				new WorkflowArgument("tablePath"), new WorkflowArgument("recordDescription", WorkflowInputKind.Prose)
			],
			Gates = [McpFeature.AutoAssembler]
		};
}
