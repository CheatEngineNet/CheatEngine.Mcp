using System.Net.Http.Json;
using System.Text.Json;

using CheatEngine.Mcp.Hosting.Discovery;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     Confirms, over one authenticated loopback GET, that the backend behind a registry record is the activation the
///     record names. The gateway runs it before every routed call and for every <c>instance_list</c> candidate.
/// </summary>
internal sealed class InstanceIdentityVerifier : IDisposable
{
	// An identity body is a few hundred bytes; a larger reply is not an identity.
	private const int MaximumResponseBytes = 64 * 1024;

	/// <summary>How long one identity check may take.</summary>
	internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	private readonly HttpClient _http = BackendHttp.CreateClient(MaximumResponseBytes);

	public void Dispose()
	{
		_http.Dispose();
	}

	/// <summary>Verifies the backend's identity against its registry record.</summary>
	/// <param name="instance">The registry record.</param>
	/// <param name="cancellationToken">The caller's cancellation, which is rethrown as is.</param>
	/// <exception cref="InstanceUnavailableException">The backend is unreachable, refused or reports another identity.</exception>
	internal async Task VerifyAsync(InstanceDescriptor instance, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(instance);
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
