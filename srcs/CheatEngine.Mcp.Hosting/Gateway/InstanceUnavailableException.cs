namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     The gateway could not reach or verify a backend. Its message is caller-safe: it is built from fixed text and
///     failure classifications, never from a transport message, so it can never carry a token.
/// </summary>
internal sealed class InstanceUnavailableException : Exception
{
	public InstanceUnavailableException()
	{
	}

	public InstanceUnavailableException(string message) : base(message)
	{
	}

	public InstanceUnavailableException(string message, Exception? innerException) : base(message, innerException)
	{
	}
}
