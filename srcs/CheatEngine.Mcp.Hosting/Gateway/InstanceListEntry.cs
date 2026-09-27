using System.ComponentModel;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>One verified instance in the <c>instance_list</c> result.</summary>
/// <param name="InstanceId">The identifier every routed tool call names.</param>
/// <param name="Name">The display name; names may repeat across instances.</param>
/// <param name="ProcessId">The Cheat Engine process identifier.</param>
/// <param name="PluginVersion">The plugin version of the activation.</param>
internal sealed record InstanceListEntry(
	[property: Description("The id to pass as instanceId to every other tool; new on every plugin enable.")]
	string InstanceId,
	[property: Description("The display name; names may repeat across instances.")]
	string Name,
	[property: Description("The Cheat Engine process id, not the target's.")]
	int ProcessId,
	[property: Description("The plugin version of this activation.")]
	string PluginVersion);
