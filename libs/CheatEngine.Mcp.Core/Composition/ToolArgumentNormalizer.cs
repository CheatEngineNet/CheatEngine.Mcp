using System.Text.Json;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     Repairs common client encoding mistakes against a tool's input schema, so the published schemas can stay strict.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>A JSON <c>null</c> for a property that is not required is removed, so the C# default applies.</item>
///         <item>
///             A string for an <c>array</c> or <c>object</c> property that parses (up to
///             <see cref="MaxEmbeddedJsonLength" />
///             characters) to that kind is replaced by the parsed value.
///         </item>
///         <item><c>"true"</c> or <c>"false"</c> for a <c>boolean</c> property becomes the boolean.</item>
///     </list>
///     A property whose schema admits <c>string</c> is never rewritten. Numbers sent as strings need no repair: the
///     serializer options read numbers from strings.
/// </remarks>
internal static class ToolArgumentNormalizer
{
	internal const int MaxEmbeddedJsonLength = 1024 * 1024;

	private static readonly JsonElement True = CreateBoolean(true);
	private static readonly JsonElement False = CreateBoolean(false);

	/// <summary>Returns repaired arguments, or <see langword="null" /> when nothing needed a repair.</summary>
	/// <param name="inputSchema">The tool's published input schema.</param>
	/// <param name="arguments">The call's arguments.</param>
	/// <returns>A new dictionary with the repairs applied, or <see langword="null" />.</returns>
	internal static Dictionary<string, JsonElement>? Normalize(JsonElement inputSchema,
		IDictionary<string, JsonElement> arguments)
	{
		if (inputSchema.ValueKind != JsonValueKind.Object ||
			!inputSchema.TryGetProperty("properties", out JsonElement properties) ||
			properties.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		HashSet<string> required = new(StringComparer.Ordinal);
		if (inputSchema.TryGetProperty("required", out JsonElement requiredNames) &&
			requiredNames.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement name in requiredNames.EnumerateArray())
			{
				if (name.ValueKind == JsonValueKind.String)
				{
					required.Add(name.GetString()!);
				}
			}
		}

		Dictionary<string, JsonElement>? normalized = null;
		foreach ((string name, JsonElement value) in arguments)
		{
			if (!properties.TryGetProperty(name, out JsonElement property) ||
				property.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			if (value.ValueKind == JsonValueKind.Null && !required.Contains(name))
			{
				normalized ??= new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
				normalized.Remove(name);
			}
			else if (value.ValueKind == JsonValueKind.String && TryRepairString(property, value.GetString()!,
						 out JsonElement repaired))
			{
				normalized ??= new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
				normalized[name] = repaired;
			}
		}

		return normalized;
	}

	private static bool TryRepairString(JsonElement property, string text, out JsonElement repaired)
	{
		repaired = default;
		if (!property.TryGetProperty("type", out JsonElement type) || Admits(type, "string"))
		{
			return false;
		}

		if (Admits(type, "boolean"))
		{
			if (text.Equals("true", StringComparison.OrdinalIgnoreCase))
			{
				repaired = True;
				return true;
			}

			if (text.Equals("false", StringComparison.OrdinalIgnoreCase))
			{
				repaired = False;
				return true;
			}
		}

		bool array = Admits(type, "array");
		bool obj = Admits(type, "object");
		if ((!array && !obj) || text.Length > MaxEmbeddedJsonLength)
		{
			return false;
		}

		ReadOnlySpan<char> trimmed = text.AsSpan().TrimStart();
		if (trimmed.IsEmpty || !((array && trimmed[0] == '[') || (obj && trimmed[0] == '{')))
		{
			return false;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(text);
			JsonValueKind kind = document.RootElement.ValueKind;
			if ((array && kind == JsonValueKind.Array) || (obj && kind == JsonValueKind.Object))
			{
				repaired = document.RootElement.Clone();
				return true;
			}
		}
		catch (JsonException)
		{
			// Not JSON after all: the binder reports the original value.
		}

		return false;
	}

	private static bool Admits(JsonElement type, string name)
	{
		if (type.ValueKind == JsonValueKind.String)
		{
			return type.ValueEquals(name);
		}

		if (type.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement member in type.EnumerateArray())
			{
				if (member.ValueKind == JsonValueKind.String && member.ValueEquals(name))
				{
					return true;
				}
			}
		}

		return false;
	}

	private static JsonElement CreateBoolean(bool value)
	{
		using JsonDocument document = JsonDocument.Parse(value ? "true" : "false");
		return document.RootElement.Clone();
	}
}
