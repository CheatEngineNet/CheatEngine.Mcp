using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Text;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Util;

/// <summary>Pure managed conversions and bounded 64-bit arithmetic for the <c>util_*</c> contract.</summary>
[McpServerToolType]
public sealed class UtilTools
{
	private const int MaximumExpressionLength = 4096;

	/// <summary>Converts a value through bounded bytes without reading a target or calling Cheat Engine.</summary>
	[McpServerTool(Name = CheatEngineToolNames.UtilConvertValue, Title = "Convert a value", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Convert a bounded value through its byte representation without accessing the target. Numeric and pointer bytes are presented in the requested byte order; UTF-8 and UTF-16 strings keep their encoding order. The source and target scalar byte counts must match.")]
	public static UtilConvertValueResult ConvertValue(
		[Description("The input value, using the same text syntax as memory_read and memory_write.")]
		string value,
		[Description("The type used to parse value.")]
		McpValueType sourceType,
		[Description("The type used to decode the converted bytes.")]
		McpValueType targetType,
		[Description("The byte order to use when presenting scalar bytes.")]
		ValueByteOrder byteOrder = ValueByteOrder.LittleEndian,
		[Description("The pointer width for pointer inputs or outputs: 4 or 8.")]
		int pointerSize = 8)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (pointerSize is not (4 or 8))
		{
			throw CheatEngineToolException.InvalidArgument("pointerSize", "must be 4 or 8.");
		}

		byte[] littleEndian = McpValueCodec.Encode(sourceType, value, pointerSize, "value");
		byte[] presented = IsText(sourceType) ? littleEndian : Order(littleEndian, byteOrder);
		string decoded = Decode(targetType, presented, byteOrder, pointerSize);
		return new UtilConvertValueResult(sourceType, targetType, byteOrder, pointerSize, HexFormat.Bytes(presented),
			decoded);
	}

	/// <summary>Evaluates a bounded unsigned 64-bit expression with wraparound arithmetic.</summary>
	[McpServerTool(Name = CheatEngineToolNames.UtilCalculate, Title = "Calculate a 64-bit expression", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Evaluate an integer expression with unsigned 64-bit wraparound. Literals are decimal or 0x hexadecimal. Supported operators are + - * / % << >> & ^ | ~ and parentheses; division is unsigned and division by zero is refused.")]
	public static UtilCalculateResult Calculate(
		[Description("An unsigned 64-bit expression up to 4096 characters, such as (0x7FF60000 + 0x1C0) - 8.")]
		string expression)
	{
		ulong value = UtilExpression.Evaluate(expression, MaximumExpressionLength);
		return new UtilCalculateResult(HexFormat.UInt64(value), HexFormat.Int64(unchecked((long) value)),
			HexFormat.Address(value));
	}

	private static string Decode(McpValueType type, byte[] presented, ValueByteOrder order, int pointerSize)
	{
		if (type is McpValueType.String or McpValueType.WString)
		{
			return type is McpValueType.String
				? new UTF8Encoding(false, true).GetString(presented)
				: DecodeUtf16(presented);
		}

		if (type is McpValueType.Bytes)
		{
			return HexFormat.Bytes(presented);
		}

		int expected = McpValueCodec.FixedSize(type, pointerSize) ?? throw new InvalidOperationException();
		if (presented.Length != expected)
		{
			throw CheatEngineToolException.InvalidArgument("targetType",
				$"requires {expected} bytes, but sourceType produced {presented.Length} bytes.");
		}

		byte[] bytes = Order(presented, order);
		return type switch
		{
			McpValueType.Int8 => ((sbyte) bytes[0]).ToString(CultureInfo.InvariantCulture),
			McpValueType.UInt8 => bytes[0].ToString(CultureInfo.InvariantCulture),
			McpValueType.Int16 => BinaryPrimitives.ReadInt16LittleEndian(bytes).ToString(CultureInfo.InvariantCulture),
			McpValueType.UInt16 => BinaryPrimitives.ReadUInt16LittleEndian(bytes)
				.ToString(CultureInfo.InvariantCulture),
			McpValueType.Int32 => BinaryPrimitives.ReadInt32LittleEndian(bytes).ToString(CultureInfo.InvariantCulture),
			McpValueType.UInt32 => BinaryPrimitives.ReadUInt32LittleEndian(bytes)
				.ToString(CultureInfo.InvariantCulture),
			McpValueType.Int64 => BinaryPrimitives.ReadInt64LittleEndian(bytes).ToString(CultureInfo.InvariantCulture),
			McpValueType.UInt64 => BinaryPrimitives.ReadUInt64LittleEndian(bytes)
				.ToString(CultureInfo.InvariantCulture),
			McpValueType.Float => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes))
				.ToString("R", CultureInfo.InvariantCulture),
			McpValueType.Double => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes))
				.ToString("R", CultureInfo.InvariantCulture),
			McpValueType.Pointer => HexFormat.Address(pointerSize == 4
				? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
				: BinaryPrimitives.ReadUInt64LittleEndian(bytes)),
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown value type.")
		};
	}

	private static string DecodeUtf16(byte[] bytes)
	{
		if (bytes.Length % 2 != 0)
		{
			throw CheatEngineToolException.InvalidArgument("targetType",
				"wstring needs an even number of bytes.");
		}

		return new UnicodeEncoding(false, false, true).GetString(bytes);
	}

	private static bool IsText(McpValueType type)
	{
		return type is McpValueType.String or McpValueType.WString;
	}

	private static byte[] Order(byte[] bytes, ValueByteOrder order)
	{
		if (order is ValueByteOrder.LittleEndian || bytes.Length < 2)
		{
			return bytes;
		}

		byte[] reversed = [.. bytes];
		Array.Reverse(reversed);
		return reversed;
	}
}

