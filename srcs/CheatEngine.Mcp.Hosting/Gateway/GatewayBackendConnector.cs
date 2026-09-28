using CheatEngine.Mcp.Hosting.Discovery;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     Leases the pooled client of exactly the instance a request names, after re-reading the registry and verifying the
///     backend identity; tool calls and resource reads share it. It never retries and never falls back to another
///     instance.
/// </summary>
internal sealed class GatewayBackendConnector(
	InstanceRegistry registry,
	InstanceIdentityVerifier verifier,
	BackendConnectionPool pool)
{
	/// <summary>Connects to one instance.</summary>
	/// <param name="instanceId">The requested instance.</param>
	/// <param name="cancellationToken">The request's cancellation.</param>
	/// <returns>The lease, which the caller disposes and evicts on any failure.</returns>
	/// <exception cref="GatewayRoutingException">
	///     No single active record has the id, or the backend could not be verified or connected; nothing was sent.
	/// </exception>
	internal async Task<BackendConnectionPool.BackendLease> ConnectAsync(string instanceId,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(instanceId);
		// Re-read on every request: a withdrawn or republished record must never reach an old client.
		InstanceDescriptor[] records = registry.ReadActive(cancellationToken)
			.Where(candidate => string.Equals(candidate.InstanceId, instanceId, StringComparison.Ordinal)).ToArray();
		if (records is not [InstanceDescriptor instance])
		{
			pool.Evict(instanceId, "no single active record");
			throw new GatewayRoutingException(true,
				"No enabled Cheat Engine instance has this instanceId; it may have been disabled, enabled again or " +
				"closed.");
		}

		try
		{
			await verifier.VerifyAsync(instance, cancellationToken).ConfigureAwait(false);
		}
		catch (InstanceUnavailableException exception)
		{
			pool.Evict(instanceId, "identity check failed");
			throw new GatewayRoutingException(false, exception.Message, exception);
		}

		try
		{
			return await pool.RentAsync(instance, cancellationToken).ConfigureAwait(false);
		}
		catch (InstanceUnavailableException exception)
		{
			throw new GatewayRoutingException(false, exception.Message, exception);
		}
	}
}

/// <summary>
///     The gateway could not route a request to the instance it names; nothing reached a backend. The message is
///     caller-safe: it never carries a transport message or a token.
/// </summary>
internal sealed class GatewayRoutingException : Exception
{
	public GatewayRoutingException()
	{
	}

	public GatewayRoutingException(string message) : base(message)
	{
	}

	public GatewayRoutingException(string message, Exception? innerException) : base(message, innerException)
	{
	}

	/// <summary>Creates the failure.</summary>
	/// <param name="unknownInstance">Whether no single active registry record has the id.</param>
	/// <param name="message">The caller-safe reason.</param>
	/// <param name="innerException">The verification or connection failure.</param>
	internal GatewayRoutingException(bool unknownInstance, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		UnknownInstance = unknownInstance;
	}

	/// <summary>Whether no single active registry record has the id, as opposed to an unreachable backend.</summary>
	internal bool UnknownInstance
	{
		get;
	}
}
