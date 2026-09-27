using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace CheatEngine.Mcp.Tools.Modules;

/// <summary>A mapped PE image addressed by relative virtual address, such as a module in target memory.</summary>
internal interface IPeImage
{
	/// <summary>Copies exactly <paramref name="destination" />.Length bytes that start at <paramref name="rva" />.</summary>
	/// <param name="rva">The relative virtual address of the first byte.</param>
	/// <param name="destination">Receives the bytes.</param>
	/// <exception cref="InvalidDataException">The range lies outside the image.</exception>
	public void Read(uint rva, Span<byte> destination);
}

/// <summary>One entry of the optional header's data directory.</summary>
/// <param name="Rva">The relative virtual address of the table.</param>
/// <param name="Size">The size of the table in bytes.</param>
internal readonly record struct PeDataDirectory(uint Rva, uint Size)
{
	/// <summary>Whether the image has this table.</summary>
	internal bool IsPresent => Rva != 0 && Size != 0;

	/// <summary>Whether <paramref name="rva" /> lies inside the table.</summary>
	/// <param name="rva">A relative virtual address.</param>
	/// <returns><see langword="true" /> when the table contains the address.</returns>
	internal bool Contains(uint rva)
	{
		return IsPresent && rva >= Rva && rva < (ulong) Rva + Size;
	}
}

/// <summary>One section header.</summary>
/// <param name="Name">The section name, up to eight characters.</param>
/// <param name="VirtualAddress">The section's relative virtual address.</param>
/// <param name="VirtualSize">The section's size in memory.</param>
/// <param name="PointerToRawData">The file offset of the section's data.</param>
/// <param name="SizeOfRawData">The size of the section's data in the file.</param>
/// <param name="Characteristics">The section flags.</param>
internal sealed record PeSection(
	string Name,
	uint VirtualAddress,
	uint VirtualSize,
	uint PointerToRawData,
	uint SizeOfRawData,
	uint Characteristics)
{
	private const uint ContainsCode = 0x00000020;
	private const uint MemoryExecute = 0x20000000;
	private const uint MemoryWrite = 0x80000000;

	/// <summary>Whether the section holds code or is mapped executable.</summary>
	internal bool IsExecutable => (Characteristics & (ContainsCode | MemoryExecute)) != 0;

	/// <summary>Whether the section is mapped writable.</summary>
	internal bool IsWritable => (Characteristics & MemoryWrite) != 0;

	/// <summary>The number of bytes the file supplies to the mapped section; the rest of the section is zero-filled.</summary>
	internal uint FileBackedSize => VirtualSize == 0 ? SizeOfRawData : Math.Min(VirtualSize, SizeOfRawData);
}

