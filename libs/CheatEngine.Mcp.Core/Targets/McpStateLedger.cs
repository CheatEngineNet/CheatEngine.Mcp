using System.Globalization;

using CheatEngine.Mcp.Core.Execution;
using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>
///     The activation's view of the one Lua state root, <c>__cheatengine_mcp_state</c>, that Core owns in Cheat Engine's
///     Lua: per activation namespace, the Lua jobs and the recorded effects that no Client lease covers (a speedhack other
///     than 1, a pause, MCP-set breakpoints, Mono hooks). The root outlives a plugin disable, so the next activation sees
///     what an earlier one left as orphans.
/// </summary>
/// <remarks>
///     <para>
///         Only the fixed scripts of <see cref="LuaJobKernelScripts" /> touch the root. Every member that reads or changes
///         it runs inside a dispatch through <see cref="ToolDispatch.ExecuteLua{T}" />; primitives reach it through
///         <see cref="TargetResources" /> and <see cref="JobRegistry" />.
///     </para>
///     <para>
///         During disable, <see cref="JobRegistry" /> runs each retained Lua job's fixed, bounded stop hook through the
///         Client dispatcher. A failed cleanup remains in this root, so the next activation can release or acknowledge
///         it.
///     </para>
/// </remarks>
public sealed class McpStateLedger
{
	/// <summary>How many ids one acknowledgement accepts.</summary>
	public const int MaximumAcknowledgedIds = 64;

	/// <summary>The caller-safe description of a Lua cleanup failure.</summary>
	internal const string CleanupFailedMessage = "The recorded cleanup did not complete.";

	private long _sequence;

