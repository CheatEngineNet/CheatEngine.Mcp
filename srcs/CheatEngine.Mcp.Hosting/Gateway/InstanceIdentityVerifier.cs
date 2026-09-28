using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

using CheatEngine.Mcp.Hosting.Discovery;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     Confirms, over one authenticated loopback GET, that the backend behind a registry record is the activation the
///     record names. The gateway runs it before every routed call and for every <c>instance_list</c> candidate.
/// </summary>
/// <remarks>
///     It remembers two things, ids and identities only, never a record, an endpoint or a token: when each id was last
///     verified (<see cref="RecentlyVerified" />, which completion uses), and the identity each backend last confirmed
///     (<see cref="IsConfirmed" />, which the resource list uses). A failed check forgets both.
/// </remarks>
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

	// Instance id to the identity its backend last confirmed, and when; it does not expire with the window, because an
	// activation's identity never changes while its record stays active.
	private readonly ConcurrentDictionary<string, Confirmation> _confirmed = new(StringComparer.Ordinal);

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

	/// <summary>
	///     Whether the backend of this record confirmed exactly its identity (id, activation, process, start time and
	///     plugin version) and no check failed since, however long ago; it never contacts the backend.
	/// </summary>
	/// <param name="instance">An active registry record.</param>
	/// <returns><see langword="true" /> when the last check of this id confirmed this identity.</returns>
	internal bool IsConfirmed(InstanceDescriptor instance)
	{
		ArgumentNullException.ThrowIfNull(instance);
		return _confirmed.TryGetValue(instance.InstanceId, out Confirmation? confirmation) &&
			   confirmation.Identity == InstanceIdentity.From(instance);
	}

	/// <summary>Forgets the confirmed identity of every id whose registry record is no longer active.</summary>
	/// <param name="active">The ids of the active registry records.</param>
	/// <param name="readAt">
	///     A timestamp taken before the registry was read: an identity confirmed after it is kept, since its record may
	///     be newer than the read.
	/// </param>
	internal void ForgetInactive(IReadOnlySet<string> active, long readAt)
	{
		ArgumentNullException.ThrowIfNull(active);
		foreach ((string instanceId, Confirmation confirmation) in _confirmed)
		{
			if (!active.Contains(instanceId) && confirmation.ConfirmedAt < readAt)
			{
				_confirmed.TryRemove(new KeyValuePair<string, Confirmation>(instanceId, confirmation));
			}
		}
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
			_confirmed.TryRemove(instance.InstanceId, out _);
			throw;
		}

		long verifiedAt = time.GetTimestamp();
		_verified[instance.InstanceId] = verifiedAt;
		_confirmed[instance.InstanceId] = new Confirmation(InstanceIdentity.From(instance), verifiedAt);
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

	/// <summary>An identity a backend confirmed.</summary>
	/// <param name="Identity">The confirmed identity, which never carries the endpoint or the token.</param>
	/// <param name="ConfirmedAt">The <see cref="TimeProvider" /> timestamp of the confirmation.</param>
	private sealed record Confirmation(InstanceIdentity Identity, long ConfirmedAt);
}
