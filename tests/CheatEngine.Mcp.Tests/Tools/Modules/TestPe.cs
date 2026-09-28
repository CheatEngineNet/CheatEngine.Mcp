using System.Buffers.Binary;
using System.Text;

using CheatEngine.Mcp.Tools.Modules;

namespace CheatEngine.Mcp.Tests.Tools.Modules;

/// <summary>
///     Builds small PE32 and PE32+ images at test time: headers, sections, export, import, debug and relocation
///     tables, the file layout and the image a loader would map at another base.
/// </summary>
internal sealed class TestPe
{
	internal const uint HeaderSize = 0x400;
	internal const uint FileAlignment = 0x200;
	internal const uint SectionAlignment = 0x1000;
	internal const uint Code = 0x60000020;
	internal const uint ReadOnlyData = 0x40000040;
	internal const uint WritableData = 0xC0000040;
	internal const int NtOffset = 0x80;

	private readonly (uint Rva, uint Size)[] _directories = new (uint, uint)[16];
	private readonly List<TestSection> _sections = [];

	internal bool Is64
	{
		get;
		init;
	} = true;

	internal ulong ImageBase
	{
		get;
		init;
	} = 0x140000000;

	internal ushort Machine
	{
		get;
		init;
	} = 0x8664;

	internal uint TimeDateStamp
	{
		get;
		init;
	} = 0x5F3A1B2C;

	internal uint EntryPoint
	{
		get;
		init;
	} = 0x1010;

	internal ushort Subsystem
	{
		get;
		init;
	} = 3;

	internal ushort Characteristics
	{
		get;
		init;
	} = 0x0022;

	internal List<(uint Rva, byte Type)> Relocations
	{
		get;
	} = [];

	internal IReadOnlyList<TestSection> Sections => _sections;

	internal uint SizeOfImage => Align(_sections.Count == 0
			? HeaderSize
			: _sections.Max(static section =>
				section.VirtualAddress + Math.Max(section.VirtualSize, (uint) section.Data.Length)),
		SectionAlignment);

	internal TestPe AddSection(string name, uint rva, byte[] data, uint characteristics, uint? virtualSize = null)
	{
		_sections.Add(new TestSection(name, rva, data, characteristics, virtualSize ?? (uint) data.Length));
		return this;
	}

	internal TestPe SetDirectory(int index, uint rva, uint size)
	{
		_directories[index] = (rva, size);
		return this;
	}

	/// <summary>The header bytes: DOS header, NT headers and the section table, padded to <see cref="HeaderSize" />.</summary>
	internal byte[] Headers(ulong? imageBase = null)
	{
		byte[] headers = new byte[HeaderSize];
		Span<byte> span = headers;
		BinaryPrimitives.WriteUInt16LittleEndian(span, 0x5A4D);
		BinaryPrimitives.WriteInt32LittleEndian(span[0x3C..], NtOffset);
		BinaryPrimitives.WriteUInt32LittleEndian(span[NtOffset..], 0x00004550);
		int optionalSize = Is64 ? 240 : 224;
		Span<byte> file = span.Slice(NtOffset + 4, 20);
		BinaryPrimitives.WriteUInt16LittleEndian(file, Machine);
		BinaryPrimitives.WriteUInt16LittleEndian(file[2..], (ushort) _sections.Count);
		BinaryPrimitives.WriteUInt32LittleEndian(file[4..], TimeDateStamp);
		BinaryPrimitives.WriteUInt16LittleEndian(file[16..], (ushort) optionalSize);
		BinaryPrimitives.WriteUInt16LittleEndian(file[18..], Characteristics);
		Span<byte> optional = span.Slice(NtOffset + 24, optionalSize);
		BinaryPrimitives.WriteUInt16LittleEndian(optional, (ushort) (Is64 ? 0x20B : 0x10B));
		BinaryPrimitives.WriteUInt32LittleEndian(optional[16..], EntryPoint);
		if (Is64)
		{
			BinaryPrimitives.WriteUInt64LittleEndian(optional[24..], imageBase ?? ImageBase);
		}
		else
		{
			BinaryPrimitives.WriteUInt32LittleEndian(optional[28..], (uint) (imageBase ?? ImageBase));
		}

		BinaryPrimitives.WriteUInt32LittleEndian(optional[32..], SectionAlignment);
		BinaryPrimitives.WriteUInt32LittleEndian(optional[36..], FileAlignment);
		BinaryPrimitives.WriteUInt32LittleEndian(optional[56..], SizeOfImage);
		BinaryPrimitives.WriteUInt32LittleEndian(optional[60..], HeaderSize);
		BinaryPrimitives.WriteUInt16LittleEndian(optional[68..], Subsystem);
		int directories = Is64 ? 112 : 96;
		BinaryPrimitives.WriteUInt32LittleEndian(optional[(directories - 4)..], 16);
		for (int index = 0; index < 16; index++)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(optional[(directories + (index * 8))..], _directories[index].Rva);
			BinaryPrimitives.WriteUInt32LittleEndian(optional[(directories + (index * 8) + 4)..],
				_directories[index].Size);
		}

