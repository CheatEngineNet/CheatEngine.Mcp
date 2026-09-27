using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Execution;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>Releases MCP-owned target resources in reverse creation order before an explicit process change.</summary>
public sealed class TargetResources
{
	private readonly List<Entry> _entries = [];

	/// <summary>Tracks a Client lease owned by a tool, with the tool-side removal and a bounded recovery descriptor.</summary>
	/// <param name="lease">The Client lease.</param>
	/// <param name="remove">Removes the tool's own reference once the lease is released.</param>
	/// <param name="recovery">A bounded description reported when manual recovery is needed.</param>
	public void Track(ICheatEngineLease lease, Action remove, object? recovery = null)
	{
		_entries.Add(new Entry(lease, remove, recovery));
	}

	/// <summary>Stops tracking a lease that its owner released.</summary>
	/// <param name="lease">The released lease.</param>
	public void Forget(ICheatEngineLease lease)
	{
		_entries.RemoveAll(entry => ReferenceEquals(entry.Lease, lease));
	}

	/// <summary>Refuses a process change while owned resources remain.</summary>
	/// <returns><see langword="null" /> when a switch may proceed; otherwise an unsuccessful result.</returns>
	public object? PrepareForTargetChange()
	{
		return _entries.Count == 0
			? null
			: ToolExecution.Error(
				"Release owned target resources with release_target_resources before switching processes.");
	}

	/// <summary>Forgets resources that ended outside a request and reports the first one needing recovery.</summary>
	/// <returns><see langword="null" /> when every ended resource completed; otherwise an unsuccessful result.</returns>
	public object? ReportEndedResources()
	{
		foreach (Entry entry in _entries.ToArray().Reverse())
		{
			if (!entry.Lease.IsReleased)
			{
				continue;
			}

			LeaseReleaseOutcome outcome = entry.Lease.Release();
			_entries.Remove(entry);
			entry.Remove();
			if (!outcome.IsComplete)
			{
				return new
				{
					success = false,
					error = "A target resource ended outside this request and needs recovery.",
					release = outcome.Kind.ToString(),
					hostEffect = outcome.HostEffect.ToString(),
					resource = entry.Recovery,
					manualRecoveryRequired = outcome.RequiresManualRecovery
				};
			}
		}

		return null;
	}

	/// <summary>Releases every tracked resource newest first and stops at the first incomplete release.</summary>
	/// <returns><see langword="null" /> when everything was released; otherwise an unsuccessful result.</returns>
	public object? ReleaseAll()
	{
		for (int index = _entries.Count - 1; index >= 0; index--)
		{
			Entry entry = _entries[index];
			LeaseReleaseOutcome outcome = entry.Lease.Release();
			if (!outcome.IsRetryable)
			{
				_entries.RemoveAt(index);
				entry.Remove();
			}

			if (!outcome.IsComplete)
			{
				// Older resources may be dependencies of this one. Keep them until this failure is handled.
				return new
				{
					success = false,
					error = "An owned target resource could not be fully released.",
					release = outcome.Kind.ToString(),
					hostEffect = outcome.HostEffect.ToString(),
					resource = entry.Recovery,
					retryable = outcome.IsRetryable,
					manualRecoveryRequired = outcome.RequiresManualRecovery
				};
			}
		}

		return null;
	}

	private sealed record Entry(ICheatEngineLease Lease, Action Remove, object? Recovery);
}
