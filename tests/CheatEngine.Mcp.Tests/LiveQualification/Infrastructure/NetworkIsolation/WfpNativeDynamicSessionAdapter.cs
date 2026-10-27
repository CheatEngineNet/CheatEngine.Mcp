using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure.NetworkIsolation;

/// <summary>
/// The opt-in native implementation. Construction is inert; <see cref="Activate"/> is the only path to WFP and
/// independently validates the acknowledgement and the current process token before opening the engine.
/// </summary>
internal sealed unsafe partial class WfpNativeDynamicSessionAdapter : IWfpDynamicSessionAdapter, IDisposable
{
	private const uint RpcCAuthnWinnt = 10;
	private const uint FwpByteBlobType = 12;
	private const uint FwpUInt32Type = 3;
	private const uint FwpMatchEqual = 0;
	private const uint FwpMatchFlagsNoneSet = 8;
	private const uint FwpActionBlock = 0x00001001;
	private const uint FwpErrorFilterNotFound = 0x80320003;
	private const uint FwpErrorSublayerNotFound = 0x80320007;
	private const ushort PrivateSublayerWeight = ushort.MaxValue;
	private readonly string? _acknowledgement;
	private WfpEngineHandle? _engine;
	private WfpMemoryHandle? _appId;
	private WfpOwnedPathLease? _pathLease;
	private WfpActivationReceipt? _active;
	private Guid _activePlanSublayerKey;
	private bool _disposed;

	internal WfpNativeDynamicSessionAdapter(string? acknowledgement)
	{
		_acknowledgement = acknowledgement;
	}

