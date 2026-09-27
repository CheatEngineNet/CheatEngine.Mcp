using System.ComponentModel;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>One resource and what releasing it did.</summary>
/// <param name="Resource">The resource, as it was described when the release ran.</param>
/// <param name="Release">What the release did.</param>
public sealed record ReleasedResource(
	[property: Description("The resource.")]
	TargetResourceDescriptor Resource,
	[property: Description("What releasing it did.")]
	ResourceReleaseOutcome Release);
