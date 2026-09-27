using System.Reflection;
using System.Text.Json;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     Serves a manifest's Local primitives (static resource and prompt methods: knowledge documents and prompts) on a
///     server that routes everything else, such as the stdio gateway. They are bound live, without any activation,
///     because a static method needs no target and touches no Cheat Engine instance.
/// </summary>
public static class McpLocalPrimitives
{
	private const BindingFlags StaticMethods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

	/// <summary>
	///     Adds every Local resource and prompt of <paramref name="manifest" /> to the server's collections, with the same
	///     serializer and schema options as a backend, and the Core error filters for resource reads and prompt requests.
	/// </summary>
	/// <param name="options">The server options, before the server is created.</param>
	/// <param name="manifest">The composition.</param>
	/// <param name="services">The server's services, used only to tell request services from arguments.</param>
	public static void AddTo(McpServerOptions options, CheatEngineMcpPrimitiveOptions manifest,
		IServiceProvider services)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(manifest);
		ArgumentNullException.ThrowIfNull(services);
		JsonSerializerOptions json = CheatEngineMcpJson.CreateOptions(manifest, CheatEngineMcpJson.StrictByDefault);
		foreach (CheatEngineMcpPrimitive primitive in manifest.Primitives)
		{
			foreach (MethodInfo method in primitive.Type.GetMethods(StaticMethods))
			{
				switch (primitive.Kind)
				{
					case CheatEngineMcpPrimitiveKind.Resource
						when method.GetCustomAttribute<McpServerResourceAttribute>() is not null:
						(options.ResourceCollection ??= []).Add(CheatEngineMcpServerBuilderExtensions.CreateResource(
							services, primitive.Type, method, McpPrimitiveBinding.Catalog, json));
						break;
					case CheatEngineMcpPrimitiveKind.Prompt
						when method.GetCustomAttribute<McpServerPromptAttribute>() is not null:
						(options.PromptCollection ??= []).Add(CheatEngineMcpServerBuilderExtensions.CreatePrompt(
							services, primitive.Type, method, McpPrimitiveBinding.Catalog, json));
						break;
				}
			}
		}

		options.Filters.Request.ReadResourceFilters.Add(CheatEngineToolFilters.MapResourceErrors);
		options.Filters.Request.GetPromptFilters.Add(CheatEngineToolFilters.MapPromptErrors);
	}
}