		int table = NtOffset + 24 + optionalSize;
		uint raw = HeaderSize;
		foreach (TestSection section in _sections)
		{
			Span<byte> header = span.Slice(table, 40);
			Encoding.ASCII.GetBytes(section.Name.AsSpan(0, Math.Min(8, section.Name.Length)), header);
			BinaryPrimitives.WriteUInt32LittleEndian(header[8..], section.VirtualSize);
			BinaryPrimitives.WriteUInt32LittleEndian(header[12..], section.VirtualAddress);
			BinaryPrimitives.WriteUInt32LittleEndian(header[16..], Align((uint) section.Data.Length, FileAlignment));
			BinaryPrimitives.WriteUInt32LittleEndian(header[20..], raw);
			BinaryPrimitives.WriteUInt32LittleEndian(header[36..], section.Characteristics);
			raw += Align((uint) section.Data.Length, FileAlignment);
			table += 40;
		}

		return headers;
	}

	/// <summary>The PE file: headers, then each section's data at its file-aligned offset.</summary>
	internal byte[] File()
	{
		uint length = HeaderSize + (uint) _sections.Sum(static section =>
			Align((uint) section.Data.Length, FileAlignment));
		byte[] file = new byte[length];
		Headers().CopyTo(file, 0);
		uint raw = HeaderSize;
		foreach (TestSection section in _sections)
		{
			section.Data.CopyTo(file, raw);
			raw += Align((uint) section.Data.Length, FileAlignment);
		}

		return file;
	}

	/// <summary>The image a loader maps at <paramref name="actualBase" />, relocated and with its header rewritten.</summary>
	internal byte[] Map(ulong actualBase)
	{
		byte[] image = new byte[SizeOfImage];
		Headers(actualBase).CopyTo(image, 0);
		foreach (TestSection section in _sections)
		{
			section.Data.AsSpan(0, (int) Math.Min(section.VirtualSize, (uint) section.Data.Length))
				.CopyTo(image.AsSpan((int) section.VirtualAddress));
		}

		ulong delta = unchecked(actualBase - ImageBase);
		foreach ((uint rva, byte type) in Relocations)
		{
			Span<byte> target = image.AsSpan((int) rva);
			if (type == PeRelocations.Dir64)
			{
				BinaryPrimitives.WriteUInt64LittleEndian(target,
					unchecked(BinaryPrimitives.ReadUInt64LittleEndian(target) + delta));
			}
			else
			{
				BinaryPrimitives.WriteUInt32LittleEndian(target,
					unchecked(BinaryPrimitives.ReadUInt32LittleEndian(target) + (uint) delta));
			}
		}

		return image;
	}

	/// <summary>Encodes relocation blocks, one per 4 KiB page, each padded to a four-byte boundary.</summary>
	internal static byte[] RelocationBlocks(IEnumerable<(uint Rva, byte Type)> relocations)
	{
		List<byte> data = [];
		foreach (IGrouping<uint, (uint Rva, byte Type)> page in relocations.GroupBy(static relocation =>
					 relocation.Rva & ~0xFFFu).OrderBy(static page => page.Key))
		{
			List<ushort> entries =
			[
				.. page.Select(static relocation =>
					(ushort) ((relocation.Type << 12) | (int) (relocation.Rva & 0xFFF)))
			];
			if (entries.Count % 2 != 0)
			{
				entries.Add(0);
			}

			byte[] block = new byte[8 + (entries.Count * 2)];
			BinaryPrimitives.WriteUInt32LittleEndian(block, page.Key);
			BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), (uint) block.Length);
			for (int index = 0; index < entries.Count; index++)
			{
				BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(8 + (index * 2)), entries[index]);
			}

			data.AddRange(block);
		}

		return [.. data];
	}

	/// <summary>
	///     Encodes an export directory placed at <paramref name="rva" />: the directory, the function, name and ordinal
	///     tables, then the strings. A function with a forwarder points at its forwarder string.
	/// </summary>
	internal static byte[] ExportData(uint rva, string moduleName, uint ordinalBase,
		IReadOnlyList<(string? Name, uint Rva, string? Forwarder)> functions)
	{
		(string Name, int Index)[] named =
		[
			.. functions.Select(static (function, index) => (function.Name, index))
				.Where(static pair => pair.Name is not null)
				.Select(static pair => (pair.Name!, pair.index))
				.OrderBy(static pair => pair.Item1, StringComparer.Ordinal)
		];
		int functionsOffset = 40;
		int namesOffset = functionsOffset + (functions.Count * 4);
		int ordinalsOffset = namesOffset + (named.Length * 4);
		List<byte> strings = [];
		int stringsOffset = ordinalsOffset + (named.Length * 2);

		int Add(string text)
		{
			int offset = stringsOffset + strings.Count;
			strings.AddRange(Encoding.ASCII.GetBytes(text));
			strings.Add(0);
			return offset;
		}

		int moduleOffset = Add(moduleName);
		int[] nameOffsets = [.. named.Select(pair => Add(pair.Name))];
		int[] forwarderOffsets =
			[.. functions.Select(function => function.Forwarder is null ? -1 : Add(function.Forwarder))];
		byte[] data = new byte[stringsOffset + strings.Count];
		Span<byte> span = data;
		BinaryPrimitives.WriteUInt32LittleEndian(span[12..], rva + (uint) moduleOffset);
		BinaryPrimitives.WriteUInt32LittleEndian(span[16..], ordinalBase);
		BinaryPrimitives.WriteUInt32LittleEndian(span[20..], (uint) functions.Count);
		BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint) named.Length);
		BinaryPrimitives.WriteUInt32LittleEndian(span[28..], rva + (uint) functionsOffset);
		BinaryPrimitives.WriteUInt32LittleEndian(span[32..], rva + (uint) namesOffset);
		BinaryPrimitives.WriteUInt32LittleEndian(span[36..], rva + (uint) ordinalsOffset);
		for (int index = 0; index < functions.Count; index++)
		{
			uint target = forwarderOffsets[index] >= 0 ? rva + (uint) forwarderOffsets[index] : functions[index].Rva;
			BinaryPrimitives.WriteUInt32LittleEndian(span[(functionsOffset + (index * 4))..], target);
		}

		for (int index = 0; index < named.Length; index++)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(span[(namesOffset + (index * 4))..],
				rva + (uint) nameOffsets[index]);
			BinaryPrimitives.WriteUInt16LittleEndian(span[(ordinalsOffset + (index * 2))..],
				(ushort) named[index].Index);
		}

		strings.CopyTo(data, stringsOffset);
		return data;
	}

	/// <summary>Encodes one debug directory entry at <paramref name="rva" /> followed by its RSDS CodeView record.</summary>
	internal static byte[] DebugData(uint rva, Guid signature, uint age, string path)
	{
		byte[] pathBytes = Encoding.UTF8.GetBytes(path);
		byte[] data = new byte[28 + 24 + pathBytes.Length + 1];
		Span<byte> span = data;
		BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 2);
		BinaryPrimitives.WriteUInt32LittleEndian(span[16..], (uint) (24 + pathBytes.Length + 1));
		BinaryPrimitives.WriteUInt32LittleEndian(span[20..], rva + 28);
		BinaryPrimitives.WriteUInt32LittleEndian(span[28..], 0x53445352);
		signature.TryWriteBytes(span.Slice(32, 16));
		BinaryPrimitives.WriteUInt32LittleEndian(span[48..], age);
		pathBytes.CopyTo(span[52..]);
		return data;
	}

	/// <summary>
	///     Encodes an import directory (or, with <paramref name="delayLoaded" />, a delay-load directory) placed at
	///     <paramref name="rva" /> as a loader leaves it: the descriptors and their terminator, then per DLL its lookup
	///     table (unless the DLL has none) and its import address table already holding each function's value, then the
	///     hint/name entries and the DLL names. Delay-load descriptors take <paramref name="delayForm" />; in the address
	///     form, the descriptor fields and the named lookup entries hold addresses of the image linked at
	///     <paramref name="imageBase" />, and <see cref="TestImportTable.AddressFields" /> lists them for relocation.
	/// </summary>
	internal static TestImportTable ImportData(uint rva, bool is64, IReadOnlyList<TestImportDll> dlls,
		bool delayLoaded = false, TestDelayForm delayForm = TestDelayForm.Relative, ulong imageBase = 0)
	{
		int width = is64 ? 8 : 4;
		int descriptorSize = delayLoaded ? 32 : 20;
		int offset = (int) Align((uint) ((dlls.Count + 1) * descriptorSize), 8);
		int[] lookupOffsets = new int[dlls.Count];
		int[] slotOffsets = new int[dlls.Count];
		for (int index = 0; index < dlls.Count; index++)
		{
			int tableSize = (dlls[index].Functions.Count + 1) * width;
			lookupOffsets[index] = dlls[index].WithoutLookupTable ? -1 : offset;
			offset += dlls[index].WithoutLookupTable ? 0 : tableSize;
			slotOffsets[index] = offset;
			offset += tableSize;
		}

		int stringsOffset = offset;
		List<byte> strings = [];

		int Add(IEnumerable<byte> bytes)
		{
			int position = stringsOffset + strings.Count;
			strings.AddRange(bytes);
			if (strings.Count % 2 != 0)
			{
				strings.Add(0);
			}

			return position;
		}

		int[][] hintNames =
		[
			.. dlls.Select(dll => dll.Functions.Select(function => function.Name is null
				? -1
				: Add([
					(byte) function.Hint, (byte) (function.Hint >> 8), .. Encoding.ASCII.GetBytes(function.Name), 0
				])).ToArray())
		];
		int[] dllNames = [.. dlls.Select(dll => Add([.. Encoding.ASCII.GetBytes(dll.Dll), 0]))];
		byte[] data = new byte[stringsOffset + strings.Count];
		ulong ordinalFlag = is64 ? 1UL << 63 : 1UL << 31;
		bool addresses = delayLoaded && delayForm is TestDelayForm.Addresses;
		List<(uint Rva, byte Type)> addressFields = [];

		void WriteThunk(int position, ulong value)
		{
			if (is64)
			{
				BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(position), value);
			}
			else
			{
				BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(position), (uint) value);
			}
		}

		// Writes where the table data at dataOffset lies: in the address form, an address the loader relocates.
		void WriteField(int position, int dataOffset)
		{
			uint target = rva + (uint) dataOffset;
			if (addresses)
			{
				target = (uint) (imageBase + target);
				addressFields.Add((rva + (uint) position, PeRelocations.HighLow));
			}

			BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(position), target);
		}

		for (int index = 0; index < dlls.Count; index++)
		{
			TestImportDll dll = dlls[index];
			int descriptor = index * descriptorSize;
			if (delayLoaded)
			{
				BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(descriptor),
					delayForm is TestDelayForm.Relative ? 1u : 0u);
				WriteField(descriptor + 4, dllNames[index]);
				WriteField(descriptor + 12, slotOffsets[index]);
				if (lookupOffsets[index] >= 0)
				{
					WriteField(descriptor + 16, lookupOffsets[index]);
				}
			}
			else
			{
				if (lookupOffsets[index] >= 0)
				{
					WriteField(descriptor, lookupOffsets[index]);
				}

				WriteField(descriptor + 12, dllNames[index]);
				WriteField(descriptor + 16, slotOffsets[index]);
			}

			for (int function = 0; function < dll.Functions.Count; function++)
			{
				TestImport import = dll.Functions[function];
				WriteThunk(slotOffsets[index] + (function * width), import.Value);
				if (lookupOffsets[index] < 0)
				{
					continue;
				}

				int lookup = lookupOffsets[index] + (function * width);
				if (import.Ordinal is { } ordinal)
				{
					WriteThunk(lookup, ordinalFlag | ordinal);
				}
				else if (addresses)
				{
					// The old form's lookup entry is the relocated address of the hint and the name.
					WriteThunk(lookup, imageBase + rva + (uint) hintNames[index][function]);
					addressFields.Add((rva + (uint) lookup, is64 ? PeRelocations.Dir64 : PeRelocations.HighLow));
				}
				else
				{
					WriteThunk(lookup, rva + (uint) hintNames[index][function]);
				}
			}
		}

		strings.CopyTo(data, stringsOffset);
		return new TestImportTable(data, (uint) ((dlls.Count + 1) * descriptorSize),
			[.. lookupOffsets.Select(lookup => lookup < 0 ? 0 : rva + (uint) lookup)],
			[.. slotOffsets.Select(slot => rva + (uint) slot)], [.. addressFields]);
	}

	internal static uint Align(uint value, uint alignment)
	{
		return (value + alignment - 1) & ~(alignment - 1);
	}

	internal sealed record TestSection(
		string Name,
		uint VirtualAddress,
		byte[] Data,
		uint Characteristics,
		uint VirtualSize);
}

