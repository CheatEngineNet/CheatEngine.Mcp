using System.Globalization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Values;

/// <summary>
///     Parses the contract's hexadecimal inputs. Invalid input throws <see cref="CheatEngineToolException" /> with
///     <see cref="ToolErrorKind.InvalidArgument" /> or <see cref="ToolErrorKind.LimitExceeded" /> for the named parameter.
/// </summary>
public static class HexParse
{
	private static readonly char[] ByteSeparators = [' ', '\t', '\r', '\n', ','];

	/// <summary>
	///     Parses bytes such as <c>48 8B 05</c>, <c>488B05</c> or <c>0x48,0x8B</c>: whitespace or commas separate tokens,
	///     a token may start with <c>0x</c>, an even run of digits holds several bytes and a single digit is one byte.
	/// </summary>
	/// <param name="text">The byte text.</param>
	/// <param name="parameter">The parameter name reported on failure.</param>
	/// <param name="maxBytes">The largest accepted byte count.</param>
	/// <returns>The bytes, at least one.</returns>
	public static byte[] Bytes(string? text, string parameter, int maxBytes)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);
		if (string.IsNullOrWhiteSpace(text))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"must contain at least one hexadecimal byte, such as 48 8B 05.");
		}

		List<byte> bytes = [];
		foreach (string token in text.Split(ByteSeparators, StringSplitOptions.RemoveEmptyEntries))
		{
			ReadOnlySpan<char> digits = WithoutHexPrefix(token);
			if (digits.IsEmpty || (digits.Length > 1 && digits.Length % 2 != 0) || !IsHex(digits))
			{
				throw CheatEngineToolException.InvalidArgument(parameter,
					$"'{token}' is not a hexadecimal byte; use pairs such as 48 8B 05.");
			}

			for (int index = 0; index < digits.Length; index += 2)
			{
				if (bytes.Count == maxBytes)
				{
					throw CheatEngineToolException.LimitExceeded(parameter, $"accepts at most {maxBytes} bytes.");
				}

				ReadOnlySpan<char> pair = digits.Slice(index, Math.Min(2, digits.Length - index));
				bytes.Add(byte.Parse(pair, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
			}
		}

		if (bytes.Count == 0)
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"must contain at least one hexadecimal byte, such as 48 8B 05.");
		}

		return [.. bytes];
	}

	/// <summary>Parses a signed hexadecimal offset such as <c>1C</c>, <c>-8</c>, <c>+0x10</c> or <c>-0x10</c>.</summary>
	/// <param name="text">The offset text; Cheat Engine offsets are hexadecimal.</param>
	/// <param name="parameter">The parameter name reported on failure.</param>
	/// <returns>The offset.</returns>
	public static long Offset(string? text, string parameter)
	{
		ReadOnlySpan<char> span = (text ?? string.Empty).AsSpan().Trim();
		bool negative = false;
		if (!span.IsEmpty && span[0] is '+' or '-')
		{
			negative = span[0] == '-';
			span = span[1..];
		}

		span = WithoutHexPrefix(span);
		if (span.IsEmpty || span.Length > 16 || !IsHex(span) ||
		    !ulong.TryParse(span, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong magnitude) ||
		    magnitude > (negative ? 1UL << 63 : long.MaxValue))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"must be a signed hexadecimal offset such as 1C, -8 or 0x10.");
		}

		return negative ? unchecked((long) (0UL - magnitude)) : (long) magnitude;
	}

	/// <summary>Parses signed hexadecimal offsets, kept in dereference order.</summary>
	/// <param name="offsets">The offsets, first dereference first; <see langword="null" /> means none.</param>
	/// <param name="parameter">The parameter name reported on failure; items are reported as <c>parameter[i]</c>.</param>
	/// <param name="maxCount">The largest accepted offset count.</param>
	/// <returns>The offsets in the same order.</returns>
	public static long[] Offsets(IReadOnlyList<string>? offsets, string parameter, int maxCount)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(maxCount);
		if (offsets is null || offsets.Count == 0)
		{
			return [];
		}

		if (offsets.Count > maxCount)
		{
			throw CheatEngineToolException.LimitExceeded(parameter, $"accepts at most {maxCount} offsets.");
		}

		long[] parsed = new long[offsets.Count];
		for (int index = 0; index < parsed.Length; index++)
		{
			parsed[index] = Offset(offsets[index], $"{parameter}[{index.ToString(CultureInfo.InvariantCulture)}]");
		}

		return parsed;
	}

	/// <summary>
	///     Parses a plain hexadecimal address such as <c>7FF6A1B2C3D0</c> or <c>0x7FF6A1B2C3D0</c>. Only tools that never
	///     reach Cheat Engine use it; address inputs of target tools are Cheat Engine expressions.
	/// </summary>
	/// <param name="text">The address text.</param>
	/// <param name="value">The address when this method returns <see langword="true" />.</param>
	/// <returns><see langword="true" /> when <paramref name="text" /> is 1 to 16 hexadecimal digits.</returns>
	public static bool TryAddress(string? text, out ulong value)
	{
		ReadOnlySpan<char> span = WithoutHexPrefix((text ?? string.Empty).AsSpan().Trim());
		value = 0;
		return !span.IsEmpty && span.Length <= 16 && IsHex(span) &&
		       ulong.TryParse(span, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
	}

	internal static ReadOnlySpan<char> WithoutHexPrefix(ReadOnlySpan<char> text)
	{
		return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
	}

	internal static bool IsHex(ReadOnlySpan<char> text)
	{
		foreach (char character in text)
		{
			if (!char.IsAsciiHexDigit(character))
			{
				return false;
			}
		}

		return true;
	}
}
