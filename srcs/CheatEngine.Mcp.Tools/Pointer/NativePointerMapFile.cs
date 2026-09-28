using System.IO.Compression;
using System.Text;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>CE version-1 .scandata interchange; no native objects or scan-worker state are retained.</summary>
/// <remarks>Layout: cheat-engine/cheat-engine, pointervaluelist.pas, exportToStream/LoadHeader/createFromStream.</remarks>
internal static class NativePointerMapFile
{
	internal const int MaximumFileBytes = 64 * 1024 * 1024;
	internal const int MaximumEntries = 1_000_000;
	private const int MaximumModules = 4096;
	private static readonly UTF8Encoding ModuleEncoding = new(false, true);

	internal static void Save(Stream output, PointerMap map, CancellationToken stopping)
	{
		ArgumentNullException.ThrowIfNull(output);
		ArgumentNullException.ThrowIfNull(map);
		if (map.Width is not (4 or 8) || map.Entries.Length is < 1 or > MaximumEntries || map.Modules.Length > MaximumModules)
		{
			throw new InvalidDataException("CE pointer maps require 1-1000000 entries, at most 4096 modules, and 4- or 8-byte pointers. CE cannot load an empty map.");
		}
		ulong limit = map.Width == 4 ? uint.MaxValue : ulong.MaxValue;
		if (map.Modules.Any(module => module.BaseAddress > limit) || map.StaticRange?.End > limit)
		{
			throw new InvalidDataException("A module or static range exceeds the pointer address width.");
		}
		{
			using ZLibStream compressed = new(output, CompressionLevel.Fastest, leaveOpen: true);
			using BinaryWriter writer = new(compressed, ModuleEncoding, leaveOpen: true);
			writer.Write((byte) 0xCE);
			writer.Write((byte) 1);
			writer.Write(map.Modules.Length);
			foreach (PointerModule module in map.Modules)
			{
				byte[] name = ModuleEncoding.GetBytes(module.Name);
				if (name.Length is < 1 or > 4096 || module.Name.Contains('\0'))
				{
					throw new InvalidDataException("Module names must contain 1-4096 UTF-8 bytes and no NUL characters.");
				}
				writer.Write(name.Length);
				writer.Write(name);
				writer.Write(module.BaseAddress);
			}
			// Static classification is retained per address, including direct-address (-1) roots.
			writer.Write(map.StaticRange.HasValue ? (byte) 1 : (byte) 0);
			if (map.StaticRange is { } range)
			{
				writer.Write(range.Start);
				writer.Write(range.End);
			}
			writer.Write(map.Width == 8 ? 15 : 7);
			writer.Write((ulong) map.Entries.Length);
			int first = 0;
			while (first < map.Entries.Length)
			{
				stopping.ThrowIfCancellationRequested();
				PointerEntry firstEntry = map.EntryByValue(first);
				int end = first + 1;
				while (end < map.Entries.Length && map.EntryByValue(end).Value == firstEntry.Value)
				{
					end++;
				}
				writer.Write(firstEntry.Value);
				writer.Write(end - first);
				for (int index = first; index < end; index++)
				{
					stopping.ThrowIfCancellationRequested();
					PointerEntry entry = map.EntryByValue(index);
					if (entry.Address > limit || entry.Value > limit)
					{
						throw new InvalidDataException("A pointer address or value exceeds the pointer address width.");
					}
					writer.Write(entry.Address);
					PointerStaticRoot? root = map.GetStaticRoot(entry.Address);
					writer.Write(root.HasValue ? (byte) 1 : (byte) 0);
					if (root is { } value)
					{
						if (value.Offset > uint.MaxValue)
						{
							throw new InvalidDataException("CE .scandata stores static offsets in 32 bits; this root cannot be exported without losing information.");
						}
						if (value.ModuleIndex < -1 || value.ModuleIndex >= map.Modules.Length ||
							value.ModuleIndex >= 0 &&
							(map.Modules[value.ModuleIndex].BaseAddress > limit - value.Offset ||
							 map.Modules[value.ModuleIndex].BaseAddress + value.Offset != entry.Address))
						{
							throw new InvalidDataException("A static root has an invalid module or address.");
						}
						writer.Write(value.ModuleIndex);
						writer.Write((uint) value.Offset);
					}
				}
				first = end;
			}
		}
	}

