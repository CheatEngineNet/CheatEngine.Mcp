using System.Text;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Values;

/// <summary>Normalizes array-of-bytes patterns to the canonical text that Cheat Engine's <c>AOBScan</c> accepts.</summary>
/// <remarks>
///     The documented string form of <c>AOBScan</c> (celua.txt) takes hexadecimal bytes and whole-byte wildcards. Nibble
///     wildcards such as <c>4?</c> are not documented there, and the Client's pattern type refuses them, so they are
///     rejected here instead of being passed through.
/// </remarks>
public static class AobPattern
{
	/// <summary>The largest accepted pattern, in byte positions.</summary>
	public const int MaxBytes = 4096;

	/// <summary>
	///     Normalizes a pattern such as <c>48 8b ? 05</c>, <c>488B??05</c> or <c>48 8B * 05</c> to <c>48 8B ?? 05</c>:
	///     uppercase byte pairs and <c>??</c> wildcards separated by single spaces. A standalone <c>?</c>, <c>??</c> or
	///     <c>*</c> is one wildcard byte.
	/// </summary>
	/// <param name="pattern">The pattern text.</param>
	/// <param name="parameter">The parameter name reported on failure.</param>
	/// <returns>The normalized pattern, which holds at least one concrete byte.</returns>
	public static string Normalize(string? pattern, string parameter)
	{
		if (string.IsNullOrWhiteSpace(pattern))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"must be an array-of-bytes pattern such as 48 8B ?? 05.");
		}

		StringBuilder normalized = new(pattern.Length);
		int count = 0;
		bool concrete = false;
		foreach (string token in pattern.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries))
		{
			if (token is "?" or "??" or "*")
			{
				Append(normalized, "??", ref count, parameter);
				continue;
			}

			if (token.Length % 2 != 0)
			{
				throw InvalidToken(parameter, token);
			}

			for (int index = 0; index < token.Length; index += 2)
			{
				char high = token[index];
				char low = token[index + 1];
				if (high == '?' && low == '?')
				{
					Append(normalized, "??", ref count, parameter);
				}
				else if (char.IsAsciiHexDigit(high) && char.IsAsciiHexDigit(low))
				{
					Append(normalized, string.Concat(char.ToUpperInvariant(high), char.ToUpperInvariant(low)),
						ref count,
						parameter);
					concrete = true;
				}
				else if ((high == '?' && char.IsAsciiHexDigit(low)) || (char.IsAsciiHexDigit(high) && low == '?'))
				{
					throw CheatEngineToolException.InvalidArgument(parameter,
						$"'{token}' uses a nibble wildcard, which Cheat Engine patterns do not support; use ?? for a whole byte.");
				}
				else
				{
					throw InvalidToken(parameter, token);
				}
			}
		}

		if (!concrete)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "needs at least one concrete byte.");
		}

		return normalized.ToString();
	}

	private static void Append(StringBuilder normalized, string token, ref int count, string parameter)
	{
		if (count == MaxBytes)
		{
			throw CheatEngineToolException.LimitExceeded(parameter, $"accepts at most {MaxBytes} byte positions.");
		}

		if (count++ > 0)
		{
			normalized.Append(' ');
		}

		normalized.Append(token);
	}

	private static CheatEngineToolException InvalidToken(string parameter, string token)
	{
		return CheatEngineToolException.InvalidArgument(parameter,
			$"'{token}' is not a byte or wildcard; use hexadecimal pairs and ?? such as 48 8B ?? 05.");
	}
}