/// <summary>The fields of a PE image's headers that the module tools report or rely on.</summary>
/// <param name="Machine">The COFF machine type.</param>
/// <param name="TimeDateStamp">The COFF time stamp, which reproducible builds fill with a hash.</param>
/// <param name="Characteristics">The COFF characteristics.</param>
/// <param name="IsPe32Plus">Whether the optional header is PE32+ (64-bit).</param>
/// <param name="AddressOfEntryPoint">The entry point's relative virtual address, or zero.</param>
/// <param name="ImageBase">The optional header's image base as found in these bytes.</param>
/// <param name="SizeOfImage">The mapped size of the image.</param>
/// <param name="SizeOfHeaders">The size of the headers.</param>
/// <param name="Subsystem">The optional header's subsystem.</param>
/// <param name="Directories">The data directory, always 16 entries (absent ones are zero).</param>
/// <param name="Sections">The section headers in table order.</param>
internal sealed record PeHeaders(
	ushort Machine,
	uint TimeDateStamp,
	ushort Characteristics,
	bool IsPe32Plus,
	uint AddressOfEntryPoint,
	ulong ImageBase,
	uint SizeOfImage,
	uint SizeOfHeaders,
	ushort Subsystem,
	ImmutableArray<PeDataDirectory> Directories,
	ImmutableArray<PeSection> Sections)
{
	internal const int ExportDirectory = 0;
	internal const int BaseRelocationDirectory = 5;
	internal const int DebugDirectory = 6;
	internal const int ImportAddressTableDirectory = 12;
	internal const int ClrRuntimeHeaderDirectory = 14;

	/// <summary>Whether the image is a DLL.</summary>
	internal bool IsDll => (Characteristics & 0x2000) != 0;

	/// <summary>Whether the image has a CLR runtime header, which marks a .NET assembly.</summary>
	internal bool IsManaged => Directories[ClrRuntimeHeaderDirectory].IsPresent;

	/// <summary>Returns the section that maps <paramref name="rva" />, if any.</summary>
	/// <param name="rva">A relative virtual address.</param>
	/// <returns>The section, or <see langword="null" />.</returns>
	internal PeSection? FindSection(uint rva)
	{
		foreach (PeSection section in Sections)
		{
			uint size = Math.Max(section.VirtualSize, section.SizeOfRawData);
			if (rva >= section.VirtualAddress && rva < (ulong) section.VirtualAddress + size)
			{
				return section;
			}
		}

		return null;
	}
}

/// <summary>One exported symbol.</summary>
/// <param name="Name">The export name, or <see langword="null" /> for an ordinal-only export.</param>
/// <param name="Ordinal">The export ordinal.</param>
/// <param name="Rva">The function's relative virtual address; for a forwarder, the forwarder string's.</param>
/// <param name="Forwarder">The forwarder, such as <c>NTDLL.RtlAllocateHeap</c>, or <see langword="null" />.</param>
internal sealed record PeExport(string? Name, uint Ordinal, uint Rva, string? Forwarder);

/// <summary>The CodeView record that names the image's PDB.</summary>
/// <param name="Path">The PDB path recorded at link time.</param>
/// <param name="Guid">The PDB signature.</param>
/// <param name="Age">The PDB age.</param>
internal sealed record PeCodeView(string Path, Guid Guid, uint Age);

/// <summary>
///     The base relocations of an image, sorted by address: the locations the loader adjusts by the difference between
///     the actual and the preferred image base.
/// </summary>
internal sealed class PeRelocations
{
	internal const byte Absolute = 0;
	internal const byte High = 1;
	internal const byte Low = 2;
	internal const byte HighLow = 3;
	internal const byte Dir64 = 10;

	internal PeRelocations(uint[] rvas, byte[] types, int unsupported)
	{
		Rvas = rvas;
		Types = types;
		Unsupported = unsupported;
	}

	/// <summary>An image without relocations.</summary>
	internal static PeRelocations None
	{
		get;
	} = new([], [], 0);

	/// <summary>The relocated locations in ascending order.</summary>
	internal uint[] Rvas
	{
		get;
	}

	/// <summary>The relocation type of each location.</summary>
	internal byte[] Types
	{
		get;
	}

	/// <summary>How many relocations have a type these tools do not apply.</summary>
	internal int Unsupported
	{
		get;
	}

	/// <summary>The number of bytes a relocation type changes, or zero for one that is not applied.</summary>
	/// <param name="type">The relocation type.</param>
	/// <returns>The width in bytes.</returns>
	internal static int Width(byte type)
	{
		return type switch
		{
			HighLow => 4,
			Dir64 => 8,
			High or Low => 2,
			_ => 0
		};
	}
}

/// <summary>
///     A managed, bounded reader of PE images. It never trusts a count or an offset: every table must lie inside the
///     bytes it was given, and every count has a fixed ceiling, so a malformed image raises
///     <see cref="InvalidDataException" /> instead of an unbounded allocation or read.
/// </summary>
internal static class PeImageReader
{
	/// <summary>The most header bytes the tools read from a module or a file.</summary>
	internal const int MaximumHeaderBytes = 4096;