	internal static PointerMap Load(Stream input, int maximumPointers, CancellationToken stopping)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPointers);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumPointers, MaximumEntries);
		return Read();

		PointerMap Read()
		{
			using ZLibStream compressed = new(input, CompressionMode.Decompress, leaveOpen: true);
			using BinaryReader reader = new(compressed, ModuleEncoding);
			byte signature = reader.ReadByte();
			byte version = reader.ReadByte();
			if (signature != 0xCE || version != 1)
			{
				throw new InvalidDataException("Expected a CE version-1 .scandata pointer map.");
			}
			int moduleCount = ReadCount(reader, MaximumModules, "module");
			PointerModule[] modules = new PointerModule[moduleCount];
			for (int index = 0; index < moduleCount; index++)
			{
				stopping.ThrowIfCancellationRequested();
				int length = ReadCount(reader, 4096, "module-name byte");
				byte[] bytes = reader.ReadBytes(length);
				if (length == 0 || bytes.Length != length || bytes.Contains((byte) 0))
				{
					throw new InvalidDataException("Invalid or truncated module name.");
				}
				modules[index] = new PointerModule(ModuleEncoding.GetString(bytes), reader.ReadUInt64(), 0);
			}
			PointerStaticRange? staticRange = null;
			if (ReadFlag(reader))
			{
				ulong first = reader.ReadUInt64();
				ulong last = reader.ReadUInt64();
				if (last < first)
				{
					throw new InvalidDataException("Reversed native static-base range.");
				}
				staticRange = new PointerStaticRange(first, last);
			}
			int width = reader.ReadUInt32() switch
			{
				7 => 4,
				15 => 8,
				_ => throw new InvalidDataException("Unsupported native pointer-map address width.")
			};
			ulong limit = width == 4 ? uint.MaxValue : ulong.MaxValue;
			if (modules.Any(module => module.BaseAddress > limit) || staticRange?.End > limit)
			{
				throw new InvalidDataException("Module address exceeds the native pointer width.");
			}
			ulong count = reader.ReadUInt64();
			if (count == 0 || count > (ulong) maximumPointers)
			{
				throw new InvalidDataException($"Native map has {count} pointers; supported count is 1-{maximumPointers}. No partial map was loaded.");
			}
			PointerEntry[] entries = new PointerEntry[(int) count];
			Dictionary<ulong, PointerStaticRoot> roots = [];
			ulong previousValue = 0;
			int position = 0;
			while (position < entries.Length)
			{
				stopping.ThrowIfCancellationRequested();
				ulong value = reader.ReadUInt64();
				if (value > limit || (position != 0 && value <= previousValue))
				{
					throw new InvalidDataException("Native pointer values are out of range or not strictly ordered.");
				}
				previousValue = value;
				int groupSize = ReadCount(reader, entries.Length - position, "pointer group");
				if (groupSize == 0)
				{
					throw new InvalidDataException("Native pointer groups cannot be empty.");
				}
				for (int index = 0; index < groupSize; index++)
				{
					stopping.ThrowIfCancellationRequested();
					ulong address = reader.ReadUInt64();
					if (address > limit)
					{
						throw new InvalidDataException("Pointer storage address exceeds the native pointer width.");
					}
					entries[position++] = new PointerEntry(address, value);
					if (ReadFlag(reader))
					{
						int moduleIndex = reader.ReadInt32();
						uint offset = reader.ReadUInt32();
						if (moduleIndex < -1 || moduleIndex >= modules.Length)
						{
							throw new InvalidDataException("Native static root refers to an invalid module index.");
						}
						if (moduleIndex >= 0)
						{
							PointerModule module = modules[moduleIndex];
							if (module.BaseAddress > limit - offset || module.BaseAddress + offset != address)
							{
								throw new InvalidDataException("Native static root does not match its module-relative address.");
							}
							// Only these root offsets are known; do not invent an image size from the next module base.
							modules[moduleIndex] = module with
							{
								Size = Math.Max(module.Size, (ulong) offset + 1)
							};
						}
						if (!roots.TryAdd(address, new PointerStaticRoot(moduleIndex, offset)))
						{
							throw new InvalidDataException("Duplicate pointer storage address.");
						}
					}
				}
			}
			// Every allocation and decompressed read above is count-bounded, independent of compressed size.
			if (compressed.ReadByte() != -1)
			{
				throw new InvalidDataException("Unexpected data after native pointer-map entries.");
			}
			Array.Sort(entries, (left, right) => left.Address.CompareTo(right.Address));
			for (int index = 1; index < entries.Length; index++)
			{
				if (entries[index - 1].Address == entries[index].Address)
				{
					throw new InvalidDataException("Duplicate pointer storage address.");
				}
			}
			stopping.ThrowIfCancellationRequested();
			// Native files contain neither process identity nor capture/completeness statistics.
			return new PointerMap(null, width, entries, modules, true, 0, 0, roots, staticRange);
		}
	}

	private static int ReadCount(BinaryReader reader, int maximum, string field)
	{
		uint value = reader.ReadUInt32();
		return value <= maximum ? (int) value : throw new InvalidDataException($"Native {field} count exceeds {maximum}.");
	}

	private static bool ReadFlag(BinaryReader reader) => reader.ReadByte() switch
	{
		0 => false,
		1 => true,
		_ => throw new InvalidDataException("Invalid native boolean flag.")
	};
}
