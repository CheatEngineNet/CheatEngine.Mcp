using System.ComponentModel;

using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Runtime;

/// <summary>One capability that the connected Cheat Engine host reported.</summary>
public sealed record RuntimeCapability(
	[property: Description("The Client capability name.")]
	string Name,
	[property: Description("The host-reported capability state.")]
	string State,
	[property: Description("Whether the capability is available to this activation.")]
	bool IsAvailable,
	[property: Description("Why the capability is unavailable, when the host explained it.")]
	string? Reason = null,
	[property: Description("The host evidence for the capability state, when present.")]
	string? Evidence = null);

/// <summary>
///     The <c>Mcp:Enable*</c> exposure switches of this activation. A tool whose switch is off stays listed but is
///     refused with <c>capability_disabled</c>; they change only when the plugin is disabled and enabled again.
/// </summary>
/// <param name="AutoAssembler">Whether <c>Mcp:EnableAutoAssembler</c> is on.</param>
/// <param name="UnsafeLua">Whether <c>Mcp:EnableUnsafeLua</c> is on.</param>
/// <param name="TargetCodeExecution">Whether <c>Mcp:EnableTargetCodeExecution</c> is on.</param>
/// <param name="KernelAccess">Whether <c>Mcp:EnableKernelAccess</c> is on.</param>
public sealed record RuntimeGates(
	[property: Description(
		"Whether Mcp:EnableAutoAssembler is on: callers may check and apply their own Auto Assembler scripts, " +
		"including Auto Assembler records. Template generation works either way.")]
	bool AutoAssembler,
	[property: Description(
		"Whether Mcp:EnableUnsafeLua is on: callers may run their own Lua and load tables that carry Lua. Fixed " +
		"tool Lua works either way; with this switch on, the other switches are advisory.")]
	bool UnsafeLua,
	[property: Description(
		"Whether Mcp:EnableTargetCodeExecution is on: tools may run code in the target or in Cheat Engine, such as " +
		"remote calls, DLL and .NET injection, C and C# compilation, Mono attach and invocation, and the speedhack.")]
	bool TargetCodeExecution,
	[property: Description(
		"Whether Mcp:EnableKernelAccess is on: tools may use the DBK kernel driver and DBVM for physical memory, " +
		"control and model-specific registers, and kernel-mode reads and writes.")]
	bool KernelAccess);

/// <summary>The stable identity, capability snapshot and exposure switches of this MCP activation.</summary>
public sealed record RuntimeInfoResult(
	[property: Description("The Client runtime epoch. It changes when the host runtime is replaced.")]
	long Epoch,
	[property: Description("The connected Cheat Engine host version.")]
	string? HostVersion,
	[property: Description("The host platform.")]
	string? Platform,
	[property: Description("The plugin assembly version, when available.")]
	string? PluginVersion,
	[property: Description("The loaded plugin assembly file name, without host directories.")]
	string PluginFileName,
	[property: Description("The runtime file name supplied by the plugin host, without host directories.")]
	string RuntimeFileName,
	[property: Description("The plugin application name.")]
	string ApplicationName,
	[property: Description("Every Client capability and its evidence.")]
	RuntimeCapability[] Capabilities,
	[property: Description(
		"The Mcp:Enable* switches of this activation, as booleans; runtime_get_info and runtime_get_overview always " +
		"report them. A tool whose switch is off stays listed but is refused with capability_disabled, and a " +
		"change applies only after the plugin is disabled and enabled again.")]
	RuntimeGates? Gates = null);

/// <summary>The selected target included in a runtime overview.</summary>
public sealed record RuntimeCurrentProcess(
	[property: Description("Whether Cheat Engine currently has a selected target.")]
	bool IsOpen,
	[property: Description("The selected process id, when a target is open.")]
	int? ProcessId = null,
	[property: Description("The selected process name, when a target is open.")]
	string? ProcessName = null,
	[property: Description("The configured target pointer width in bytes, when a target is open.")]
	int? PointerSize = null,
	[property: Description("The target-selection epoch, when a target is open.")]
	long? SelectionEpoch = null);

/// <summary>A compact, read-only summary for orientation before target work.</summary>
public sealed record RuntimeOverviewResult(
	[property: Description("The activation identity, host capability evidence and Mcp:Enable* switches.")]
	RuntimeInfoResult Runtime,
	[property: Description("The currently selected target, if any.")]
	RuntimeCurrentProcess Process,
	[property: Description("Resources that this activation or an earlier activation still reports.")]
	int ResourceCount,
	[property: Description("Retained jobs in this activation.")]
	int JobCount);

/// <summary>The resources that still hold state in Cheat Engine.</summary>
public sealed record RuntimeResourceList(
	[property: Description("Resources in reverse creation order, followed by unmanaged or orphaned Lua state.")]
	TargetResourceDescriptor[] Resources);

/// <summary>What a resource release request completed.</summary>
public sealed record RuntimeReleaseResourcesResult(
	[property: Description("Resources acknowledged after external manual recovery, before this release.")]
	TargetResourceDescriptor[] Acknowledged,
	[property:
		Description(
			"The completed release outcome. An incomplete cleanup is returned as a partial_effect error instead.")]
	ReleaseAllResult Release);

/// <summary>The retained jobs for this activation.</summary>
public sealed record RuntimeJobList(
	[property: Description("Jobs in creation order, including completed jobs until their TTL ends.")]
	JobStatus[] Jobs);