	/// <summary>Creates the activation's ledger with a fresh namespace; nothing touches Cheat Engine until it is used.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="time">The clock that dates the Lua entries.</param>
	public McpStateLedger(ToolDispatch dispatch, TimeProvider time)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(time);
		Dispatch = dispatch;
		Time = time;
		Namespace = CreateNamespace();
	}

	/// <summary>The activation's namespace: eight lowercase hexadecimal digits, part of every id it issues.</summary>
	public string Namespace
	{
		get;
	}

	/// <summary>The activation's dispatch facade, for jobs that dispatch their own polls.</summary>
	internal ToolDispatch Dispatch
	{
		get;
	}

	/// <summary>The clock that dates the Lua entries.</summary>
	internal TimeProvider Time
	{
		get;
	}

	/// <summary>Issues the next id of the activation, <c>kind-namespace-number</c>, for a resource or a job.</summary>
	/// <param name="kind">A lowercase identifier of at most 32 characters, such as <c>capture</c> or <c>speedhack</c>.</param>
	/// <returns>The id.</returns>
	/// <exception cref="ArgumentException"><paramref name="kind" /> is not a valid kind.</exception>
	public string NextId(string kind)
	{
		McpStateIds.CheckKind(kind);
		return McpStateIds.Format(kind, Namespace, Interlocked.Increment(ref _sequence));
	}

	/// <summary>A fresh activation namespace.</summary>
	/// <returns>Eight lowercase hexadecimal digits.</returns>
	internal static string CreateNamespace()
	{
		return Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
	}

	/// <summary>Describes a Lua entry for callers; an entry of another namespace is an orphan.</summary>
	/// <param name="entry">The entry.</param>
	/// <returns>The descriptor.</returns>
	internal TargetResourceDescriptor Describe(LuaStateEntry entry)
	{
		TargetResourceState state = entry.CleanupError is not null
			? TargetResourceState.CleanupFailed
			: entry.HoldsHostState
				? TargetResourceState.Active
				: TargetResourceState.Ended;
		return new TargetResourceDescriptor(entry.Id, entry.Kind,
			entry.IsJob ? TargetResourceCategory.Job : TargetResourceCategory.LuaState, state,
			Time.GetUtcNow() - TimeSpan.FromMilliseconds(entry.AgeMs), entry.Name, entry.Address, entry.Size,
			entry.Detail, entry.ProcessId, !string.Equals(entry.Namespace, Namespace, StringComparison.Ordinal),
			PublicCleanupError(entry.CleanupError), entry.CleanupError is not null);
	}

	/// <summary>Maps untrusted Lua failure text to the bounded public contract.</summary>
	/// <param name="cleanupError">The internal Lua failure text.</param>
	/// <returns>A caller-safe cleanup status.</returns>
	internal static string? PublicCleanupError(string? cleanupError)
	{
		return cleanupError is null ? null : CleanupFailedMessage;
	}

	/// <summary>Reads every entry of the root, holders first, then newest first. Inside a dispatch.</summary>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>The bounded snapshot.</returns>
	internal LuaStateSnapshot Snapshot(CancellationToken cancellationToken)
	{
		return Dispatch.ExecuteLua("mcp_state_snapshot", LuaJobKernelScripts.StateSnapshot,
			StateJsonContext.Default.LuaStateSnapshot, cancellationToken, Namespace);
	}

	/// <summary>
	///     Releases, newest first, the entries that hold host state and that no managed handle tracks: every entry of an
	///     earlier activation and any untracked entry of this one. It stops at the first failed cleanup, which stays
	///     recorded for manual recovery. With <paramref name="release" /> false it only lists them. Inside a dispatch.
	/// </summary>
	/// <param name="managedIds">The ids this activation tracks in <see cref="TargetResources" />.</param>
	/// <param name="release">Whether to release them or only list them.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>What was released and what remains.</returns>
	internal LuaStateRelease ReleaseUnmanaged(IReadOnlyCollection<string> managedIds, bool release,
		CancellationToken cancellationToken)
	{
		return Dispatch.ExecuteLua("mcp_state_release", LuaJobKernelScripts.StateReleaseUnmanaged,
			StateJsonContext.Default.LuaStateRelease, cancellationToken, Namespace, managedIds, release);
	}

	/// <summary>
	///     Forgets entries after a manual recovery, without running their cleanup: a failed cleanup, an orphaned effect or an
	///     ended job. It refuses the whole request when an id is unknown, still running or tracked by this activation.
	///     Inside a dispatch.
	/// </summary>
	/// <param name="ids">The ids to forget, 1 to <see cref="MaximumAcknowledgedIds" />, each valid and distinct.</param>
	/// <param name="managedIds">The ids this activation tracks in <see cref="TargetResources" />.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>The forgotten entries.</returns>
	internal LuaStateAcknowledgement Acknowledge(IReadOnlyCollection<string> ids,
		IReadOnlyCollection<string> managedIds, CancellationToken cancellationToken)
	{
		return Dispatch.ExecuteLua("mcp_state_acknowledge", LuaJobKernelScripts.StateAcknowledge,
			StateJsonContext.Default.LuaStateAcknowledgement, cancellationToken, Namespace, ids, managedIds);
	}

	/// <summary>Runs the recorded release of one resource of this activation, once. Inside a dispatch.</summary>
	/// <param name="id">The resource id.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>What the release did.</returns>
	internal LuaResourceRelease ReleaseResource(string id, CancellationToken cancellationToken)
	{
		return Dispatch.ExecuteLua("mcp_state_release_resource", LuaJobKernelScripts.StateReleaseResource,
			StateJsonContext.Default.LuaResourceRelease, cancellationToken, Namespace, id);
	}

	/// <summary>Reads one page of a Lua job of this activation. Inside a dispatch.</summary>
	/// <param name="id">The job id.</param>
	/// <param name="afterSequence">The cursor.</param>
	/// <param name="limit">The page size.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>The page; an unknown job is the declared <c>not_found</c> error.</returns>
	internal LuaJobPage PollJob(string id, long afterSequence, int limit, CancellationToken cancellationToken)
	{
		return Dispatch.ExecuteLua("mcp_job_poll", LuaJobKernelScripts.Poll, StateJsonContext.Default.LuaJobPage,
			cancellationToken, Namespace, id, afterSequence, limit);
	}

	/// <summary>Reads the status of every Lua job of this activation. Inside a dispatch.</summary>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>The statuses.</returns>
	internal LuaJobStatuses JobStatuses(CancellationToken cancellationToken)
	{
		return Dispatch.ExecuteLua("mcp_job_status", LuaJobKernelScripts.Statuses,
			StateJsonContext.Default.LuaJobStatuses, cancellationToken, Namespace);
	}

	/// <summary>Stops a Lua job of this activation and discards its results. Inside a dispatch.</summary>
	/// <param name="id">The job id.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>What the stop did.</returns>
	internal LuaJobStop StopJob(string id, CancellationToken cancellationToken)
	{
		return Dispatch.ExecuteLua("mcp_job_stop", LuaJobKernelScripts.Stop, StateJsonContext.Default.LuaJobStop,
			cancellationToken, Namespace, id);
	}

	/// <summary>
	///     Ends expired jobs and resources in every namespace and removes the emptied namespaces of earlier activations, as
	///     the root's timer does every second. Inside a dispatch.
	/// </summary>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>What the sweep did.</returns>
	internal LuaStateSweep Sweep(CancellationToken cancellationToken)
	{
		return Dispatch.ExecuteLua("mcp_state_sweep", LuaJobKernelScripts.Sweep,
			StateJsonContext.Default.LuaStateSweep, cancellationToken, Namespace);
	}
}
