using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveDebuggerTransitionMemoryTests
{
	[Fact]
	public void Read_SnapshotsOnlyThePublishedEntryAndCounters()
	{
		nint allocation = Marshal.AllocHGlobal(0x1000);
		try
		{
			byte[] bytes = new byte[0x1000];
			bytes[9] = 0xCC;
			BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x104), 123u);
			BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x108), 456u);
			Marshal.Copy(bytes, 0, allocation, bytes.Length);
			using Process process = Process.GetCurrentProcess();
			ulong codeStart = checked((ulong) allocation);
			LiveDebuggerExecutionQualification.Manifest manifest = new(process.Id, 1, codeStart, codeStart + 9,
				codeStart + 23, codeStart + 55, $"0x{codeStart + 0x108:X}", $"0x{codeStart + 0x104:X}");

			Assert.Equal(new LiveDebuggerTransitionMemory.Snapshot(0xCC, 123, 456),
				LiveDebuggerTransitionMemory.Read(process, manifest));
		}
		finally
		{
			Marshal.FreeHGlobal(allocation);
		}
	}

	[Fact]
	public void Read_RejectsManifestForAnotherProcessBeforeNativeRead()
	{
		using Process process = Process.GetCurrentProcess();
		LiveDebuggerExecutionQualification.Manifest wrong = new(process.Id + 1, 1, 0x1000, 0x1009, 0x1017, 0x1037,
			"0x1108", "0x1104");

		Assert.Throws<InvalidDataException>(() => LiveDebuggerTransitionMemory.Read(process, wrong));
	}
}
