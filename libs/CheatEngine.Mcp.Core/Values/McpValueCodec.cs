using System.Buffers.Binary;
using System.Globalization;
using System.Text;

using CheatEngine.Client;
using CheatEngine.Client.Memory;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Core.Values;

/// <summary>
///     Reads, writes, formats and encodes memory values as the contract's strings: integers in invariant decimal (input
///     also accepts <c>0x</c> hexadecimal), floats round-trippable with <c>NaN</c>, <c>Infinity</c> and <c>-Infinity</c>,
///     pointers as uppercase hexadecimal addresses and bytes as <c>48 8B 05</c>.
/// </summary>
/// <remarks>
///     Every Client call is a closed generic instantiation of the typed memory API, so the codec needs no reflection.
///     Call <see cref="Read" /> and <see cref="Write" /> inside the tool's Client dispatch.
/// </remarks>
public static class McpValueCodec
{
	/// <summary>The largest byte or string length a single value may have.</summary>
	public const int MaxLength = 65536;

	/// <summary>The string read length used when a tool passes none.</summary>
	public const int DefaultStringLength = 256;

	/// <summary>Returns the size of a fixed-size type.</summary>
	/// <param name="type">The value type.</param>
	/// <param name="pointerSize">The target pointer size in bytes, 4 or 8.</param>
	/// <returns>The size in bytes, or <see langword="null" /> for strings and bytes.</returns>
	public static int? FixedSize(McpValueType type, int pointerSize)
	{
		return type switch
		{
			McpValueType.Int8 or McpValueType.UInt8 => 1,
			McpValueType.Int16 or McpValueType.UInt16 => 2,
			McpValueType.Int32 or McpValueType.UInt32 or McpValueType.Float => 4,
			McpValueType.Int64 or McpValueType.UInt64 or McpValueType.Double => 8,
			McpValueType.Pointer => ValidatePointerSize(pointerSize),
			McpValueType.String or McpValueType.WString or McpValueType.Bytes => null,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown value type.")
		};
	}

	/// <summary>Reads one value through the Client's typed memory API.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="address">The resolved target address.</param>
	/// <param name="type">The value type.</param>
	/// <param name="length">
	///     The byte count for <see cref="McpValueType.Bytes" /> (required) or the maximum string length (default
	///     <see cref="DefaultStringLength" />); ignored by fixed-size types.
	/// </param>
	/// <param name="cancellationToken">Observed before each Client call is dispatched.</param>
	/// <returns>The value as contract text.</returns>
	public static string Read(ICheatEngineClient client, Address address, McpValueType type, int? length,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(client);
		IMemoryClient memory = client.Memory;
		return type switch
		{
			McpValueType.Int8 => Format(memory.ReadPrimitive<sbyte>(address, cancellationToken)),
			McpValueType.UInt8 => Format(memory.ReadPrimitive<byte>(address, cancellationToken)),
			McpValueType.Int16 => Format(memory.ReadPrimitive<short>(address, cancellationToken)),
			McpValueType.UInt16 => Format(memory.ReadPrimitive<ushort>(address, cancellationToken)),
			McpValueType.Int32 => Format(memory.ReadPrimitive<int>(address, cancellationToken)),
			McpValueType.UInt32 => Format(memory.ReadPrimitive<uint>(address, cancellationToken)),
			McpValueType.Int64 => Format(memory.ReadPrimitive<long>(address, cancellationToken)),
			McpValueType.UInt64 => Format(memory.ReadPrimitive<ulong>(address, cancellationToken)),
			McpValueType.Float => Format(memory.ReadPrimitive<float>(address, cancellationToken)),
			McpValueType.Double => Format(memory.ReadPrimitive<double>(address, cancellationToken)),
			McpValueType.Pointer => Format(memory.ReadPrimitive<Address>(address, cancellationToken)),
			McpValueType.String => memory.ReadString(
				new MemoryStringReadRequest(address, Length(length ?? DefaultStringLength),
					MemoryStringEncoding.Utf8), cancellationToken),
			McpValueType.WString => memory.ReadString(
				new MemoryStringReadRequest(address, Length(length ?? DefaultStringLength),
					MemoryStringEncoding.Utf16), cancellationToken),
			McpValueType.Bytes => HexFormat.Bytes(memory.ReadBytes(
				new MemoryBytesReadRequest(address,
					Length(length ?? throw CheatEngineToolException.InvalidArgument("length",
						"is required for the bytes value type."))), cancellationToken).AsSpan()),
			_ => throw UnknownType(type)
		};
	}