	public WfpActivationReceipt Activate(WfpNetworkIsolationPlan plan)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ArgumentNullException.ThrowIfNull(plan);
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("WFP dynamic isolation is available only on Windows.");
		}
		if (!string.Equals(_acknowledgement, WfpNetworkIsolationSession.Acknowledgement, StringComparison.Ordinal))
		{
			throw new UnauthorizedAccessException("The exact WFP acknowledgement is required before opening the WFP engine.");
		}
		if (!IsCurrentTokenElevated())
		{
			throw new UnauthorizedAccessException("The current Windows token is not elevated; WFP engine access was not attempted.");
		}
		if (_active is not null)
		{
			throw new InvalidOperationException("This WFP adapter already owns an active session.");
		}

		try
		{
			plan.RevalidateCandidate();
			_pathLease = WfpOwnedPathLease.Acquire(plan);
			_pathLease.VerifySha256(plan.ApplicationSha256);
			using NativeDisplayName sessionName = new("CheatEngine.Mcp symbol-source qualification");
			FwpmSession0 session = new()
			{
				SessionKey = plan.SessionKey,
				Flags = WfpNetworkIsolationPlan.DynamicSessionFlag,
				DisplayData = new FwpmDisplayData0 { Name = (char*) sessionName.Pointer }
			};
			ThrowIfFailed(WfpNativeMethods.FwpmEngineOpen0(null, RpcCAuthnWinnt, 0, in session, out WfpEngineHandle engine), "FwpmEngineOpen0");
			_engine = engine;
			ThrowIfFailed(WfpNativeMethods.FwpmGetAppIdFromFileName0(plan.ApplicationPath, out nint appId), "FwpmGetAppIdFromFileName0");
			_appId = WfpMemoryHandle.FromNative(appId);
			_pathLease.VerifyAppId((FwpByteBlob*) appId);
			ThrowIfFailed(FwpmTransactionBegin0(engine, 0), "FwpmTransactionBegin0");
			bool transactionOpen = true;
			try
			{
				using NativeDisplayName sublayerName = new("CheatEngine.Mcp private CE source isolation");
				FwpmSublayer0 sublayer = new()
				{
					SublayerKey = plan.SublayerKey,
					Weight = PrivateSublayerWeight,
					DisplayData = new FwpmDisplayData0 { Name = (char*) sublayerName.Pointer }
				};
				ThrowIfFailed(FwpmSubLayerAdd0(engine, in sublayer, 0), "FwpmSubLayerAdd0");
				ulong[] ids = new ulong[plan.Filters.Count];
				for (int index = 0; index < plan.Filters.Count; index++)
				{
					ids[index] = AddFilter(engine, plan.Filters[index], (FwpByteBlob*) appId);
				}
				List<ulong> enumerated = EnumerateFilterIds(engine).Where(ids.Contains).ToList();
				List<WfpIsolationFilter> readBack = enumerated.Select(id => ReadFilter(engine, id, plan)).ToList();
				WfpActivationReceipt receipt = new(plan.SessionKey, ids, enumerated, readBack);
				receipt.Validate(plan);
				ThrowIfFailed(WfpNativeMethods.FwpmTransactionCommit0(engine), "FwpmTransactionCommit0");
				transactionOpen = false;
				_active = receipt;
				_activePlanSublayerKey = plan.SublayerKey;
				return receipt;
			}
			finally
			{
				if (transactionOpen)
				{
					WfpNativeMethods.FwpmTransactionAbort0(engine);
				}
			}
		}
		catch
		{
			DisposeNativeState();
			throw;
		}
	}

	public WfpCleanupReceipt Remove(WfpActivationReceipt activation)
	{
		ArgumentNullException.ThrowIfNull(activation);
		if (_active is null || activation.SessionKey != _active.SessionKey)
		{
			throw new InvalidOperationException("This adapter does not own the supplied WFP activation receipt.");
		}
		if (_engine is not null)
		{
			foreach (ulong id in activation.FilterIds)
			{
				uint status = WfpNativeMethods.FwpmFilterDeleteById0(_engine, id);
				if (status is not 0 and not FwpErrorFilterNotFound)
				{
					ThrowIfFailed(status, "FwpmFilterDeleteById0");
				}
			}
			uint deleteSublayer = FwpmSubLayerDeleteByKey0(_engine, in _activePlanSublayerKey);
			if (deleteSublayer is not 0 and not FwpErrorSublayerNotFound)
			{
				ThrowIfFailed(deleteSublayer, "FwpmSubLayerDeleteByKey0");
			}
			_engine.Dispose();
			_engine = null;
		}

		List<ulong> remaining = VerifyCleanupWithIndependentObserver(activation, _activePlanSublayerKey);
		_active = null;
		_activePlanSublayerKey = Guid.Empty;
		DisposeNativeState();
		return new WfpCleanupReceipt(activation.FilterIds, remaining, true, true);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		if (_active is not null)
		{
			Remove(_active);
		}
		else
		{
			DisposeNativeState();
		}
		_disposed = true;
	}

	private static ulong AddFilter(WfpEngineHandle engine, WfpIsolationFilter planned, FwpByteBlob* appId)
	{
		using NativeDisplayName filterName = new("CheatEngine.Mcp private CE non-loopback block " + planned.AddressFamily);
		FwpmFilterCondition0* conditions = stackalloc FwpmFilterCondition0[2];
		conditions[0] = new FwpmFilterCondition0
		{
			FieldKey = WfpNetworkIsolationPlan.AleOriginalApplicationIdCondition,
			MatchType = FwpMatchEqual,
			ConditionValue = new FwpConditionValue0 { Type = FwpByteBlobType, ByteBlob = appId }
		};
		conditions[1] = new FwpmFilterCondition0
		{
			FieldKey = WfpNetworkIsolationPlan.FlagsCondition,
			MatchType = FwpMatchFlagsNoneSet,
			ConditionValue = new FwpConditionValue0 { Type = FwpUInt32Type, UInt32 = WfpNetworkIsolationPlan.IsLoopbackFlag }
		};
		FwpmFilter0 filter = new()
		{
			FilterKey = planned.FilterKey,
			DisplayData = new FwpmDisplayData0 { Name = (char*) filterName.Pointer },
			SublayerKey = planned.SublayerKey,
			LayerKey = planned.LayerKey,
			Flags = planned.Flags,
			NumberOfConditions = 2,
			Conditions = conditions,
			Action = new FwpmAction0 { Type = FwpActionBlock }
		};
		ThrowIfFailed(WfpNativeMethods.FwpmFilterAdd0(engine, &filter, 0, out ulong id), "FwpmFilterAdd0");
		return id;
	}

	private sealed class NativeDisplayName : IDisposable
	{
		private nint _pointer;

		internal NativeDisplayName(string value) => _pointer = Marshal.StringToHGlobalUni(value);
		internal nint Pointer => _pointer;

		public void Dispose()
		{
			nint pointer = Interlocked.Exchange(ref _pointer, 0);
			if (pointer != 0)
			{
				Marshal.FreeHGlobal(pointer);
			}
		}
	}

	private WfpIsolationFilter ReadFilter(WfpEngineHandle engine, ulong id, WfpNetworkIsolationPlan plan)
	{
		ThrowIfFailed(WfpNativeMethods.FwpmFilterGetById0(engine, id, out nint native), "FwpmFilterGetById0");
		using WfpMemoryHandle memory = WfpMemoryHandle.FromNative(native);
		FwpmFilter0* filter = (FwpmFilter0*) native;
		if (filter->NumberOfConditions != 2 || filter->Action.Type != FwpActionBlock || filter->Flags != WfpNetworkIsolationPlan.FilterFlags ||
			filter->Weight.Type != 0 || (filter->LayerKey != WfpNetworkIsolationPlan.AleAuthConnectV4Layer && filter->LayerKey != WfpNetworkIsolationPlan.AleAuthConnectV6Layer))
		{
			throw new InvalidOperationException("WFP filter readback does not contain the expected terminating block shape.");
		}
		FwpmFilterCondition0* appCondition = null;
		FwpmFilterCondition0* flagsCondition = null;
		for (int index = 0; index < filter->NumberOfConditions; index++)
		{
			FwpmFilterCondition0* condition = filter->Conditions + index;
			if (condition->FieldKey == WfpNetworkIsolationPlan.AleOriginalApplicationIdCondition)
			{
				appCondition = condition;
			}
			if (condition->FieldKey == WfpNetworkIsolationPlan.FlagsCondition)
			{
				flagsCondition = condition;
			}
		}
		if (appCondition == null || flagsCondition == null || appCondition->MatchType != FwpMatchEqual ||
			flagsCondition->MatchType != FwpMatchFlagsNoneSet || appCondition->ConditionValue.Type != FwpByteBlobType ||
			flagsCondition->ConditionValue.Type != FwpUInt32Type || flagsCondition->ConditionValue.UInt32 != WfpNetworkIsolationPlan.IsLoopbackFlag ||
			!ByteBlobsEqual(appCondition->ConditionValue.ByteBlob, (FwpByteBlob*) _appId!.DangerousGetHandle()))
		{
			throw new InvalidOperationException("WFP filter readback conditions differ from the normalized original-AppId non-loopback block.");
		}
		return new WfpIsolationFilter(filter->FilterKey, filter->SublayerKey, filter->LayerKey,
			filter->LayerKey == WfpNetworkIsolationPlan.AleAuthConnectV4Layer ? WfpNetworkAddressFamily.Ipv4 : WfpNetworkAddressFamily.Ipv6,
			WfpFilterAction.Block, filter->Flags, WfpFilterWeight.Auto,
			[
				new WfpFilterCondition(appCondition->FieldKey, WfpMatchType.Equal, WfpConditionValue.ApplicationId, plan.ApplicationPath, null),
				new WfpFilterCondition(flagsCondition->FieldKey, WfpMatchType.FlagsNoneSet, WfpConditionValue.UInt32, null, flagsCondition->ConditionValue.UInt32)
			]);
	}

	private static bool ByteBlobsEqual(FwpByteBlob* left, FwpByteBlob* right)
	{
		if (left == null || right == null || left->Size != right->Size)
		{
			return false;
		}
		return new ReadOnlySpan<byte>(left->Data, checked((int) left->Size)).SequenceEqual(new ReadOnlySpan<byte>(right->Data, checked((int) right->Size)));
	}

	private static List<ulong> EnumerateFilterIds(WfpEngineHandle engine)
	{
		ThrowIfFailed(WfpNativeMethods.FwpmFilterCreateEnumHandle0(engine, 0, out nint enumeration), "FwpmFilterCreateEnumHandle0");
		try
		{
			ThrowIfFailed(WfpNativeMethods.FwpmFilterEnum0(engine, enumeration, uint.MaxValue, out nint entries, out uint returned), "FwpmFilterEnum0");
			using WfpMemoryHandle memory = WfpMemoryHandle.FromNative(entries);
			List<ulong> result = new(checked((int) returned));
			FwpmFilter0** filters = (FwpmFilter0**) entries;
			for (int index = 0; index < returned; index++)
			{
				result.Add(filters[index]->FilterId);
			}
			return result;
		}
		finally
		{
			WfpNativeMethods.FwpmFilterDestroyEnumHandle0(engine, enumeration);
		}
	}

	private void DisposeNativeState()
	{
		_appId?.Dispose();
		_appId = null;
		_engine?.Dispose();
		_engine = null;
		_pathLease?.Dispose();
		_pathLease = null;
	}

	private static List<ulong> VerifyCleanupWithIndependentObserver(
		WfpActivationReceipt activation, Guid sublayerKey)
	{
		// This separate engine is deliberately used only for read APIs after the owning dynamic engine has closed.
		FwpmSession0 observerSession = new();
		ThrowIfFailed(WfpNativeMethods.FwpmEngineOpen0(null, RpcCAuthnWinnt, 0, in observerSession, out WfpEngineHandle observer),
			"FwpmEngineOpen0 observer");
		using (observer)
		{
			List<ulong> remaining = new();
			foreach (ulong id in activation.FilterIds)
			{
				uint status = WfpNativeMethods.FwpmFilterGetById0(observer, id, out nint filter);
				if (status == 0)
				{
					using WfpMemoryHandle memory = WfpMemoryHandle.FromNative(filter);
					remaining.Add(id);
					continue;
				}
				if (status is not FwpErrorFilterNotFound)
				{
					ThrowIfFailed(status, "FwpmFilterGetById0 observer");
				}
			}
			uint sublayerStatus = FwpmSubLayerGetByKey0(observer, in sublayerKey, out nint sublayer);
			if (sublayerStatus == 0)
			{
				using WfpMemoryHandle memory = WfpMemoryHandle.FromNative(sublayer);
				throw new InvalidOperationException("The independently opened observer engine still sees the session-owned WFP sublayer.");
			}
			if (sublayerStatus is not FwpErrorSublayerNotFound)
			{
				ThrowIfFailed(sublayerStatus, "FwpmSubLayerGetByKey0 observer");
			}
			if (remaining.Count != 0)
			{
				throw new InvalidOperationException("The independently opened observer engine still sees session-owned WFP filters.");
			}
			return remaining;
		}
	}

	private static void ThrowIfFailed(uint status, string operation)
	{
		if (status is not 0)
		{
			throw new Win32Exception((int) status, operation + " failed.");
		}
	}

	[SupportedOSPlatform("windows")]
	private static bool IsCurrentTokenElevated()
	{
		using WindowsIdentity identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
		return GetTokenInformation(identity.Token, 20, out uint elevation, sizeof(uint), out uint returned) != 0 && returned == sizeof(uint) && elevation != 0;
	}

	internal static bool CurrentProcessIsElevated()
	{
		if (!OperatingSystem.IsWindows())
		{
			return false;
		}
		return IsCurrentTokenElevated();
	}

	[StructLayout(LayoutKind.Sequential, Size = 72)]
	private struct FwpmSublayer0
	{
		internal Guid SublayerKey; internal FwpmDisplayData0 DisplayData; internal uint Flags; internal nint ProviderKey; internal FwpByteBlob ProviderData; internal ushort Weight;
	}

	[LibraryImport("fwpuclnt.dll", EntryPoint = "FwpmSubLayerAdd0")]
	private static partial uint FwpmSubLayerAdd0(WfpEngineHandle engineHandle, in FwpmSublayer0 sublayer, nint securityDescriptor);

	[LibraryImport("fwpuclnt.dll", EntryPoint = "FwpmSubLayerDeleteByKey0")]
	private static partial uint FwpmSubLayerDeleteByKey0(WfpEngineHandle engineHandle, in Guid sublayerKey);

	[LibraryImport("fwpuclnt.dll", EntryPoint = "FwpmSubLayerGetByKey0")]
	private static partial uint FwpmSubLayerGetByKey0(WfpEngineHandle engineHandle, in Guid sublayerKey, out nint sublayer);

	[LibraryImport("fwpuclnt.dll", EntryPoint = "FwpmTransactionBegin0")]
	private static partial uint FwpmTransactionBegin0(WfpEngineHandle engineHandle, uint flags);

	[LibraryImport("advapi32.dll", SetLastError = true)]
	private static partial int GetTokenInformation(nint token, uint informationClass, out uint tokenInformation, uint length, out uint returnLength);
}
