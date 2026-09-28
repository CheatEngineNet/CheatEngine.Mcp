using CheatEngine.Mcp.Core.Contract;

using static CheatEngine.Mcp.Prompts.Workflows.WorkflowValues;

namespace CheatEngine.Mcp.Prompts.Workflows;

/// <summary>The workflows that find, compare, identify and hold values in memory.</summary>
internal static class ValueWorkflows
{
	private static readonly WorkflowTarget ScanValueType = new(CheatEngineToolNames.ScanFirst, "valueType");

	internal static readonly WorkflowDefinition FindKnownValue =
		new("find_known_value", "find-known-value", ["value-scans", "ce-tutorial", "troubleshooting-scans"])
		{
			Arguments =
			[
				new WorkflowArgument("currentValue", Required: true),
				new WorkflowArgument("valueType") { Default = "auto", Allowed = KnownValueTypes, Target = ScanValueType },
				new WorkflowArgument("description", WorkflowInputKind.Prose)
			]
		};

	internal static readonly WorkflowDefinition FindUnknownValue =
		new("find_unknown_value", "find-unknown-value", ["value-scans", "troubleshooting-scans"])
		{
			Arguments =
			[
				new WorkflowArgument("valueType")
				{
					Default = "auto", Allowed = UnknownValueTypes, Target = ScanValueType
				},
				new WorkflowArgument("description", WorkflowInputKind.Prose)
			]
		};

	internal static readonly WorkflowDefinition FindFloatValue =
		new("find_float_value", "find-float-value", ["value-scans", "structures", "troubleshooting-scans"])
		{
			Arguments =
			[
				new WorkflowArgument("displayedValue"), new WorkflowArgument("description", WorkflowInputKind.Prose)
			]
		};

	internal static readonly WorkflowDefinition FindFlag =
		new("find_flag", "find-flag", ["value-scans", "code-analysis", "value-types"])
		{
			Arguments =
			[
				new WorkflowArgument("state", Required: true) { Allowed = FlagStates },
				new WorkflowArgument("width") { Default = "auto", Allowed = FlagWidths, Target = ScanValueType },
				new WorkflowArgument("description", WorkflowInputKind.Prose)
			]
		};

	internal static readonly WorkflowDefinition FindPosition =
		new("find_position", "find-position", ["value-scans", "structures", "game-engines", "value-types"])
		{
			Arguments =
			[
				new WorkflowArgument("valueType") { Default = "auto", Allowed = PositionTypes, Target = ScanValueType },
				new WorkflowArgument("address"),
				new WorkflowArgument("description", WorkflowInputKind.Prose)
			]
		};

	internal static readonly WorkflowDefinition FindTimer =
		new("find_timer", "find-timer", ["value-scans", "code-analysis", "speedhack"])
		{
			Arguments =
			[
				new WorkflowArgument("kind") { Default = "unknown", Allowed = TimerKinds },
				new WorkflowArgument("displayedValue"),
				new WorkflowArgument("description", WorkflowInputKind.Prose)
			]
		};

	internal static readonly WorkflowDefinition FindText =
		new("find_text", "find-text", ["value-types", "mono-and-dotnet", "structures"])
		{
			Arguments =
			[
				new WorkflowArgument("text", WorkflowInputKind.Prose, true),
				new WorkflowArgument("encoding") { Default = "auto", Allowed = TextEncodings },
				new WorkflowArgument("purpose") { Default = "find", Allowed = TextPurposes }
			]
		};

	internal static readonly WorkflowDefinition GroupScan =
		new("group_scan", "group-scan", ["value-scans", "value-types", "structures"])
		{
			Arguments =
			[
				new WorkflowArgument("values", Required: true),
				new WorkflowArgument("region") { Default = "heap", Allowed = ScanRegions }
			]
		};

	internal static readonly WorkflowDefinition CompareSnapshots =
		new("compare_snapshots", "compare-snapshots", ["structures", "value-types", "memory-model"])
		{
			Arguments =
			[
				new WorkflowArgument("address", Required: true),
				// memory_create_snapshot copies at most 16 MiB.
				new WorkflowArgument("size", WorkflowInputKind.PositiveInteger)
				{
					Default = "256",
					Maximum = 16 * 1024 * 1024
				},
				new WorkflowArgument("valueType")
				{
					Default = "int32",
					Allowed = SnapshotValueTypes,
					Target = new WorkflowTarget(CheatEngineToolNames.MemoryCompareSnapshot, "valueType")
				},
				new WorkflowArgument("description", WorkflowInputKind.Prose)
			]
		};

	internal static readonly WorkflowDefinition IdentifyAddress =
		new("identify_address", "identify-address", ["memory-model", "code-analysis", "structures"])
		{
			Arguments = [new WorkflowArgument("address", Required: true)]
		};

	internal static readonly WorkflowDefinition FreezeValue = new("freeze_value", "freeze-value", ["cheat-tables"])
	{
		Arguments =
		[
			new WorkflowArgument("address", Required: true),
			new WorkflowArgument("valueType", Required: true)
			{
				Allowed = RecordValueTypes,
				ValueNotes = RecordTypeNotes,
				Target = new WorkflowTarget(CheatEngineToolNames.MemoryRead, "valueType")
			},
			new WorkflowArgument("value"),
			new WorkflowArgument("description", WorkflowInputKind.Prose)
		]
	};
}
