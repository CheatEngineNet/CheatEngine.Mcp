using System.Text.Json;

using CheatEngine.Client;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>
///     The activation's MCP-owned target resources: Client leases, jobs and effects recorded in the Lua state root. It
///     refuses an explicit target change while any of them, or any orphan an earlier activation left, still holds host
///     state, and it releases them newest first.
/// </summary>
/// <remarks>
///     <para>
///         The list is lock-protected because managed jobs end on thread-pool threads, but the lock only guards the list:
///         it is never held during a dispatch, a Client call or a Lua call. Members that take a
///         <see cref="CancellationToken" /> run inside a dispatch; the others are pure managed reads.
///     </para>
///     <para>
///         Ids read <c>kind-namespace-number</c>. With a <see cref="McpStateLedger" /> the namespace and numbering are the
///         ledger's, and the Lua state root adds the orphans of earlier activations, as well as any failed Lua cleanup
///         that awaits an acknowledgement, to the listing, the transition check and the release.
///     </para>
/// </remarks>
public sealed class TargetResources : ITargetTransitionGuard
{
	internal const string ReleaseHint =
		"Release them with runtime_release_resources before changing the target; acknowledge an entry that needs manual recovery once it is recovered.";

	private const int MaximumIdsInMessage = 8;
	private const int MaximumDescriptorText = LuaJobKernelScripts.MaximumTextBytes;

	private readonly List<Entry> _entries = [];
	private readonly Lock _lock = new();
	private readonly TimeProvider _time;
	private long _sequence;

	/// <summary>Creates the activation's resource list.</summary>
	/// <param name="ledger">The activation's Lua state ledger, or <see langword="null" /> for managed resources only.</param>
	/// <param name="time">The clock that dates new resources; the system clock by default.</param>
	public TargetResources(McpStateLedger? ledger = null, TimeProvider? time = null)
	{
		Ledger = ledger;
		_time = time ?? TimeProvider.System;
		Namespace = ledger?.Namespace ?? McpStateLedger.CreateNamespace();
	}

	/// <summary>The activation namespace that every id of this list carries.</summary>
	public string Namespace
	{
		get;
	}

	/// <summary>The activation's Lua state ledger, when there is one.</summary>
	public McpStateLedger? Ledger
	{
		get;
	}

	/// <summary>How many resources are tracked, ended ones included.</summary>
	public int Count
	{
		get
		{
			lock (_lock)
			{
				return _entries.Count;
			}
		}
	}

	/// <summary>
	///     Refuses a target change while a tracked resource or an unmanaged Lua state entry holds host state; ended
	///     resources that hold none are ignored, and a reselection of the selected process is always allowed. Inside the
	///     dispatch that changes the target.
	/// </summary>
	/// <param name="transition">The selected and the requested process.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <exception cref="CheatEngineToolException">
	///     <c>busy</c> with <c>not_started</c>; its details list the blocking resources (
	///     <see cref="TargetTransitionRefusal" />).
	/// </exception>
	public void EnsureCanChangeTarget(TargetTransition transition, CancellationToken cancellationToken)
	{
		if (transition.IsReselection)
		{
			return;
		}

		Entry[] entries = NewestFirst();
		LuaStateSnapshot? snapshot = Ledger?.Snapshot(cancellationToken);
		Observe(entries, snapshot);
		List<TargetResourceDescriptor> blockers =
		[
			.. entries.Where(static entry => entry.Resource.HoldsHostState)
				.Select(static entry => entry.Resource.Descriptor)
		];
		blockers.AddRange(Unmanaged(entries, snapshot));
		if (blockers.Count == 0)
		{
			return;
		}

		JsonElement details = JsonSerializer.SerializeToElement(new TargetTransitionRefusal(blockers),
			StateJsonContext.Default.TargetTransitionRefusal);
		string listed = string.Join(", ", blockers.Take(MaximumIdsInMessage).Select(static blocker => blocker.Id));
		string more = blockers.Count > MaximumIdsInMessage ? $" and {blockers.Count - MaximumIdsInMessage} more" : "";
		throw new CheatEngineToolException(new ToolError(ToolErrorKind.Busy,
			$"{blockers.Count} MCP-owned resource(s) still hold state in Cheat Engine or the target: {listed}{more}.",
			null, ToolHostEffect.NotStarted,
			ToolFailureMapping.IsRetryable(ToolErrorKind.Busy, ToolHostEffect.NotStarted), ReleaseHint, details));
	}

