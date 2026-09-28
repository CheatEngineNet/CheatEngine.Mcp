using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     Sends <c>notifications/resources/list_changed</c> when the instances whose live resources the gateway lists
///     change: every <see cref="PollInterval" />, <see cref="GatewayLiveInstances.PollChange" /> compares them with the
///     listings a client received, without any identity check, and reports a change once it has lasted one interval.
/// </summary>
/// <remarks>
///     The SDK delivers the notification when the server's resource collection changes: as a session broadcast before
///     protocol 2026-07-28, only over a <c>subscriptions/listen</c> stream that asked for it from then on, and never on
///     a stateless transport, exactly as it advertises <c>resources.listChanged</c>. Adding and removing an unlisted
///     marker inside one deferral scope raises that single change through the public collection API.
/// </remarks>
internal sealed partial class GatewayResourceListMonitor(
	GatewayLiveInstances live,
	IOptions<McpServerOptions> server,
	TimeProvider time,
	ILogger<GatewayResourceListMonitor> logger) : BackgroundService
{
	/// <summary>How often an outstanding listing is compared; a change must also last one interval.</summary>
	internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

	private readonly ListChangedMarker _marker = new();

	/// <summary>Compares the listed instances with the client's listings once, and notifies a settled change.</summary>
	/// <param name="cancellationToken">The monitor's cancellation.</param>
	/// <returns><see langword="true" /> when this poll raised <c>notifications/resources/list_changed</c>.</returns>
	internal bool Poll(CancellationToken cancellationToken)
	{
		if (!live.PollChange(cancellationToken))
		{
			return false;
		}

		// Without a collection the SDK advertises no resources.listChanged; the gateway's setup always creates one.
		if (server.Value.ResourceCollection is { } resources)
		{
			using (resources.DeferChangedEvents())
			{
				if (resources.TryAdd(_marker))
				{
					resources.Remove(_marker);
				}
			}
		}

		LogListChanged(logger);
		return true;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using PeriodicTimer timer = new(PollInterval, time);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
			{
				try
				{
					Poll(stoppingToken);
				}
				catch (Exception exception) when (exception is not OperationCanceledException)
				{
					// A failed poll must never stop the gateway; the next one tries again.
					string failure = exception.GetType().Name;
					LogPollFailed(logger, failure);
				}
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// The gateway is stopping.
		}
	}

	[LoggerMessage(Level = LogLevel.Debug,
		Message = "The listed instances changed; notifications/resources/list_changed was raised.")]
	private static partial void LogListChanged(ILogger logger);

	[LoggerMessage(Level = LogLevel.Warning,
		Message = "Comparing the listed instances failed ({FailureType}); the next poll tries again.")]
	private static partial void LogPollFailed(ILogger logger, string failureType);

	/// <summary>
	///     The resource the monitor adds and removes to raise one collection change. It is never listed (it has no
	///     listing entry and no template variable), matches no URI and answers an exact read as not found, like the
	///     router would: its URI is in the gateway's reserved space, which no composed resource may use.
	/// </summary>
	private sealed class ListChangedMarker : McpServerResource
	{
		private const string MarkerUri = McpResourceUris.GatewayInstances + "?listChanged";

		public override ResourceTemplate ProtocolResourceTemplate
		{
			get;
		} = new()
		{
			UriTemplate = MarkerUri,
			Name = "list_changed_marker"
		};

		public override Resource? ProtocolResource => null;

		public override IReadOnlyList<object> Metadata => [];

		public override bool IsMatch(string uri)
		{
			return false;
		}

		public override ValueTask<ReadResourceResult> ReadAsync(RequestContext<ReadResourceRequestParams> request,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);
			throw GatewayErrors.ResourceNotFound(request.Params?.Uri ?? MarkerUri,
				"List the resources and resource templates, and use their URIs exactly.",
				request.Server.NegotiatedProtocolVersion);
		}
	}
}