	/// <summary>Parses contract text and writes it through the Client's typed memory API.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="address">The resolved target address.</param>
	/// <param name="type">The value type.</param>
	/// <param name="value">The value text; invalid text is reported as the <c>value</c> parameter.</param>
	/// <param name="cancellationToken">Observed before the Client call is dispatched.</param>
	public static void Write(ICheatEngineClient client, Address address, McpValueType type, string value,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(client);
		const string parameter = "value";
		IMemoryClient memory = client.Memory;
		switch (type)
		{
			case McpValueType.Int8:
				memory.WritePrimitive(address, unchecked((sbyte) ParseInteger(value, 8, true, parameter)),
					cancellationToken);
				break;
			case McpValueType.UInt8:
				memory.WritePrimitive(address, (byte) ParseInteger(value, 8, false, parameter), cancellationToken);
				break;
			case McpValueType.Int16:
				memory.WritePrimitive(address, unchecked((short) ParseInteger(value, 16, true, parameter)),
					cancellationToken);
				break;
			case McpValueType.UInt16:
				memory.WritePrimitive(address, (ushort) ParseInteger(value, 16, false, parameter), cancellationToken);
				break;
			case McpValueType.Int32:
				memory.WritePrimitive(address, unchecked((int) ParseInteger(value, 32, true, parameter)),
					cancellationToken);
				break;
			case McpValueType.UInt32:
				memory.WritePrimitive(address, (uint) ParseInteger(value, 32, false, parameter), cancellationToken);
				break;
			case McpValueType.Int64:
				memory.WritePrimitive(address, unchecked((long) ParseInteger(value, 64, true, parameter)),
					cancellationToken);
				break;
			case McpValueType.UInt64:
				memory.WritePrimitive(address, ParseInteger(value, 64, false, parameter), cancellationToken);
				break;
			case McpValueType.Float:
				memory.WritePrimitive(address, ParseSingle(value, parameter), cancellationToken);
				break;
			case McpValueType.Double:
				memory.WritePrimitive(address, ParseDouble(value, parameter), cancellationToken);
				break;
			case McpValueType.Pointer:
				memory.WritePrimitive(address, new Address(ParsePointer(value, 8, parameter)), cancellationToken);
				break;
			case McpValueType.String:
				memory.WriteString(new MemoryStringWriteRequest(address, value,
					Math.Max(1, StringLength(Encoding.UTF8.GetByteCount(value), parameter)),
					MemoryStringEncoding.Utf8), cancellationToken);
				break;
			case McpValueType.WString:
				memory.WriteString(new MemoryStringWriteRequest(address, value,
					Math.Max(1, StringLength(value.Length, parameter)), MemoryStringEncoding.Utf16), cancellationToken);
				break;
			case McpValueType.Bytes:
				memory.WriteBytes(new MemoryBytesWriteRequest(address, HexParse.Bytes(value, parameter, MaxLength)),
					cancellationToken);
				break;
			default:
				throw UnknownType(type);
		}
	}

	/// <summary>Formats a primitive the Client reads as contract text.</summary>
	/// <typeparam name="T">
	///     An 8-, 16-, 32- or 64-bit integer, <see cref="float" />, <see cref="double" /> or
	///     <see cref="Address" />.
	/// </typeparam>
	/// <param name="value">The value.</param>
	/// <returns>Invariant decimal for integers, round-trippable text for floats, hexadecimal for addresses.</returns>
	public static string Format<T>(T value) where T : unmanaged
	{
		return value switch
		{
			sbyte v => v.ToString(CultureInfo.InvariantCulture),
			byte v => v.ToString(CultureInfo.InvariantCulture),
			short v => v.ToString(CultureInfo.InvariantCulture),
			ushort v => v.ToString(CultureInfo.InvariantCulture),
			int v => v.ToString(CultureInfo.InvariantCulture),
			uint v => v.ToString(CultureInfo.InvariantCulture),
			long v => v.ToString(CultureInfo.InvariantCulture),
			ulong v => v.ToString(CultureInfo.InvariantCulture),
			float v => v.ToString("R", CultureInfo.InvariantCulture),
			double v => v.ToString("R", CultureInfo.InvariantCulture),
			Address v => HexFormat.Address(v),
			_ => throw new NotSupportedException($"{typeof(T).Name} is not a contract memory value type.")
		};
	}

	/// <summary>Encodes contract text as the little-endian bytes the target would hold, without touching the target.</summary>
	/// <param name="type">The value type.</param>
	/// <param name="value">The value text.</param>
	/// <param name="pointerSize">The target pointer size in bytes, 4 or 8.</param>
	/// <param name="parameter">The parameter name reported on failure.</param>
	/// <returns>The encoded bytes; strings carry no terminator.</returns>
	public static byte[] Encode(McpValueType type, string value, int pointerSize, string parameter)
	{
		switch (type)
		{
			case McpValueType.Int8 or McpValueType.UInt8:
				return [(byte) ParseInteger(value, 8, type is McpValueType.Int8, parameter)];
			case McpValueType.Int16 or McpValueType.UInt16:
				return Little(ParseInteger(value, 16, type is McpValueType.Int16, parameter), 2);
			case McpValueType.Int32 or McpValueType.UInt32:
				return Little(ParseInteger(value, 32, type is McpValueType.Int32, parameter), 4);
			case McpValueType.Int64 or McpValueType.UInt64:
				return Little(ParseInteger(value, 64, type is McpValueType.Int64, parameter), 8);
			case McpValueType.Float:
				return Little(BitConverter.SingleToUInt32Bits(ParseSingle(value, parameter)), 4);
			case McpValueType.Double:
				return Little(BitConverter.DoubleToUInt64Bits(ParseDouble(value, parameter)), 8);
			case McpValueType.Pointer:
				int size = ValidatePointerSize(pointerSize);
				return Little(ParsePointer(value, size, parameter), size);
			case McpValueType.String:
				ArgumentNullException.ThrowIfNull(value);
				StringLength(Encoding.UTF8.GetByteCount(value), parameter);
				return Encoding.UTF8.GetBytes(value);
			case McpValueType.WString:
				ArgumentNullException.ThrowIfNull(value);
				StringLength(value.Length, parameter);
				return Encoding.Unicode.GetBytes(value);
			case McpValueType.Bytes:
				return HexParse.Bytes(value, parameter, MaxLength);
			default:
				throw UnknownType(type);
		}
	}

