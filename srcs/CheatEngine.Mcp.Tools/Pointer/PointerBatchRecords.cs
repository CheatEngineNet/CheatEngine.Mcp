using System.ComponentModel;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>A caller-supplied pointer chain to validate.</summary>
public sealed record PointerChainCandidate(
	[property: Description("A stable caller-chosen identifier, unique within this request.")]
	string Id,
	[property: Description("The address expression that holds the first pointer.")]
	string Base,
	[property: Description("One to 64 signed hexadecimal offsets in dereference order.")]
	string[] Offsets);

/// <summary>Whether the chain itself resolved.</summary>
[JsonConverter(typeof(ContractEnumConverter<PointerChainResolutionStatus>))]
public enum PointerChainResolutionStatus
{
	Resolved,
	Unreadable,
	Error
}

/// <summary>Whether a resolved final address matched the requested target.</summary>
[JsonConverter(typeof(ContractEnumConverter<PointerChainComparisonStatus>))]
public enum PointerChainComparisonStatus
{
	NotRequested,
	NotResolved,
	NotAttempted,
	Match,
	Miss
}

/// <summary>Whether the optional final value was read.</summary>
[JsonConverter(typeof(ContractEnumConverter<PointerChainValueStatus>))]
public enum PointerChainValueStatus
{
	NotRequested,
	NotResolved,
	NotAttempted,
	Read,
	Failed
}

/// <summary>The result for one submitted candidate.</summary>
public sealed record PointerChainCandidateResult(
	[property: Description("The stable identifier supplied by the caller.")]
	string Id,
	[property: Description("Whether every pointer hop resolved.")]
	PointerChainResolutionStatus ChainStatus,
	[property: Description("The final address when the chain resolved, uppercase hexadecimal.")]
	string? Address,
	[property: Description("The zero-based hop that could not be read, when chainStatus is unreadable.")]
	int? FailedHop,
	[property: Description("The address that could not be read, when chainStatus is unreadable.")]
	string? FailedReadAt,
	[property: Description("The per-candidate chain error when chainStatus is error.")]
	ToolError? ChainError,
	[property: Description("Whether the resolved address matched target.")]
	PointerChainComparisonStatus ComparisonStatus,
	[property: Description("Whether the optional final value was read.")]
	PointerChainValueStatus ValueStatus,
	[property: Description("The final value as text when valueStatus is read.")]
	string? Value,
	[property: Description("The final-value read error when valueStatus is failed.")]
	ToolError? ValueError);

/// <summary>Counts covering submitted candidates, including ones hidden by matchesOnly.</summary>
public sealed record PointerChainBatchSummary(
	[property: Description("How many candidates were submitted.")]
	int Submitted,
	[property: Description("How many candidates were validated before this response ended.")]
	int Processed,
	[property: Description("How many chains resolved.")]
	int Resolved,
	[property: Description("How many chains had an unreadable hop.")]
	int Unreadable,
	[property: Description("How many chains ended with another per-candidate error.")]
	int Errors,
	[property: Description("How many resolved chains matched target.")]
	int Matches,
	[property: Description("How many resolved chains missed target.")]
	int Misses,
	[property: Description("How many optional final values were read.")]
	int ValuesRead,
	[property: Description("How many optional final values could not be read.")]
	int ValueErrors,
	[property: Description("Whether caller cancellation stopped validation before every candidate was processed.")]
	bool Cancelled);

/// <summary>The bounded validation result for supplied pointer chains.</summary>
public sealed record PointerChainBatchResult(
	[property: Description("Candidate outcomes, or only matches when matchesOnly was requested.")]
	IReadOnlyList<PointerChainCandidateResult> Candidates,
	[property: Description("Counts over all processed candidates, including hidden errors and misses.")]
	PointerChainBatchSummary Summary);
