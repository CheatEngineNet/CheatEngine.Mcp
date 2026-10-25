using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Independent read-only allocation cleanup oracle for an owned target process.</summary>
[SupportedOSPlatform("windows")]
internal static partial class TargetMemoryProbe
{
	private const uint ProcessQueryInformation = 0x0400;
	private const uint ProcessVmRead = 0x0010;
	private const uint MemoryFree = 0x10000;
	private const uint MemoryCommit = 0x1000;
	private const uint MemoryPrivate = 0x20000;
	internal const int MaximumDiagnosticReadLength = 16;

	internal static bool IsFree(int processId, ulong address)
	{
		MemoryObservation observation = Inspect(processId, address, 0);
		if (!observation.QuerySucceeded)
		{
			throw new Win32Exception(observation.QueryError, "VirtualQueryEx failed for the owned allocation.");
		}
		return observation.State == MemoryFree;
	}

	internal static bool IsCommittedAndCovering(int processId, ulong address)
	{
		MemoryObservation observation = Inspect(processId, address, 0);
		if (!observation.QuerySucceeded)
		{
			throw new Win32Exception(observation.QueryError, "VirtualQueryEx failed for the owned allocation.");
		}
		return Covers(observation.State, observation.Type, observation.BaseAddress, observation.AllocationBase,
			checked((nuint) observation.RegionSize), address);
	}

	/// <summary>Records the exact allocation state and, when requested, a bounded direct read from the owned target.</summary>
	internal static MemoryObservation Inspect(int processId, ulong address, int readLength)
	{
		ValidateInspectionRequest(address, readLength);
		nint raw = OpenProcess(ProcessQueryInformation | (readLength == 0 ? 0U : ProcessVmRead), false, processId);
		if (raw == 0)
		{
			return MemoryObservation.OpenProcessFailed(Marshal.GetLastWin32Error());
		}
		using ProcessHandle process = new(raw);
		if (VirtualQueryEx(process, unchecked((nint) address), out MemoryBasicInformation info,
			checked((nuint) Marshal.SizeOf<MemoryBasicInformation>())) == 0)
		{
			return MemoryObservation.VirtualQueryFailed(Marshal.GetLastWin32Error());
		}

		ulong baseAddress = unchecked((ulong) info.BaseAddress);
		ulong allocationBase = unchecked((ulong) info.AllocationBase);
		ulong regionSize = checked((ulong) info.RegionSize);
		bool readRangeCovered = CoversReadRange(baseAddress, info.RegionSize, address, readLength);
		if (!readRangeCovered)
		{
			return new MemoryObservation(true, 0, baseAddress, allocationBase, info.AllocationProtect, info.PartitionId,
				regionSize, info.State, info.Protect, info.Type, false, false, false, false, 0, 0, string.Empty);
		}

		byte[] buffer = new byte[readLength];
		nuint bytesRead = 0;
		bool readAttempted = readLength != 0;
		bool readReturned = !readAttempted || ReadProcessMemory(process, unchecked((nint) address), buffer,
			checked((nuint) buffer.Length), out bytesRead);
		int readError = readReturned ? 0 : Marshal.GetLastWin32Error();
		ulong bytesReadValue = readLength == 0 ? 0 : checked((ulong) bytesRead);
		int recordedLength = checked((int) Math.Min(bytesReadValue, (ulong) buffer.Length));
		return new MemoryObservation(
			QuerySucceeded: true,
			QueryError: 0,
			BaseAddress: baseAddress,
			AllocationBase: allocationBase,
			AllocationProtect: info.AllocationProtect,
			PartitionId: info.PartitionId,
			RegionSize: regionSize,
			State: info.State,
			Protect: info.Protect,
			Type: info.Type,
			ReadRangeCovered: true,
			ReadAttempted: readAttempted,
			ReadReturned: readReturned,
			ReadSucceeded: readReturned && bytesReadValue == (ulong) readLength,
			ReadError: readError,
			BytesRead: bytesReadValue,
			BytesHex: Convert.ToHexString(buffer.AsSpan(0, recordedLength)));
	}

	internal static void ValidateInspectionRequest(ulong address, int readLength)
	{
		if (address == 0 || address > (ulong) nint.MaxValue)
		{
			throw new ArgumentOutOfRangeException(nameof(address), "The target address must be a positive native pointer.");
		}
		ArgumentOutOfRangeException.ThrowIfNegative(readLength);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(readLength, MaximumDiagnosticReadLength);
		if ((ulong) readLength > (ulong) nint.MaxValue - address)
		{
			throw new ArgumentOutOfRangeException(nameof(readLength), "The diagnostic read would exceed the native address range.");
		}
	}

	internal static bool Covers(uint state, uint type, ulong baseAddress, ulong allocationBase, nuint regionSize, ulong address) =>
		state == MemoryCommit && type == MemoryPrivate && allocationBase != 0 && address >= baseAddress && address - baseAddress < regionSize;

	internal static bool CoversReadRange(ulong baseAddress, nuint regionSize, ulong address, int readLength) =>
		readLength >= 0 && address >= baseAddress && address - baseAddress <= regionSize
		&& (ulong) readLength <= (ulong) regionSize - (address - baseAddress);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);
	[LibraryImport("kernel32.dll", SetLastError = true)]
	private static partial nuint VirtualQueryEx(nint process, nint address, out MemoryBasicInformation information, nuint length);
	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, nuint size, out nuint bytesRead);
	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool CloseHandle(nint handle);

	private struct MemoryBasicInformation
	{
		public nint BaseAddress;
		public nint AllocationBase;
		public uint AllocationProtect;
		public ushort PartitionId;
		public nuint RegionSize;
		public uint State;
		public uint Protect;
		public uint Type;
	}

	internal sealed record MemoryObservation(
		bool QuerySucceeded,
		int QueryError,
		ulong BaseAddress,
		ulong AllocationBase,
		uint AllocationProtect,
		ushort PartitionId,
		ulong RegionSize,
		uint State,
		uint Protect,
		uint Type,
		bool ReadRangeCovered,
		bool ReadAttempted,
		bool ReadReturned,
		bool ReadSucceeded,
		int ReadError,
		ulong BytesRead,
		string BytesHex)
	{
		internal static MemoryObservation OpenProcessFailed(int error) => new(false, error, 0, 0, 0, 0, 0, 0, 0, 0, false, false, false, false, 0, 0, string.Empty);
		internal static MemoryObservation VirtualQueryFailed(int error) => new(false, error, 0, 0, 0, 0, 0, 0, 0, 0, false, false, false, false, 0, 0, string.Empty);
	}

	private readonly struct ProcessHandle(nint value) : IDisposable
	{
		private nint Handle => value;
		public static implicit operator nint(ProcessHandle handle) => handle.Handle;
		public void Dispose()
		{
			if (value != 0)
			{
				CloseHandle(value);
			}
		}
	}
}
