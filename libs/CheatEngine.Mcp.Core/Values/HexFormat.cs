using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Core.Values;

/// <summary>Formats the contract's textual numbers: addresses, byte strings, offsets and 64-bit integers.</summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
	Justification = "Int64 and UInt64 name the integer width they format, like the Convert class does.")]
public static class HexFormat
{
	/// <summary>Formats an address as uppercase hexadecimal without <c>0x</c> or padding, such as <c>7FF6A1B2C3D0</c>.</summary>
	/// <param name="value">The address.</param>
	/// <returns>The formatted address, which Cheat Engine resolves back as an address expression.</returns>
	public static string Address(ulong value)
	{
		return value.ToString("X", CultureInfo.InvariantCulture);
	}

	/// <summary>Formats an address as uppercase hexadecimal without <c>0x</c> or padding.</summary>
	/// <param name="value">The address.</param>
	/// <returns>The formatted address.</returns>
	public static string Address(Address value)
	{
		return Address(value.ToUInt64());
	}

	/// <summary>Formats bytes as spaced uppercase hexadecimal pairs, such as <c>48 8B 05</c>.</summary>
	/// <param name="bytes">The bytes.</param>
	/// <returns>The formatted bytes; empty for no bytes.</returns>
	public static string Bytes(ReadOnlySpan<byte> bytes)
	{
		if (bytes.IsEmpty)
		{
			return string.Empty;
		}

		const string digits = "0123456789ABCDEF";
		int length = (bytes.Length * 3) - 1;
		Span<char> text = length <= 768 ? stackalloc char[length] : new char[length];
		for (int index = 0; index < bytes.Length; index++)
		{
			int position = index * 3;
			text[position] = digits[bytes[index] >> 4];
			text[position + 1] = digits[bytes[index] & 0xF];
			if (position + 2 < text.Length)
			{
				text[position + 2] = ' ';
			}
		}

		return new string(text);
	}

	/// <summary>Formats a signed offset as hexadecimal with a leading <c>-</c> when negative, such as <c>-1C</c>.</summary>
	/// <param name="value">The offset.</param>
	/// <returns>The formatted offset, which <see cref="HexParse.Offset" /> reads back.</returns>
	public static string Offset(long value)
	{
		ulong magnitude = value < 0 ? unchecked((ulong) -(value + 1)) + 1 : (ulong) value;
		string digits = magnitude.ToString("X", CultureInfo.InvariantCulture);
		return value < 0 ? "-" + digits : digits;
	}

	/// <summary>Formats a signed 64-bit integer in invariant decimal.</summary>
	/// <param name="value">The value.</param>
	/// <returns>The decimal text.</returns>
	public static string Int64(long value)
	{
		return value.ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>Formats an unsigned 64-bit integer in invariant decimal.</summary>
	/// <param name="value">The value.</param>
	/// <returns>The decimal text.</returns>
	public static string UInt64(ulong value)
	{
		return value.ToString(CultureInfo.InvariantCulture);
	}
}
