using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Mcp.Core.Contract;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>The gateway's tool list: <c>instance_list</c>, then the composed backend tools with a routing argument.</summary>
internal sealed class GatewayToolCatalog(GatewayPrimitiveCatalog primitives)
{
	/// <summary>The gateway-local discovery tool.</summary>
	internal const string InstanceListToolName = GatewayInstanceTool.Name;

	/// <summary>The routing argument every backend tool gains.</summary>
	internal const string InstanceIdArgumentName = "instanceId";

	/// <summary>The routing argument's description, repeated in every routed tool, so it stays short.</summary>
	internal const string InstanceIdDescription = $"Instance id from {CheatEngineToolNames.InstanceList}.";

	private static readonly JsonSerializerOptions ProtocolJson = CreateProtocolJson();

	private readonly Lazy<Listing> _listing = new(() => new Listing(Create(primitives.Catalog)));

	/// <summary>Every tool, <c>instance_list</c> first.</summary>
	internal IReadOnlyList<Tool> Tools => _listing.Value.Tools;

	/// <summary>Whether a tool name is one of the routed backend tools.</summary>
	/// <param name="name">The requested tool name.</param>
	/// <returns><see langword="true" /> for a backend tool.</returns>
	internal bool IsRouted(string name)
	{
		return _listing.Value.Routed.Contains(name);
	}

	/// <summary>Builds the listing from a manifest without constructing any primitive.</summary>
	/// <param name="manifest">The routed composition.</param>
	/// <returns><c>instance_list</c>, then the backend tools in name order.</returns>
	internal static IReadOnlyList<Tool> Create(CheatEngineMcpPrimitiveOptions manifest)
	{
		return Create(McpPrimitiveCatalog.Create(manifest));
	}

	/// <summary>Builds the listing from a schema-only catalog.</summary>
	/// <param name="catalog">The routed composition's catalog.</param>
	/// <returns><c>instance_list</c>, then the backend tools in name order.</returns>
	internal static IReadOnlyList<Tool> Create(McpPrimitiveCatalog catalog)
	{
		List<Tool> tools = [GatewayInstanceTool.CreateProtocolTool()];
		tools.AddRange(catalog.Tools.Select(AddRoutingArgument));
		return tools;
	}

	/// <summary>
	///     Clones a backend tool through its JSON form, so every field survives, including ones a later SDK adds, and
	///     puts the required routing argument first in <c>properties</c> and <c>required</c>.
	/// </summary>
	/// <param name="tool">The backend tool.</param>
	/// <returns>The routed tool.</returns>
	internal static Tool AddRoutingArgument(Tool tool)
	{
		JsonTypeInfo<Tool> typeInfo = ProtocolJson.GetTypeInfo<Tool>();
		JsonObject node = JsonSerializer.SerializeToNode(tool, typeInfo)!.AsObject();
		JsonObject schema = node["inputSchema"] as JsonObject ?? new JsonObject { ["type"] = "object" };
		JsonObject properties = new()
		{
			[InstanceIdArgumentName] =
				new JsonObject { ["type"] = "string", ["description"] = InstanceIdDescription }
		};
		if (schema["properties"] is JsonObject declared)
		{
			foreach ((string name, JsonNode? value) in declared)
			{
				if (!string.Equals(name, InstanceIdArgumentName, StringComparison.Ordinal))
				{
					properties[name] = value?.DeepClone();
				}
			}
		}

		JsonArray required = new(JsonValue.Create(InstanceIdArgumentName));
		if (schema["required"] is JsonArray declaredRequired)
		{
			foreach (JsonNode? value in declaredRequired)
			{
				if (value is not JsonValue name || !name.TryGetValue(out string? text) ||
					!string.Equals(text, InstanceIdArgumentName, StringComparison.Ordinal))
				{
					required.Add(value?.DeepClone());
				}
			}
		}

		schema["properties"] = properties;
		schema["required"] = required;
		if (schema.Parent is null)
		{
			node["inputSchema"] = schema;
		}

		return node.Deserialize(typeInfo)!;
	}

	private static JsonSerializerOptions CreateProtocolJson()
	{
		return CheatEngineMcpJson.CreateOptions(new CheatEngineMcpPrimitiveOptions());
	}

	private sealed class Listing(IReadOnlyList<Tool> tools)
	{
		internal IReadOnlyList<Tool> Tools
		{
			get;
		} = tools;

		internal FrozenSet<string> Routed
		{
			get;
		} = tools.Where(static tool => tool.Name != InstanceListToolName).Select(static tool => tool.Name)
			.ToFrozenSet(StringComparer.Ordinal);
	}
}
