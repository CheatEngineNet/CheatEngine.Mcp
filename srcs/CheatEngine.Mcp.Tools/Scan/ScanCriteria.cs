using System.Globalization;

using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Scan;

/// <summary>
///     Checks the caller's value-scan arguments and builds the Client requests. Every refusal is
///     <c>invalid_argument</c> or <c>limit_exceeded</c> with <c>not_started</c> and is raised before any Cheat
///     Engine call, including the refusals the Client's request and value factories would otherwise raise as
///     exceptions: an ordered comparison on text or bytes, empty text or bytes, and a non-finite float.
/// </summary>
internal static class ScanCriteria
{
	/// <summary>The longest accepted <c>value</c> or <c>upperValue</c>, in characters.</summary>
	internal const int MaximumValueCharacters = 1024 * 1024;

	/// <summary>The most decimals a float or double value is written with.</summary>
	internal const int MaximumFloatDecimals = 15;

	/// <summary>The decimals a float value is written with when the caller gives none.</summary>
	internal const int DefaultSingleDecimals = 6;

	/// <summary>The decimals a double value is written with when the caller gives none.</summary>
	internal const int DefaultDoubleDecimals = 12;

	private const string TypeNames = "byte, int16, int32, int64, float, double, string, wstring or bytes";

	/// <summary>Parses a contract value type, before any dispatch.</summary>
	/// <param name="valueType">The caller's value type, case-insensitive.</param>
	/// <returns>The Client value type.</returns>
	/// <exception cref="CheatEngineToolException">
	///     The type is not a scan value type (<c>invalid_argument</c>).
	/// </exception>
	internal static ValueScanValueType ParseValueType(string? valueType)
	{
		return TryParseValueType(valueType, out ValueScanValueType type)
			? type
			: throw CheatEngineToolException.InvalidArgument("valueType", $"must be {TypeNames}.");
	}

	/// <summary>Parses a contract value type or the type Cheat Engine's main scanner reports.</summary>
	/// <param name="valueType">
	///     The type name, case-insensitive; <c>unsupported</c> and <see langword="null" /> fail.
	/// </param>
	/// <param name="type">The Client value type when the method returns <see langword="true" />.</param>
	/// <returns><see langword="true" /> for a scan value type.</returns>
	internal static bool TryParseValueType(string? valueType, out ValueScanValueType type)
	{
		ValueScanValueType? parsed = valueType?.Trim().ToLowerInvariant() switch
		{
			"byte" or "integer8" => ValueScanValueType.Integer8,
			"int16" or "integer16" => ValueScanValueType.Integer16,
			"int32" or "integer32" or "int" => ValueScanValueType.Integer32,
			"int64" or "integer64" or "long" => ValueScanValueType.Integer64,
			"float" or "singlefloat" => ValueScanValueType.SingleFloat,
			"double" or "doublefloat" => ValueScanValueType.DoubleFloat,
			"string" or "utf8string" => ValueScanValueType.Utf8String,
			"wstring" or "utf16string" => ValueScanValueType.Utf16String,
			"bytes" or "bytearray" => ValueScanValueType.ByteArray,
			_ => null
		};
		type = parsed.GetValueOrDefault();
		return parsed is not null;
	}

	/// <summary>Parses a first-scan comparison, before any dispatch.</summary>
	/// <param name="comparison">The caller's comparison; case, spaces and underscores are ignored.</param>
	/// <returns>The Client comparison.</returns>
	/// <exception cref="CheatEngineToolException">The comparison is not a first-scan comparison.</exception>
	internal static ValueScanComparison ParseFirstComparison(string? comparison)
	{
		return Normalize(comparison) switch
		{
			"exact" => ValueScanComparison.Exact,
			"unknown" => ValueScanComparison.UnknownInitialValue,
			"between" => ValueScanComparison.Between,
			"greater" => ValueScanComparison.BiggerThan,
			"less" => ValueScanComparison.SmallerThan,
			_ => throw CheatEngineToolException.InvalidArgument("comparison",
				"must be exact, unknown, between, greater or less.")
		};
	}

