using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure.NetworkIsolation;

// ABI pinned by artifacts/final-release-20261027/wfp-preparation/wfp_abi_check.txt (Windows x64 SDK 10.0.26100.0).
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FwpByteBlob
{
	internal uint Size;
	internal byte* Data;
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal unsafe struct FwpValue0
{
	[FieldOffset(0)] internal uint Type;
	[FieldOffset(8)] internal uint UInt32;
	[FieldOffset(8)] internal ulong* UInt64;
	[FieldOffset(8)] internal FwpByteBlob* ByteBlob;
	[FieldOffset(8)] internal char* UnicodeString;
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal unsafe struct FwpConditionValue0
{
	[FieldOffset(0)] internal uint Type;
	[FieldOffset(8)] internal uint UInt32;
	[FieldOffset(8)] internal FwpByteBlob* ByteBlob;
	[FieldOffset(8)] internal char* UnicodeString;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FwpmDisplayData0
{
	internal char* Name;
	internal char* Description;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpmSession0
{
	internal Guid SessionKey;
	internal FwpmDisplayData0 DisplayData;
	internal uint Flags;
	internal uint TransactionWaitTimeoutInMilliseconds;
	internal uint ProcessId;
	private readonly uint _padding;
	internal nint Sid;
	internal nint Username;
	internal int KernelMode; // Win32 BOOL, never C# bool.
	private readonly uint _tailPadding;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FwpmFilterCondition0
{
	internal Guid FieldKey;
	internal uint MatchType;
	private readonly uint _padding;
	internal FwpConditionValue0 ConditionValue;
}

[StructLayout(LayoutKind.Explicit, Size = 20)]
internal struct FwpmAction0
{
	[FieldOffset(0)] internal uint Type;
	[FieldOffset(4)] internal Guid FilterType;
}

[StructLayout(LayoutKind.Explicit, Size = 200)]
internal unsafe struct FwpmFilter0
{
	[FieldOffset(0)] internal Guid FilterKey;
	[FieldOffset(16)] internal FwpmDisplayData0 DisplayData;
	[FieldOffset(32)] internal uint Flags;
	[FieldOffset(40)] internal Guid* ProviderKey;
	[FieldOffset(48)] internal FwpByteBlob ProviderData;
	[FieldOffset(64)] internal Guid LayerKey;
	[FieldOffset(80)] internal Guid SublayerKey;
	[FieldOffset(96)] internal FwpValue0 Weight;
	[FieldOffset(112)] internal uint NumberOfConditions;
	[FieldOffset(120)] internal FwpmFilterCondition0* Conditions;
	[FieldOffset(128)] internal FwpmAction0 Action;
	[FieldOffset(152)] internal ulong RawContext;
	[FieldOffset(160)] internal Guid* Reserved;
	[FieldOffset(176)] internal ulong FilterId;
	[FieldOffset(184)] internal FwpValue0 EffectiveWeight;
}

internal sealed class WfpEngineHandle : SafeHandleZeroOrMinusOneIsInvalid
{
	public WfpEngineHandle() : base(true) { }

	protected override bool ReleaseHandle() => WfpNativeMethods.FwpmEngineClose0(handle) == 0;
}

internal sealed class WfpMemoryHandle : SafeHandleZeroOrMinusOneIsInvalid
{
	internal WfpMemoryHandle() : base(true) { }

	internal static WfpMemoryHandle FromNative(nint value)
	{
		WfpMemoryHandle result = new();
		result.SetHandle(value);
		return result;
	}

	protected override bool ReleaseHandle()
	{
		nint value = handle;
		WfpNativeMethods.FwpmFreeMemory0(ref value);
		return true;
	}
}