	/// <summary>
	///     Parses an integer as its two's-complement bit pattern: invariant decimal with an optional sign, or <c>0x</c>
	///     hexadecimal. Hexadecimal without a sign may use the whole width, so <c>0xFF</c> is -1 as <c>int8</c>.
	/// </summary>
	internal static ulong ParseInteger(string? text, int bits, bool signed, string parameter)
	{
		ReadOnlySpan<char> span = (text ?? string.Empty).AsSpan().Trim();
		bool negative = false;
		if (!span.IsEmpty && span[0] is '+' or '-')
		{
			negative = span[0] == '-';
			span = span[1..];
		}

		bool hex = span.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
		if (hex)
		{
			span = span[2..];
		}

		ulong magnitude = 0;
		bool parsed = !span.IsEmpty && (hex
			? span.Length <= 16 && HexParse.IsHex(span) &&
			  ulong.TryParse(span, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out magnitude)
			: IsDecimal(span) && ulong.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out magnitude));
		string typeName = (signed ? "int" : "uint") + bits.ToString(CultureInfo.InvariantCulture);
		if (!parsed)
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"'{text}' is not an {typeName} value; use decimal such as -12 or hexadecimal such as 0x1F.");
		}

		ulong unsignedMax = bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
		ulong signedMax = unsignedMax >> 1;
		ulong limit;
		if (negative)
		{
			limit = signed ? signedMax + 1 : 0;
		}
		else
		{
			limit = signed && !hex ? signedMax : unsignedMax;
		}

		if (magnitude > limit)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, $"'{text}' is out of range for {typeName}.");
		}

		return negative ? (0UL - magnitude) & unsignedMax : magnitude;
	}

	private static float ParseSingle(string? text, string parameter)
	{
		if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
		    (float.IsInfinity(value) && !NamesInfinity(text)))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"'{text}' is not a float value; use invariant text such as 1.5, -2E-3, NaN, Infinity or -Infinity.");
		}

		return value;
	}

	private static double ParseDouble(string? text, string parameter)
	{
		if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
		    (double.IsInfinity(value) && !NamesInfinity(text)))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"'{text}' is not a double value; use invariant text such as 1.5, -2E-3, NaN, Infinity or -Infinity.");
		}

		return value;
	}

	private static ulong ParsePointer(string? text, int size, string parameter)
	{
		if (!HexParse.TryAddress(text, out ulong value) || (size == 4 && value > uint.MaxValue))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"'{text}' is not a {(size * 8).ToString(CultureInfo.InvariantCulture)}-bit hexadecimal address such as 7FF6A1B2C3D0.");
		}

		return value;
	}

	private static bool NamesInfinity(string? text)
	{
		return text is not null &&
		       text.Trim().TrimStart('+', '-').Equals("Infinity", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsDecimal(ReadOnlySpan<char> text)
	{
		foreach (char character in text)
		{
			if (!char.IsAsciiDigit(character))
			{
				return false;
			}
		}

		return true;
	}

	private static byte[] Little(ulong value, int size)
	{
		byte[] bytes = new byte[8];
		BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
		return bytes[..size];
	}

	private static int Length(int length)
	{
		if (length < 1)
		{
			throw CheatEngineToolException.InvalidArgument("length", "must be at least 1.");
		}

		return length <= MaxLength
			? length
			: throw CheatEngineToolException.LimitExceeded("length", $"must be at most {MaxLength}.");
	}

	private static int StringLength(int length, string parameter)
	{
		return length <= MaxLength
			? length
			: throw CheatEngineToolException.LimitExceeded(parameter,
				$"encodes to more than {MaxLength} bytes or code units.");
	}

	private static int ValidatePointerSize(int pointerSize)
	{
		return pointerSize is 4 or 8
			? pointerSize
			: throw new ArgumentOutOfRangeException(nameof(pointerSize), pointerSize, "A pointer is 4 or 8 bytes.");
	}

	private static ArgumentOutOfRangeException UnknownType(McpValueType type)
	{
		return new ArgumentOutOfRangeException(nameof(type), type, "Unknown value type.");
	}
}
