namespace CheatEngine.Mcp.Tests.LiveQualification;

internal sealed record LiveSandboxHost(
	string Name,
	int ProcessId,
	int TargetProcessId,
	string TargetAddress,
	string PluginPath,
	string InstanceId,
	string Endpoint);