	/// <summary>Parses a next-scan comparison, before any dispatch.</summary>
	/// <param name="comparison">The caller's comparison; case, spaces and underscores are ignored.</param>
	/// <returns>The Client comparison.</returns>
	/// <exception cref="CheatEngineToolException">The comparison is not a next-scan comparison.</exception>
	internal static ValueScanComparison ParseNextComparison(string? comparison)
	{
		return Normalize(comparison) switch
		{
			"exact" => ValueScanComparison.Exact,
			"between" => ValueScanComparison.Between,
			"greater" => ValueScanComparison.BiggerThan,
			"less" => ValueScanComparison.SmallerThan,
			"increased" => ValueScanComparison.Increased,
			"increasedby" => ValueScanComparison.IncreasedBy,
			"decreased" => ValueScanComparison.Decreased,
			"decreasedby" => ValueScanComparison.DecreasedBy,
			"changed" => ValueScanComparison.Changed,
			"unchanged" => ValueScanComparison.Unchanged,
			_ => throw CheatEngineToolException.InvalidArgument("comparison",
				"must be exact, between, greater, less, increased, decreased, increasedBy, decreasedBy, changed or unchanged.")
		};
	}

	/// <summary>
	///     Checks the arguments that do not depend on the value type, before any dispatch: value lengths, which values
	///     the comparison needs or refuses, and the range of <c>floatDecimals</c>.
	/// </summary>
	/// <param name="comparison">The parsed comparison.</param>
	/// <param name="value">The caller's value.</param>
	/// <param name="upperValue">The caller's inclusive upper value.</param>
	/// <param name="floatDecimals">The caller's float decimals.</param>
	/// <exception cref="CheatEngineToolException">An argument is refused.</exception>
	internal static void CheckArguments(ValueScanComparison comparison, string? value, string? upperValue,
		int? floatDecimals)
	{
		CheckLength(value, "value");
		CheckLength(upperValue, "upperValue");
		if (!TakesValue(comparison))
		{
			if (value is not null || upperValue is not null)
			{
				throw CheatEngineToolException.InvalidArgument("comparison",
					$"comparison '{Name(comparison)}' does not accept value or upperValue.");
			}
		}
		else if (value is null)
		{
			throw CheatEngineToolException.InvalidArgument("value", "is required for this comparison.");
		}
		else if (comparison == ValueScanComparison.Between && upperValue is null)
		{
			throw CheatEngineToolException.InvalidArgument("upperValue", "is required for between.");
		}
		else if (comparison != ValueScanComparison.Between && upperValue is not null)
		{
			throw CheatEngineToolException.InvalidArgument("upperValue", "is accepted only by between.");
		}

		if (floatDecimals is < 0 or > MaximumFloatDecimals)
		{
			throw CheatEngineToolException.InvalidArgument("floatDecimals", "must be between 0 and 15.");
		}
	}

	/// <summary>Checks every argument against the value type and builds a first-scan request.</summary>
	/// <param name="type">The scanned value type.</param>
	/// <param name="comparison">The parsed first-scan comparison.</param>
	/// <param name="value">The caller's value.</param>
	/// <param name="upperValue">The caller's inclusive upper value.</param>
	/// <param name="floatDecimals">The caller's float decimals.</param>
	/// <returns>The request, which scans the whole address space until it is narrowed.</returns>
	/// <exception cref="CheatEngineToolException">An argument is refused.</exception>
	internal static ValueScanFirstRequest First(ValueScanValueType type, ValueScanComparison comparison,
		string? value, string? upperValue, int? floatDecimals)
	{
		CheckArguments(comparison, value, upperValue, floatDecimals);
		CheckType(type, comparison, floatDecimals);
		int decimals = Decimals(type, floatDecimals);
		return comparison switch
		{
			ValueScanComparison.Exact => ValueScanFirstRequest.Exact(Parse(type, value!, "value", decimals)),
			ValueScanComparison.Between => ValueScanFirstRequest.Between(Parse(type, value!, "value", decimals),
				Parse(type, upperValue!, "upperValue", decimals)),
			ValueScanComparison.BiggerThan =>
				ValueScanFirstRequest.BiggerThan(Parse(type, value!, "value", decimals)),
			ValueScanComparison.SmallerThan =>
				ValueScanFirstRequest.SmallerThan(Parse(type, value!, "value", decimals)),
			ValueScanComparison.UnknownInitialValue => ValueScanFirstRequest.UnknownInitialValue(type),
			_ => throw new ArgumentOutOfRangeException(nameof(comparison))
		};
	}

