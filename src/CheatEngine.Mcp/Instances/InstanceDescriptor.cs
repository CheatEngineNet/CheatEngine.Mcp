namespace CheatEngine.Mcp.Instances;

/// <summary>An activation-specific local connection record. AccessToken is never part of public discovery results.</summary>
internal sealed record InstanceDescriptor(string InstanceId, string Name, Guid ActivationId, int ProcessId,
	long ProcessStartUtcTicks, string Endpoint, string AccessToken, string PluginVersion);
