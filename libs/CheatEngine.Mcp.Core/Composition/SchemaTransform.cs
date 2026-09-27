using System.Text.Json.Nodes;

using Microsoft.Extensions.AI;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     A JSON-schema transform that collapses nullable "type": [ ..., "null" ] arrays down to their single non-null
///     type and drops numeric keywords outside the signed 64-bit range. The Anthropic API tool-schema converter (Claude
///     Code
///     and other Anthropic-API MCP clients) rejects both with a 400 error. Optional parameters are already absent from
///     "required", so the result is semantically identical. The MCP SDK accepts schema options only through the
///     primitive create options, which <see cref="CheatEngineMcpServerBuilderExtensions" /> supplies.
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

			if (obj.TryGetPropertyValue("type", out JsonNode? typeNode) &&
			    typeNode is JsonArray typeArray)
			{
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

				if (hadNull && nonNull is not null && nonNullCount == 1)
				{
					obj["type"] = nonNull;
				}
			}

			// Drop numeric schema keywords whose value overflows signed 64-bit.
			// The Anthropic tool-schema converter rejects them with
			// "int too big to convert"; e.g. a `ulong x = ulong.MaxValue`
			// parameter (ScanTool.stopAddress) emits
			// "default": 18446744073709551615. The method still applies its
			// own default at call time, and these are only optional hints/
			// bounds, so removing them is semantically identical.
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
