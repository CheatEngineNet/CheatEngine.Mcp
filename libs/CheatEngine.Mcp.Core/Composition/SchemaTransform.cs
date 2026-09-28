using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

using Microsoft.Extensions.AI;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     A JSON-schema transform that keeps generated tool schemas compatible with strict MCP clients:
///     <list type="bullet">
///         <item>
///             collapses nullable <c>"type": [ ..., "null" ]</c> arrays to their single non-null type and drops
///             <c>null</c> from <c>enum</c> lists, because the Anthropic API tool-schema converter (Claude Code and other
///             Anthropic-API MCP clients) rejects nullable type arrays with a 400 error;
///         </item>
///         <item>
///             removes nullable members from an object's <c>required</c> list: the serializer omits null members
///             (<c>WhenWritingNull</c>), so a client that validates <c>structuredContent</c> against the output schema
///             (the TypeScript SDK does) would otherwise reject a result that leaves one out, and an input object may
///             leave it out too;
///         </item>
///         <item>drops numeric keywords outside the signed 64-bit range.</item>
///     </list>
///     The MCP SDK accepts schema options only through the primitive create options, which
///     <see cref="CheatEngineMcpServerBuilderExtensions" /> supplies.
/// </summary>
public static class SchemaTransform
{
	private static readonly string[] OversizedNumericKeywords =
	{
		"default", "const", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf"
	};

	/// <summary>Schema options that keep generated tool schemas compatible with strict MCP clients.</summary>
	public static AIJsonSchemaCreateOptions SchemaCreateOptions
	{
		get;
	} = new()
	{
		TransformSchemaNode = static (context, node) =>
		{
			if (node is not JsonObject obj)
			{
				return node;
			}

			CollapseNullableType(obj);
			RemoveNullableMembersFromRequired(context.TypeInfo, obj);

			// Drop numeric schema keywords whose value overflows signed 64-bit.
			// The Anthropic tool-schema converter rejects them with
			// "int too big to convert"; e.g. a `ulong x = ulong.MaxValue`
			// parameter emits "default": 18446744073709551615. The method still
			// applies its own default at call time, and these are only optional
			// hints/bounds, so removing them is semantically identical.
			foreach (string keyword in OversizedNumericKeywords)
			{
				if (obj.TryGetPropertyValue(keyword, out JsonNode? valueNode) &&
					valueNode is JsonValue value && !FitsInt64(value))
				{
					obj.Remove(keyword);
				}
			}

			return node;
		}
	};

	private static void CollapseNullableType(JsonObject obj)
	{
		if (!obj.TryGetPropertyValue("type", out JsonNode? typeNode) || typeNode is not JsonArray typeArray)
		{
			return;
		}

		string? nonNull = null;
		bool hadNull = false;
		int nonNullCount = 0;
		foreach (JsonNode? member in typeArray)
		{
			string? value = member?.GetValue<string>();
			if (value == "null")
			{
				hadNull = true;
			}
			else
			{
				nonNullCount++;
				nonNull ??= value;
			}
		}

		if (!hadNull || nonNull is null || nonNullCount != 1)
		{
			return;
		}

		obj["type"] = nonNull;
		// A nullable enum lists null among its values; the collapsed type no longer admits it.
		if (obj.TryGetPropertyValue("enum", out JsonNode? enumNode) && enumNode is JsonArray values)
		{
			for (int index = values.Count - 1; index >= 0; index--)
			{
				if (values[index] is null)
				{
					values.RemoveAt(index);
				}
			}
		}
	}

	private static void RemoveNullableMembersFromRequired(JsonTypeInfo typeInfo, JsonObject obj)
	{
		if (typeInfo.Kind != JsonTypeInfoKind.Object ||
			!obj.TryGetPropertyValue("required", out JsonNode? requiredNode) || requiredNode is not JsonArray required)
		{
			return;
		}

		foreach (JsonPropertyInfo property in typeInfo.Properties)
		{
			if (!property.IsGetNullable && Nullable.GetUnderlyingType(property.PropertyType) is null)
			{
				continue;
			}

			for (int index = required.Count - 1; index >= 0; index--)
			{
				if (required[index]?.GetValue<string>() == property.Name)
				{
					required.RemoveAt(index);
				}
			}
		}

		if (required.Count == 0)
		{
			obj.Remove("required");
		}
	}

	private static bool FitsInt64(JsonValue value)
	{
		// In range as a signed 64-bit integer.
		if (value.TryGetValue(out long _))
		{
			return true;
		}

		// A positive integer larger than long.MaxValue (e.g. ulong.MaxValue).
		if (value.TryGetValue(out ulong _))
		{
			return false;
		}

		// An integral value outside the signed-64 range in either direction.
		if (value.TryGetValue(out decimal dec) && decimal.Truncate(dec) == dec)
		{
			return dec is >= long.MinValue and <= long.MaxValue;
		}

		// Non-integer (float) or non-numeric — leave it untouched.
		return true;
	}
}
