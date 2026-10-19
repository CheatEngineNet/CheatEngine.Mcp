using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32.SafeHandles;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Reads only the fixed bytes published by the still-owned debugger-loop target.</summary>
[SupportedOSPlatform("windows")]
internal static partial class LiveDebuggerTransitionMemory
{
	private const int EntryOffset = 9;
	private const int HitCounterOffset = 0x104;
	private const int ProgressOffset = 0x108;

	internal readonly record struct Snapshot(byte EntryByte, uint HitCounter, uint Progress);

	internal static Snapshot Read(Process process, LiveDebuggerExecutionQualification.Manifest manifest)
	{
		ArgumentNullException.ThrowIfNull(process);
		manifest.Validate(process.Id);
		process.Refresh();
		if (process.HasExited || process.SafeHandle is not SafeProcessHandle handle || handle.IsInvalid || handle.IsClosed)
		{
			throw new InvalidOperationException("The owned debugger transition target is no longer readable.");
		}

		ulong entry = manifest.Entry;
		ulong counters = checked(manifest.CodeStart + HitCounterOffset);
		if (entry > (ulong) nint.MaxValue || counters > (ulong) nint.MaxValue)
		{
			throw new InvalidDataException("The owned debugger transition address cannot be represented by this process.");
		}

		Span<byte> entryBuffer = stackalloc byte[1];
		ReadExactly(handle, (nint) entry, entryBuffer);
		Span<byte> counterBuffer = stackalloc byte[sizeof(uint) * 2];
		ReadExactly(handle, (nint) counters, counterBuffer);
		return new(entryBuffer[0], BinaryPrimitives.ReadUInt32LittleEndian(counterBuffer),
			BinaryPrimitives.ReadUInt32LittleEndian(counterBuffer[sizeof(uint)..]));
	}

	private static void ReadExactly(SafeProcessHandle process, nint address, Span<byte> buffer)
	{
		if (!ReadProcessMemory(process, address, buffer, (nuint) buffer.Length, out nuint read))
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError(), "ReadProcessMemory failed for the owned debugger transition target.");
		}
		if (read != (nuint) buffer.Length)
		{
			throw new InvalidDataException("ReadProcessMemory returned a short read for the owned debugger transition target.");
		}
	}

	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool ReadProcessMemory(SafeProcessHandle process, nint baseAddress, Span<byte> buffer,
		nuint size, out nuint bytesRead);
}
