using System.Runtime.InteropServices;
using System.Security.Cryptography;

using CheatEngine.Mcp.Tests.LiveQualification.Infrastructure.NetworkIsolation;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class NetworkIsolationTests : IAsyncDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "CheatEngine.Mcp.NetworkIsolation", Guid.NewGuid().ToString("N"));

	[Fact]
	public void NativeTypes_WindowsX64AbiReceipt_MatchesPinnedSdkLayout()
	{
		Assert.Equal(16, Marshal.SizeOf<FwpByteBlob>());
		Assert.Equal(16, Marshal.SizeOf<FwpValue0>());
		Assert.Equal(16, Marshal.SizeOf<FwpConditionValue0>());
		Assert.Equal(72, Marshal.SizeOf<FwpmSession0>());
		Assert.Equal(40, Marshal.SizeOf<FwpmFilterCondition0>());
		Assert.Equal(20, Marshal.SizeOf<FwpmAction0>());
		Assert.Equal(200, Marshal.SizeOf<FwpmFilter0>());
		Assert.Equal(32, Marshal.OffsetOf<FwpmSession0>("Flags").ToInt32());
		Assert.Equal(64, Marshal.OffsetOf<FwpmFilter0>("LayerKey").ToInt32());
		Assert.Equal(80, Marshal.OffsetOf<FwpmFilter0>("SublayerKey").ToInt32());
		Assert.Equal(120, Marshal.OffsetOf<FwpmFilter0>("Conditions").ToInt32());
		Assert.Equal(128, Marshal.OffsetOf<FwpmFilter0>("Action").ToInt32());
		Assert.Equal(176, Marshal.OffsetOf<FwpmFilter0>("FilterId").ToInt32());
	}

	[Fact]
	public void Prepare_ExecutableOutsideOwnedRunRoot_RefusesBeforeActivation()
	{
		string application = WritePrivateExecutable(Path.Combine(_root, "outside"));
		Directory.CreateDirectory(Path.Combine(_root, "owned"));

		ArgumentException failure = Assert.Throws<ArgumentException>(() =>
			WfpNetworkIsolationSession.Prepare(Path.Combine(_root, "owned"), application, Hash(application)));

		Assert.Contains("strictly below", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Prepare_HashMismatch_RefusesBeforeActivation()
	{
		string application = WritePrivateExecutable(Path.Combine(_root, "owned", "private-ce"));

		InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
			WfpNetworkIsolationSession.Prepare(_root, application, new string('0', 64)));

		Assert.Contains("hash", failure.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Prepare_ValidPrivateExecutable_DescribesIpv4AndIpv6NonLoopbackBlocksWithoutPermit()
	{
		string application = WritePrivateExecutable(Path.Combine(_root, "owned", "private-ce"));

		using WfpNetworkIsolationSession session = WfpNetworkIsolationSession.Prepare(_root, application, Hash(application));

		Assert.Equal(WfpSessionKind.Dynamic, session.Plan.SessionKind);
		Assert.Equal([WfpNetworkAddressFamily.Ipv4, WfpNetworkAddressFamily.Ipv6],
			session.Plan.Filters.Select(static filter => filter.AddressFamily));
		Assert.Equal([WfpNetworkIsolationPlan.AleAuthConnectV4Layer, WfpNetworkIsolationPlan.AleAuthConnectV6Layer],
			session.Plan.Filters.Select(static filter => filter.LayerKey));
		Assert.All(session.Plan.Filters, filter => Assert.Equal(session.Plan.SublayerKey, filter.SublayerKey));
		Assert.Equal(2, session.Plan.Filters.Select(static filter => filter.FilterKey).Distinct().Count());
		Assert.All(session.Plan.Filters, filter =>
		{
			Assert.Equal(WfpFilterAction.Block, filter.Action);
			Assert.Equal(WfpNetworkIsolationPlan.FilterFlags, filter.Flags);
			Assert.Equal(WfpFilterWeight.Auto, filter.Weight);
			Assert.Equal(2, filter.Conditions.Count);
			Assert.Contains(filter.Conditions, condition => condition.FieldKey == WfpNetworkIsolationPlan.AleOriginalApplicationIdCondition &&
				condition.MatchType == WfpMatchType.Equal && condition.ValueType == WfpConditionValue.ApplicationId &&
				string.Equals(condition.ApplicationPath, application, StringComparison.Ordinal));
			Assert.Contains(filter.Conditions, condition => condition.FieldKey == WfpNetworkIsolationPlan.FlagsCondition &&
				condition.MatchType == WfpMatchType.FlagsNoneSet && condition.ValueType == WfpConditionValue.UInt32 &&
				condition.UInt32Value == WfpNetworkIsolationPlan.IsLoopbackFlag);
		});
	}

	[Fact]
	public void Activate_MissingAcknowledgement_RefusesBeforeAdapterMutation()
	{
		RecordingAdapter adapter = new();
		using WfpNetworkIsolationSession session = PrepareForAdapter(adapter);

		Assert.Throws<UnauthorizedAccessException>(() => session.Activate(new WfpNetworkIsolationAuthorization(null), new ElevatedToken(true)));

		Assert.Equal(0, adapter.ActivateCalls);
	}

	[Fact]
	public void Activate_NotElevated_RefusesBeforeAdapterMutation()
	{
		RecordingAdapter adapter = new();
		using WfpNetworkIsolationSession session = PrepareForAdapter(adapter);

		Assert.Throws<UnauthorizedAccessException>(() => session.Activate(new WfpNetworkIsolationAuthorization(WfpNetworkIsolationSession.Acknowledgement), new ElevatedToken(false)));

		Assert.Equal(0, adapter.ActivateCalls);
	}

	[Fact]
	public void Activate_ReadbackMismatch_RollsBackBeforeReportingAnActiveSession()
	{
		RecordingAdapter adapter = new()
		{
			ReturnMismatchedReadback = true
		};
		using WfpNetworkIsolationSession session = PrepareForAdapter(adapter);

		Assert.Throws<InvalidOperationException>(() => session.Activate(
			new WfpNetworkIsolationAuthorization(WfpNetworkIsolationSession.Acknowledgement), new ElevatedToken(true)));

		Assert.Null(session.Activation);
		Assert.Equal(1, adapter.RemoveCalls);
	}

	[Fact]
	public void Activate_ReadbackRollbackFailure_PreservesReceiptForDisposeRetry()
	{
		RecordingAdapter adapter = new()
		{
			ReturnMismatchedReadback = true,
			FailRemoveCount = 1
		};
		WfpNetworkIsolationSession session = PrepareForAdapter(adapter);
		try
		{
			Assert.Throws<InvalidOperationException>(() => session.Activate(
				new WfpNetworkIsolationAuthorization(WfpNetworkIsolationSession.Acknowledgement), new ElevatedToken(true)));
			Assert.NotNull(session.Activation);

			session.Dispose();
		}
		finally
		{
			session.Dispose();
		}

		Assert.Equal(2, adapter.RemoveCalls);
	}

	[Fact]
	public void Activate_ExecutableReplacedAfterPreparation_RefusesBeforeAdapterMutation()
	{
		RecordingAdapter adapter = new();
		WfpNetworkIsolationSession session = PrepareForAdapter(adapter);
		try
		{
			File.WriteAllBytes(session.Plan.ApplicationPath, [0x4D, 0x5A, 0x91, 0x00]);

			Assert.Throws<InvalidOperationException>(() => session.Activate(
				new WfpNetworkIsolationAuthorization(WfpNetworkIsolationSession.Acknowledgement), new ElevatedToken(true)));
		}
		finally
		{
			session.Dispose();
		}

		Assert.Equal(0, adapter.ActivateCalls);
	}

	[Fact]
	public void Activate_DefaultAdapter_RefusesEvenAfterApproval()
	{
		string application = WritePrivateExecutable(Path.Combine(_root, "owned", "private-ce"));
		using WfpNetworkIsolationSession session = WfpNetworkIsolationSession.Prepare(_root, application, Hash(application));

		Assert.Throws<NotSupportedException>(() => session.Activate(
			new WfpNetworkIsolationAuthorization(WfpNetworkIsolationSession.Acknowledgement), new ElevatedToken(true)));

		Assert.Null(session.Activation);
	}

	[Fact]
	public void Dispose_ActivatedAdapterReceipt_RemovesAndVerifiesExactlyOwnedFilterIds()
	{
		RecordingAdapter adapter = new();
		WfpNetworkIsolationSession session = PrepareForAdapter(adapter);
		try
		{
			WfpActivationReceipt receipt = session.Activate(new WfpNetworkIsolationAuthorization(WfpNetworkIsolationSession.Acknowledgement), new ElevatedToken(true));
			Assert.Equal([101UL, 202UL], receipt.FilterIds);
		}
		finally
		{
			session.Dispose();
		}

		Assert.Equal(1, adapter.RemoveCalls);
		Assert.Equal([101UL, 202UL], adapter.RemovedFilterIds);
	}

	[Fact]
	public void Dispose_CleanupFailure_AllowsOneExplicitRetry()
	{
		RecordingAdapter adapter = new()
		{
			FailRemoveCount = 1
		};
		WfpNetworkIsolationSession session = PrepareForAdapter(adapter);
		session.Activate(new WfpNetworkIsolationAuthorization(WfpNetworkIsolationSession.Acknowledgement), new ElevatedToken(true));

		Assert.Throws<InvalidOperationException>(() => session.Dispose());
		session.Dispose();

		Assert.Equal(2, adapter.RemoveCalls);
	}

	public ValueTask DisposeAsync()
	{
		return new ValueTask(TestDirectory.DeleteAsync(_root));
	}

	private WfpNetworkIsolationSession PrepareForAdapter(RecordingAdapter adapter)
	{
		string application = WritePrivateExecutable(Path.Combine(_root, "owned", "private-ce"));
		WfpNetworkIsolationPlan plan = WfpNetworkIsolationPlan.Create(_root, application, Hash(application));
		return WfpNetworkIsolationSession.PrepareForAdapterTest(plan, adapter);
	}

	private static string WritePrivateExecutable(string directory)
	{
		Directory.CreateDirectory(directory);
		string application = Path.Combine(directory, WfpNetworkIsolationPlan.RequiredExecutableName);
		File.WriteAllBytes(application, [0x4D, 0x5A, 0x90, 0x00]);
		return application;
	}

	private static string Hash(string path)
	{
		using FileStream stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}

	private sealed class ElevatedToken(bool elevated) : IWfpElevationToken
	{
		public bool IsElevated => elevated;
	}

	private sealed class RecordingAdapter : IWfpDynamicSessionAdapter
	{
		internal int ActivateCalls
		{
			get; private set;
		}
		internal int RemoveCalls
		{
			get; private set;
		}
		internal bool ReturnMismatchedReadback
		{
			get; init;
		}
		internal int FailRemoveCount
		{
			get; set;
		}
		internal IReadOnlyList<ulong> RemovedFilterIds { get; private set; } = [];

		public WfpActivationReceipt Activate(WfpNetworkIsolationPlan plan)
		{
			ActivateCalls++;
			List<WfpIsolationFilter> readback = plan.Filters.Select(Clone).ToList();
			if (ReturnMismatchedReadback)
			{
				readback = [readback[0] with { LayerKey = Guid.NewGuid() }, readback[1]];
			}
			return new WfpActivationReceipt(plan.SessionKey, [101UL, 202UL], [202UL, 101UL], readback);
		}

		public WfpCleanupReceipt Remove(WfpActivationReceipt activation)
		{
			RemoveCalls++;
			if (FailRemoveCount-- > 0)
			{
				throw new InvalidOperationException("Simulated cleanup failure.");
			}
			RemovedFilterIds = activation.FilterIds;
			return new WfpCleanupReceipt(activation.FilterIds, [], true, true);
		}

		private static WfpIsolationFilter Clone(WfpIsolationFilter filter)
		{
			return filter with
			{
				Conditions = filter.Conditions.Select(static condition => condition with { }).ToList()
			};
		}
	}
}
