using System.ComponentModel;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>The <c>instance_list</c> result: the verified instances, never their tokens or endpoints.</summary>
/// <param name="Instances">The instances whose identity matched their registry record.</param>
/// <param name="DiscoveryIncomplete">Whether the discovery budget expired before every candidate was verified.</param>
internal sealed record InstanceListResult(
	[property: Description("The instances whose backend confirmed its registry identity, ordered by name.")]
	InstanceListEntry[] Instances,
	[property: Description("True when discovery stopped at its 10 s budget; call " + CheatEngineToolNames.InstanceList +
						   " again before "
						   + "concluding that an instance is missing.")]
	bool DiscoveryIncomplete);
