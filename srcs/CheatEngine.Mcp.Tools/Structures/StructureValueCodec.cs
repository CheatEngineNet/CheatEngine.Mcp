using System.Buffers.Binary;
using System.Text;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>
///     Maps structure elements between Cheat Engine's variable types (<c>defines.lua</c>) and the contract, and decodes
///     and encodes their bytes in managed code, so reads need no per-element Lua round trip.
/// </summary>
/// <remarks>
///     Cheat Engine keeps an integer's signedness in the element's display method: <c>dtSignedInteger</c> reads as
///     <c>intN</c>, <c>dtUnsignedInteger</c> and <c>dtHexadecimal</c> as <c>uintN</c>. Strings and byte arrays take
///     their length from the element's byte size. Bit fields, custom types and any other variable type are formatted by
///     Cheat Engine itself.
/// </remarks>
internal static class StructureValueCodec
{
	internal const int VtByte = 0;
	internal const int VtWord = 1;
	internal const int VtDword = 2;
	internal const int VtQword = 3;
	internal const int VtSingle = 4;
	internal const int VtDouble = 5;
	internal const int VtString = 6;
	internal const int VtWideString = 7;
	internal const int VtByteArray = 8;
	internal const int VtBinary = 9;
	internal const int VtPointer = 12;
	internal const int VtCustom = 13;

	internal const string DisplayUnsigned = "dtUnsignedInteger";
	internal const string DisplaySigned = "dtSignedInteger";
	internal const string DisplayHex = "dtHexadecimal";

	/// <summary>The largest byte size of a string, wstring or bytes element.</summary>
	internal const int MaxByteSize = McpValueCodec.MaxLength;

	/// <summary>Returns Cheat Engine's variable type for a contract value type.</summary>
	internal static int Vartype(McpValueType type)
	{
		return type switch
		{
			McpValueType.Int8 or McpValueType.UInt8 => VtByte,
			McpValueType.Int16 or McpValueType.UInt16 => VtWord,
			McpValueType.Int32 or McpValueType.UInt32 => VtDword,
			McpValueType.Int64 or McpValueType.UInt64 => VtQword,
			McpValueType.Float => VtSingle,
			McpValueType.Double => VtDouble,
			McpValueType.Pointer => VtPointer,
			McpValueType.String => VtString,
			McpValueType.WString => VtWideString,
			McpValueType.Bytes => VtByteArray,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown value type.")
		};
	}

	/// <summary>Whether a value type is an integer, whose signedness Cheat Engine stores as its display.</summary>
	internal static bool IsInteger(McpValueType type)
	{
		return type is McpValueType.Int8 or McpValueType.UInt8 or McpValueType.Int16 or McpValueType.UInt16
			or McpValueType.Int32 or McpValueType.UInt32 or McpValueType.Int64 or McpValueType.UInt64;
	}

	/// <summary>Whether a value type takes its length from the element's byte size.</summary>
	internal static bool IsSized(McpValueType type)
	{
		return type is McpValueType.String or McpValueType.WString or McpValueType.Bytes;
	}

	/// <summary>Returns Cheat Engine's display method name for a display.</summary>
	internal static string DisplayMethod(StructureDisplay display)
	{
		return display switch
		{
			StructureDisplay.Signed => DisplaySigned,
			StructureDisplay.Hex => DisplayHex,
			_ => DisplayUnsigned
		};
	}

