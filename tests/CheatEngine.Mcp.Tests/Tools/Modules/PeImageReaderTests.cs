using System.Buffers.Binary;

using CheatEngine.Mcp.Tools.Modules;

namespace CheatEngine.Mcp.Tests.Tools.Modules;

/// <summary>The managed PE reader: headers, exports, CodeView, relocations and its refusal of malformed images.</summary>
public sealed class PeImageReaderTests
{
	public static TheoryData<string> MalformedHeaders => new()
	{
		"short",
		"signature",
		"offset",
		"pe",
		"magic",
		"sections",
		"table"
	};

	[Fact]
	public void ParseHeaders_Pe32Plus_ReadsTheReportedFieldsAndEverySection()
	{
		TestPe pe = SampleModule.Build();

		PeHeaders headers = PeImageReader.ParseHeaders(pe.Headers());

		Assert.True(headers.IsPe32Plus);
		Assert.Equal((ushort) 0x8664, headers.Machine);
		Assert.Equal(0x5F3A1B2Cu, headers.TimeDateStamp);
		Assert.Equal(SampleModule.TextRva + 0x10, headers.AddressOfEntryPoint);
		Assert.Equal(SampleModule.PreferredBase, headers.ImageBase);
		Assert.Equal(pe.SizeOfImage, headers.SizeOfImage);
		Assert.Equal(TestPe.HeaderSize, headers.SizeOfHeaders);
		Assert.True(headers.IsDll);
		Assert.False(headers.IsManaged);
		Assert.Equal([".rdata", ".data", ".reloc", ".text"], headers.Sections.Select(static section => section.Name));
		PeSection text = headers.Sections[3];
		Assert.True(text.IsExecutable);
		Assert.False(text.IsWritable);
		Assert.True(headers.Sections[1].IsWritable);
		Assert.Equal((uint) SampleModule.TextSize, text.FileBackedSize);
		Assert.Equal(16, headers.Directories.Length);
		Assert.Equal(new PeDataDirectory(SampleModule.IatRva, 16),
			headers.Directories[PeHeaders.ImportAddressTableDirectory]);
		Assert.Same(text, headers.FindSection(SampleModule.TextRva + 5));
		Assert.Null(headers.FindSection(0x100000));
	}

	[Fact]
	public void ParseHeaders_Pe32_ReadsThe32BitImageBaseAndDirectories()
	{
		TestPe pe = SampleModule.Build(is64: false);

		PeHeaders headers = PeImageReader.ParseHeaders(pe.Headers());

		Assert.False(headers.IsPe32Plus);
		Assert.Equal((ushort) 0x014C, headers.Machine);
		Assert.Equal(0x10000000UL, headers.ImageBase);
		Assert.Equal(SampleModule.ExportRva, headers.Directories[PeHeaders.ExportDirectory].Rva);
	}

	[Theory]
	[MemberData(nameof(MalformedHeaders))]
	public void ParseHeaders_MalformedImage_IsRefusedWithoutReadingPastTheBuffer(string defect)
	{
		byte[] headers = SampleModule.Build().Headers();
		int optionalOffset = TestPe.NtOffset + 24;
		byte[] image = defect switch
		{
			"short" => headers[..63],
			"signature" => Patch(headers, 0, 0x4D, 0x5B),
			"offset" => Patch(headers, 0x3C, 0xFF, 0x0F),
			"pe" => Patch(headers, TestPe.NtOffset, (byte) 'X'),
			"magic" => Patch(headers, optionalOffset, 0x07, 0x01),
			"sections" => Patch(headers, TestPe.NtOffset + 6, 97, 0),
			_ => headers[..(optionalOffset + 240 + 40)]
		};

		InvalidDataException exception = Assert.Throws<InvalidDataException>(() => PeImageReader.ParseHeaders(image));

		Assert.False(string.IsNullOrWhiteSpace(exception.Message));
	}

