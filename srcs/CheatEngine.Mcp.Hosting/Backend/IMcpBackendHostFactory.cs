namespace CheatEngine.Mcp.Hosting.Backend;

/// <summary>Creates the activation's backend from its options, log sink and composed primitives.</summary>
public interface IMcpBackendHostFactory
{
	/// <summary>The configured listener address, reported when a start fails.</summary>
	public string BaseUrl
	{
		get;
	}

	/// <summary>Creates an unstarted backend with a fresh discovery publication.</summary>
	/// <param name="targets">The activation's primitive instances, which the backend borrows and never disposes.</param>
	/// <param name="stopping">Cancelled when the activation starts to deactivate; the backend then answers 503.</param>
	/// <returns>The backend host.</returns>
	public IMcpBackendHost Create(McpPrimitiveTargets targets, CancellationToken stopping);
}
