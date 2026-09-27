using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

using CheatEngine.Mcp.Hosting.Discovery;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     Confirms, over one authenticated loopback GET, that the backend behind a registry record is the activation the
///     record names. The gateway runs it before every routed call and for every <c>instance_list</c> candidate.
/// </summary>
internal sealed class InstanceIdentityVerifier(TimeProvider time) : IDisposable
{
	// An identity body is a few hundred bytes; a larger reply is not an identity.
	private const int MaximumResponseBytes = 64 * 1024;

	/// <summary>How long one identity check may take.</summary>
	internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	/// <summary>
	///     How long a successful identity remains useful to completion. Entries older than this are removed on the next
	///     successful verification, even when no completion request arrives.
	/// </summary>
	internal static readonly TimeSpan VerificationWindow = TimeSpan.FromSeconds(10);

	private readonly HttpClient _http = BackendHttp.CreateClient(MaximumResponseBytes);

	// Instance id to the timestamp of its last successful verification; ids only, never a record or a token.
	private readonly ConcurrentDictionary<string, long> _verified = new(StringComparer.Ordinal);

	public void Dispose()
	{
		_http.Dispose();
	}

	/// <summary>
	///     The instance ids whose identity was confirmed within <paramref name="window" />, ordered; the gateway completes
	///     <c>instanceId</c> from them without probing any backend.
	/// </summary>
	/// <param name="window">How recent a verification must be.</param>
	/// <returns>The ids, never names, endpoints or tokens.</returns>
	internal IReadOnlyList<string> RecentlyVerified(TimeSpan window)
	{
		long now = time.GetTimestamp();
		List<string> recent = [];
		foreach ((string instanceId, long verifiedAt) in _verified)
		{
			if (time.GetElapsedTime(verifiedAt, now) <= window)
			{
				recent.Add(instanceId);
			}
			else
			{
				_verified.TryRemove(new KeyValuePair<string, long>(instanceId, verifiedAt));
			}
		}

		recent.Sort(StringComparer.Ordinal);
		return recent;
	}

	/// <summary>Verifies the backend's identity against its registry record.</summary>
	/// <param name="instance">The registry record.</param>
	/// <param name="cancellationToken">The caller's cancellation, which is rethrown as is.</param>
	/// <exception cref="InstanceUnavailableException">The backend is unreachable, refused or reports another identity.</exception>
	internal async Task VerifyAsync(InstanceDescriptor instance, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(instance);
		try
		{
			await VerifyCoreAsync(instance, cancellationToken).ConfigureAwait(false);
		}
		catch (InstanceUnavailableException)
		{
			_verified.TryRemove(instance.InstanceId, out _);
			throw;
		}

		long verifiedAt = time.GetTimestamp();
		_verified[instance.InstanceId] = verifiedAt;
		PruneExpired(verifiedAt);
	}

	private void PruneExpired(long now)
	{
		foreach ((string instanceId, long verifiedAt) in _verified)
		{
			if (time.GetElapsedTime(verifiedAt, now) > VerificationWindow)
			{
				_verified.TryRemove(new KeyValuePair<string, long>(instanceId, verifiedAt));
			}
		}
	}

	private async Task VerifyCoreAsync(InstanceDescriptor instance, CancellationToken cancellationToken)
	{
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(Timeout);
		InstanceIdentity? identity;
		try
		{
			using HttpRequestMessage request = new(HttpMethod.Get, new Uri(new Uri(instance.Endpoint), "instance"));
			request.Headers.Authorization = BackendHttp.Bearer(instance.AccessToken);
			using HttpResponseMessage response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				throw new InstanceUnavailableException(
					$"The instance identity endpoint returned HTTP {(int) response.StatusCode}.");
			}

			identity = await response.Content
				.ReadFromJsonAsync(HostingJsonContext.Default.InstanceIdentity, timeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException exception)
		{
			throw new InstanceUnavailableException(
				$"The instance did not confirm its identity within {Timeout.TotalSeconds:0} s.", exception);
		}
		catch (HttpRequestException exception)
		{
			throw new InstanceUnavailableException(BackendHttp.Describe(exception), exception);
		}
		catch (Exception exception) when (exception is JsonException or NotSupportedException or IOException)
		{
			throw new InstanceUnavailableException("The instance returned an unreadable identity.", exception);
		}

		if (identity is null || identity != InstanceIdentity.From(instance))
		{
			throw new InstanceUnavailableException(
				"The instance identity no longer matches its registry record; the plugin was disabled or restarted.");
		}
	}
}
