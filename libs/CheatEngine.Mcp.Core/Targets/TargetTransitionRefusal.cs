using System.ComponentModel;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>The details of a target change refused because owned resources still hold host state.</summary>
/// <param name="Resources">The resources that block the change, newest first.</param>
public sealed record TargetTransitionRefusal(
	[property: Description("The resources that block the change, newest first.")]
	IReadOnlyList<TargetResourceDescriptor> Resources);