	/// <summary>The most sections the Windows loader accepts.</summary>
	internal const int MaximumSections = 96;

	/// <summary>The most exported functions or names read from one module.</summary>
	internal const int MaximumExports = 65536;

	/// <summary>The most entries read from one base-relocation table.</summary>
	internal const int MaximumRelocationEntries = 1_000_000;

	/// <summary>The longest export name or forwarder read, in bytes.</summary>
	internal const int MaximumNameBytes = 1024;

	/// <summary>The most debug directory entries inspected.</summary>
	internal const int MaximumDebugEntries = 32;

	/// <summary>The most CodeView bytes read.</summary>
	internal const int MaximumCodeViewBytes = 1024;

	private const ushort DosSignature = 0x5A4D;
	private const uint NtSignature = 0x00004550;
	private const ushort Pe32Magic = 0x10B;
	private const ushort Pe32PlusMagic = 0x20B;
	private const int FileHeaderSize = 20;
	private const int SectionHeaderSize = 40;
	private const int DataDirectoryCount = 16;
	private const int DebugEntrySize = 28;
	private const uint CodeViewType = 2;
	private const uint RsdsSignature = 0x53445352;

	/// <summary>Parses the DOS, NT and section headers found at the start of <paramref name="image" />.</summary>
	/// <param name="image">The first bytes of a mapped image or of a PE file, up to <see cref="MaximumHeaderBytes" />.</param>
	/// <returns>The parsed headers.</returns>
	/// <exception cref="InvalidDataException">The bytes are not a PE image, or a table lies outside them.</exception>
	internal static PeHeaders ParseHeaders(ReadOnlySpan<byte> image)
	{
		if (image.Length < 64 || BinaryPrimitives.ReadUInt16LittleEndian(image) != DosSignature)
		{
			throw new InvalidDataException("The image does not start with an MZ header.");
		}

		int ntOffset = BinaryPrimitives.ReadInt32LittleEndian(image[0x3C..]);
		if (ntOffset < 64 || (long) ntOffset + 4 + FileHeaderSize > image.Length)
		{
			throw new InvalidDataException("The PE header offset lies outside the header bytes.");
		}

		if (BinaryPrimitives.ReadUInt32LittleEndian(image[ntOffset..]) != NtSignature)
		{
			throw new InvalidDataException("The PE signature is missing.");
		}

		ReadOnlySpan<byte> fileHeader = image.Slice(ntOffset + 4, FileHeaderSize);
		ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(fileHeader);
		int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(fileHeader[2..]);
		uint timeDateStamp = BinaryPrimitives.ReadUInt32LittleEndian(fileHeader[4..]);
		int optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(fileHeader[16..]);
		ushort characteristics = BinaryPrimitives.ReadUInt16LittleEndian(fileHeader[18..]);
		int optionalOffset = ntOffset + 4 + FileHeaderSize;
		if (optionalSize < 2 || (long) optionalOffset + optionalSize > image.Length)
		{
			throw new InvalidDataException("The optional header lies outside the header bytes.");
		}

		ReadOnlySpan<byte> optional = image.Slice(optionalOffset, optionalSize);
		ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(optional);
		bool plus = magic switch
		{
			Pe32Magic => false,
			Pe32PlusMagic => true,
			_ => throw new InvalidDataException("The optional header is neither PE32 nor PE32+.")
		};
		int directoriesOffset = plus ? 112 : 96;
		if (optional.Length < directoriesOffset)
		{
			throw new InvalidDataException("The optional header is too short.");
		}

		uint entryPoint = BinaryPrimitives.ReadUInt32LittleEndian(optional[16..]);
		ulong imageBase = plus
			? BinaryPrimitives.ReadUInt64LittleEndian(optional[24..])
			: BinaryPrimitives.ReadUInt32LittleEndian(optional[28..]);
		uint sizeOfImage = BinaryPrimitives.ReadUInt32LittleEndian(optional[56..]);
		uint sizeOfHeaders = BinaryPrimitives.ReadUInt32LittleEndian(optional[60..]);
		ushort subsystem = BinaryPrimitives.ReadUInt16LittleEndian(optional[68..]);
		uint declared = BinaryPrimitives.ReadUInt32LittleEndian(optional[(directoriesOffset - 4)..]);
		int available = (optional.Length - directoriesOffset) / 8;
		int directoryCount = (int) Math.Min(Math.Min(declared, DataDirectoryCount), (uint) available);
		ImmutableArray<PeDataDirectory>.Builder directories = ImmutableArray.CreateBuilder<PeDataDirectory>(
			DataDirectoryCount);
		for (int index = 0; index < DataDirectoryCount; index++)
		{
			if (index < directoryCount)
			{
				ReadOnlySpan<byte> entry = optional.Slice(directoriesOffset + (index * 8), 8);
				directories.Add(new PeDataDirectory(BinaryPrimitives.ReadUInt32LittleEndian(entry),
					BinaryPrimitives.ReadUInt32LittleEndian(entry[4..])));
			}
			else
			{
				directories.Add(default);
			}
		}

		if (sectionCount > MaximumSections)
		{
			throw new InvalidDataException($"The image declares {sectionCount} sections, more than {MaximumSections}.");
		}

		int sectionsOffset = optionalOffset + optionalSize;
		if (sectionsOffset + ((long) sectionCount * SectionHeaderSize) > image.Length)
		{
			throw new InvalidDataException("The section table lies outside the header bytes.");
		}

		ImmutableArray<PeSection>.Builder sections = ImmutableArray.CreateBuilder<PeSection>(sectionCount);
		for (int index = 0; index < sectionCount; index++)
		{
			ReadOnlySpan<byte> header = image.Slice(sectionsOffset + (index * SectionHeaderSize), SectionHeaderSize);
			sections.Add(new PeSection(SectionName(header[..8]), BinaryPrimitives.ReadUInt32LittleEndian(header[12..]),
				BinaryPrimitives.ReadUInt32LittleEndian(header[8..]),
				BinaryPrimitives.ReadUInt32LittleEndian(header[20..]),
				BinaryPrimitives.ReadUInt32LittleEndian(header[16..]),
				BinaryPrimitives.ReadUInt32LittleEndian(header[36..])));
		}

		return new PeHeaders(machine, timeDateStamp, characteristics, plus, entryPoint, imageBase, sizeOfImage,
			sizeOfHeaders, subsystem, directories.MoveToImmutable(), sections.MoveToImmutable());
	}