	[Fact]
	public void ReadExports_NamedOrdinalOnlyAndForwarded_AreListedByOrdinal()
	{
		TestPe pe = SampleModule.Build();
		ArrayImage image = new(pe.Map(SampleModule.LoadedBase));
		PeHeaders headers = PeImageReader.ParseHeaders(pe.Headers());

		List<PeExport> exports = PeImageReader.ReadExports(headers, image);

		PeExport[] expected =
		[
			new("Alpha", 1, SampleModule.TextRva + 0x10, null),
			new(null, 2, SampleModule.TextRva + 0x20, null),
			new("Beta", 3, SampleModule.TextRva + 0x30, null),
			exports[3]
		];
		Assert.Equal(expected, exports);
		Assert.Equal(("Forwarded", 4u, "NTDLL.RtlAllocateHeap"),
			(exports[3].Name, exports[3].Ordinal, exports[3].Forwarder));
		Assert.True(headers.Directories[PeHeaders.ExportDirectory].Contains(exports[3].Rva));
	}

	[Fact]
	public void ReadExports_NoExportDirectory_IsEmptyWithoutReading()
	{
		TestPe pe = SampleModule.Build().SetDirectory(0, 0, 0);
		ArrayImage image = new(pe.Map(SampleModule.LoadedBase));

		Assert.Empty(PeImageReader.ReadExports(PeImageReader.ParseHeaders(pe.Headers()), image));
		Assert.Equal(0, image.Reads);
	}

	[Theory]
	[InlineData(20, 65537u)]
	[InlineData(24, 65537u)]
	public void ReadExports_CountOverTheCeiling_IsRefusedBeforeReadingTables(int field, uint count)
	{
		TestPe pe = SampleModule.Build();
		byte[] mapped = pe.Map(SampleModule.LoadedBase);
		BinaryPrimitives.WriteUInt32LittleEndian(mapped.AsSpan((int) SampleModule.ExportRva + field), count);
		ArrayImage image = new(mapped);

		Assert.Throws<InvalidDataException>(() =>
			PeImageReader.ReadExports(PeImageReader.ParseHeaders(pe.Headers()), image));
		Assert.Equal(1, image.Reads);
	}

	[Fact]
	public void ReadExports_NameOrdinalPastTheFunctionTable_IsRefused()
	{
		TestPe pe = SampleModule.Build();
		byte[] mapped = pe.Map(SampleModule.LoadedBase);
		uint ordinals = BinaryPrimitives.ReadUInt32LittleEndian(mapped.AsSpan((int) SampleModule.ExportRva + 36));
		BinaryPrimitives.WriteUInt16LittleEndian(mapped.AsSpan((int) ordinals), 40);

		Assert.Throws<InvalidDataException>(() =>
			PeImageReader.ReadExports(PeImageReader.ParseHeaders(pe.Headers()), new ArrayImage(mapped)));
	}

	[Theory]
	[InlineData(0u)]
	[InlineData(0x7FFFFFF0u)]
	public void ReadExports_ZeroOrOutsideFunctionRva_IsRefused(uint rva)
	{
		TestPe pe = SampleModule.Build();
		byte[] mapped = pe.Map(SampleModule.LoadedBase);
		uint functions = BinaryPrimitives.ReadUInt32LittleEndian(mapped.AsSpan((int) SampleModule.ExportRva + 28));
		BinaryPrimitives.WriteUInt32LittleEndian(mapped.AsSpan((int) functions), rva);

		InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
			PeImageReader.ReadExports(PeImageReader.ParseHeaders(pe.Headers()), new ArrayImage(mapped)));

