using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Mcp.Core.Contract;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     The resources the gateway itself adds to the Local ones every host serves: <c>cheatengine://instances</c>, the
///     gateway form <c>cheatengine://instances/{instanceId}/…</c> of every backend live resource and template, and that
///     form of each concrete live resource for one verified instance.
/// </summary>
internal sealed class GatewayResourceCatalog(GatewayPrimitiveCatalog primitives)
{
	/// <summary>The annotation priority of <c>cheatengine://instances</c>, above the live resources' 0.3.</summary>
	internal const float InstancesPriority = 0.6f;

	private static readonly JsonSerializerOptions ProtocolJson = CreateProtocolJson();

	private readonly Lazy<Routing> _routing = new(() => new Routing(primitives.Catalog.InstanceResources));

	/// <summary>The gateway form of every backend live resource and template, ordered by URI template.</summary>
	internal IReadOnlyList<ResourceTemplate> RoutedTemplates => _routing.Value.Templates;

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
			MimeType = McpResourceUris.JsonMimeType,
			Annotations = new Annotations { Audience = [Role.Assistant, Role.User], Priority = InstancesPriority },
			Meta = new JsonObject { [McpSourceToolAttribute.MetaKey] = CheatEngineToolNames.InstanceList }
		};
	}

	/// <summary>
	///     The gateway form of every concrete backend live resource for one instance whose identity the gateway
	///     verified, ordered by URI: <c>cheatengine://instances/{instanceId}/process</c> and so on.
	/// </summary>
	/// <param name="instance">
	///     The verified instance: its id selects the URIs, its name and process id the titles.
	/// </param>
	/// <returns>New listing entries.</returns>
	internal IEnumerable<Resource> CreateLiveResources(InstanceListEntry instance)
	{
		ArgumentNullException.ThrowIfNull(instance);
		return _routing.Value.Concrete.Select(resource => ToGateway(resource, instance));
	}

	/// <summary>
	///     Clones a backend live resource through its JSON form, so its description, MIME type, annotations and
	///     <c>_meta</c> are kept, swaps in the instance prefix and names the instance in the title.
	/// </summary>
	/// <param name="backend">A concrete backend live resource.</param>
	/// <param name="instance">The verified instance that serves it.</param>
	/// <returns>The gateway listing entry, such as <c>Current process (CE 'Main', pid 1234)</c>.</returns>
	internal static Resource ToGateway(Resource backend, InstanceListEntry instance)
	{
		ArgumentNullException.ThrowIfNull(backend);
		ArgumentNullException.ThrowIfNull(instance);
		JsonTypeInfo<Resource> typeInfo = ProtocolJson.GetTypeInfo<Resource>();
		Resource routed = JsonSerializer.SerializeToElement(backend, typeInfo).Deserialize(typeInfo)!;
		routed.Uri = McpResourceUris.ToGateway(backend.Uri, instance.InstanceId);
		routed.Title = string.Create(CultureInfo.InvariantCulture,
			$"{backend.Title ?? backend.Name} (CE '{instance.Name}', pid {instance.ProcessId})");
		return routed;
	}

	/// <summary>Whether a backend live URI names one of the composed live resources.</summary>
	/// <param name="backendUri">A <c>cheatengine://instance/…</c> URI.</param>
	/// <returns><see langword="true" /> when a backend would serve it.</returns>
	internal bool IsRouted(string backendUri)
	{
		return _routing.Value.Resources.Any(resource => resource.IsMatch(backendUri));
	}

	/// <summary>Finds the live resource behind one of <see cref="RoutedTemplates" />, compared ordinally.</summary>
	/// <param name="uriTemplate">A gateway template, such as a completion reference's URI.</param>
	/// <param name="resource">
	///     The catalog entry, whose <see cref="McpCatalogResource.Template" /> is the backend form of the template.
	/// </param>
	/// <returns><see langword="true" /> for a routed template.</returns>
	internal bool TryGetRoutedTemplate(string uriTemplate, [NotNullWhen(true)] out McpCatalogResource? resource)
	{
		ArgumentNullException.ThrowIfNull(uriTemplate);
		return _routing.Value.ByTemplate.TryGetValue(uriTemplate, out resource);
	}

	/// <summary>Clones a backend template through its JSON form and swaps in the gateway prefix.</summary>
	/// <param name="backend">A backend live resource or template.</param>
	/// <returns>The gateway template.</returns>
	internal static ResourceTemplate ToGateway(ResourceTemplate backend)
	{
		JsonTypeInfo<ResourceTemplate> typeInfo = ProtocolJson.GetTypeInfo<ResourceTemplate>();
		ResourceTemplate routed = JsonSerializer.SerializeToElement(backend, typeInfo).Deserialize(typeInfo)!;
		routed.UriTemplate =
			McpResourceUris.ToGateway(backend.UriTemplate, "{" + McpResourceUris.InstanceIdVariable + "}");
		return routed;
	}

	private static JsonSerializerOptions CreateProtocolJson()
	{
		return CheatEngineMcpJson.CreateOptions(new CheatEngineMcpPrimitiveOptions());
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

		internal IReadOnlyList<Resource> Concrete
		{
			get;
		} =
		[
			.. resources.Where(static resource => resource.Resource is not null)
				.Select(static resource => resource.Resource!)
				.OrderBy(static resource => resource.Uri, StringComparer.Ordinal)
		];

		internal FrozenDictionary<string, McpCatalogResource> ByTemplate
		{
			get;
		} = resources.ToFrozenDictionary(
			static resource => McpResourceUris.ToGateway(resource.Template.UriTemplate,
				"{" + McpResourceUris.InstanceIdVariable + "}"), StringComparer.Ordinal);
	}
}
