using System.ComponentModel;
using System.Text.Json;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>One guard's refusal of a target change.</summary>
/// <param name="Message">What blocks the change.</param>
/// <param name="Hint">How to release the blocker, when known.</param>
/// <param name="Details">The guard's structured details, when it gave some.</param>
public sealed record TargetTransitionBlocker(
	[property: Description("What blocks the change.")]
	string Message,
	[property: Description("How to release the blocker, when known.")]
	string? Hint = null,
	[property: Description("The guard's structured details, when it gave some.")]
	JsonElement? Details = null);
