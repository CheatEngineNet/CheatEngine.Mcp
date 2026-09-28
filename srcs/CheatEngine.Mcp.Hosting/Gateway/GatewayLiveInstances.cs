using CheatEngine.Mcp.Hosting.Discovery;

using Microsoft.Extensions.Logging;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     Decides whose live resources the gateway's <c>resources/list</c> shows, and when a client's listing is out of
///     date. An instance is listed while its registry record is active, single and alive, and its backend confirmed
///     that record's identity (through <c>instance_list</c>, reading <c>cheatengine://instances</c>, a routed call or a
///     routed read). It reads the registry and the verifier's cache only: it never contacts a backend.
/// </summary>
/// <remarks>
///     It remembers the distinct listings served since the last change notification (normally one).
///     <see cref="GatewayResourceListMonitor" /> compares them with the current set through <see cref="PollChange" />,
///     which reports a change once the set differs from any served listing (a client may still hold that one) and
///     stayed the same for one poll; nothing is read while no listing is outstanding. The rule errs toward one extra
///     notification: a client that listed again before a change settled is notified although its latest listing is
///     current, and never misses a change another listing still shows.
/// </remarks>
internal sealed partial class GatewayLiveInstances(
	InstanceRegistry registry,
	InstanceIdentityVerifier verifier,
	TimeProvider time,
	ILogger<GatewayLiveInstances> logger)
{
	private readonly Lock _gate = new();

	// The distinct listings served since the last change was reported.
	private readonly List<InstanceListEntry[]> _served = [];

	// The set the previous poll read while a listing was outstanding.
	private InstanceListEntry[]? _observed;

	/// <summary>
	///     Reads the instances a <c>resources/list</c> answer lists, and remembers that answer as served.
	/// </summary>
	/// <param name="cancellationToken">The request's cancellation.</param>
	/// <returns>The instances to list, ordered by name, then id.</returns>
	internal IReadOnlyList<InstanceListEntry> ListForClient(CancellationToken cancellationToken)
	{
		InstanceListEntry[] listed = Read(cancellationToken);
		lock (_gate)
		{
			if (!_served.Exists(served => served.SequenceEqual(listed)))
			{
				_served.Add(listed);
			}
		}

		return listed;
	}

	/// <summary>
	///     Reads the instances whose live resources are listed, and lets the verifier forget the identities of records
	///     that are gone.
	/// </summary>
	/// <param name="cancellationToken">The caller's cancellation.</param>
	/// <returns>The instances, ordered by name, then id; none when the registry cannot be read.</returns>
	internal InstanceListEntry[] Read(CancellationToken cancellationToken)
	{
		long readAt = time.GetTimestamp();
		IReadOnlyList<InstanceDescriptor> active;
		try
		{
			active = registry.ReadActive(cancellationToken);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// Listing must never fail the documents with it; nothing is forgotten on a failed read.
			string failure = exception.GetType().Name;
			LogRegistryUnreadable(logger, failure);
			return [];
		}

		verifier.ForgetInactive(active.Select(static instance => instance.InstanceId)
			.ToHashSet(StringComparer.Ordinal), readAt);
		return
		[
			.. active.GroupBy(static instance => instance.InstanceId, StringComparer.Ordinal)
				.Where(static records => records.Count() == 1)
				.Select(static records => records.First())
				.Where(verifier.IsConfirmed)
				.Select(static instance => new InstanceListEntry(instance.InstanceId, instance.Name,
					instance.ProcessId, instance.PluginVersion))
		];
	}

	/// <summary>
	///     Compares the listed set with the listings served since the last reported change, once. A change is reported
	///     when the set differs from at least one of them and the previous poll read the same set; the served listings
	///     are then forgotten, so the next change is reported only after a client lists again.
	/// </summary>
	/// <param name="cancellationToken">The caller's cancellation.</param>
	/// <returns><see langword="true" /> when the client must be told that the list changed.</returns>
	internal bool PollChange(CancellationToken cancellationToken)
	{
		lock (_gate)
		{
			if (_served.Count == 0)
			{
				_observed = null;
				return false;
			}
		}

		InstanceListEntry[] current = Read(cancellationToken);
		lock (_gate)
		{
			bool changed = !_served.TrueForAll(served => served.SequenceEqual(current));
			bool settled = _observed is not null && _observed.SequenceEqual(current);
			_observed = current;
			if (!changed || !settled)
			{
				return false;
			}

			_served.Clear();
			_observed = null;
			return true;
		}
	}

	[LoggerMessage(Level = LogLevel.Debug,
		Message = "The instance registry could not be read for the resource list ({FailureType}).")]
	private static partial void LogRegistryUnreadable(ILogger logger, string failureType);
}