	/// <summary>Checks every argument against the scan's value type and builds a next-scan request.</summary>
	/// <param name="type">The value type of the scan being narrowed.</param>
	/// <param name="comparison">The parsed next-scan comparison.</param>
	/// <param name="value">The caller's value.</param>
	/// <param name="upperValue">The caller's inclusive upper value.</param>
	/// <param name="floatDecimals">The caller's float decimals.</param>
	/// <returns>The request.</returns>
	/// <exception cref="CheatEngineToolException">An argument is refused.</exception>
	internal static ValueScanNextRequest Next(ValueScanValueType type, ValueScanComparison comparison,
		string? value, string? upperValue, int? floatDecimals)
	{
		CheckArguments(comparison, value, upperValue, floatDecimals);
		CheckType(type, comparison, floatDecimals);
		int decimals = Decimals(type, floatDecimals);
		return comparison switch
		{
			ValueScanComparison.Exact => ValueScanNextRequest.Exact(Parse(type, value!, "value", decimals)),
			ValueScanComparison.Between => ValueScanNextRequest.Between(Parse(type, value!, "value", decimals),
				Parse(type, upperValue!, "upperValue", decimals)),
			ValueScanComparison.BiggerThan =>
				ValueScanNextRequest.BiggerThan(Parse(type, value!, "value", decimals)),
			ValueScanComparison.SmallerThan =>
				ValueScanNextRequest.SmallerThan(Parse(type, value!, "value", decimals)),
			ValueScanComparison.Increased => ValueScanNextRequest.Increased(),
			ValueScanComparison.IncreasedBy =>
				ValueScanNextRequest.IncreasedBy(Parse(type, value!, "value", decimals)),
			ValueScanComparison.Decreased => ValueScanNextRequest.Decreased(),
			ValueScanComparison.DecreasedBy =>
				ValueScanNextRequest.DecreasedBy(Parse(type, value!, "value", decimals)),
			ValueScanComparison.Changed => ValueScanNextRequest.Changed(),
			ValueScanComparison.Unchanged => ValueScanNextRequest.Unchanged(),
			_ => throw new ArgumentOutOfRangeException(nameof(comparison))
		};
	}

	/// <summary>The contract name of a value type, as outputs report it.</summary>
	/// <param name="type">The Client value type.</param>
	/// <returns>byte, int16, int32, int64, float, double, string, wstring or bytes.</returns>
	internal static string Name(ValueScanValueType type)
	{
		return type switch
		{
			ValueScanValueType.Integer8 => "byte",
			ValueScanValueType.Integer16 => "int16",
			ValueScanValueType.Integer32 => "int32",
			ValueScanValueType.Integer64 => "int64",
			ValueScanValueType.SingleFloat => "float",
			ValueScanValueType.DoubleFloat => "double",
			ValueScanValueType.Utf8String => "string",
			ValueScanValueType.Utf16String => "wstring",
			ValueScanValueType.ByteArray => "bytes",
			_ => throw new ArgumentOutOfRangeException(nameof(type))
		};
	}

	/// <summary>The contract name of a comparison, as the tool descriptions spell it.</summary>
	/// <param name="comparison">The Client comparison.</param>
	/// <returns>The contract name, such as <c>increasedBy</c>.</returns>
	internal static string Name(ValueScanComparison comparison)
	{
		return comparison switch
		{
			ValueScanComparison.Exact => "exact",
			ValueScanComparison.Between => "between",
			ValueScanComparison.BiggerThan => "greater",
			ValueScanComparison.SmallerThan => "less",
			ValueScanComparison.UnknownInitialValue => "unknown",
			ValueScanComparison.Increased => "increased",
			ValueScanComparison.IncreasedBy => "increasedBy",
			ValueScanComparison.Decreased => "decreased",
			ValueScanComparison.DecreasedBy => "decreasedBy",
			ValueScanComparison.Changed => "changed",
			ValueScanComparison.Unchanged => "unchanged",
			_ => throw new ArgumentOutOfRangeException(nameof(comparison))
		};
	}

	private static string Normalize(string? comparison)
	{
		return comparison?.Trim().ToLowerInvariant().Replace("_", string.Empty, StringComparison.Ordinal)
				.Replace(" ", string.Empty, StringComparison.Ordinal) switch
		{
			null => string.Empty,
			"unknowninitial" or "unknowninitialvalue" => "unknown",
			"greaterthan" or "bigger" or "biggerthan" => "greater",
			"lessthan" or "smaller" or "smallerthan" => "less",
			string normalized => normalized
		};
	}

