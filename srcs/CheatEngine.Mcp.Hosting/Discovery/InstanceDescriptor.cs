using System.Text;

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
	string PluginVersion)
{
	// The synthesized ToString, which assertion messages and diagnostics print, never shows the token.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("InstanceId = ").Append(InstanceId)
			.Append(", Name = ").Append(Name)
			.Append(", ActivationId = ").Append(ActivationId)
			.Append(", ProcessId = ").Append(ProcessId)
			.Append(", ProcessStartUtcTicks = ").Append(ProcessStartUtcTicks)
			.Append(", Endpoint = ").Append(Endpoint)
			.Append(", AccessToken = [redacted]")
			.Append(", PluginVersion = ").Append(PluginVersion);
		return true;
	}
}