	/// <summary>Issues the next id of the activation for a resource or job of <paramref name="kind" />.</summary>
	/// <param name="kind">A lowercase identifier of at most 32 characters.</param>
	/// <returns>The id, <c>kind-namespace-number</c>.</returns>
	/// <exception cref="ArgumentException"><paramref name="kind" /> is not a valid kind.</exception>
	public string NextId(string kind)
	{
		if (Ledger is not null)
		{
			return Ledger.NextId(kind);
		}

		McpStateIds.CheckKind(kind);
		return McpStateIds.Format(kind, Namespace, Interlocked.Increment(ref _sequence));
	}

	/// <summary>Tracks a Client lease that a tool owns.</summary>
	/// <param name="lease">The Client lease.</param>
	/// <param name="kind">What it is, such as <c>allocation</c>, <c>patch</c>, <c>scan</c> or <c>symbol</c>.</param>
	/// <param name="onReleased">Removes the tool's own reference once the lease was released or forgotten here.</param>
	/// <param name="name">The owner-chosen name, if any; bounded, never target data.</param>
	/// <param name="address">The address as uppercase hexadecimal without <c>0x</c>, if any.</param>
	/// <param name="size">The size in bytes, if any.</param>
	/// <returns>The tracked resource.</returns>
	public ITargetResource Track(ICheatEngineLease lease, string kind, Action? onReleased = null, string? name = null,
		string? address = null, long? size = null)
	{
		ArgumentNullException.ThrowIfNull(lease);
		ClientLeaseResource resource = new(lease, new TargetResourceDescriptor(NextId(kind), kind,
			TargetResourceCategory.ClientLease, TargetResourceState.Active, _time.GetUtcNow(), Bounded(name),
			Bounded(address), size));
		Track(resource, onReleased);
		return resource;
	}

	/// <summary>
	///     Tracks an effect that a fixed script of this activation recorded in the Lua state root with
	///     <c>resourceRecord(namespace, id, kind, fields, release)</c>, under an id from <see cref="NextId" />. Releasing it
	///     runs the recorded release function once.
	/// </summary>
	/// <param name="id">The id the script recorded.</param>
	/// <param name="kind">The kind the script recorded, such as <c>speedhack</c>, <c>pause</c> or <c>breakpoint</c>.</param>
	/// <param name="onReleased">Removes the tool's own reference once the effect was released or forgotten here.</param>
	/// <param name="name">The owner-chosen name, if any.</param>
	/// <param name="address">The address as uppercase hexadecimal without <c>0x</c>, if any.</param>
	/// <param name="size">The size in bytes, if any.</param>
	/// <param name="detail">A short detail, such as a speed.</param>
	/// <returns>The tracked resource.</returns>
	/// <exception cref="InvalidOperationException">The list has no state ledger.</exception>
	/// <exception cref="ArgumentException">The id is not an id of this activation for <paramref name="kind" />.</exception>
	public ITargetResource TrackState(string id, string kind, Action? onReleased = null, string? name = null,
		string? address = null, long? size = null, string? detail = null)
	{
		McpStateLedger ledger = Ledger ?? throw new InvalidOperationException(
			"Recorded Lua state needs the activation's state ledger.");
		if (!McpStateIds.TryParse(id, out string parsedKind, out string parsedNamespace) ||
			!string.Equals(parsedKind, kind, StringComparison.Ordinal) ||
			!string.Equals(parsedNamespace, Namespace, StringComparison.Ordinal))
		{
			throw new ArgumentException("The id must be an id of this activation for the same kind.", nameof(id));
		}

		LuaStateResource resource = new(ledger, new TargetResourceDescriptor(id, kind,
			TargetResourceCategory.LuaState, TargetResourceState.Active, _time.GetUtcNow(), Bounded(name),
			Bounded(address), size, Bounded(detail)));
		Track(resource, onReleased);
		return resource;
	}

