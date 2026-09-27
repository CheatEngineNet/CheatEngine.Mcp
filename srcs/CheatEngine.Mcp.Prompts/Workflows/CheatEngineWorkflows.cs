namespace CheatEngine.Mcp.Prompts.Workflows;

/// <summary>
///     The 22 guided workflows: prompt name, embedded body and linked documents. The linked documents follow the workflow
///     index of <c>cheatengine://docs/workflows</c>, which a test keeps in step.
/// </summary>
internal static class CheatEngineWorkflows
{
	internal static readonly WorkflowDefinition AttachAndOrient =
		new("attach_and_orient", "attach-and-orient", ["safety", "mono-and-dotnet"]);

	internal static readonly WorkflowDefinition FindKnownValue =
		new("find_known_value", "find-known-value", ["value-scans", "ce-tutorial"]);

	internal static readonly WorkflowDefinition FindUnknownValue =
		new("find_unknown_value", "find-unknown-value", ["value-scans"]);

	internal static readonly WorkflowDefinition FindFloatValue =
		new("find_float_value", "find-float-value", ["value-scans", "structures"]);

	internal static readonly WorkflowDefinition FreezeValue = new("freeze_value", "freeze-value", ["cheat-tables"]);

	internal static readonly WorkflowDefinition FindWriter =
		new("find_writer", "find-writer", ["debugger", "code-analysis"]);

	internal static readonly WorkflowDefinition NopPatch =
		new("nop_patch", "nop-patch", ["auto-assembler", "x64-injection"]);

	internal static readonly WorkflowDefinition ManualPointerChain =
		new("manual_pointer_chain", "manual-pointer-chain", ["pointers", "debugger"]);

	internal static readonly WorkflowDefinition PointerScan = new("pointer_scan", "pointer-scan", ["pointers"]);

	internal static readonly WorkflowDefinition AobInjection =
		new("aob_injection", "aob-injection", ["auto-assembler", "x64-injection", "aob-signatures"]);

	internal static readonly WorkflowDefinition SharedCodeFilter =
		new("shared_code_filter", "shared-code-filter", ["structures", "auto-assembler", "debugger"]);

	internal static readonly WorkflowDefinition InjectionCopyBase =
		new("injection_copy_base", "injection-copy-base", ["auto-assembler", "pointers"]);

	internal static readonly WorkflowDefinition MakeAobSignature =
		new("make_aob_signature", "make-aob-signature", ["aob-signatures"]);

	internal static readonly WorkflowDefinition DissectStructure =
		new("dissect_structure", "dissect-structure", ["structures", "mono-and-dotnet"]);

	internal static readonly WorkflowDefinition TraceLogic =
		new("trace_logic", "trace-logic", ["debugger", "code-analysis", "x64-injection"]);

	internal static readonly WorkflowDefinition UnityMonoRecon =
		new("unity_mono_recon", "unity-mono-recon", ["mono-and-dotnet", "safety"]);

	internal static readonly WorkflowDefinition DotNetRecon = new("dotnet_recon", "dotnet-recon", ["mono-and-dotnet"]);

	internal static readonly WorkflowDefinition Speedhack = new("speedhack", "speedhack", ["speedhack", "safety"]);

	internal static readonly WorkflowDefinition BuildRobustTable =
		new("build_robust_table", "build-robust-table", ["cheat-tables", "aob-signatures", "auto-assembler"]);

	internal static readonly WorkflowDefinition RepairAfterUpdate =
		new("repair_after_update", "repair-after-update", ["aob-signatures", "cheat-tables"]);

	internal static readonly WorkflowDefinition CleanupSession =
		new("cleanup_session", "cleanup-session", ["errors-and-recovery"]);

	internal static readonly WorkflowDefinition CeTutorialWalkthrough =
		new("ce_tutorial_walkthrough", "ce-tutorial-walkthrough", ["ce-tutorial"]);

	/// <summary>Every workflow, in the order of the workflow index.</summary>
	internal static IReadOnlyList<WorkflowDefinition> All
	{
		get;
	} =
	[
		AttachAndOrient, FindKnownValue, FindUnknownValue, FindFloatValue, FreezeValue, FindWriter, NopPatch,
		ManualPointerChain, PointerScan, AobInjection, SharedCodeFilter, InjectionCopyBase, MakeAobSignature,
		DissectStructure, TraceLogic, UnityMonoRecon, DotNetRecon, Speedhack, BuildRobustTable, RepairAfterUpdate,
		CleanupSession, CeTutorialWalkthrough
	];

	/// <summary>The scan value types of <c>find_known_value</c>; <c>auto</c> lets the workflow choose.</summary>
	internal static IReadOnlyList<string> KnownValueTypes
	{
		get;
	} =
	[
		"auto", "int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float", "double",
		"string", "wstring"
	];

	/// <summary>The scan value types of <c>find_unknown_value</c>.</summary>
	internal static IReadOnlyList<string> UnknownValueTypes
	{
		get;
	} = ["auto", "int32", "float", "double"];

	/// <summary>The record value types of <c>freeze_value</c>.</summary>
	internal static IReadOnlyList<string> RecordValueTypes
	{
		get;
	} =
	[
		"int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float", "double", "string",
		"wstring", "bytes"
	];

	/// <summary>The capture triggers of <c>find_writer</c>.</summary>
	internal static IReadOnlyList<string> Triggers
	{
		get;
	} = ["write", "access"];

	/// <summary>The value sizes of <c>find_writer</c>, in bytes.</summary>
	internal static IReadOnlyList<string> ValueSizes
	{
		get;
	} = ["1", "2", "4", "8"];

	/// <summary>The pointer levels of the pointer workflows.</summary>
	internal static IReadOnlyList<string> PointerLevels
	{
		get;
	} = ["1", "2", "3", "4", "5", "6", "7", "8"];

	/// <summary>The script templates of <c>aob_injection</c>.</summary>
	internal static IReadOnlyList<string> InjectionTemplates
	{
		get;
	} = ["aob", "full", "code"];

	/// <summary>The answers of <c>cleanup_session</c>'s keepAddressList.</summary>
	internal static IReadOnlyList<string> YesNo
	{
		get;
	} = ["yes", "no"];

	/// <summary>The practice targets of <c>ce_tutorial_walkthrough</c>.</summary>
	internal static IReadOnlyList<string> Tutorials
	{
		get;
	} = ["tutorial", "gtutorial"];

	/// <summary>The tutorial steps; gtutorial has levels 1 to 3 only.</summary>
	internal static IReadOnlyList<string> TutorialSteps
	{
		get;
	} = ["1", "2", "3", "4", "5", "6", "7", "8", "9"];
}
