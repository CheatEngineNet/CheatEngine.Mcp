using System.ComponentModel;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>The details of a target change refused by more than one guard.</summary>
/// <param name="Refusals">Each guard's refusal, in guard order.</param>
public sealed record TargetTransitionRefusals(
	[property: Description("Each refusal, in guard order.")]
	IReadOnlyList<TargetTransitionBlocker> Refusals);
