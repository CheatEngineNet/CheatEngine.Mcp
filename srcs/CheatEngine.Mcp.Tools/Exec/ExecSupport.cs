using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tools.Exec;

/// <summary>Argument checks and Client-first address resolution shared by the execution tools.</summary>
internal static class ExecSupport
{
	internal const int MaximumArguments = 16;
	internal const int MaximumArgumentText = 4096;
	internal const int MaximumExpression = 1024;

	internal static string Expression(string? value, string parameter)
	{
		if (value is null || value.Any(char.IsControl))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"must be an address expression of 1 to {MaximumExpression} characters without control characters.");
		}

		string text = value.Trim();
		if (text.Length is 0 or > MaximumExpression)
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"must be an address expression of 1 to {MaximumExpression} characters without control characters.");
		}

		return text;
	}

	internal static Address Resolve(ICheatEngineClient client, string expression, string parameter,
		CancellationToken cancellationToken)
	{
		if (client.Inspection.TryResolveAddress(new SymbolExpression(expression), AddressResolutionMode.Default,
				out Address address, out CheatEngineFailure failure, cancellationToken))
		{
			return address;
		}

		if (failure.Kind is CheatEngineFailureKind.NotFound)
		{
			throw CheatEngineToolException.NotFound($"{parameter} does not resolve to an address.",
				"Check the expression with symbol_resolve.");
		}

		throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
	}

	internal static (int[] Types, object?[] Values) Arguments(ExecCallArgument[]? arguments)
	{
		if (arguments is null)
		{
			throw CheatEngineToolException.InvalidArgument("arguments",
				"is required; pass an empty array for no arguments.");
		}

		if (arguments.Length > MaximumArguments)
		{
			throw CheatEngineToolException.LimitExceeded("arguments", $"accepts at most {MaximumArguments} arguments.");
		}

		int[] types = new int[arguments.Length];
		object?[] values = new object?[arguments.Length];
		for (int index = 0; index < arguments.Length; index++)
		{
			ExecCallArgument argument = arguments[index] ?? throw CheatEngineToolException.InvalidArgument(
				$"arguments[{index.ToString(CultureInfo.InvariantCulture)}]", "must be an object.");
			string parameter = $"arguments[{index.ToString(CultureInfo.InvariantCulture)}].value";
			if (!Enum.IsDefined(argument.Type))
			{
				throw CheatEngineToolException.InvalidArgument(
					$"arguments[{index.ToString(CultureInfo.InvariantCulture)}].type",
					"must be integer, float or double. Allocate and write strings or buffers separately, then pass their address as integer.");
			}

			if (argument.Value is null || argument.Value.Length > MaximumArgumentText)
			{
				throw CheatEngineToolException.InvalidArgument(parameter,
					$"must contain at most {MaximumArgumentText} characters.");
			}

			types[index] = (int) argument.Type;
			values[index] = argument.Type switch
			{
				ExecArgumentType.Integral => Integer(argument.Value, parameter),
				ExecArgumentType.SinglePrecision => Float(argument.Value, parameter),
				ExecArgumentType.DoublePrecision => Double(argument.Value, parameter),
				_ => throw new ArgumentOutOfRangeException(nameof(arguments), argument.Type, "Unknown argument type.")
			};
		}

		return (types, values);
	}

	internal static void Timeout(int timeoutMilliseconds)
	{
		if (timeoutMilliseconds is < 1 or > 10_000)
		{
			throw CheatEngineToolException.InvalidArgument("timeoutMilliseconds", "must be between 1 and 10000.");
		}
	}

	private static object Integer(string value, string parameter)
	{
		if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long signed))
		{
			return signed;
		}

		if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong unsigned))
		{
			return unsigned;
		}

		ReadOnlySpan<char> text = value.AsSpan().Trim();
		if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			text = text[2..];
		}

		if (!text.IsEmpty && text.Length <= 16 && IsHex(text) &&
			ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out unsigned))
		{
			return unsigned;
		}

		throw CheatEngineToolException.InvalidArgument(parameter,
			"must be an invariant signed or unsigned integer, or a hexadecimal pointer such as 7FF6A1B2C3D0.");
	}

	private static float Float(string value, string parameter)
	{
		if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) &&
			float.IsFinite(parsed))
		{
			return parsed;
		}

		throw CheatEngineToolException.InvalidArgument(parameter, "must be a finite invariant floating-point number.");
	}

	private static double Double(string value, string parameter)
	{
		if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
			double.IsFinite(parsed))
		{
			return parsed;
		}

		throw CheatEngineToolException.InvalidArgument(parameter, "must be a finite invariant floating-point number.");
	}

	private static bool IsHex(ReadOnlySpan<char> value)
	{
		foreach (char character in value)
		{
			if (!char.IsAsciiHexDigit(character))
			{
				return false;
			}
		}

		return true;
	}
}
