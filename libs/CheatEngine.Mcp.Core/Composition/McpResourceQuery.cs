using System.Globalization;
using System.Text;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     Parses the variables of a resource URI template and builds the canonical URI of a read.
/// </summary>
/// <remarks>
///     The SDK binds every template variable as a string after percent-decoding it: an absent query variable is
///     <see langword="null" />, while <c>?limit=</c> and an empty path segment are <c>""</c>, which is a value and is
///     refused here. Every refusal is an <c>invalid_argument</c> <see cref="CheatEngineToolException" />, which a resource
///     read reports as JSON-RPC <c>-32602</c>: a number is parsed without overflow, and a bound is checked here rather
///     than by the tool, whose larger-than-maximum refusal would be <c>limit_exceeded</c> (<c>-32603</c>).
/// </remarks>
public static class McpResourceQuery
{
	/// <summary>Parses an optional decimal variable, such as <c>?limit=</c>.</summary>
	/// <param name="value">The bound value; <see langword="null" /> when the variable is absent.</param>
	/// <param name="name">The template variable, reported with the refusal.</param>
	/// <param name="minimum">The smallest accepted value, zero or greater.</param>
	/// <param name="maximum">The largest accepted value.</param>
	/// <returns>The value, or <see langword="null" /> when the variable is absent.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>invalid_argument</c> for anything but decimal digits within the bounds, including <c>""</c>.
	/// </exception>
	public static int? Number(string? value, string name, int minimum, int maximum)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentOutOfRangeException.ThrowIfNegative(minimum);
		ArgumentOutOfRangeException.ThrowIfLessThan(maximum, minimum);
		if (value is null)
		{
			return null;
		}

		// NumberStyles.None: digits only, so no sign, space, separator or exponent, and no OverflowException.
		return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) &&
			   parsed >= minimum && parsed <= maximum
			? parsed
			: throw CheatEngineToolException.InvalidArgument(name,
				string.Create(CultureInfo.InvariantCulture,
					$"must be a decimal integer from {minimum} to {maximum}."),
				$"Omit {name} to use the default.");
	}

	/// <summary>Parses a required decimal variable, such as a record id in the path.</summary>
	/// <param name="value">The bound value.</param>
	/// <param name="name">The template variable, reported with the refusal.</param>
	/// <param name="minimum">The smallest accepted value, zero or greater.</param>
	/// <param name="maximum">The largest accepted value.</param>
	/// <returns>The value.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>invalid_argument</c> when the variable is missing, empty or not a decimal integer within the bounds.
	/// </exception>
	public static int RequiredNumber(string? value, string name, int minimum, int maximum)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentOutOfRangeException.ThrowIfNegative(minimum);
		ArgumentOutOfRangeException.ThrowIfLessThan(maximum, minimum);
		return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) &&
			   parsed >= minimum && parsed <= maximum
			? parsed
			: throw CheatEngineToolException.InvalidArgument(name, string.Create(CultureInfo.InvariantCulture,
				$"must be a decimal integer from {minimum} to {maximum}."));
	}

	/// <summary>Checks a required path variable, such as a module name.</summary>
	/// <param name="value">The bound, percent-decoded value.</param>
	/// <param name="name">The template variable, reported with the refusal.</param>
	/// <returns>The value, unchanged; the tool applies its own checks.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_argument</c> when it is missing, empty or blank.</exception>
	public static string Required(string? value, string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		return string.IsNullOrWhiteSpace(value)
			? throw CheatEngineToolException.InvalidArgument(name, "is required in the resource URI.",
				"Percent-encode the value into its path segment, such as %2F for a slash.")
			: value;
	}

	/// <summary>Percent-encodes one path value, so the canonical URI of a read matches its template again.</summary>
	/// <param name="value">The decoded value.</param>
	/// <returns>The encoded segment.</returns>
	public static string Segment(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		return Uri.EscapeDataString(value);
	}

	/// <summary>
	///     Appends the supplied query values in the template's order, which the SDK matcher requires; an absent value is
	///     left out, so a bare URI stays bare.
	/// </summary>
	/// <param name="uri">The URI without a query, with its path values already encoded.</param>
	/// <param name="query">The template's query variables in order, with the parsed values.</param>
	/// <returns>The canonical URI.</returns>
	public static string WithQuery(string uri, params ReadOnlySpan<(string Name, int? Value)> query)
	{
		ArgumentNullException.ThrowIfNull(uri);
		StringBuilder builder = new(uri);
		char separator = '?';
		foreach ((string name, int? value) in query)
		{
			if (value is { } supplied)
			{
				builder.Append(separator).Append(name).Append('=')
					.Append(supplied.ToString(CultureInfo.InvariantCulture));
				separator = '&';
			}
		}

		return builder.ToString();
	}
}