	private static bool TakesValue(ValueScanComparison comparison)
	{
		return comparison is ValueScanComparison.Exact or ValueScanComparison.Between or
			ValueScanComparison.BiggerThan or ValueScanComparison.SmallerThan or ValueScanComparison.IncreasedBy or
			ValueScanComparison.DecreasedBy;
	}

	private static bool IsNumeric(ValueScanValueType type)
	{
		return type is ValueScanValueType.Integer8 or ValueScanValueType.Integer16 or ValueScanValueType.Integer32 or
			ValueScanValueType.Integer64 or ValueScanValueType.SingleFloat or ValueScanValueType.DoubleFloat;
	}

	private static void CheckLength(string? value, string parameter)
	{
		if (value is { Length: > MaximumValueCharacters })
		{
			throw CheatEngineToolException.LimitExceeded(parameter, "must not exceed 1048576 characters.");
		}
	}

	private static void CheckType(ValueScanValueType type, ValueScanComparison comparison, int? floatDecimals)
	{
		// Cheat Engine offers only a search for text and byte arrays; the Client factories refuse the others.
		if (comparison != ValueScanComparison.Exact && !IsNumeric(type))
		{
			throw CheatEngineToolException.InvalidArgument("comparison",
				$"comparison '{Name(comparison)}' needs a numeric value type; {Name(type)} scans support exact only.");
		}

		if (floatDecimals is not null && type is not (ValueScanValueType.SingleFloat or ValueScanValueType.DoubleFloat))
		{
			throw CheatEngineToolException.InvalidArgument("floatDecimals",
				$"applies only to float and double scans, not {Name(type)}.");
		}
	}

	private static int Decimals(ValueScanValueType type, int? floatDecimals)
	{
		return floatDecimals ??
			   (type == ValueScanValueType.SingleFloat ? DefaultSingleDecimals : DefaultDoubleDecimals);
	}

	private static ValueScanValue Parse(ValueScanValueType type, string value, string parameter, int decimals)
	{
		try
		{
			return type switch
			{
				ValueScanValueType.Integer8 =>
					ValueScanValue.FromByte(byte.Parse(value, CultureInfo.InvariantCulture)),
				ValueScanValueType.Integer16 =>
					ValueScanValue.FromInt16(short.Parse(value, CultureInfo.InvariantCulture)),
				ValueScanValueType.Integer32 =>
					ValueScanValue.FromInt32(int.Parse(value, CultureInfo.InvariantCulture)),
				ValueScanValueType.Integer64 =>
					ValueScanValue.FromInt64(long.Parse(value, CultureInfo.InvariantCulture)),
				ValueScanValueType.SingleFloat => ValueScanValue.FromSingle(
					Finite(float.Parse(value, CultureInfo.InvariantCulture), parameter), decimals),
				ValueScanValueType.DoubleFloat => ValueScanValue.FromDouble(
					Finite(double.Parse(value, CultureInfo.InvariantCulture), parameter), decimals),
				ValueScanValueType.Utf8String => ValueScanValue.FromUtf8String(NonEmpty(value, parameter)),
				ValueScanValueType.Utf16String => ValueScanValue.FromUtf16String(NonEmpty(value, parameter)),
				ValueScanValueType.ByteArray => ValueScanValue.FromBytes(
					HexParse.Bytes(value, parameter, MaximumValueCharacters)),
				_ => throw new ArgumentOutOfRangeException(nameof(type))
			};
		}
		catch (FormatException)
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				type is ValueScanValueType.SingleFloat or ValueScanValueType.DoubleFloat
					? $"is not a valid {Name(type)} value; use a decimal point, such as 12.5."
					: $"is not a valid {Name(type)} value; integers are decimal without 0x, such as -25.");
		}
		catch (OverflowException)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, $"is outside the range of {Name(type)}.",
				type == ValueScanValueType.Integer8
					? null
					: "Pass an unsigned value above the signed maximum as its signed equivalent, such as -1 for 4294967295 in int32.");
		}
	}

	private static float Finite(float value, string parameter)
	{
		return float.IsFinite(value)
			? value
			: throw CheatEngineToolException.InvalidArgument(parameter, "must be a finite float value.");
	}

	private static double Finite(double value, string parameter)
	{
		return double.IsFinite(value)
			? value
			: throw CheatEngineToolException.InvalidArgument(parameter, "must be a finite double value.");
	}

	private static string NonEmpty(string value, string parameter)
	{
		return value.Length > 0
			? value
			: throw CheatEngineToolException.InvalidArgument(parameter, "must not be empty text.");
	}
}