	/// <summary>Reads the export directory: one entry per exported name, then one per unnamed function, by ordinal.</summary>
	/// <param name="headers">The image's headers.</param>
	/// <param name="image">The mapped image.</param>
	/// <returns>The exports; empty when the image exports nothing.</returns>
	/// <exception cref="InvalidDataException">The export directory is malformed or exceeds a ceiling.</exception>
	internal static List<PeExport> ReadExports(PeHeaders headers, IPeImage image)
	{
		ArgumentNullException.ThrowIfNull(headers);
		ArgumentNullException.ThrowIfNull(image);
		PeDataDirectory directory = headers.Directories[PeHeaders.ExportDirectory];
		if (!directory.IsPresent)
		{
			return [];
		}

		Span<byte> table = stackalloc byte[40];
		image.Read(directory.Rva, table);
		uint ordinalBase = BinaryPrimitives.ReadUInt32LittleEndian(table[16..]);
		uint functionCount = BinaryPrimitives.ReadUInt32LittleEndian(table[20..]);
		uint nameCount = BinaryPrimitives.ReadUInt32LittleEndian(table[24..]);
		uint functionsRva = BinaryPrimitives.ReadUInt32LittleEndian(table[28..]);
		uint namesRva = BinaryPrimitives.ReadUInt32LittleEndian(table[32..]);
		uint ordinalsRva = BinaryPrimitives.ReadUInt32LittleEndian(table[36..]);
		if (functionCount > MaximumExports || nameCount > MaximumExports)
		{
			throw new InvalidDataException(
				$"The export directory declares {functionCount} functions and {nameCount} names; at most {MaximumExports} of each are read.");
		}

		uint[] functions = ReadUInt32s(image, functionsRva, (int) functionCount);
		uint[] names = ReadUInt32s(image, namesRva, (int) nameCount);
		ushort[] ordinals = new ushort[nameCount];
		if (nameCount > 0)
		{
			byte[] raw = new byte[nameCount * 2];
			image.Read(ordinalsRva, raw);
			for (int index = 0; index < ordinals.Length; index++)
			{
				ordinals[index] = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(index * 2));
			}
		}

