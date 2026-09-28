using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>
///     The managed C generator of <c>structure_generate_c_header</c>: forward typedefs, then one definition per
///     structure packed to one byte, with every member at its Cheat Engine offset, padding arrays for gaps, and
///     offsets, value types and the size check as comments.
/// </summary>
/// <remarks>
///     <para>
///         Identifiers keep ASCII letters, digits and underscores; any other character becomes an underscore, a
///         leading digit gains one, C and C++ keywords and the typedef and macro names of <c>stdint.h</c> gain a
///         trailing one, and repeats gain <c>_2</c>, <c>_3</c> and so on. An unnamed element is <c>field_</c> and its
///         hexadecimal offset.
///     </para>
///     <para>
///         An element C cannot place, at a negative offset, without a size, or overlapping the member before it (a
///         union or a bit field sharing its storage), becomes a comment line. Cheat Engine's size ends at the element
///         with the highest offset, so an earlier, longer element can make a definition larger; its size check
///         then states the size C gives it. An embedded (nested) child structure is defined before its parent; one
///         that would contain itself, or whose C size differs from the element's, becomes a byte array. A pointer
///         that lands inside its child structure (<c>ChildStructStart</c>) is a <c>uint8_t *</c>, since a pointer to
///         the child type would address the child's members from the wrong base. Comments carry Cheat Engine's names
///         with every character outside printable ASCII replaced.
///     </para>
/// </remarks>
internal static class StructureHeaderWriter
{
	private const int Visiting = 1;
	private const int Visited = 2;

	/// <summary>
	///     The names an identifier may not take: C and C++ keywords, and the typedefs and object-like macros of
	///     <c>stdint.h</c>, which the header includes.
	/// </summary>
	private static readonly FrozenSet<string> Reserved = FrozenSet.Create(StringComparer.Ordinal,
	[
		"_Alignas", "_Alignof", "_Atomic", "_BitInt", "_Bool", "_Complex", "_Decimal128", "_Decimal32", "_Decimal64",
		"_Generic", "_Imaginary", "_Noreturn", "_Static_assert", "_Thread_local", "NULL", "alignas", "alignof", "and",
		"and_eq", "asm", "auto", "bitand", "bitor", "bool", "break", "case", "catch", "char", "char16_t", "char32_t",
		"char8_t", "class", "co_await", "co_return", "co_yield", "compl", "concept", "const", "const_cast", "consteval",
		"constexpr", "constinit", "continue", "decltype", "default", "delete", "do", "double", "dynamic_cast", "else",
		"enum", "explicit", "export", "extern", "false", "float", "for", "friend", "goto", "if", "inline", "int",
		"int16_t", "int32_t", "int64_t", "int8_t", "intptr_t", "long", "mutable", "namespace", "new", "noexcept", "not",
		"not_eq", "nullptr", "operator", "or", "or_eq", "private", "protected", "ptrdiff_t", "public", "register",
		"reinterpret_cast", "requires", "restrict", "return", "short", "signed", "size_t", "sizeof", "static",
		"static_assert", "static_cast", "struct", "switch", "template", "this", "thread_local", "throw", "true", "try",
		"typedef", "typeid", "typename", "typeof", "typeof_unqual", "uint16_t", "uint32_t", "uint64_t", "uint8_t",
		"uintptr_t", "union", "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while", "xor", "xor_eq",
		"intmax_t", "uintmax_t", "INTPTR_MIN", "INTPTR_MAX", "UINTPTR_MAX", "INTMAX_MIN", "INTMAX_MAX", "UINTMAX_MAX",
		"PTRDIFF_MIN", "PTRDIFF_MAX", "SIG_ATOMIC_MIN", "SIG_ATOMIC_MAX", "SIZE_MAX", "RSIZE_MAX", "WCHAR_MIN",
		"WCHAR_MAX", "WINT_MIN", "WINT_MAX", "INTPTR_WIDTH", "UINTPTR_WIDTH", "INTMAX_WIDTH", "UINTMAX_WIDTH",
		"PTRDIFF_WIDTH", "SIG_ATOMIC_WIDTH", "SIZE_WIDTH", "WCHAR_WIDTH", "WINT_WIDTH", .. IntegerFamilyNames()
	]);