	/// <summary>Tracks a resource, newest last.</summary>
	/// <param name="resource">The resource.</param>
	/// <param name="onReleased">Runs once the resource was released or forgotten because it ended.</param>
	/// <exception cref="InvalidOperationException">The resource, or another with its id, is already tracked.</exception>
	public void Track(ITargetResource resource, Action? onReleased = null)
	{
		ArgumentNullException.ThrowIfNull(resource);
		// Tracking never calls into a Client lease: its descriptor reads the lease's state.
		string id = resource is ClientLeaseResource lease ? lease.Id : resource.Descriptor.Id;
		lock (_lock)
		{
			if (_entries.Exists(entry => ReferenceEquals(entry.Resource, resource) ||
										 string.Equals(entry.Id, id, StringComparison.Ordinal)))
			{
				throw new InvalidOperationException($"The resource {id} is already tracked.");
			}

			_entries.Add(new Entry(resource, id, onReleased));
		}
	}

	/// <summary>Stops tracking a resource that its owner released; its release callback does not run.</summary>
	/// <param name="resource">The resource.</param>
	/// <returns><see langword="true" /> when it was tracked.</returns>
	public bool Forget(ITargetResource resource)
	{
		ArgumentNullException.ThrowIfNull(resource);
		lock (_lock)
		{
			return _entries.RemoveAll(entry => ReferenceEquals(entry.Resource, resource)) > 0;
		}
	}

	/// <summary>Stops tracking a Client lease that its owner released; its release callback does not run.</summary>
	/// <param name="lease">The released lease.</param>
	/// <returns><see langword="true" /> when it was tracked.</returns>
	public bool Forget(ICheatEngineLease lease)
	{
		ArgumentNullException.ThrowIfNull(lease);
		lock (_lock)
		{
			return _entries.RemoveAll(entry =>
				entry.Resource is ClientLeaseResource client && ReferenceEquals(client.Lease, lease)) > 0;
		}
	}

	/// <summary>Stops tracking the resource with <paramref name="id" />; its release callback does not run.</summary>
	/// <param name="id">The resource id.</param>
	/// <returns><see langword="true" /> when it was tracked.</returns>
	public bool Forget(string id)
	{
		ArgumentNullException.ThrowIfNull(id);
		lock (_lock)
		{
			return _entries.RemoveAll(entry => string.Equals(entry.Id, id, StringComparison.Ordinal)) > 0;
		}
	}

	/// <summary>Describes the tracked resources, newest first, without any Cheat Engine call.</summary>
	/// <returns>The descriptors of this activation's resources; orphans need <see cref="ListAll" />.</returns>
	public IReadOnlyList<TargetResourceDescriptor> List()
	{
		return [.. NewestFirst().Select(static entry => entry.Resource.Descriptor)];
	}

	/// <summary>
	///     Describes the tracked resources, newest first, then the Lua state entries that hold host state and that no
	///     handle tracks: orphans of earlier activations (<see cref="TargetResourceDescriptor.Orphaned" />) and failed
	///     cleanups awaiting an acknowledgement. Inside a dispatch.
	/// </summary>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>The descriptors.</returns>
	public IReadOnlyList<TargetResourceDescriptor> ListAll(CancellationToken cancellationToken)
	{
		Entry[] entries = NewestFirst();
		LuaStateSnapshot? snapshot = Ledger?.Snapshot(cancellationToken);
		Observe(entries, snapshot);
		List<TargetResourceDescriptor> descriptors = [.. entries.Select(static entry => entry.Resource.Descriptor)];
		descriptors.AddRange(Unmanaged(entries, snapshot));
		return descriptors;
	}

