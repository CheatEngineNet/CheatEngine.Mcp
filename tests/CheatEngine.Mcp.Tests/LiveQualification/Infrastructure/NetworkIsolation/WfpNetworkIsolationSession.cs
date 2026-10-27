namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure.NetworkIsolation;

/// <summary>
/// Test-harness-only lifetime owner for an explicitly approved dynamic WFP session.
/// Preparing a session is inert. The default factory always refuses activation; only the explicit native
/// qualification factory selects the separately gated Windows adapter.
/// </summary>
internal sealed class WfpNetworkIsolationSession : IDisposable
{
	internal const string Acknowledgement =
		"I_AUTHORIZE_DYNAMIC_WFP_ISOLATION_FOR_PRIVATE_CE_SOURCE_QUALIFICATION";

	private readonly IWfpDynamicSessionAdapter _adapter;
	private WfpActivationReceipt? _activation;
	private bool _disposed;

	private WfpNetworkIsolationSession(WfpNetworkIsolationPlan plan, IWfpDynamicSessionAdapter adapter)
	{
		Plan = plan;
		_adapter = adapter;
	}

	internal WfpNetworkIsolationPlan Plan
	{
		get;
	}
	internal WfpActivationReceipt? Activation => _activation;
	internal WfpCleanupReceipt? Cleanup
	{
		get; private set;
	}

	internal static WfpNetworkIsolationSession Prepare(string ownedRunRoot, string applicationPath,
		string expectedApplicationSha256)
	{
		return new WfpNetworkIsolationSession(WfpNetworkIsolationPlan.Create(ownedRunRoot, applicationPath,
			expectedApplicationSha256), DisabledWfpDynamicSessionAdapter.Instance);
	}

	internal static WfpNetworkIsolationSession PrepareForAdapterTest(WfpNetworkIsolationPlan plan,
		IWfpDynamicSessionAdapter adapter)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(adapter);
		return new WfpNetworkIsolationSession(plan, adapter);
	}

	internal static WfpNetworkIsolationSession PrepareNativeForQualification(string ownedRunRoot, string applicationPath,
		string expectedApplicationSha256, string? acknowledgement)
	{
		if (!string.Equals(acknowledgement, Acknowledgement, StringComparison.Ordinal))
		{
			throw new UnauthorizedAccessException("The exact native isolation acknowledgement is required before preparation.");
		}
		return new WfpNetworkIsolationSession(WfpNetworkIsolationPlan.Create(ownedRunRoot, applicationPath,
			expectedApplicationSha256), new WfpNativeDynamicSessionAdapter(acknowledgement));
	}

	internal WfpActivationReceipt Activate(WfpNetworkIsolationAuthorization authorization,
		IWfpElevationToken elevationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ArgumentNullException.ThrowIfNull(authorization);
		ArgumentNullException.ThrowIfNull(elevationToken);
		if (_activation is not null)
		{
			throw new InvalidOperationException("The WFP isolation session is already active.");
		}
		if (!string.Equals(authorization.Acknowledgement, Acknowledgement, StringComparison.Ordinal))
		{
			throw new UnauthorizedAccessException("The exact WFP isolation acknowledgement is required before any policy mutation.");
		}
		if (!elevationToken.IsElevated)
		{
			throw new UnauthorizedAccessException("An elevated token is required before any WFP policy mutation.");
		}

		Plan.RevalidateCandidate();
		WfpActivationReceipt activation = _adapter.Activate(Plan);
		_activation = activation;
		try
		{
			activation.Validate(Plan);
		}
		catch
		{
			_adapter.Remove(activation).Validate(activation);
			_activation = null;
			throw;
		}
		return activation;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		if (_activation is not null)
		{
			WfpCleanupReceipt cleanup = _adapter.Remove(_activation);
			cleanup.Validate(_activation);
			Cleanup = cleanup;
		}
		(_adapter as IDisposable)?.Dispose();
		_disposed = true;
	}
}

internal sealed record WfpNetworkIsolationAuthorization(string? Acknowledgement);

internal interface IWfpElevationToken
{
	public bool IsElevated
	{
		get;
	}
}

internal sealed class WindowsWfpElevationToken : IWfpElevationToken
{
	public bool IsElevated => WfpNativeDynamicSessionAdapter.CurrentProcessIsElevated();
}

