using System.Globalization;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>
///     The ids of MCP-owned resources and jobs: <c>kind-namespace-number</c>, where the kind is a lowercase identifier of
///     at
///     most 32 characters, the namespace names the plugin activation and the number counts from 1 in that activation.
/// </summary>
internal static class McpStateIds
{
	internal const int MaximumKindLength = 32;
	internal const int MaximumNamespaceLength = 32;
	internal const int MaximumLength = 80;

	/// <summary>Whether <paramref name="kind" /> is a valid kind.</summary>
	/// <param name="kind">The candidate kind.</param>
	/// <returns><see langword="true" /> for a lowercase identifier of at most 32 characters.</returns>
	internal static bool IsKind(ReadOnlySpan<char> kind)
	{
		if (kind.Length is < 1 or > MaximumKindLength || !char.IsAsciiLetterLower(kind[0]))
		{
			return false;
		}

		foreach (char character in kind)
		{
			if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>Whether <paramref name="value" /> is a valid activation namespace.</summary>
	/// <param name="value">The candidate namespace.</param>
	/// <returns><see langword="true" /> for 1 to 32 lowercase letters or digits.</returns>
	internal static bool IsNamespace(ReadOnlySpan<char> value)
	{
		if (value.Length is < 1 or > MaximumNamespaceLength)
		{
			return false;
		}

		foreach (char character in value)
		{
			if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>Throws when <paramref name="kind" /> is not a valid kind; kinds are implementation constants.</summary>
	/// <param name="kind">The kind.</param>
	/// <exception cref="ArgumentException">The kind is invalid.</exception>
	internal static void CheckKind(string kind)
	{
		ArgumentNullException.ThrowIfNull(kind);
		if (!IsKind(kind))
		{
			throw new ArgumentException("A kind must be a lowercase identifier of at most 32 characters.",
				nameof(kind));
		}
	}

	/// <summary>Formats an id.</summary>
	/// <param name="kind">A valid kind.</param>
	/// <param name="namespace">A valid namespace.</param>
	/// <param name="number">A positive number.</param>
	/// <returns>The id.</returns>
	internal static string Format(string kind, string @namespace, long number)
	{
		return string.Create(CultureInfo.InvariantCulture, $"{kind}-{@namespace}-{number}");
	}

	/// <summary>Parses an id.</summary>
	/// <param name="id">The candidate id.</param>
	/// <param name="kind">The kind.</param>
	/// <param name="namespace">The namespace.</param>
	/// <returns><see langword="true" /> when <paramref name="id" /> reads <c>kind-namespace-number</c>.</returns>
	internal static bool TryParse(string? id, out string kind, out string @namespace)
	{
		kind = string.Empty;
		@namespace = string.Empty;
		if (id is null || id.Length > MaximumLength)
		{
			return false;
		}

		ReadOnlySpan<char> text = id;
		int first = text.IndexOf('-');
		if (first < 0)
		{
			return false;
		}

		int second = text[(first + 1)..].IndexOf('-');
		if (second < 0)
		{
			return false;
		}

		second += first + 1;
		ReadOnlySpan<char> kindPart = text[..first];
		ReadOnlySpan<char> namespacePart = text[(first + 1)..second];
		ReadOnlySpan<char> number = text[(second + 1)..];
		if (!IsKind(kindPart) || !IsNamespace(namespacePart) || number.Length is < 1 or > 18 || number[0] == '0')
		{
			return false;
		}

		foreach (char character in number)
		{
			if (!char.IsAsciiDigit(character))
			{
				return false;
			}
		}

		kind = kindPart.ToString();
		@namespace = namespacePart.ToString();
		return true;
	}
}