	/// <summary>
	///     Forgets the tracked resources that ended outside a request, such as a lease that the Client ended, running their
	///     release callbacks. Inside a dispatch; it makes no Cheat Engine call for an ended Client lease.
	/// </summary>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>The forgotten resources and what ended them, newest first.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>partial_effect</c> when one of them ended without a complete cleanup; its details list them all, and they are
	///     forgotten anyway.
	/// </exception>
	public IReadOnlyList<ReleasedResource> ReportEndedResources(CancellationToken cancellationToken)
	{
		List<ReleasedResource> ended = [];
		ReleasedResource? incomplete = null;
		foreach (Entry entry in NewestFirst())
		{
			if (!entry.Resource.IsEnded)
			{
				continue;
			}

			ResourceReleaseOutcome outcome = entry.Resource.Release(cancellationToken);
			ReleasedResource item = new(entry.Resource.Descriptor, outcome);
			ended.Add(item);
			incomplete ??= outcome.IsComplete ? null : item;
			if (Remove(entry))
			{
				entry.OnReleased?.Invoke();
			}
		}

		if (incomplete is null)
		{
			return ended;
		}

		ReleaseAllResult details = new(ended, incomplete, []);
		throw CheatEngineToolException.PartialEffect(
			$"The resource {incomplete.Resource.Id} ended outside this request without a complete cleanup ({incomplete.Release.Kind}).",
			incomplete.Release.HostEffect, details, StateJsonContext.Default.ReleaseAllResult, false,
			"Recover it manually; it is no longer tracked.");
	}

	/// <summary>
	///     Releases every tracked resource newest first, because an older one may be a dependency of a newer one, and stops
	///     at the first release that does not complete. A retryable handle stays tracked for a later attempt; one that
	///     needs manual recovery is reported and forgotten. Then, through the ledger, it releases the unmanaged Lua state
	///     entries when <paramref name="includeOrphans" /> is set, or only lists them. Inside a dispatch; it never throws
	///     for a failed cleanup: use <see cref="ReleaseAllResult.ThrowIfIncomplete" /> to report an incomplete release.
	/// </summary>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <param name="includeOrphans">Whether to release the orphans of earlier activations and untracked Lua state too.</param>
	/// <returns>What was released, the first incomplete release and what still holds host state.</returns>
	public ReleaseAllResult ReleaseAll(CancellationToken cancellationToken, bool includeOrphans = false)
	{
		List<ReleasedResource> released = [];
		foreach (Entry entry in NewestFirst())
		{
			ITargetResource resource = entry.Resource;
			if (resource.IsEnded && !resource.HoldsHostState)
			{
				if (Remove(entry))
				{
					entry.OnReleased?.Invoke();
				}

				continue;
			}

			ResourceReleaseOutcome outcome = resource.Release(cancellationToken);
			ReleasedResource item = new(resource.Descriptor, outcome);
			if (!outcome.IsRetryable && Remove(entry))
			{
				entry.OnReleased?.Invoke();
			}

			if (!outcome.IsComplete)
			{
				// Older resources may be dependencies of this one: keep them until this failure is handled.
				return new ReleaseAllResult(released, item, RemainingManaged());
			}

			released.Add(item);
		}

		if (Ledger is null)
		{
			return new ReleaseAllResult(released, null, RemainingManaged());
		}

		LuaStateRelease state = Ledger.ReleaseUnmanaged(ManagedIds(), includeOrphans, cancellationToken);
		released.AddRange(state.Released.Select(entry =>
			new ReleasedResource(Ledger.Describe(entry), ResourceReleaseOutcome.Released())));
		ReleasedResource? failed = state.Failed is { } failure
			? new ReleasedResource(Ledger.Describe(failure), ResourceReleaseOutcome.CleanupFailed())
			: null;
		List<TargetResourceDescriptor> remaining = RemainingManaged();
		remaining.AddRange(state.Remaining.Select(Ledger.Describe));
		return new ReleaseAllResult(released, failed, remaining);
	}

