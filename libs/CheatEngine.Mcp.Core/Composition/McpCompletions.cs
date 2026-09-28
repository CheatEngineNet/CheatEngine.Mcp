using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     The one shape of every <c>completion/complete</c> answer a CheatEngine MCP host computes itself, and the bound
///     every answer keeps, whoever computed it.
/// </summary>
public static class McpCompletions
{
	/// <summary>The most values one completion returns, the bound the MCP specification sets.</summary>
	public const int MaximumValues = 100;

	/// <summary>
	///     Offers the candidates that start with the typed prefix, ignoring case, in the candidates' order without
	///     empty values or duplicates: at most <see cref="MaximumValues" />, with the number of matches as <c>total</c>
	///     and <c>hasMore</c> when some were left out.
	/// </summary>
	/// <param name="candidates">Every candidate value.</param>
	/// <param name="prefix">The typed argument value; <see langword="null" /> or empty matches everything.</param>
	/// <returns>A new completion whose values list the SDK may still append to.</returns>
	public static Completion Match(IEnumerable<string?> candidates, string? prefix)
	{
		ArgumentNullException.ThrowIfNull(candidates);
		string typed = prefix ?? string.Empty;
		List<string> matches =
		[
			.. candidates.OfType<string>()
				.Where(candidate =>
					candidate.Length > 0 && candidate.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
				.Distinct(StringComparer.Ordinal)
		];
		return new Completion
		{
			Values = [.. matches.Take(MaximumValues)],
			Total = matches.Count,
			HasMore = matches.Count > MaximumValues
		};
	}

	/// <summary>
	///     Bounds a finished completion, whoever computed it, to its first <see cref="MaximumValues" /> values: a
	///     longer one keeps its first values in order, reports how many values there were as <c>total</c> (or the
	///     larger total it already reported) and sets <c>hasMore</c>. A completion within the bound is left as it is.
	/// </summary>
	/// <remarks>
	///     Every host installs it as a <c>completion/complete</c> filter, because the SDK appends a parameter's
	///     <c>[AllowedValues]</c> that match the typed prefix without any bound and then sets <c>total</c> to the
	///     number of values it returns.
	/// </remarks>
	/// <param name="completion">The completion to bound in place.</param>
	internal static void Bound(Completion completion)
	{
		ArgumentNullException.ThrowIfNull(completion);
		if (completion.Values is not { Count: > MaximumValues } values)
		{
			return;
		}

		int total = Math.Max(values.Count, completion.Total ?? 0);
		completion.Values = [.. values.Take(MaximumValues)];
		completion.Total = total;
		completion.HasMore = true;
	}
}
