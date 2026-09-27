using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Mcp.Core.Contract;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     The resources the gateway itself adds to the Local ones every host serves: <c>cheatengine://instances</c>, and
///     the gateway form <c>cheatengine://instances/{instanceId}/…</c> of every backend live resource.
/// </summary>
internal sealed class GatewayResourceCatalog(GatewayPrimitiveCatalog primitives)
{
	/// <summary>The gateway's instance list: the same data as <c>instance_list</c>, never tokens or endpoints.</summary>
	/// <returns>A new listing entry.</returns>
	internal static Resource CreateInstancesResource()
	{
		return new Resource
		{
			Uri = McpResourceUris.GatewayInstances,
			Name = "instances",
			Title = "Cheat Engine instances",
			Description = "The local Cheat Engine instances whose plugin is enabled and verified: ids, names, CE " +
						  "process IDs and plugin versions, like " + CheatEngineToolNames.InstanceList +
						  ". Never tokens or endpoints.",
			MimeType = McpResourceUris.JsonMimeType
		};
	}

	private readonly Lazy<Routing> _routing = new(() => new Routing(primitives.Catalog.InstanceResources));

	/// <summary>The gateway form of every backend live resource and template, ordered by URI template.</summary>
	internal IReadOnlyList<ResourceTemplate> RoutedTemplates => _routing.Value.Templates;

	/// <summary>Whether a backend live URI names one of the composed live resources.</summary>
	/// <param name="backendUri">A <c>cheatengine://instance/…</c> URI.</param>
	/// <returns><see langword="true" /> when a backend would serve it.</returns>
	internal bool IsRouted(string backendUri)
	{
		return _routing.Value.Resources.Any(resource => resource.IsMatch(backendUri));
	}

	/// <summary>Whether a URI template is one of <see cref="RoutedTemplates" />, compared ordinally.</summary>
	/// <param name="uriTemplate">A template, such as a completion reference's URI.</param>
	/// <returns><see langword="true" /> for a routed template.</returns>
	internal bool IsRoutedTemplate(string uriTemplate)
	{
		return _routing.Value.TemplateUris.Contains(uriTemplate);
	}

	/// <summary>Clones a backend template through its JSON form and swaps in the gateway prefix.</summary>
	/// <param name="backend">A backend live resource or template.</param>
	/// <returns>The gateway template.</returns>
	internal static ResourceTemplate ToGateway(ResourceTemplate backend)
	{
		JsonTypeInfo<ResourceTemplate> typeInfo = McpJsonUtilities.DefaultOptions.GetTypeInfo<ResourceTemplate>();
		ResourceTemplate routed = JsonSerializer.SerializeToElement(backend, typeInfo).Deserialize(typeInfo)!;
		routed.UriTemplate =
			McpResourceUris.ToGateway(backend.UriTemplate, "{" + McpResourceUris.InstanceIdVariable + "}");
		return routed;
	}

	private sealed class Routing(IReadOnlyList<McpCatalogResource> resources)
	{
		internal IReadOnlyList<McpCatalogResource> Resources
		{
			get;
		} = resources;

		internal IReadOnlyList<ResourceTemplate> Templates
		{
			get;
		} = [.. resources.Select(static resource => ToGateway(resource.Template))];

		internal FrozenSet<string> TemplateUris
		{
			get;
		} = resources.Select(static resource => ToGateway(resource.Template).UriTemplate)
			.ToFrozenSet(StringComparer.Ordinal);
	}
}