		Assert.Contains("Export ordinal 1", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ReadExports_TablesOutsideTheImage_AreRefused()
	{
		TestPe pe = SampleModule.Build();
		byte[] mapped = pe.Map(SampleModule.LoadedBase);
		BinaryPrimitives.WriteUInt32LittleEndian(mapped.AsSpan((int) SampleModule.ExportRva + 28), 0x7FFFFFF0);

		Assert.Throws<InvalidDataException>(() =>
			PeImageReader.ReadExports(PeImageReader.ParseHeaders(pe.Headers()), new ArrayImage(mapped)));
	}

	[Fact]
	public void ReadExports_UnterminatedNameAtTheImageEnd_IsRefused()
	{
		TestPe pe = SampleModule.Build();
		byte[] mapped = pe.Map(SampleModule.LoadedBase);
		uint names = BinaryPrimitives.ReadUInt32LittleEndian(mapped.AsSpan((int) SampleModule.ExportRva + 32));
		uint lastByte = (uint) mapped.Length - 1;
		BinaryPrimitives.WriteUInt32LittleEndian(mapped.AsSpan((int) names), lastByte);
		mapped[lastByte] = (byte) 'Z';

		Assert.Throws<InvalidDataException>(() =>
			PeImageReader.ReadExports(PeImageReader.ParseHeaders(pe.Headers()), new ArrayImage(mapped)));
	}

	[Fact]
	public void ReadImports_Pe32Plus_ListsNamedOrdinalBoundAndDelayLoadedSlotsWithTheirValues()
	{
		(TestPe pe, TestImportTable imports, TestImportTable? delayed) = SampleModule.BuildWithImports();

		List<PeImport> read = PeImageReader.ReadImports(PeImageReader.ParseHeaders(pe.Headers()),
			new ArrayImage(pe.Map(SampleModule.LoadedBase)), true, SampleModule.LoadedBase);

		uint kernel = imports.SlotRvas[0];
		uint bound = imports.SlotRvas[1];
		PeImport[] expected =
		[
			new("KERNEL32.dll", kernel, false, "Sleep", null, 0x5A1, SampleModule.KernelBase64 + 0x1000),
			new("KERNEL32.dll", kernel + 8, false, "GetTickCount", null, 0x2B3, SampleModule.KernelBase64 + 0x2000),
			new("KERNEL32.dll", kernel + 16, false, null, 17, null, SampleModule.KernelBase64 + 0x3000),
			new("bound.dll", bound, false, null, null, null, SampleModule.BoundValue),
			new("bound.dll", bound + 8, false, null, null, null, SampleModule.BoundValue + 0x10),
			new("USER32.dll", delayed!.SlotRvas[0], true, "MessageBoxW", null, 0x28A,
				SampleModule.LoadedBase + SampleModule.TextRva + 0x40)
		];
		Assert.Equal(expected, read);
	}

	[Fact]
	public void ReadImports_Pe32_ReadsFourByteThunksAndOrdinalFlag()
	{
		(TestPe pe, TestImportTable imports, _) = SampleModule.BuildWithImports(false, false);

		List<PeImport> read = PeImageReader.ReadImports(PeImageReader.ParseHeaders(pe.Headers()),
			new ArrayImage(pe.Map(0x10000000)), true, 0x10000000);

		Assert.Equal(5, read.Count);
		Assert.Equal(new PeImport("KERNEL32.dll", imports.SlotRvas[0] + 4, false, "GetTickCount", null, 0x2B3,
			SampleModule.KernelBase32 + 0x2000), read[1]);
		Assert.Equal(((ushort?) 17, SampleModule.KernelBase32 + 0x3000), (read[2].Ordinal, read[2].Value));
		Assert.Equal(imports.SlotRvas[1] + 4, read[4].SlotRva);
	}

	[Fact]
	public void ReadImports_WithoutDelayLoaded_SkipsTheDelayLoadDirectory()
	{
		(TestPe pe, _, _) = SampleModule.BuildWithImports();
		ArrayImage image = new(pe.Map(SampleModule.LoadedBase));

		List<PeImport> read = PeImageReader.ReadImports(PeImageReader.ParseHeaders(pe.Headers()), image, false,
			SampleModule.LoadedBase);

		Assert.Equal(5, read.Count);
		Assert.DoesNotContain(read, static import => import.DelayLoaded);
	}

	/// <summary>
	///     Each delay-load descriptor form of PE32 and PE32+, at its preferred base and rebased: the old PE32 form holds
	///     addresses, above 2 GiB with the ordinal flag's bit set; a PE32+ descriptor is relative even without the RVA
	///     attribute.
	/// </summary>
	[Theory]
	[InlineData(false, nameof(TestDelayForm.Relative), SampleModule.PreferredBase32)]
	[InlineData(false, nameof(TestDelayForm.Relative), 0x00400000UL)]
	[InlineData(false, nameof(TestDelayForm.Addresses), SampleModule.PreferredBase32)]
	[InlineData(false, nameof(TestDelayForm.Addresses), 0x00400000UL)]
	[InlineData(false, nameof(TestDelayForm.Addresses), 0x8A000000UL)]
	[InlineData(true, nameof(TestDelayForm.Relative), SampleModule.LoadedBase)]
	[InlineData(true, nameof(TestDelayForm.RelativeWithoutAttribute), SampleModule.LoadedBase)]
	[InlineData(true, nameof(TestDelayForm.RelativeWithoutAttribute), SampleModule.PreferredBase32)]
	public void ReadImports_DelayLoadDescriptorForms_ListTheSameNamedAndOrdinalSlots(bool is64, string form,
		ulong loadedBase)
	{
		(TestPe pe, TestImportTable delayed) =
			SampleModule.BuildWithDelayLoads(is64, Enum.Parse<TestDelayForm>(form));

		List<PeImport> read = PeImageReader.ReadImports(PeImageReader.ParseHeaders(pe.Headers(loadedBase)),
			new ArrayImage(pe.Map(loadedBase)), true, loadedBase);

		uint slot = delayed.SlotRvas[0];
		PeImport[] expected =
		[
			new("USER32.dll", slot, true, "MessageBoxW", null, 0x28A, loadedBase + SampleModule.DelayStubRva),
			new("USER32.dll", slot + (is64 ? 8u : 4u), true, null, 42, null,
				loadedBase + SampleModule.DelayStubRva + 0x10)
		];
		Assert.Equal(expected, read);
	}

	[Fact]
	public void BuildWithDelayLoads_AddressForm_HoldsRelocatedAddressesAsOldLinkersWroteThem()
	{
		const uint loadedBase = 0x00400000;
		(TestPe pe, TestImportTable delayed) = SampleModule.BuildWithDelayLoads(false, TestDelayForm.Addresses);

		byte[] mapped = pe.Map(loadedBase);

		ReadOnlySpan<byte> descriptor = mapped.AsSpan((int) SampleModule.DelayImportRva, 32);
		uint named = BinaryPrimitives.ReadUInt32LittleEndian(mapped.AsSpan((int) delayed.LookupRvas[0]));
		uint ordinal = BinaryPrimitives.ReadUInt32LittleEndian(mapped.AsSpan((int) delayed.LookupRvas[0] + 4));
		Assert.Equal((0u, loadedBase + delayed.SlotRvas[0], loadedBase + delayed.LookupRvas[0]),
			(BinaryPrimitives.ReadUInt32LittleEndian(descriptor),
				BinaryPrimitives.ReadUInt32LittleEndian(descriptor[12..]),
				BinaryPrimitives.ReadUInt32LittleEndian(descriptor[16..])));
		// The named entry is the address of the hint and the name; read as an RVA, it lies far past the image.
		Assert.InRange(named, loadedBase + SampleModule.DelayImportRva, loadedBase + pe.SizeOfImage - 1);
		Assert.Equal(0x8000002Au, ordinal);
	}

	[Theory]
	[InlineData("descriptor", "A delay-load descriptor holds the address 7")]
	[InlineData("lookup", "A delay-load lookup entry of USER32.dll holds the address FFF0000, outside the image.")]
	public void ReadImports_OldDelayLoadAddressOutsideTheImage_IsRefused(string field, string message)
	{
		// A PE32 descriptor without the RVA attribute holds addresses: relative fields lie below the image base.
		TestDelayForm form = field == "descriptor" ? TestDelayForm.RelativeWithoutAttribute : TestDelayForm.Addresses;
		(TestPe pe, TestImportTable delayed) = SampleModule.BuildWithDelayLoads(false, form);
		byte[] mapped = pe.Map(SampleModule.PreferredBase32);
		if (field == "lookup")
		{
			BinaryPrimitives.WriteUInt32LittleEndian(mapped.AsSpan((int) delayed.LookupRvas[0]), 0x0FFF0000);
		}

		InvalidDataException exception = Assert.Throws<InvalidDataException>(() => PeImageReader.ReadImports(
			PeImageReader.ParseHeaders(pe.Headers()), new ArrayImage(mapped), true, SampleModule.PreferredBase32));

		Assert.StartsWith(message, exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ReadImports_NoImportDirectories_IsEmptyWithoutReading()
	{
		TestPe pe = SampleModule.Build();
		ArrayImage image = new(pe.Map(SampleModule.LoadedBase));

		Assert.Empty(PeImageReader.ReadImports(PeImageReader.ParseHeaders(pe.Headers()), image, true,
			SampleModule.LoadedBase));
		Assert.Equal(0, image.Reads);
	}

	[Theory]
	[InlineData("name")]
	[InlineData("lookup")]
	[InlineData("hint")]
	[InlineData("slots")]
	public void ReadImports_TablesOutsideTheImage_AreRefused(string table)
	{
		(TestPe pe, TestImportTable imports, _) = SampleModule.BuildWithImports(withDelayLoads: false);
		byte[] mapped = pe.Map(SampleModule.LoadedBase);
		int descriptor = (int) SampleModule.ImportRva;
		switch (table)
		{
			case "name":
				BinaryPrimitives.WriteUInt32LittleEndian(mapped.AsSpan(descriptor + 12), 0x7FFFFFF0);
				break;
			case "lookup":
				BinaryPrimitives.WriteUInt32LittleEndian(mapped.AsSpan(descriptor), 0x7FFFFFF0);
				break;
			case "hint":
				BinaryPrimitives.WriteUInt64LittleEndian(mapped.AsSpan((int) imports.LookupRvas[0]), 0x7FFFFFF0);
				break;
			default:
				BinaryPrimitives.WriteUInt32LittleEndian(mapped.AsSpan(descriptor + 16), 0x7FFFFFF0);
				break;
		}

		Assert.Throws<InvalidDataException>(() => PeImageReader.ReadImports(
			PeImageReader.ParseHeaders(pe.Headers()), new ArrayImage(mapped), true, SampleModule.LoadedBase));
	}

	[Fact]
	public void ReadImports_LookupEntryNeitherOrdinalNorRva_IsRefused()
	{
		(TestPe pe, TestImportTable imports, _) = SampleModule.BuildWithImports(withDelayLoads: false);
		byte[] mapped = pe.Map(SampleModule.LoadedBase);
		BinaryPrimitives.WriteUInt64LittleEndian(mapped.AsSpan((int) imports.LookupRvas[0]), 0x1_0000_1000);

		InvalidDataException exception = Assert.Throws<InvalidDataException>(() => PeImageReader.ReadImports(
			PeImageReader.ParseHeaders(pe.Headers()), new ArrayImage(mapped), true, SampleModule.LoadedBase));

		Assert.Contains("neither an ordinal nor an RVA", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ReadImports_DescriptorsWithoutTerminatorPastTheCeiling_AreRefused()
	{
		const uint rva = 0x10000;
		int descriptors = PeImageReader.MaximumImportDescriptors + 1;
		int nameOffset = descriptors * 20;
		int thunkOffset = nameOffset + 8;
		byte[] data = new byte[thunkOffset + 8];
		"a.dll"u8.CopyTo(data.AsSpan(nameOffset));
		for (int index = 0; index < descriptors; index++)
		{
			// Every descriptor names a DLL whose table is empty, so only the descriptor ceiling stops the walk.
			BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan((index * 20) + 12), rva + (uint) nameOffset);
			BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan((index * 20) + 16), rva + (uint) thunkOffset);
		}

		TestPe pe = SampleModule.Build().AddSection(".idata", rva, data, TestPe.WritableData)
			.SetDirectory(1, rva, (uint) nameOffset);

		InvalidDataException exception = Assert.Throws<InvalidDataException>(() => PeImageReader.ReadImports(
			PeImageReader.ParseHeaders(pe.Headers()), new ArrayImage(pe.Map(SampleModule.LoadedBase)), true,
			SampleModule.LoadedBase));

		Assert.Contains($"more than {PeImageReader.MaximumImportDescriptors} descriptors", exception.Message,
			StringComparison.Ordinal);
	}

	[Fact]
	public void ReadImports_MoreSlotsThanTheCeiling_AreRefused()
	{
		const uint rva = 0x10000;
		TestImport[] functions =
			[.. Enumerable.Range(0, PeImageReader.MaximumImports + 1).Select(static index => new TestImport(null, 1))];
		TestImportTable imports = TestPe.ImportData(rva, true, [new TestImportDll("many.dll", functions, true)]);
		TestPe pe = SampleModule.Build().AddSection(".idata", rva, imports.Data, TestPe.WritableData)
			.SetDirectory(1, rva, imports.DescriptorsSize);

		InvalidDataException exception = Assert.Throws<InvalidDataException>(() => PeImageReader.ReadImports(
			PeImageReader.ParseHeaders(pe.Headers()), new ArrayImage(pe.Map(SampleModule.LoadedBase)), true,
			SampleModule.LoadedBase));

		Assert.Contains($"more than {PeImageReader.MaximumImports} functions", exception.Message,
			StringComparison.Ordinal);
	}

	[Fact]
	public void ReadCodeView_RsdsRecord_ReturnsPathSignatureAndAge()
	{
		TestPe pe = SampleModule.Build();

		PeCodeView? codeView = PeImageReader.ReadCodeView(PeImageReader.ParseHeaders(pe.Headers()),
			new ArrayImage(pe.Map(SampleModule.LoadedBase)));

		Assert.Equal(new PeCodeView("C:\\build\\sample.pdb", SampleModule.Signature, 3), codeView);
	}

	[Fact]
	public void ReadCodeView_NoDebugDirectoryOrOtherFormat_IsNull()
	{
		TestPe withoutDebug = SampleModule.Build().SetDirectory(6, 0, 0);
		TestPe pe = SampleModule.Build();
		byte[] mapped = pe.Map(SampleModule.LoadedBase);
		mapped[SampleModule.DebugRva + 28] = (byte) 'N';

		Assert.Null(PeImageReader.ReadCodeView(PeImageReader.ParseHeaders(withoutDebug.Headers()),
			new ArrayImage(withoutDebug.Map(SampleModule.LoadedBase))));
		Assert.Null(PeImageReader.ReadCodeView(PeImageReader.ParseHeaders(pe.Headers()), new ArrayImage(mapped)));
	}

	[Fact]
	public void ParseRelocations_Blocks_AreSortedAndUnsupportedTypesCounted()
	{
		byte[] data = TestPe.RelocationBlocks([(0x2010, PeRelocations.Dir64), (0x1004, PeRelocations.HighLow)]);
		byte[] withUnsupported = [.. data, .. TestPe.RelocationBlocks([(0x3000, 5)])];

		PeRelocations relocations = PeImageReader.ParseRelocations(withUnsupported);

		Assert.Equal([0x1004u, 0x2010u], relocations.Rvas);
		Assert.Equal([PeRelocations.HighLow, PeRelocations.Dir64], relocations.Types);
		Assert.Equal(1, relocations.Unsupported);
	}

	[Theory]
	[InlineData(4u)]
	[InlineData(9u)]
	[InlineData(4096u)]
	public void ParseRelocations_MalformedBlockSize_IsRefused(uint blockSize)
	{
		byte[] data = TestPe.RelocationBlocks([(0x1004, PeRelocations.HighLow)]);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), blockSize);

		Assert.Throws<InvalidDataException>(() => PeImageReader.ParseRelocations(data));
	}

	[Fact]
	public void ParseRelocations_EntryCountOverCeiling_IsRefused()
	{
		int entryCount = PeImageReader.MaximumRelocationEntries + 1;
		byte[] data = new byte[8 + (entryCount * 2)];
		BinaryPrimitives.WriteUInt32LittleEndian(data, 0x1000);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint) data.Length);

