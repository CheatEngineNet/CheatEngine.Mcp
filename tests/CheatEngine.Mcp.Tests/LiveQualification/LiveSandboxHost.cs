namespace CheatEngine.Mcp.Tests.LiveQualification;

internal sealed record LiveSandboxHost(
	string Name,
	int ProcessId,
	int TargetProcessId,
	string TargetAddress,
	string PointerRootAddress,
	string PointerTargetAddress,
	string ZeroPointerRootAddress,
	string UnreadableRootAddress,
	string PluginPath,
	string InstanceId,
	string Endpoint,
	int TargetIncarnation = 0,
	string ManagedObjectAddress = "",
	string ManagedObjectType = "",
	int? ManagedObjectMarker = null,
	int? ManagedObjectMirror = null,
	int? ManagedObjectToggle = null);
