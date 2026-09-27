namespace CheatEngine.Mcp.Hosting.Discovery;

/// <summary>
///     What a backend's <c>/instance</c> endpoint reports and the gateway compares with the registry record before it
///     routes. It never carries the access token or the endpoint.
/// </summary>
/// <param name="InstanceId">The immutable routing identifier.</param>
/// <param name="ActivationId">The activation that published the record.</param>
/// <param name="ProcessId">The Cheat Engine process identifier.</param>
/// <param name="ProcessStartUtcTicks">The process start time, which distinguishes a reused process identifier.</param>
/// <param name="PluginVersion">The plugin version of the activation.</param>
internal sealed record InstanceIdentity(
	string InstanceId,
	Guid ActivationId,
	int ProcessId,
	long ProcessStartUtcTicks,
	string PluginVersion)
{
	/// <summary>The identity a registry record claims.</summary>
	/// <param name="descriptor">The registry record.</param>
	/// <returns>The record's identity fields.</returns>
	internal static InstanceIdentity From(InstanceDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		return new InstanceIdentity(descriptor.InstanceId, descriptor.ActivationId, descriptor.ProcessId,
			descriptor.ProcessStartUtcTicks, descriptor.PluginVersion);
	}
}
