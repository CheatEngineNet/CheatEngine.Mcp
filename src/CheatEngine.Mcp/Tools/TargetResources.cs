using CheatEngine.Client;
using CheatEngine.Client.Results;

namespace CheatEngine.Mcp.Tools;

/// <summary>Releases MCP-owned target resources in reverse creation order before an explicit process change.</summary>
public sealed class TargetResources
{
	private readonly List<Entry> _entries = [];

	internal void Track(ICheatEngineLease lease, Action remove, object? recovery = null) =>
		_entries.Add(new Entry(lease, remove, recovery));

	internal void Forget(ICheatEngineLease lease) => _entries.RemoveAll(entry => ReferenceEquals(entry.Lease, lease));

	internal object? PrepareForTargetChange() => _entries.Count == 0 ? null : ToolExecution.Error(
		"Release owned target resources with release_target_resources before switching processes.");

	internal object? ReportEndedResources()
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

	internal object? ReleaseAll()
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
