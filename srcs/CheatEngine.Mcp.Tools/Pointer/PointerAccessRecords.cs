using System.ComponentModel;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>The point at which the supplied register context was observed.</summary>
[JsonConverter(typeof(ContractEnumConverter<PointerAccessContextPhase>))]
public enum PointerAccessContextPhase
{
	/// <summary>The registers were captured before the instruction executed.</summary>
	PreExecution,

	/// <summary>The registers were captured after a write or access event and may no longer describe the access.</summary>
	PostExecution
}

/// <summary>The confidence of supplied-facts access analysis.</summary>
[JsonConverter(typeof(ContractEnumConverter<PointerAccessStatus>))]
public enum PointerAccessStatus
{
	/// <summary>The supplied facts describe one explicit memory operand.</summary>
	Supported,

	/// <summary>A required supplied fact was absent.</summary>
	MissingFacts,

	/// <summary>Two supplied facts disagree.</summary>
	ContradictoryFacts,

	/// <summary>The operand form is not safely supported.</summary>
	Unsupported
}

/// <summary>Analysis of one supplied x86 or x64 instruction access.</summary>
public sealed record PointerAccessInfo(
	[property: Description("Whether the supplied facts were supported, missing, contradictory, or unsupported.")]
	PointerAccessStatus Status,
	[property: Description("The supplied instruction text used as the authoritative representation.")]
	string Instruction,
	[property: Description("The evaluated memory operand, when supported.")]
	string? MemoryOperand = null,
	[property: Description("The canonical base register, when present.")]
	string? BaseRegister = null,
	[property: Description("The canonical index register, when present.")]
	string? IndexRegister = null,
	[property: Description("The index scale, when present.")]
	int? Scale = null,
	[property: Description("The signed displacement, uppercase hexadecimal.")]
	string? Displacement = null,
	[property: Description("The computed effective address, uppercase hexadecimal.")]
	string? EffectiveAddress = null,
	[property: Description("The candidate structure base before a stable displacement, uppercase hexadecimal.")]
	string? CandidateStructureBase = null,
	[property: Description("The value to search for in a pointer map: candidateStructureBase when stable.")]
	string? NextPointerSearchValue = null,
	[property: Description("Whether index addressing makes the displacement dynamic rather than a stable pointer-chain offset.")]
	bool DynamicOffset = false,
	[property: Description("Whether the supplied observed address differs from the computed effective address.")]
	bool ObservedAddressMismatch = false,
	[property: Description("Whether post-execution registers may have changed before capture.")]
	bool ContextMayHaveChanged = false,
	[property: Description("A bounded explanation of missing, contradictory, or unsupported facts.")]
	string? Uncertainty = null);