		InvalidDataException exception =
			Assert.Throws<InvalidDataException>(() => PeImageReader.ParseRelocations(data));

		Assert.Contains($"more than {PeImageReader.MaximumRelocationEntries}", exception.Message,
			StringComparison.Ordinal);
	}

	[Fact]
	public void ApplyRelocations_Delta_IsAddedOnlyInsideTheCountedRangeAndTheWindow()
	{
		PeRelocations relocations = PeImageReader.ParseRelocations(TestPe.RelocationBlocks(
		[
			(0x1000, PeRelocations.Dir64), (0x1010, PeRelocations.HighLow), (0x1020, PeRelocations.Dir64),
			(0x103C, PeRelocations.Dir64)
		]));
		byte[] window = new byte[0x40];
		BinaryPrimitives.WriteUInt64LittleEndian(window.AsSpan(0x00), 0x180002000);
		BinaryPrimitives.WriteUInt32LittleEndian(window.AsSpan(0x10), 0x80002000);
		BinaryPrimitives.WriteUInt64LittleEndian(window.AsSpan(0x20), 0x180003000);

		int applied = PeImageReader.ApplyRelocations(window, 0x1000, relocations, 0x10000, 0x1008, 0x1040);

		// 0x1000 lies before the counted range; 0x103C would run past the window's end.
		Assert.Equal(2, applied);
		Assert.Equal(0x180002000UL, BinaryPrimitives.ReadUInt64LittleEndian(window.AsSpan(0x00)));
		Assert.Equal(0x80012000u, BinaryPrimitives.ReadUInt32LittleEndian(window.AsSpan(0x10)));
		Assert.Equal(0x180013000UL, BinaryPrimitives.ReadUInt64LittleEndian(window.AsSpan(0x20)));
	}

	[Fact]
	public void ApplyRelocations_RelocationSpanningCountBoundary_IsAdjustedWithoutDoubleCounting()
	{
		PeRelocations relocations = PeImageReader.ParseRelocations(TestPe.RelocationBlocks(
			[(0x0FFF, PeRelocations.Dir64)]));
		byte[] window = new byte[16];
		BinaryPrimitives.WriteUInt64LittleEndian(window, 0x180001000);

		int applied = PeImageReader.ApplyRelocations(window, 0x0FFF, relocations, 0x10000, 0x1000, 0x100F);

		Assert.Equal(0, applied);
		Assert.Equal(0x180011000UL, BinaryPrimitives.ReadUInt64LittleEndian(window));
	}

	[Fact]
	public void TryMapToFile_HeadersSectionsAndGaps_MapOnlyFileBackedRanges()
	{
		PeHeaders headers = PeImageReader.ParseHeaders(SampleModule.Build().Headers());

		Assert.True(PeImageReader.TryMapToFile(headers, 0x40, 8, out long header));
		Assert.True(PeImageReader.TryMapToFile(headers, SampleModule.RdataRva + 4, 8, out long rdata));
		Assert.False(PeImageReader.TryMapToFile(headers, SampleModule.RdataRva + 0x0FFC, 8, out _));
		Assert.False(PeImageReader.TryMapToFile(headers, 0x100000, 1, out _));
		Assert.Equal(0x40, header);
		Assert.Equal(TestPe.HeaderSize + 4, rdata);
	}

	private static byte[] Patch(byte[] source, int offset, params byte[] bytes)
	{
		byte[] copy = [.. source];
		bytes.CopyTo(copy, offset);
		return copy;
	}
}
