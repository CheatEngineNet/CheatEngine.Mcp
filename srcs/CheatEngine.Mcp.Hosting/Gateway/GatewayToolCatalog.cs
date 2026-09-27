using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>The gateway's tool list: the composed backend tools with a required routing argument, plus list_instances.</summary>
internal sealed class GatewayToolCatalog(IOptions<CheatEngineMcpPrimitiveOptions> manifest)
{
	internal const string ListInstancesToolName = "list_instances";
	internal const string InstanceIdArgumentName = "instanceId";

	private readonly Lazy<IReadOnlyList<Tool>> _tools = new(() => Create(manifest.Value));

	internal IReadOnlyList<Tool> Tools => _tools.Value;

	internal static IReadOnlyList<Tool> Create(CheatEngineMcpPrimitiveOptions manifest)
	{
		List<Tool> tools = [CreateListInstancesTool()];
		tools.AddRange(McpPrimitiveCatalog.Create(manifest).Tools.Select(AddRoutingArgument));
		return tools;
	}

	private static Tool CreateListInstancesTool()
	{
		return new Tool
		{
			Name = ListInstancesToolName,
			Description =
				"Lists responsive local Cheat Engine plugin instances that can receive routed tool calls.",
			InputSchema = JsonSerializer.SerializeToElement(new JsonObject
			{
				["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false
			})
		};
	}

	private static Tool AddRoutingArgument(Tool tool)
	{
		JsonObject schema = JsonNode.Parse(tool.InputSchema.GetRawText()) as JsonObject
		                    ?? new JsonObject { ["type"] = "object" };
		JsonObject properties = schema["properties"] as JsonObject ?? new JsonObject();
		properties[InstanceIdArgumentName] = new JsonObject
		{
			["type"] = "string",
			["description"] =
				"Exact identifier returned by list_instances for the Cheat Engine instance that must execute this operation."
		};
		schema["properties"] = properties;
		JsonArray required = schema["required"] as JsonArray ?? new JsonArray();
		if (!required.Any(value =>
			    string.Equals(value?.GetValue<string>(), InstanceIdArgumentName, StringComparison.Ordinal)))
		{
			required.Add(InstanceIdArgumentName);
		}

		schema["required"] = required;

		return new Tool
		{
			Name = tool.Name,
			Title = tool.Title,
			Description = tool.Description,
			InputSchema = JsonSerializer.SerializeToElement(schema),
			OutputSchema = tool.OutputSchema,
			Annotations = tool.Annotations,
			Icons = tool.Icons,
			Meta = tool.Meta
		};
	}
}