	/// <summary>Writes the declarations of the copied structures.</summary>
	/// <param name="structures">The structures, requested ones first; children are found among them by name.</param>
	/// <returns>The C text, with <c>\n</c> line ends.</returns>
	internal static string Write(IReadOnlyList<StructureLuaHeaderStructure> structures)
	{
		ArgumentNullException.ThrowIfNull(structures);
		int count = structures.Count;
		string[] types = new string[count];
		StructureLuaHeaderElement[][] sorted = new StructureLuaHeaderElement[count][];
		long[] placedEnds = new long[count];
		Dictionary<string, int> byName = new(StringComparer.Ordinal);
		HashSet<string> usedTypes = new(StringComparer.Ordinal);
		for (int index = 0; index < count; index++)
		{
			StructureLuaHeaderStructure structure = structures[index];
			types[index] = Unique(Identifier(structure.Name, "structure_" + Decimal(index)), usedTypes);
			byName.TryAdd(structure.Name, index);
			sorted[index] = [.. structure.Elements.OrderBy(static element => element.Offset)];
			placedEnds[index] = PlacedEnd(sorted[index]);
		}

		// An embedded child must be complete before its parent, so definitions follow a depth-first order over them.
		HashSet<(int Structure, int Element)> embedded = [];
		int[] states = new int[count];
		List<int> order = new(count);
		for (int index = 0; index < count; index++)
		{
			Visit(index);
		}

		StringBuilder text = new();
		text.Append("// C declarations of ").Append(Decimal(count))
			.Append(count == 1 ? " Cheat Engine structure" : " Cheat Engine structures")
			.Append(", written by CheatEngine.Mcp.\n")
			.Append("// Offsets and sizes are hexadecimal. Packing is 1 byte and gaps are padding arrays, so each\n")
			.Append("// member sits at its Cheat Engine offset. Pointer members take the compiler's pointer size:\n")
			.Append("// compile for the target's bitness.\n")
			.Append("#pragma once\n\n#include <stdint.h>\n\n");
		foreach (string type in types)
		{
			text.Append("typedef struct ").Append(type).Append(' ').Append(type).Append(";\n");
		}

		text.Append("\n#pragma pack(push, 1)\n");
		foreach (int index in order)
		{
			text.Append('\n');
			WriteStructure(text, structures[index], sorted[index], index, types, byName, embedded);
		}

		return text.Append("\n#pragma pack(pop)\n").ToString();

		void Visit(int index)
		{
			if (states[index] != 0)
			{
				return;
			}

			states[index] = Visiting;
			StructureLuaHeaderElement[] elements = sorted[index];
			for (int position = 0; position < elements.Length; position++)
			{
				StructureLuaHeaderElement element = elements[position];
				if (element is { Vartype: StructureValueCodec.VtPointer, Nested: true, Child: { } child } &&
					byName.TryGetValue(child, out int nested) && states[nested] != Visiting)
				{
					Visit(nested);
					// The child is embedded only when its C definition spans exactly the element's bytes: its
					// members must not reach past Cheat Engine's size, which the element's size must match.
					int size = structures[nested].Size;
					if (element.ByteSize > 0 && element.ByteSize == size && placedEnds[nested] <= size)
					{
						embedded.Add((index, position));
					}
				}
			}

			states[index] = Visited;
			order.Add(index);
		}
	}