		List<PeExport> exports = new((int) Math.Max(functionCount, nameCount));
		bool[] named = new bool[functionCount];
		for (int index = 0; index < names.Length; index++)
		{
			int function = ordinals[index];
			if (function >= functions.Length)
			{
				throw new InvalidDataException($"Export name {index} points past the function table.");
			}

			named[function] = true;
			exports.Add(Export(headers, directory, image, ReadCString(image, names[index], MaximumNameBytes),
				ordinalBase + (uint) function, functions[function]));
		}

		for (int function = 0; function < functions.Length; function++)
		{
			if (!named[function] && functions[function] != 0)
			{
				exports.Add(Export(headers, directory, image, null, ordinalBase + (uint) function,
					functions[function]));
			}
		}

		exports.Sort(static (left, right) => left.Ordinal != right.Ordinal
			? left.Ordinal.CompareTo(right.Ordinal)
			: string.CompareOrdinal(left.Name, right.Name));
		return exports;
	}

	/// <summary>Finds the image's first RSDS CodeView record, which names its PDB.</summary>
	/// <param name="headers">The image's headers.</param>
	/// <param name="image">The mapped image.</param>
	/// <returns>The record, or <see langword="null" /> when the image names no PDB.</returns>
	/// <exception cref="InvalidDataException">A debug entry points outside the image.</exception>
	internal static PeCodeView? ReadCodeView(PeHeaders headers, IPeImage image)
	{
		ArgumentNullException.ThrowIfNull(headers);
		ArgumentNullException.ThrowIfNull(image);
		PeDataDirectory directory = headers.Directories[PeHeaders.DebugDirectory];
		if (!directory.IsPresent)
		{
			return null;
		}

		int count = (int) Math.Min(directory.Size / DebugEntrySize, MaximumDebugEntries);
		byte[] entries = new byte[count * DebugEntrySize];
		image.Read(directory.Rva, entries);
		for (int index = 0; index < count; index++)
		{
			ReadOnlySpan<byte> entry = entries.AsSpan(index * DebugEntrySize, DebugEntrySize);
			uint type = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
			uint size = BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]);
			uint rva = BinaryPrimitives.ReadUInt32LittleEndian(entry[20..]);
			if (type != CodeViewType || rva == 0 || size < 25)
			{
				continue;
			}

			byte[] record = new byte[Math.Min(size, MaximumCodeViewBytes)];
			image.Read(rva, record);
			if (BinaryPrimitives.ReadUInt32LittleEndian(record) != RsdsSignature)
			{
				continue;
			}

			Guid guid = new(record.AsSpan(4, 16));
			uint age = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(20));
			ReadOnlySpan<byte> path = record.AsSpan(24);
			int end = path.IndexOf((byte) 0);
			return new PeCodeView(Encoding.UTF8.GetString(end < 0 ? path : path[..end]), guid, age);
		}

		return null;
	}

	/// <summary>Parses a base relocation table.</summary>
	/// <param name="data">The table's bytes.</param>
	/// <returns>The relocations, sorted by address.</returns>
	/// <exception cref="InvalidDataException">A block is malformed.</exception>
	internal static PeRelocations ParseRelocations(ReadOnlySpan<byte> data)
	{
		List<uint> rvas = [];
		List<byte> types = [];
		int relocationEntries = 0;
		int unsupported = 0;
		int offset = 0;
		while (data.Length - offset >= 8)
		{
			uint page = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
			uint blockSize = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
			if (blockSize == 0 && page == 0)
			{
				// Some linkers pad the table with an empty block.
				break;
			}

			if (blockSize < 8 || blockSize % 2 != 0 || blockSize > (uint) (data.Length - offset))
			{
				throw new InvalidDataException($"The relocation block at table offset {offset} is malformed.");
			}

			int entries = ((int) blockSize - 8) / 2;
			if (entries > MaximumRelocationEntries - relocationEntries)
			{
				throw new InvalidDataException(
					$"The relocation table declares more than {MaximumRelocationEntries} entries.");
			}

			relocationEntries += entries;
			for (int entry = offset + 8; entry < offset + (int) blockSize; entry += 2)
			{
				ushort value = BinaryPrimitives.ReadUInt16LittleEndian(data[entry..]);
				byte type = (byte) (value >> 12);
				if (type == PeRelocations.Absolute)
				{
					continue;
				}

				if (PeRelocations.Width(type) == 0)
				{
					unsupported++;
					continue;
				}

				rvas.Add(page + (uint) (value & 0xFFF));
				types.Add(type);
			}

			offset += (int) blockSize;
		}

		uint[] sortedRvas = [.. rvas];
		byte[] sortedTypes = [.. types];
		Array.Sort(sortedRvas, sortedTypes);
		return new PeRelocations(sortedRvas, sortedTypes, unsupported);
	}

	/// <summary>
	///     Applies relocations that overlap <c>[countFrom, countTo)</c> and lie wholly inside the window, adding
	///     <paramref name="delta" /> as the loader does. Relocations that start before <paramref name="countFrom" />
	///     are adjusted for a spill window but are not counted, so adjacent chunks count every relocation once.
	/// </summary>
	/// <param name="window">File bytes mapped at <paramref name="windowRva" />, changed in place.</param>
	/// <param name="windowRva">The relative virtual address of the window's first byte.</param>
	/// <param name="relocations">The image's relocations.</param>
	/// <param name="delta">The actual image base minus the preferred one, modulo 2^64.</param>
	/// <param name="countFrom">The first address of the compared range.</param>
	/// <param name="countTo">The address after the compared range.</param>
	/// <returns>How many applied relocations start in the compared range.</returns>
	internal static int ApplyRelocations(Span<byte> window, uint windowRva, PeRelocations relocations, ulong delta,
		uint countFrom, uint countTo)
	{
		ArgumentNullException.ThrowIfNull(relocations);
		int index = LowerBound(relocations.Rvas, windowRva);
		int applied = 0;
		for (; index < relocations.Rvas.Length && relocations.Rvas[index] < countTo; index++)
		{
			uint rva = relocations.Rvas[index];
			byte type = relocations.Types[index];
			int width = PeRelocations.Width(type);
			if ((ulong) rva + (uint) width <= countFrom)
			{
				continue;
			}

			long position = (long) rva - windowRva;
			if (position < 0 || position + width > window.Length)
			{
				continue;
			}

			Span<byte> target = window.Slice((int) position, width);
			switch (type)
			{
				case PeRelocations.HighLow:
					BinaryPrimitives.WriteUInt32LittleEndian(target,
						unchecked(BinaryPrimitives.ReadUInt32LittleEndian(target) + (uint) delta));
					break;
				case PeRelocations.Dir64:
					BinaryPrimitives.WriteUInt64LittleEndian(target,
						unchecked(BinaryPrimitives.ReadUInt64LittleEndian(target) + delta));
					break;
				case PeRelocations.High:
					BinaryPrimitives.WriteUInt16LittleEndian(target,
						unchecked((ushort) (BinaryPrimitives.ReadUInt16LittleEndian(target) + (ushort) (delta >> 16))));
					break;
				default:
					BinaryPrimitives.WriteUInt16LittleEndian(target,
						unchecked((ushort) (BinaryPrimitives.ReadUInt16LittleEndian(target) + (ushort) delta)));
					break;
			}

			if (rva >= countFrom)
			{
				applied++;
			}
		}

		return applied;
	}

	/// <summary>Maps a relative virtual address to its offset in the PE file.</summary>
	/// <param name="headers">The file's headers.</param>
	/// <param name="rva">The relative virtual address.</param>
	/// <param name="length">The number of bytes that must be backed by the file.</param>
	/// <param name="offset">The file offset when this method returns <see langword="true" />.</param>
	/// <returns><see langword="true" /> when the whole range is backed by file data.</returns>
	internal static bool TryMapToFile(PeHeaders headers, uint rva, uint length, out long offset)
	{
		ArgumentNullException.ThrowIfNull(headers);
		if ((ulong) rva + length <= headers.SizeOfHeaders)
		{
			offset = rva;
			return true;
		}

		foreach (PeSection section in headers.Sections)
		{
			if (rva >= section.VirtualAddress &&
				(ulong) rva + length <= (ulong) section.VirtualAddress + section.SizeOfRawData)
			{
				offset = section.PointerToRawData + (long) (rva - section.VirtualAddress);
				return true;
			}
		}

		offset = 0;
		return false;
	}

	private static PeExport Export(PeHeaders headers, PeDataDirectory directory, IPeImage image, string? name,
		uint ordinal, uint rva)
	{
		if (rva == 0 || rva >= headers.SizeOfImage)
		{
			throw new InvalidDataException($"Export ordinal {ordinal} points outside the image.");
		}

		string? forwarder = directory.Contains(rva) ? ReadCString(image, rva, MaximumNameBytes) : null;
		return new PeExport(name, ordinal, rva, forwarder);
	}

	private static uint[] ReadUInt32s(IPeImage image, uint rva, int count)
	{
		if (count == 0)
		{
			return [];
		}

		byte[] raw = new byte[count * 4];
		image.Read(rva, raw);
		uint[] values = new uint[count];
		for (int index = 0; index < count; index++)
		{
			values[index] = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(index * 4));
		}

		return values;
	}

	/// <summary>Reads a NUL-terminated string of at most <paramref name="maximum" /> bytes, 64 bytes at a time.</summary>
	private static string ReadCString(IPeImage image, uint rva, int maximum)
	{
		byte[] buffer = new byte[maximum];
		int length = 0;
		while (length < maximum)
		{
			int step = Math.Min(64, maximum - length);
			Span<byte> chunk = buffer.AsSpan(length, step);
			try
			{
				image.Read(checked(rva + (uint) length), chunk);
			}
			catch (InvalidDataException) when (length > 0 || step > 1)
			{
				// The string may end close to the end of the image: read the rest one byte at a time.
				step = 1;
				chunk = buffer.AsSpan(length, 1);
				image.Read(checked(rva + (uint) length), chunk);
			}

			int end = chunk.IndexOf((byte) 0);
			if (end >= 0)
			{
				return Encoding.UTF8.GetString(buffer, 0, length + end);
			}

			length += step;
		}

		throw new InvalidDataException($"A string at RVA {rva:X} is longer than {maximum} bytes.");
	}

	private static string SectionName(ReadOnlySpan<byte> raw)
	{
		int end = raw.IndexOf((byte) 0);
		return Encoding.UTF8.GetString(end < 0 ? raw : raw[..end]);
	}

	private static int LowerBound(uint[] values, uint value)
	{
		int low = 0;
		int high = values.Length;
		while (low < high)
		{
			int middle = low + ((high - low) / 2);
			if (values[middle] < value)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low;
	}
}
