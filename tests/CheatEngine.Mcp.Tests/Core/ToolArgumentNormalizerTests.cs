using System.Text.Json;

using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class ToolArgumentNormalizerTests
{
	private const string Schema = """
	                              {
	                                "type": "object",
	                                "properties": {
	                                  "text": { "description": "Text.", "type": "string" },
	                                  "values": { "description": "Values.", "type": "array", "items": { "type": "string" } },
	                                  "options": { "description": "Options.", "type": "object" },
	                                  "flag": { "description": "Flag.", "type": "boolean" },
	                                  "count": { "description": "Count.", "type": "integer" },
	                                  "either": { "description": "Either.", "type": ["string", "array"] },
	                                  "needed": { "description": "Needed.", "type": "array" }
	                                },
	                                "required": ["text", "needed"]
	                              }
	                              """;

	[Fact]
	public void Normalize_StringifiedArrayAndObject_AreParsed()
	{
		Dictionary<string, JsonElement>? normalized = Normalize(
			"""{"values":"[\"a\",\"b\"]","options":" {\"x\":1}"}""");

		Assert.NotNull(normalized);
		Assert.Equal(JsonValueKind.Array, normalized["values"].ValueKind);
		Assert.Equal(2, normalized["values"].GetArrayLength());
		Assert.Equal(1, normalized["options"].GetProperty("x").GetInt32());
	}

	[Fact]
	public void Normalize_OptionalNull_IsRemovedAndRequiredNullIsKept()
	{
		Dictionary<string, JsonElement>? normalized = Normalize("""{"count":null,"needed":null,"text":"a"}""");

		Assert.NotNull(normalized);
		Assert.False(normalized.ContainsKey("count"));
		Assert.Equal(JsonValueKind.Null, normalized["needed"].ValueKind);
		Assert.Equal("a", normalized["text"].GetString());
	}

	[Theory]
	[InlineData("true", true)]
	[InlineData("false", false)]
	[InlineData("True", true)]
	public void Normalize_BooleanText_BecomesABoolean(string text, bool expected)
	{
		Dictionary<string, JsonElement>? normalized = Normalize($$"""{"flag":"{{text}}"}""");

		Assert.NotNull(normalized);
		Assert.Equal(expected, normalized["flag"].GetBoolean());
	}

	[Theory]
	[InlineData("""{"text":"[1,2]"}""")]
	[InlineData("""{"text":"true"}""")]
	[InlineData("""{"either":"[1,2]"}""")]
	[InlineData("""{"values":"not json"}""")]
	[InlineData("""{"values":"{\"x\":1}"}""")]
	[InlineData("""{"options":"[1]"}""")]
	[InlineData("""{"flag":"yes"}""")]
	[InlineData("""{"count":"12"}""")]
	[InlineData("""{"unknown":null}""")]
	[InlineData("""{"values":["a"],"flag":true}""")]
	public void Normalize_StringTypedOrUnrepairableValue_IsLeftUnchanged(string arguments)
	{
		Assert.Null(Normalize(arguments));
	}

	[Fact]
	public void Normalize_OversizedEmbeddedJson_IsLeftUnchanged()
	{
		string oversized = "[" + new string(' ', ToolArgumentNormalizer.MaxEmbeddedJsonLength) + "]";
		Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal)
		{
			["values"] = JsonSerializer.SerializeToElement(oversized, TestJsonContext.Default.String)
		};

		Assert.Null(ToolArgumentNormalizer.Normalize(ParseSchema(), arguments));
	}

	[Fact]
	public void Normalize_Repair_NeverMutatesTheCallersDictionary()
	{
		Dictionary<string, JsonElement> arguments = Parse("""{"count":null}""");

		Dictionary<string, JsonElement>? normalized = ToolArgumentNormalizer.Normalize(ParseSchema(), arguments);

		Assert.NotNull(normalized);
		Assert.True(arguments.ContainsKey("count"));
		Assert.Empty(normalized);
	}

	private static Dictionary<string, JsonElement>? Normalize(string arguments)
	{
		return ToolArgumentNormalizer.Normalize(ParseSchema(), Parse(arguments));
	}

	private static JsonElement ParseSchema()
	{
		using JsonDocument schema = JsonDocument.Parse(Schema);
		return schema.RootElement.Clone();
	}

	private static Dictionary<string, JsonElement> Parse(string arguments)
	{
		using JsonDocument document = JsonDocument.Parse(arguments);
		return document.RootElement.EnumerateObject().ToDictionary(static property => property.Name,
			static property => property.Value.Clone(), StringComparer.Ordinal);
	}
}