	/// <summary>Turns a name into a C identifier, or returns the fallback for an empty name.</summary>
	internal static string Identifier(string? name, string fallback)
	{
		if (string.IsNullOrEmpty(name))
		{
			return fallback;
		}

		StringBuilder identifier = new(name.Length + 1);
		foreach (char character in name)
		{
			identifier.Append(char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_');
		}

		if (char.IsAsciiDigit(identifier[0]))
		{
			identifier.Insert(0, '_');
		}

		string result = identifier.ToString();
		return Reserved.Contains(result) ? result + "_" : result;
	}

	/// <summary>
	///     The <c>stdint.h</c> names of each integer width: the exact, least and fast typedefs and their limit and
	///     width macros, such as <c>int_least8_t</c>, <c>INT_FAST16_MIN</c> and <c>UINT32_WIDTH</c>.
	/// </summary>
	private static IEnumerable<string> IntegerFamilyNames()
	{
		int[] widths = [8, 16, 32, 64];
		string[] families = ["", "_least", "_fast"];
		foreach (int bits in widths)
		{
			foreach (string family in families)
			{
				string lower = family + Decimal(bits);
				string upper = lower.ToUpperInvariant();
				yield return "int" + lower + "_t";
				yield return "uint" + lower + "_t";
				yield return "INT" + upper + "_MIN";
				yield return "INT" + upper + "_MAX";
				yield return "UINT" + upper + "_MAX";
				yield return "INT" + upper + "_WIDTH";
				yield return "UINT" + upper + "_WIDTH";
			}
		}
	}

	private static void WriteStructure(StringBuilder text, StructureLuaHeaderStructure structure,
		StructureLuaHeaderElement[] elements, int index, string[] types, Dictionary<string, int> byName,
		HashSet<(int Structure, int Element)> embedded)
	{
		string type = types[index];
		text.Append("// ").Append(Quote(structure.Name)).Append(": 0x").Append(Hex(structure.Size)).Append(" bytes, ")
			.Append(Decimal(elements.Length)).Append(elements.Length == 1 ? " element\n" : " elements\n")
			.Append("struct ").Append(type).Append("\n{\n");
		HashSet<string> members = new(StringComparer.Ordinal);
		long cursor = 0;
		bool declared = false;
		for (int position = 0; position < elements.Length; position++)
		{
			StructureLuaHeaderElement element = elements[position];
			string? childType = element.Child is { } child && byName.TryGetValue(child, out int target)
				? types[target]
				: null;
			bool embeds = embedded.Contains((index, position));
			string kind = Kind(element, childType, embeds);
			if (Refusal(element, cursor) is { } refusal)
			{
				text.Append("\t// ").Append(Offset(element.Offset)).Append(' ').Append(kind).Append(' ')
					.Append(element.Name is { Length: > 0 } unplaced ? Quote(unplaced) : "(unnamed)").Append(": ")
					.Append(refusal).Append(", not declared\n");
				continue;
			}

			if (element.Offset > cursor)
			{
				Pad(text, members, cursor, element.Offset - cursor);
			}

			string member = Unique(Identifier(element.Name, "field_" + Hex(element.Offset)), members);
			text.Append('\t').Append(Declaration(element, member, childType, embeds)).Append("; // ")
				.Append(Offset(element.Offset)).Append(' ').Append(kind);
			if (element.Name is { Length: > 0 } original && !string.Equals(original, member, StringComparison.Ordinal))
			{
				text.Append(' ').Append(Quote(original));
			}

			text.Append('\n');
			cursor = element.Offset + element.ByteSize;
			declared = true;
		}

		if (structure.Size > cursor)
		{
			Pad(text, members, cursor, structure.Size - cursor);
			declared = true;
		}

		if (!declared)
		{
			text.Append("\tuint8_t unused; // C needs a member; the structure has none\n");
		}

		text.Append("};\n");
		if (!declared)
		{
			return;
		}

		if (cursor > structure.Size)
		{
			text.Append("// An element ends at 0x").Append(Hex(cursor)).Append(", past Cheat Engine's size 0x")
				.Append(Hex(structure.Size)).Append(", which ends at the element with the highest offset.\n");
		}

		text.Append("// static_assert(sizeof(").Append(type).Append(") == 0x")
			.Append(Hex(Math.Max(cursor, structure.Size))).Append(", \"").Append(type).Append("\");\n");
	}

	/// <summary>Why C cannot place an element after the members that end at a cursor.</summary>
	/// <param name="element">The element.</param>
	/// <param name="cursor">The end of the members placed before it.</param>
	/// <returns>The reason, or <see langword="null" /> when the element is declared.</returns>
	private static string? Refusal(StructureLuaHeaderElement element, long cursor)
	{
		return element.Offset < 0 ? "before the structure's start"
			: element.ByteSize < 1 ? "without a size"
			: element.Offset < cursor ? "overlaps the member before it"
			: null;
	}

	/// <summary>
	///     The end of the last member C places, by the placement rule of <see cref="WriteStructure" />. Every
	///     declaration spans its element's bytes, so a definition's size is this end or Cheat Engine's size, whichever
	///     is larger.
	/// </summary>
	/// <param name="elements">The elements in offset order.</param>
	/// <returns>The end; 0 when no element is placed.</returns>
	private static long PlacedEnd(StructureLuaHeaderElement[] elements)
	{
		long cursor = 0;
		foreach (StructureLuaHeaderElement element in elements)
		{
			if (Refusal(element, cursor) is null)
			{
				cursor = element.Offset + element.ByteSize;
			}
		}

		return cursor;
	}

	private static void Pad(StringBuilder text, HashSet<string> members, long offset, long size)
	{
		text.Append("\tuint8_t ").Append(Unique("pad_" + Hex(offset), members)).Append(Count(size)).Append("; // ")
			.Append(Offset(offset)).Append(" padding\n");
	}

	private static string Declaration(StructureLuaHeaderElement element, string name, string? childType,
		bool embeds)
	{
		int size = element.ByteSize;
		switch (element.Vartype)
		{
			case >= StructureValueCodec.VtByte and <= StructureValueCodec.VtQword:
				int unit = 1 << element.Vartype;
				bool signed = string.Equals(element.Display, StructureValueCodec.DisplaySigned,
					StringComparison.Ordinal);
				string prefix = signed ? "int" : "uint";
				return Scalar(prefix + Decimal(unit * 8) + "_t", unit, name, size);
			case StructureValueCodec.VtSingle:
				return Scalar("float", 4, name, size);
			case StructureValueCodec.VtDouble:
				return Scalar("double", 8, name, size);
			case StructureValueCodec.VtString:
				return "char " + name + Count(size);
			case StructureValueCodec.VtWideString:
				return size % 2 == 0 ? "uint16_t " + name + Count(size / 2) : Bytes(name, size);
			case StructureValueCodec.VtByteArray:
				return Bytes(name, size);
			case StructureValueCodec.VtPointer:
				if (embeds)
				{
					return childType + " " + name;
				}

				if (element.Nested || size is not (4 or 8))
				{
					return Bytes(name, size);
				}

				// A pointer into the middle of its child would address the child's members from the wrong base.
				return (LandsInside(element, childType) ? "uint8_t" : childType ?? "void") + " *" + name;
			default:
				return size is 1 or 2 or 4 or 8 ? "uint" + Decimal(size * 8) + "_t " + name : Bytes(name, size);
		}
	}

	private static string Kind(StructureLuaHeaderElement element, string? childType, bool embeds)
	{
		switch (element.Vartype)
		{
			case StructureValueCodec.VtPointer when element.Nested:
				string nested = "nested " + (childType ?? Quote(element.Child ?? string.Empty));
				return embeds ? nested : nested + " as bytes";
			case StructureValueCodec.VtPointer when LandsInside(element, childType):
				long landing = element.ChildStart!.Value;
				return "pointer to " + childType + (landing < 0 ? string.Empty : "+") + Offset(landing);
			case StructureValueCodec.VtPointer:
				return childType is null ? "pointer" : "pointer to " + childType;
			case StructureValueCodec.VtBinary when element is { BitStart: { } start, BitSize: { } bits } && bits > 0:
				return "binary bits " + Decimal(start) + "-" + Decimal(start + bits - 1);
			case StructureValueCodec.VtCustom when element.CustomType is { Length: > 0 } custom:
				return "custom " + Quote(custom);
			case StructureValueCodec.VtCustom:
				return "custom";
			case StructureValueCodec.VtBinary:
				return "binary";
			default:
				return StructureValueCodec.ElementType(element.Vartype, element.Display) switch
				{
					StructureElementType.Int8 => "int8",
					StructureElementType.UInt8 => "uint8",
					StructureElementType.Int16 => "int16",
					StructureElementType.UInt16 => "uint16",
					StructureElementType.Int32 => "int32",
					StructureElementType.UInt32 => "uint32",
					StructureElementType.Int64 => "int64",
					StructureElementType.UInt64 => "uint64",
					StructureElementType.Float => "float",
					StructureElementType.Double => "double",
					StructureElementType.String => "string",
					StructureElementType.WString => "wstring",
					StructureElementType.Bytes => "bytes",
					_ => "vartype " + Decimal(element.Vartype)
				};
		}
	}

	/// <summary>Whether a pointer element lands inside its declared child structure rather than at its start.</summary>
	private static bool LandsInside(StructureLuaHeaderElement element, string? childType)
	{
		return childType is not null && element.ChildStart is not (null or 0);
	}

	private static string Scalar(string type, int unit, string name, int size)
	{
		if (size == unit)
		{
			return type + " " + name;
		}

		return size > unit && size % unit == 0 ? type + " " + name + Count(size / unit) : Bytes(name, size);
	}

	private static string Bytes(string name, int size)
	{
		return "uint8_t " + name + Count(size);
	}

	private static string Count(long count)
	{
		return "[0x" + Hex(count) + "]";
	}

	private static string Unique(string identifier, HashSet<string> used)
	{
		if (used.Add(identifier))
		{
			return identifier;
		}

		for (int suffix = 2; ; suffix++)
		{
			string candidate = identifier + "_" + Decimal(suffix);
			if (used.Add(candidate))
			{
				return candidate;
			}
		}
	}

	/// <summary>Quotes a Cheat Engine name for a line comment, replacing what a comment cannot safely carry.</summary>
	private static string Quote(string name)
	{
		StringBuilder quoted = new(name.Length + 2);
		quoted.Append('"');
		foreach (char character in name)
		{
			quoted.Append(character is >= ' ' and <= '~' and not '"' and not '\\' ? character : '?');
		}

		return quoted.Append('"').ToString();
	}

	private static string Offset(long offset)
	{
		return offset < 0 ? "-0x" + Hex(-offset) : "0x" + Hex(offset);
	}

	private static string Hex(long value)
	{
		return value.ToString("X", CultureInfo.InvariantCulture);
	}

	private static string Decimal(long value)
	{
		return value.ToString(CultureInfo.InvariantCulture);
	}
}
