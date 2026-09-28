using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Tests.Support;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Hosting;

public sealed class GatewayToolCatalogTests
{
	[Fact]
	public void InstanceList_ProtocolTool_PassesTheGatewayContractRules()
	{
		Tool tool = TestComposition.GatewayTools[0];

		Assert.Equal("instance_list", tool.Name);
		Assert.Equal("List Cheat Engine instances", tool.Title);
		ToolAnnotations annotations = Assert.IsType<ToolAnnotations>(tool.Annotations);
		Assert.Equal("List Cheat Engine instances", annotations.Title);
		Assert.True(annotations.ReadOnlyHint);
		Assert.False(annotations.DestructiveHint);
		Assert.True(annotations.IdempotentHint);
		Assert.False(annotations.OpenWorldHint);
		JsonElement output = tool.OutputSchema!.Value;
		Assert.Equal("object", output.GetProperty("type").GetString());
		Assert.Equal(["discoveryIncomplete", "instances"],
			output.GetProperty("properties").EnumerateObject().Select(static property => property.Name)
				.Order(StringComparer.Ordinal));
		Assert.Empty(tool.InputSchema.GetProperty("properties").EnumerateObject());
		Assert.Empty(McpContractRules.ValidateTool(tool, true));
		Assert.Empty(McpContractRules.ValidateTools([tool], true));
	}

	[Fact]
	public void RoutedTools_InstanceId_IsTheFirstPropertyAndRequirement()
	{
		foreach (Tool tool in TestComposition.GatewayTools.Skip(1))
		{
			JsonProperty first = tool.InputSchema.GetProperty("properties").EnumerateObject().First();
			Assert.Equal(GatewayToolCatalog.InstanceIdArgumentName, first.Name);
			Assert.Equal("string", first.Value.GetProperty("type").GetString());
			Assert.Equal("Instance id from instance_list.", first.Value.GetProperty("description").GetString());
			string?[] required = tool.InputSchema.GetProperty("required").EnumerateArray()
				.Select(static value => value.GetString()).ToArray();
			Assert.Equal(GatewayToolCatalog.InstanceIdArgumentName, required[0]);
			Assert.Single(required, static name => name == GatewayToolCatalog.InstanceIdArgumentName);
		}
	}

	[Fact]
	public void AddRoutingArgument_EveryOtherField_SurvivesTheClone()
	{
		Tool backend = new()
		{
			Name = "memory_read",
			Title = "Read memory",
			Description = "Reads memory.",
			InputSchema = Parse("""
			                    {"type":"object","properties":{"address":{"type":"string","description":"Address."}},
			                    "required":["address"],"additionalProperties":false}
			                    """),
			OutputSchema = Parse("""{"type":"object","properties":{"bytes":{"type":"string"}}}"""),
			Annotations = new ToolAnnotations { ReadOnlyHint = true, OpenWorldHint = false },
			Icons = [new Icon { Source = "https://example.invalid/icon.png" }],
			Meta = new JsonObject { ["cheatengine/dispatchClass"] = "short" }
		};

		Tool routed = GatewayToolCatalog.AddRoutingArgument(backend);

		JsonObject expected = Serialize(backend);
		JsonObject actual = Serialize(routed);
		Assert.Equal(
			"""{"type":"object","properties":{"instanceId":{"type":"string","description":"Instance id from instance_list."},"address":{"type":"string","description":"Address."}},"required":["instanceId","address"],"additionalProperties":false}""",
			actual["inputSchema"]!.ToJsonString());
		expected.Remove("inputSchema");
		actual.Remove("inputSchema");
		Assert.True(JsonNode.DeepEquals(expected, actual), actual.ToJsonString());
	}

	[Fact]
	public void ForBackend_UpstreamMeta_KeepsOnlyStringTraceContext()
	{
		JsonObject upstream = new()
		{
			["traceparent"] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
			["tracestate"] = new JsonObject { ["nested"] = "not a W3C header value" },
			["baggage"] = 7,
			["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
			["progressToken"] = "p1",
			["TRACEPARENT"] = "case matters"
		};

		JsonObject forwarded = GatewayMeta.ForBackend(upstream)!;

		Assert.Equal("""{"traceparent":"00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"}""",
			forwarded.ToJsonString());
		Assert.Null(GatewayMeta.ForBackend(new JsonObject { ["progressToken"] = 1 }));
		Assert.Null(GatewayMeta.ForBackend(null));
	}

	private static JsonElement Parse(string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}

	private static JsonObject Serialize(Tool tool)
	{
		return JsonSerializer.SerializeToNode(tool, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(Tool)))!
			.AsObject();
	}
}
