namespace CheatEngine.Mcp.Prompts.Workflows;

/// <summary>
///     Every guided workflow: prompt name, embedded body, linked documents, arguments, gates and excerpt selectors. The
///     definitions live in one class per theme; the linked documents follow the workflow index of
///     <c>cheatengine://docs/workflows</c>, which a test keeps in step.
/// </summary>
internal static class CheatEngineWorkflows
{
	/// <summary>Every workflow, in the order of the workflow index.</summary>
	internal static IReadOnlyList<WorkflowDefinition> All
	{
		get;
	} =
	[
		SessionWorkflows.AttachAndOrient, SessionWorkflows.EngineTriage, SessionWorkflows.PlanCheat,
		ValueWorkflows.FindKnownValue, ValueWorkflows.FindUnknownValue, ValueWorkflows.FindFloatValue,
		ValueWorkflows.FindFlag, ValueWorkflows.FindPosition, ValueWorkflows.FindTimer, ValueWorkflows.FindText,
		ValueWorkflows.GroupScan, ValueWorkflows.CompareSnapshots, ValueWorkflows.IdentifyAddress,
		ValueWorkflows.FreezeValue, CodeWorkflows.FindWriter, CodeWorkflows.FindCodeByString, CodeWorkflows.TraceLogic,
		CodeWorkflows.PatchBranch, CodeWorkflows.NopPatch, CodeWorkflows.AobInjection, CodeWorkflows.ReviewAaScript,
		CodeWorkflows.SharedCodeFilter, CodeWorkflows.InjectionCopyBase, CodeWorkflows.MakeAobSignature,
		CodeWorkflows.CallGameFunction, StructureWorkflows.ManualPointerChain, StructureWorkflows.PointerScan,
		StructureWorkflows.DissectStructure, StructureWorkflows.FindEntityList, RuntimeWorkflows.UnityMonoRecon,
		RuntimeWorkflows.UnityIl2CppRecon, RuntimeWorkflows.DotNetRecon, RuntimeWorkflows.UnrealRecon,
		RuntimeWorkflows.EmulatorMemory, RuntimeWorkflows.Speedhack, RuntimeWorkflows.WriteLuaScript,
		StructureWorkflows.UseCheatTable, StructureWorkflows.BuildRobustTable, StructureWorkflows.RepairAfterUpdate,
		SessionWorkflows.SessionReport, SessionWorkflows.ExplainError, SessionWorkflows.CleanupSession,
		SessionWorkflows.CeTutorialWalkthrough
	];
}
