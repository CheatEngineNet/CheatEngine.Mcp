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

	internal const uint ImportRva = 0x6000;
	internal const uint DelayImportRva = 0x7000;
	internal const uint DelayStubRva = TextRva + 0x40;
	internal const ulong PreferredBase32 = 0x10000000;
	internal const ulong KernelBase64 = 0x7FFB00000000;
	internal const ulong KernelBase32 = 0x76A00000;
	internal const ulong BoundValue = 0x7FFC00001000;

	internal static readonly Guid Signature = new("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

	/// <summary>
	///     The sample DLL with an import section at <see cref="ImportRva" />: KERNEL32.dll (two named imports and one by
	///     ordinal) and a bound bound.dll without lookup table; and, unless <paramref name="withDelayLoads" /> is false, a
	///     delay-load section at <see cref="DelayImportRva" /> with USER32.dll whose slot still points at a stub in .text.
	/// </summary>
	internal static (TestPe Pe, TestImportTable Imports, TestImportTable? DelayImports) BuildWithImports(
		bool is64 = true, bool withDelayLoads = true)
	{
		ulong kernel = is64 ? KernelBase64 : KernelBase32;
		ulong bound = is64 ? BoundValue : 0x77001000;
		TestPe pe = Build(is64: is64);
		TestImportTable imports = TestPe.ImportData(ImportRva, is64,
		[
			new TestImportDll("KERNEL32.dll",
			[
				new TestImport("Sleep", kernel + 0x1000, 0x5A1),
				new TestImport("GetTickCount", kernel + 0x2000, 0x2B3),
				new TestImport(null, kernel + 0x3000, Ordinal: 17)
			]),
			new TestImportDll("bound.dll", [new TestImport(null, bound), new TestImport(null, bound + 0x10)], true)
		]);
		pe.AddSection(".idata", ImportRva, imports.Data, TestPe.WritableData)
			.SetDirectory(1, ImportRva, imports.DescriptorsSize);
		if (!withDelayLoads)
		{
			return (pe, imports, null);
		}

		ulong stub = (is64 ? LoadedBase : PreferredBase32) + DelayStubRva;
		TestImportTable delayed = TestPe.ImportData(DelayImportRva, is64,
			[new TestImportDll("USER32.dll", [new TestImport("MessageBoxW", stub, 0x28A)])], true);
		pe.AddSection(".didat", DelayImportRva, delayed.Data, TestPe.WritableData)
			.SetDirectory(13, DelayImportRva, delayed.DescriptorsSize);
		return (pe, imports, delayed);
	}

	/// <summary>
	///     The sample DLL with only a delay-load section at <see cref="DelayImportRva" />, its descriptor laid out in
	///     <paramref name="form" />: USER32.dll imports MessageBoxW by name (hint 0x28A) and ordinal 42, and each slot
	///     still points at its load stub in .text. The stubs and, in the address form, the descriptor fields and the named
	///     lookup entries are base relocations, so <see cref="TestPe.Map" /> moves them to any base as a loader does.
	/// </summary>
	internal static (TestPe Pe, TestImportTable DelayImports) BuildWithDelayLoads(bool is64, TestDelayForm form)
	{
		ulong preferred = is64 ? PreferredBase : PreferredBase32;
		TestPe pe = Build(is64: is64);
		TestImportTable delayed = TestPe.ImportData(DelayImportRva, is64,
		[
			new TestImportDll("USER32.dll",
			[
				new TestImport("MessageBoxW", preferred + DelayStubRva, 0x28A),
				new TestImport(null, preferred + DelayStubRva + 0x10, Ordinal: 42)
			])
		], true, form, preferred);
		pe.AddSection(".didat", DelayImportRva, delayed.Data, TestPe.WritableData)
			.SetDirectory(13, DelayImportRva, delayed.DescriptorsSize);
		byte slot = is64 ? PeRelocations.Dir64 : PeRelocations.HighLow;
		pe.Relocations.Add((delayed.SlotRvas[0], slot));
		pe.Relocations.Add((delayed.SlotRvas[0] + (is64 ? 8u : 4u), slot));
		pe.Relocations.AddRange(delayed.AddressFields);
		return (pe, delayed);
	}

	internal static TestPe Build(int textSize = TextSize, bool is64 = true, uint? additionalRelocationRva = null)
	{
		byte[] text = new byte[textSize];
		for (int index = 0; index < text.Length; index++)
		{
			text[index] = (byte) (0x90 + (index % 7));
		}

		ulong preferred = is64 ? PreferredBase : PreferredBase32;
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
