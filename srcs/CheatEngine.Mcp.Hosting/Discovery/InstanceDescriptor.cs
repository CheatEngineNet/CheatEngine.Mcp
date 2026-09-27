namespace CheatEngine.Mcp.Hosting.Discovery;

/// <summary>An activation-specific local connection record. AccessToken is never part of public discovery results.</summary>
public sealed record InstanceDescriptor(
	string InstanceId,
	string Name,
	Guid ActivationId,
	int ProcessId,
	long ProcessStartUtcTicks,
	string Endpoint,
	string AccessToken,
	string PluginVersion);
