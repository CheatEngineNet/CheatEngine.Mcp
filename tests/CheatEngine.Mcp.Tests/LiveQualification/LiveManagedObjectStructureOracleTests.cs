using System.Runtime.Versioning;
using System.Text.Json.Nodes;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveManagedObjectStructureOracleTests
{
	private static readonly (string Name, int Value)[] Fields = [("Marker", 323460063), ("Mirror", -20261019), ("Toggle", 1)];
	private const string Definition = """
	                                  {"name":"Owned","size":20,"elementCount":4,"total":4,"elements":[
	                                    {"name":"Vtable","offset":"0","valueType":"pointer","byteSize":8},
	                                    {"name":"Marker","offset":"8","valueType":"int32","byteSize":4},
	                                    {"name":"Mirror","offset":"C","valueType":"int32","byteSize":4},
	                                    {"name":"Toggle","offset":"10","valueType":"int32","byteSize":4}]}
	                                  """;
	private const string Layout = """
	                              {"objectType":18,"fields":[
	                                {"name":"Marker","offset":8,"isStatic":false},
	                                {"name":"Mirror","offset":12,"isStatic":false},
	                                {"name":"Toggle","offset":16,"isStatic":false}]}
	                              """;
	private const string Filled = """{"name":"Owned","size":20,"elementCount":4}""";

	[Fact]
	public void AcceptsOnlyTheKnownHeaderAndThreeCompleteFields()
	{
		LiveManagedObjectQualification.AssertExactStructureLayout(Parse(Definition), Parse(Filled), Parse(Layout), "Owned", Fields, 8);
	}

	[Theory]
	[InlineData("header-name")]
	[InlineData("header-offset")]
	[InlineData("header-type")]
	[InlineData("header-width")]
	[InlineData("field-offset")]
	[InlineData("field-type")]
	[InlineData("field-width")]
	[InlineData("extra-element")]
	[InlineData("truncated")]
	[InlineData("summary-count")]
	[InlineData("object-kind")]
	[InlineData("pointer-width")]
	public void RejectsCorruptOrIncompleteLayouts(string corruption)
	{
		JsonNode definition = Parse(Definition);
		JsonNode filled = Parse(Filled);
		JsonNode layout = Parse(Layout);
		int width = 8;
		switch (corruption)
		{
			case "header-name":
				definition["elements"]![0]!["name"] = "Unexpected";
				break;
			case "header-offset":
				definition["elements"]![0]!["offset"] = "4";
				break;
			case "header-type":
				definition["elements"]![0]!["valueType"] = "int64";
				break;
			case "header-width":
				definition["elements"]![0]!["byteSize"] = 4;
				break;
			case "field-offset":
				definition["elements"]![1]!["offset"] = "C";
				break;
			case "field-type":
				definition["elements"]![1]!["valueType"] = "float";
				break;
			case "field-width":
				definition["elements"]![1]!["byteSize"] = 8;
				break;
			case "extra-element":
				definition["elements"]!.AsArray().Add(Parse("""{"name":"Extra","offset":"14","valueType":"int32","byteSize":4}"""));
				break;
			case "truncated":
				definition["nextOffset"] = 4;
				break;
			case "summary-count":
				filled["elementCount"] = 3;
				break;
			case "object-kind":
				layout["objectType"] = 29;
				break;
			case "pointer-width":
				width = 4;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(corruption));
		}

		Exception? failure = Record.Exception(() => LiveManagedObjectQualification.AssertExactStructureLayout(definition, filled, layout, "Owned", Fields, width));
		Assert.IsAssignableFrom<Xunit.Sdk.XunitException>(failure);
	}

	private static JsonNode Parse(string text) => JsonNode.Parse(text)!;
}
