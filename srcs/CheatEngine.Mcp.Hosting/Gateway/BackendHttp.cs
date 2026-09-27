using System.Net.Http.Headers;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>The only way the gateway builds HTTP clients for backends: loopback, no proxy, no redirects.</summary>
internal static class BackendHttp
{
	/// <summary>Creates a client that sends the bearer token only to the address it was given.</summary>
	/// <param name="maximumResponseBytes">The buffered response limit, or <see langword="null" /> for the default.</param>
	/// <returns>A client whose own timeout is infinite; callers bound every request with a token.</returns>
	internal static HttpClient CreateClient(int? maximumResponseBytes = null)
	{
		// Redirects and proxies would let a response or the environment send the bearer token somewhere else.
		HttpClient client = new(new SocketsHttpHandler
		{
			AllowAutoRedirect = false, UseProxy = false, PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30)
		}) { Timeout = Timeout.InfiniteTimeSpan };
		if (maximumResponseBytes is { } limit)
		{
			client.MaxResponseContentBufferSize = limit;
		}

		return client;
	}

	/// <summary>
	///     Whether an HTTP failure happened before the request reached the backend, so the backend cannot have acted on
	///     it.
	/// </summary>
	/// <param name="exception">The failure.</param>
	/// <returns><see langword="true" /> when no connection was established.</returns>
	internal static bool NeverReachedBackend(HttpRequestException exception)
	{
		return exception.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError
			or HttpRequestError.ProxyTunnelError or HttpRequestError.SecureConnectionError;
	}

	/// <summary>A caller-safe description built only from the failure's classification, never from its message.</summary>
	/// <param name="exception">The failure.</param>
	/// <returns>The description.</returns>
	internal static string Describe(HttpRequestException exception)
	{
		return exception switch
		{
			{ StatusCode: { } status } => $"The instance answered HTTP {(int) status}.",
			_ when NeverReachedBackend(exception) => "The instance refused the connection or is not listening.",
			{ HttpRequestError: HttpRequestError.ResponseEnded } =>
				"The instance closed the connection before answering.",
			_ => $"The connection to the instance failed ({exception.HttpRequestError})."
		};
	}

	/// <summary>The bearer authorization value for one backend.</summary>
	/// <param name="token">The registry record's access token.</param>
	/// <returns>The header value.</returns>
	internal static AuthenticationHeaderValue Bearer(string token)
	{
		return new AuthenticationHeaderValue("Bearer", token);
	}
}