	/// <summary>
	///     Returns the display method that stores a value type and an optional display, or <see langword="null" /> for a
	///     type without one.
	/// </summary>
	/// <exception cref="CheatEngineToolException">The display does not fit the value type.</exception>
	internal static string? DisplayMethodFor(McpValueType type, StructureDisplay? display, string parameter)
	{
		if (!IsInteger(type))
		{
			return display is null
				? null
				: throw CheatEngineToolException.InvalidArgument(parameter, "applies only to integer value types.");
		}

		bool signed = type is McpValueType.Int8 or McpValueType.Int16 or McpValueType.Int32 or McpValueType.Int64;
		StructureDisplay chosen = display ?? (signed ? StructureDisplay.Signed : StructureDisplay.Unsigned);
		if (signed != (chosen == StructureDisplay.Signed))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				signed
					? $"must be signed for {Name(type)}; use the unsigned type for {(chosen == StructureDisplay.Hex ? "hex" : "unsigned")}."
					: $"cannot be signed for {Name(type)}; use the signed type instead.");
		}

		return DisplayMethod(chosen);
	}

	/// <summary>Returns the contract type of an element from its variable type and display method.</summary>
	internal static StructureElementType ElementType(int vartype, string? displayMethod)
	{
		bool signed = string.Equals(displayMethod, DisplaySigned, StringComparison.Ordinal);
		return vartype switch
		{
			VtByte => signed ? StructureElementType.Int8 : StructureElementType.UInt8,
			VtWord => signed ? StructureElementType.Int16 : StructureElementType.UInt16,
			VtDword => signed ? StructureElementType.Int32 : StructureElementType.UInt32,
			VtQword => signed ? StructureElementType.Int64 : StructureElementType.UInt64,
			VtSingle => StructureElementType.Float,
			VtDouble => StructureElementType.Double,
			VtString => StructureElementType.String,
			VtWideString => StructureElementType.WString,
			VtByteArray => StructureElementType.Bytes,
			VtBinary => StructureElementType.Binary,
			VtPointer => StructureElementType.Pointer,
			VtCustom => StructureElementType.Custom,
			_ => StructureElementType.Other
		};
	}

	/// <summary>Returns the display of an integer element, or <see langword="null" /> for other types.</summary>
	internal static StructureDisplay? Display(int vartype, string? displayMethod)
	{
		if (vartype is < VtByte or > VtQword)
		{
			return null;
		}

		return displayMethod switch
		{
			DisplaySigned => StructureDisplay.Signed,
			DisplayHex => StructureDisplay.Hex,
			_ => StructureDisplay.Unsigned
		};
	}

	/// <summary>Whether managed code decodes and encodes the type; Cheat Engine formats the others.</summary>
	internal static bool IsManaged(StructureElementType type)
	{
		return type is not (StructureElementType.Binary or StructureElementType.Custom or StructureElementType.Other);
	}

	/// <summary>The bytes a fixed-size type needs, or <see langword="null" /> for pointers, strings and bytes.</summary>
	internal static int? FixedSize(StructureElementType type)
	{
		return type switch
		{
			StructureElementType.Int8 or StructureElementType.UInt8 => 1,
			StructureElementType.Int16 or StructureElementType.UInt16 => 2,
			StructureElementType.Int32 or StructureElementType.UInt32 or StructureElementType.Float => 4,
			StructureElementType.Int64 or StructureElementType.UInt64 or StructureElementType.Double => 8,
			_ => null
		};
	}

	/// <summary>
	///     Decodes an element's bytes as contract text: integers in decimal, floats round-trippable, pointers as
	///     hexadecimal addresses, text up to its first terminator and bytes as <c>48 8B 05</c>.
	/// </summary>
	/// <param name="type">The element's type; it must be managed.</param>
	/// <param name="bytes">The element's bytes, as many as its byte size.</param>
	/// <returns>The value, or <see langword="null" /> when there are fewer bytes than the type needs.</returns>
	internal static string? Decode(StructureElementType type, ReadOnlySpan<byte> bytes)
	{
		if (FixedSize(type) is { } size && bytes.Length < size)
		{
			return null;
		}

		return type switch
		{
			StructureElementType.Int8 => McpValueCodec.Format((sbyte) bytes[0]),
			StructureElementType.UInt8 => McpValueCodec.Format(bytes[0]),
			StructureElementType.Int16 => McpValueCodec.Format(BinaryPrimitives.ReadInt16LittleEndian(bytes)),
			StructureElementType.UInt16 => McpValueCodec.Format(BinaryPrimitives.ReadUInt16LittleEndian(bytes)),
			StructureElementType.Int32 => McpValueCodec.Format(BinaryPrimitives.ReadInt32LittleEndian(bytes)),
			StructureElementType.UInt32 => McpValueCodec.Format(BinaryPrimitives.ReadUInt32LittleEndian(bytes)),
			StructureElementType.Int64 => McpValueCodec.Format(BinaryPrimitives.ReadInt64LittleEndian(bytes)),
			StructureElementType.UInt64 => McpValueCodec.Format(BinaryPrimitives.ReadUInt64LittleEndian(bytes)),
			StructureElementType.Float => McpValueCodec.Format(BinaryPrimitives.ReadSingleLittleEndian(bytes)),
			StructureElementType.Double => McpValueCodec.Format(BinaryPrimitives.ReadDoubleLittleEndian(bytes)),
			StructureElementType.Pointer => bytes.Length switch
			{
				>= 8 => HexFormat.Address(BinaryPrimitives.ReadUInt64LittleEndian(bytes)),
				>= 4 => HexFormat.Address(BinaryPrimitives.ReadUInt32LittleEndian(bytes)),
				_ => null
			},
			StructureElementType.String => Encoding.UTF8.GetString(UntilTerminator(bytes, 1)),
			StructureElementType.WString => Encoding.Unicode.GetString(UntilTerminator(bytes, 2)),
			StructureElementType.Bytes => HexFormat.Bytes(bytes),
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Cheat Engine formats this element type.")
		};
	}

	/// <summary>
	///     Encodes contract text as the bytes an element holds. Text shorter than the element gets a terminator when it
	///     fits; text or bytes longer than the element are refused.
	/// </summary>
	/// <param name="type">The element's type; it must be managed.</param>
	/// <param name="value">The value text, reported as the <c>value</c> parameter.</param>
	/// <param name="byteSize">The element's byte size; for a pointer, the target's pointer size.</param>
	/// <returns>The bytes to write at the element's address.</returns>
	/// <exception cref="CheatEngineToolException">The value does not fit the element.</exception>
	internal static byte[] Encode(StructureElementType type, string value, int byteSize)
	{
		const string parameter = "value";
		switch (type)
		{
			case StructureElementType.String or StructureElementType.WString:
				int unit = type is StructureElementType.String ? 1 : 2;
				byte[] text = McpValueCodec.Encode(
					type is StructureElementType.String ? McpValueType.String : McpValueType.WString, value, 8,
					parameter);
				if (text.Length > byteSize)
				{
					throw CheatEngineToolException.InvalidArgument(parameter,
						$"encodes to {text.Length} bytes, but the element holds {byteSize}.");
				}

				return text.Length + unit <= byteSize ? [.. text, .. new byte[unit]] : text;
			case StructureElementType.Bytes:
				return HexParse.Bytes(value, parameter, Math.Max(1, byteSize));
			case StructureElementType.Pointer:
				return McpValueCodec.Encode(McpValueType.Pointer, value, byteSize >= 8 ? 8 : 4, parameter);
			default:
				return McpValueCodec.Encode(ContractType(type), value, 8, parameter);
		}
	}

	private static McpValueType ContractType(StructureElementType type)
	{
		return type switch
		{
			StructureElementType.Int8 => McpValueType.Int8,
			StructureElementType.UInt8 => McpValueType.UInt8,
			StructureElementType.Int16 => McpValueType.Int16,
			StructureElementType.UInt16 => McpValueType.UInt16,
			StructureElementType.Int32 => McpValueType.Int32,
			StructureElementType.UInt32 => McpValueType.UInt32,
			StructureElementType.Int64 => McpValueType.Int64,
			StructureElementType.UInt64 => McpValueType.UInt64,
			StructureElementType.Float => McpValueType.Float,
			StructureElementType.Double => McpValueType.Double,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Cheat Engine formats this element type.")
		};
	}

	private static ReadOnlySpan<byte> UntilTerminator(ReadOnlySpan<byte> bytes, int unit)
	{
		int usable = bytes.Length - (bytes.Length % unit);
		for (int index = 0; index < usable; index += unit)
		{
			if (unit == 1 ? bytes[index] == 0 : bytes[index] == 0 && bytes[index + 1] == 0)
			{
				return bytes[..index];
			}
		}

		return bytes[..usable];
	}

	private static string Name(McpValueType type)
	{
		return type switch
		{
			McpValueType.Int8 => "int8",
			McpValueType.UInt8 => "uint8",
			McpValueType.Int16 => "int16",
			McpValueType.UInt16 => "uint16",
			McpValueType.Int32 => "int32",
			McpValueType.UInt32 => "uint32",
			McpValueType.Int64 => "int64",
			_ => "uint64"
		};
	}
}