	/// <summary>
	///     Forgets resources after a manual recovery, without running their cleanup: an ended tracked resource, or a Lua
	///     state entry that no handle tracks (an orphan, or a failed cleanup). The whole request is refused when an id is
	///     malformed, unknown, still running or still tracked while active. Inside a dispatch.
	/// </summary>
	/// <param name="ids">1 to <see cref="McpStateLedger.MaximumAcknowledgedIds" /> resource ids.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>The forgotten resources, as they were described before.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>invalid_argument</c>, <c>not_found</c> or <c>invalid_state</c>, all with <c>not_started</c>.
	/// </exception>
	public IReadOnlyList<TargetResourceDescriptor> Acknowledge(IReadOnlyCollection<string> ids,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(ids);
		const string parameter = "acknowledgeIds";
		if (ids.Count is < 1 or > McpStateLedger.MaximumAcknowledgedIds)
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"list 1 to {McpStateLedger.MaximumAcknowledgedIds} resource ids.");
		}

		HashSet<string> distinct = new(StringComparer.Ordinal);
		foreach (string id in ids)
		{
			if (!McpStateIds.TryParse(id, out _, out _))
			{
				throw CheatEngineToolException.InvalidArgument(parameter,
					"every id must read kind-namespace-number, as runtime_list_resources reports it.");
			}

			if (!distinct.Add(id))
			{
				throw CheatEngineToolException.InvalidArgument(parameter, $"{id} is listed twice.");
			}
		}

		Entry[] entries = NewestFirst();
		List<Entry> managed = [];
		List<string> unmanaged = [];
		foreach (string id in distinct)
		{
			Entry? entry = Array.Find(entries, candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
			if (entry is null)
			{
				unmanaged.Add(id);
			}
			else if (!entry.Resource.IsEnded)
			{
				throw CheatEngineToolException.InvalidState($"{id} is still tracked and active.",
					"Release it with runtime_release_resources instead.");
			}
			else
			{
				managed.Add(entry);
			}
		}

		List<TargetResourceDescriptor> acknowledged = [];
		if (unmanaged.Count > 0)
		{
			if (Ledger is null)
			{
				throw CheatEngineToolException.NotFound($"No resource has the id {unmanaged[0]}.",
					"List them with runtime_list_resources.");
			}

			LuaStateAcknowledgement state = Ledger.Acknowledge(unmanaged, ManagedIds(), cancellationToken);
			acknowledged.AddRange(state.Acknowledged.Select(Ledger.Describe));
		}

		foreach (Entry entry in managed)
		{
			acknowledged.Add(entry.Resource.Descriptor);
			if (Remove(entry))
			{
				entry.OnReleased?.Invoke();
			}
		}

		return acknowledged;
	}

	/// <summary>Keeps descriptor text bounded, as the Lua ledger does.</summary>
	private static string? Bounded(string? text)
	{
		return text is { Length: > MaximumDescriptorText } ? text[..MaximumDescriptorText] : text;
	}

	private static void Observe(Entry[] entries, LuaStateSnapshot? snapshot)
	{
		if (snapshot is null || snapshot.Truncated)
		{
			// A truncated snapshot cannot prove that an entry is gone.
			return;
		}

		Dictionary<string, LuaStateEntry> byId = snapshot.Entries.ToDictionary(static entry => entry.Id,
			StringComparer.Ordinal);
		foreach (Entry entry in entries)
		{
			if (entry.Resource is ILuaStateBacked backed)
			{
				backed.Observe(byId.GetValueOrDefault(backed.StateId));
			}
		}
	}

	private IEnumerable<TargetResourceDescriptor> Unmanaged(Entry[] entries, LuaStateSnapshot? snapshot)
	{
		if (snapshot is null || Ledger is null)
		{
			return [];
		}

		HashSet<string> managed = [.. entries.Select(static entry => entry.Id)];
		return snapshot.Entries
			.Where(entry => entry.HoldsHostState &&
							!(string.Equals(entry.Namespace, Namespace, StringComparison.Ordinal) &&
							  managed.Contains(entry.Id)))
			.Select(Ledger.Describe);
	}

	private List<TargetResourceDescriptor> RemainingManaged()
	{
		return
		[
			.. NewestFirst().Where(static entry => entry.Resource.HoldsHostState)
				.Select(static entry => entry.Resource.Descriptor)
		];
	}

	private string[] ManagedIds()
	{
		lock (_lock)
		{
			return [.. _entries.Select(static entry => entry.Id)];
		}
	}

	private Entry[] NewestFirst()
	{
		lock (_lock)
		{
			Entry[] entries = [.. _entries];
			Array.Reverse(entries);
			return entries;
		}
	}

	private bool Remove(Entry entry)
	{
		lock (_lock)
		{
			return _entries.Remove(entry);
		}
	}

	private sealed record Entry(ITargetResource Resource, string Id, Action? OnReleased);
}
