using System.Buffers.Binary;
using System.Text;

using CheatEngine.Mcp.Tools.Modules;

namespace CheatEngine.Mcp.Tests.Tools.Modules;

/// <summary>
///     Builds small PE32 and PE32+ images at test time: headers, sections, export, debug and relocation tables, the file
///     layout and the image a loader would map at another base.
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
		: _sections.Max(static section => section.VirtualAddress + Math.Max(section.VirtualSize, (uint) section.Data.Length)),
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
			List<ushort> entries = [.. page.Select(static relocation =>
				(ushort) ((relocation.Type << 12) | (int) (relocation.Rva & 0xFFF)))];
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
		int[] forwarderOffsets = [.. functions.Select(function => function.Forwarder is null ? -1 : Add(function.Forwarder))];
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
			BinaryPrimitives.WriteUInt32LittleEndian(span[(namesOffset + (index * 4))..], rva + (uint) nameOffsets[index]);
			BinaryPrimitives.WriteUInt16LittleEndian(span[(ordinalsOffset + (index * 2))..], (ushort) named[index].Index);
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
		if ((ulong) rva + (ulong) destination.Length > (ulong) image.Length)
		{
			throw new InvalidDataException($"RVA {rva:X} is outside the image.");
		}

		image.AsSpan((int) rva, destination.Length).CopyTo(destination);
	}
}
