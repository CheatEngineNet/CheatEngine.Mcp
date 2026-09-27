using System.Buffers.Binary;

using CheatEngine.Mcp.Tools.Modules;

namespace CheatEngine.Mcp.Tests.Tools.Modules;

/// <summary>
///     A small x64 DLL used across the module tests: code with one absolute pointer, an export directory with named,
///     ordinal-only and forwarded exports, a CodeView record, an import address table, data and relocations.
/// </summary>
internal static class SampleModule
{
	internal const ulong PreferredBase = 0x180000000;
	internal const ulong LoadedBase = 0x7FFA00000000;
	internal const uint RdataRva = 0x1000;
	internal const uint DataRva = 0x2000;
	internal const uint RelocRva = 0x3000;
	internal const uint TextRva = 0x4000;
	internal const uint ExportRva = RdataRva;
	internal const uint DebugRva = RdataRva + 0x400;
	internal const uint IatRva = RdataRva + 0x800;
	internal const uint PointerRva = TextRva + 0x100;
	internal const uint Pointer32Rva = TextRva + 0x1F0;
	internal const int TextSize = 0x2000;

	internal static readonly Guid Signature = new("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

	internal static TestPe Build(int textSize = TextSize, bool is64 = true, uint? additionalRelocationRva = null)
	{
		byte[] text = new byte[textSize];
		for (int index = 0; index < text.Length; index++)
		{
			text[index] = (byte) (0x90 + (index % 7));
		}

		ulong preferred = is64 ? PreferredBase : 0x10000000;
		// An absolute pointer to .data, which the loader relocates.
		BinaryPrimitives.WriteUInt64LittleEndian(text.AsSpan((int) (PointerRva - TextRva)), preferred + DataRva);
		BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan((int) (Pointer32Rva - TextRva)),
			unchecked((uint) (preferred + DataRva)));
		byte[] rdata = new byte[0x1000];
		byte[] exports = TestPe.ExportData(ExportRva, "sample.dll", 1,
		[
			("Alpha", TextRva + 0x10, null),
			(null, TextRva + 0x20, null),
			("Beta", TextRva + 0x30, null),
			("Forwarded", 0, "NTDLL.RtlAllocateHeap"),
			(null, 0, null)
		]);
		exports.CopyTo(rdata, 0);
		byte[] debug = TestPe.DebugData(DebugRva, Signature, 3, "C:\\build\\sample.pdb");
		debug.CopyTo(rdata, (int) (DebugRva - RdataRva));
		BinaryPrimitives.WriteUInt64LittleEndian(rdata.AsSpan((int) (IatRva - RdataRva)), 0x1111);
		byte[] data = new byte[0x200];
		data.AsSpan().Fill(0xAB);
		TestPe pe = new()
		{
			Is64 = is64,
			ImageBase = preferred,
			Machine = (ushort) (is64 ? 0x8664 : 0x014C),
			Characteristics = 0x2022,
			EntryPoint = TextRva + 0x10
		};
		pe.Relocations.Add((PointerRva, is64 ? PeRelocations.Dir64 : PeRelocations.HighLow));
		pe.Relocations.Add((Pointer32Rva, PeRelocations.HighLow));
		if (additionalRelocationRva is { } relocationRva)
		{
			int offset = checked((int) (relocationRva - TextRva));
			if (is64)
			{
				BinaryPrimitives.WriteUInt64LittleEndian(text.AsSpan(offset), preferred + DataRva);
			}
			else
			{
				BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset), unchecked((uint) (preferred + DataRva)));
			}

			pe.Relocations.Add((relocationRva, is64 ? PeRelocations.Dir64 : PeRelocations.HighLow));
		}

		byte[] reloc = TestPe.RelocationBlocks(pe.Relocations);
		pe.AddSection(".rdata", RdataRva, rdata, TestPe.ReadOnlyData)
			.AddSection(".data", DataRva, data, TestPe.WritableData)
			.AddSection(".reloc", RelocRva, reloc, 0x42000040)
			.AddSection(".text", TextRva, text, TestPe.Code)
			.SetDirectory(0, ExportRva, (uint) exports.Length)
			.SetDirectory(5, RelocRva, (uint) reloc.Length)
			.SetDirectory(6, DebugRva, 28)
			.SetDirectory(12, IatRva, 16);
		return pe;
	}
}