/// <summary>A recursive-descent parser for the deliberately small <c>util_calculate</c> expression language.</summary>
internal ref struct UtilExpression
{
	private readonly ReadOnlySpan<char> _text;
	private readonly int _maximumDepth;
	private int _depth;
	private int _index;

	private UtilExpression(ReadOnlySpan<char> text, int maximumDepth)
	{
		_text = text;
		_maximumDepth = maximumDepth;
	}

	internal static ulong Evaluate(string? expression, int maximumLength)
	{
		if (string.IsNullOrWhiteSpace(expression))
		{
			throw CheatEngineToolException.InvalidArgument("expression", "must contain an integer expression.");
		}

		if (expression.Length > maximumLength)
		{
			throw CheatEngineToolException.LimitExceeded("expression",
				$"must contain at most {maximumLength} characters.");
		}

		UtilExpression parser = new(expression.AsSpan(), 64);
		ulong value = parser.Or();
		parser.SkipSpace();
		if (parser._index != parser._text.Length)
		{
			Invalid("contains an unsupported token.");
		}

		return value;
	}

	private ulong Or()
	{
		ulong value = Xor();
		while (Take('|'))
		{
			value |= Xor();
		}

		return value;
	}

	private ulong Xor()
	{
		ulong value = And();
		while (Take('^'))
		{
			value ^= And();
		}

		return value;
	}

	private ulong And()
	{
		ulong value = Shift();
		while (Take('&'))
		{
			value &= Shift();
		}

		return value;
	}

	private ulong Shift()
	{
		ulong value = Sum();
		while (true)
		{
			if (Take("<<"))
			{
				value = unchecked(value << (int) (Sum() & 63));
			}
			else if (Take(">>"))
			{
				value >>= (int) (Sum() & 63);
			}
			else
			{
				return value;
			}
		}
	}

	private ulong Sum()
	{
		ulong value = Product();
		while (true)
		{
			if (Take('+'))
			{
				value = unchecked(value + Product());
			}
			else if (Take('-'))
			{
				value = unchecked(value - Product());
			}
			else
			{
				return value;
			}
		}
	}

	private ulong Product()
	{
		ulong value = Unary();
		while (true)
		{
			if (Take('*'))
			{
				value = unchecked(value * Unary());
			}
			else if (Take('/'))
			{
				ulong divisor = Unary();
				if (divisor == 0)
				{
					Invalid("cannot divide by zero.");
				}

				value /= divisor;
			}
			else if (Take('%'))
			{
				ulong divisor = Unary();
				if (divisor == 0)
				{
					Invalid("cannot take a remainder by zero.");
				}

				value %= divisor;
			}
			else
			{
				return value;
			}
		}
	}

	private ulong Unary()
	{
		if (Take('+'))
		{
			return Unary();
		}

		if (Take('-'))
		{
			return unchecked(0UL - Unary());
		}

		if (Take('~'))
		{
			return ~Unary();
		}

		return Primary();
	}

	private ulong Primary()
	{
		if (Take('('))
		{
			if (++_depth > _maximumDepth)
			{
				Invalid("has more than 64 nested parentheses.");
			}

			ulong value = Or();
			if (!Take(')'))
			{
				Invalid("has an unclosed parenthesis.");
			}

			_depth--;
			return value;
		}

		SkipSpace();
		int start = _index;
		bool hexadecimal = _index + 2 <= _text.Length && _text[_index] == '0' &&
						   _text[_index + 1] is 'x' or 'X';
		if (hexadecimal)
		{
			_index += 2;
			int digits = _index;
			while (_index < _text.Length && char.IsAsciiHexDigit(_text[_index]))
			{
				_index++;
			}

			if (digits == _index)
			{
				Invalid("has an invalid 64-bit hexadecimal literal.");
			}

			if (!ulong.TryParse(_text[digits.._index], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
					out ulong hex))
			{
				Invalid("has an invalid 64-bit hexadecimal literal.");
			}

			return hex;
		}

		while (_index < _text.Length && char.IsAsciiDigit(_text[_index]))
		{
			_index++;
		}

		if (start == _index)
		{
			Invalid("has an invalid 64-bit decimal literal.");
		}

		if (!ulong.TryParse(_text[start.._index], NumberStyles.None, CultureInfo.InvariantCulture,
				out ulong decimalValue))
		{
			Invalid("has an invalid 64-bit decimal literal.");
		}

		return decimalValue;
	}

	private bool Take(char token)
	{
		SkipSpace();
		if (_index >= _text.Length || _text[_index] != token)
		{
			return false;
		}

		_index++;
		return true;
	}

	private bool Take(string token)
	{
		SkipSpace();
		if (!_text[_index..].StartsWith(token, StringComparison.Ordinal))
		{
			return false;
		}

		_index += token.Length;
		return true;
	}

	private void SkipSpace()
	{
		while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
		{
			_index++;
		}
	}

	private static void Invalid(string detail)
	{
		throw CheatEngineToolException.InvalidArgument("expression", detail);
	}
}