internal interface IWfpDynamicSessionAdapter
{
	/// <summary>
	/// Future native implementations must open a dynamic session, add both filters in one transaction, enumerate the
	/// exact IDs and read back the full rule shape, and abort/delete on every failure before returning. It must recheck
	/// the canonical non-reparse executable identity and hash immediately before creating the normalized AppId blob.
	/// </summary>
	public WfpActivationReceipt Activate(WfpNetworkIsolationPlan plan);

	/// <summary>Future native implementations must remove only the receipt's IDs and enumerate their absence.</summary>
	public WfpCleanupReceipt Remove(WfpActivationReceipt activation);
}

internal sealed class DisabledWfpDynamicSessionAdapter : IWfpDynamicSessionAdapter
{
	internal static DisabledWfpDynamicSessionAdapter Instance { get; } = new();

	private DisabledWfpDynamicSessionAdapter()
	{
	}

	public WfpActivationReceipt Activate(WfpNetworkIsolationPlan plan)
	{
		throw new NotSupportedException(
			"WFP activation is intentionally disabled. It requires a separately reviewed native adapter and explicit approved-run authorization.");
	}

	public WfpCleanupReceipt Remove(WfpActivationReceipt activation)
	{
		throw new InvalidOperationException("The disabled WFP adapter cannot own an activation receipt.");
	}
}

internal sealed record WfpActivationReceipt(Guid SessionKey, IReadOnlyList<ulong> FilterIds,
	IReadOnlyList<ulong> EnumeratedFilterIds, IReadOnlyList<WfpIsolationFilter> ReadBackFilters)
{
	internal void Validate(WfpNetworkIsolationPlan plan)
	{
		if (SessionKey != plan.SessionKey)
		{
			throw new InvalidOperationException("The activation receipt belongs to a different dynamic session.");
		}
		if (FilterIds.Count != plan.Filters.Count || FilterIds.Any(static id => id == 0) ||
			FilterIds.Distinct().Count() != FilterIds.Count)
		{
			throw new InvalidOperationException("The activation receipt must contain each non-zero WFP filter ID exactly once.");
		}
		if (!FilterIds.Order().SequenceEqual(EnumeratedFilterIds.Order()))
		{
			throw new InvalidOperationException("The enumerated WFP filter IDs do not exactly match the added filter IDs.");
		}
		if (plan.Filters.Count != ReadBackFilters.Count ||
			ReadBackFilters.GroupBy(static filter => filter.FilterKey).Any(static group => group.Count() != 1) ||
			!plan.Filters.All(expected => ReadBackFilters.SingleOrDefault(actual => actual.FilterKey == expected.FilterKey) is { } actual &&
				FilterMatches(expected, actual)))
		{
			throw new InvalidOperationException("WFP filter readback does not exactly match the approved layer, action, and conditions.");
		}
	}

	private static bool FilterMatches(WfpIsolationFilter expected, WfpIsolationFilter actual)
	{
		return expected.FilterKey == actual.FilterKey && expected.SublayerKey == actual.SublayerKey &&
			expected.LayerKey == actual.LayerKey && expected.AddressFamily == actual.AddressFamily &&
			expected.Action == actual.Action && expected.Flags == actual.Flags && expected.Weight == actual.Weight &&
			expected.Conditions.Count == actual.Conditions.Count && expected.Conditions.Zip(actual.Conditions)
				.All(static pair => pair.First == pair.Second);
	}
}

internal sealed record WfpCleanupReceipt(IReadOnlyList<ulong> RemovedFilterIds,
	IReadOnlyList<ulong> RemainingFilterIds, bool SublayerAbsent, bool VerifiedByIndependentObserver)
{
	internal void Validate(WfpActivationReceipt activation)
	{
		if (!activation.FilterIds.Order().SequenceEqual(RemovedFilterIds.Order()))
		{
			throw new InvalidOperationException("WFP cleanup did not remove exactly the session-owned filter IDs.");
		}
		if (RemainingFilterIds.Count != 0)
		{
			throw new InvalidOperationException("WFP cleanup enumeration still contains session-owned filters.");
		}
		if (!SublayerAbsent)
		{
			throw new InvalidOperationException("WFP cleanup enumeration still contains the session-owned sublayer.");
		}
		if (!VerifiedByIndependentObserver)
		{
			throw new InvalidOperationException("WFP cleanup requires absence confirmation from a separate observer engine.");
		}
	}
}