/// <summary>One imported function of <see cref="TestPe.ImportData" />.</summary>
/// <param name="Name">The imported name, or <see langword="null" /> for an import by ordinal.</param>
/// <param name="Value">The value the loader left in the function's import address table slot.</param>
/// <param name="Hint">The hint stored with the name.</param>
/// <param name="Ordinal">The imported ordinal, for an import by ordinal.</param>
internal sealed record TestImport(string? Name, ulong Value, ushort Hint = 0, ushort? Ordinal = null);

/// <summary>One descriptor of <see cref="TestPe.ImportData" />.</summary>
/// <param name="Dll">The DLL name.</param>
/// <param name="Functions">The imported functions in slot order.</param>
/// <param name="WithoutLookupTable">Whether the descriptor has no lookup table, as a bound import address table.</param>
internal sealed record TestImportDll(string Dll, IReadOnlyList<TestImport> Functions, bool WithoutLookupTable = false);

/// <summary>An encoded import or delay-load directory.</summary>
/// <param name="Data">The bytes to place at the directory's relative address.</param>
/// <param name="DescriptorsSize">The size of the descriptors and their terminator, for the data directory entry.</param>
/// <param name="LookupRvas">Each descriptor's lookup table address, or zero without one.</param>
/// <param name="SlotRvas">Each descriptor's first import address table slot.</param>
/// <param name="AddressFields">
///     The locations that hold an address of the image, with their relocation type: the descriptor fields and the named
///     lookup entries of the old delay-load form; empty otherwise.
/// </param>
internal sealed record TestImportTable(
	byte[] Data,
	uint DescriptorsSize,
	uint[] LookupRvas,
	uint[] SlotRvas,
	(uint Rva, byte Type)[] AddressFields);

/// <summary>How <see cref="TestPe.ImportData" /> lays out a delay-load descriptor.</summary>
internal enum TestDelayForm
{
	/// <summary>As Visual C++ 7 and later link it: the RVA attribute set and relative addresses.</summary>
	Relative,

	/// <summary>
	///     As linkers before Visual C++ 7 did: no attribute, and the descriptor and its lookup table hold relocated
	///     addresses.
	/// </summary>
	Addresses,

	/// <summary>As the PE specification lays it out: relative addresses without the RVA attribute.</summary>
	RelativeWithoutAttribute
}

/// <summary>A mapped image in a byte array, for the reader's bounds checks.</summary>
internal sealed class ArrayImage(byte[] image) : IPeImage
{
	internal int Reads
	{
		get;
		private set;
	}

	public void Read(uint rva, Span<byte> destination)
	{
		Reads++;
		if (rva + (ulong) destination.Length > (ulong) image.Length)
		{
			throw new InvalidDataException($"RVA {rva:X} is outside the image.");
		}

		image.AsSpan((int) rva, destination.Length).CopyTo(destination);
	}
}
