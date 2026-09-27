using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     One resource or resource template of a schema-only catalog, with its routing and a URI matcher. The matcher is the
///     SDK's own template parser, which only evaluates the URI and never invokes or constructs the primitive.
/// </summary>
public sealed class McpCatalogResource
{
	private readonly McpServerResource _primitive;

	internal McpCatalogResource(McpServerResource primitive, ResourceTemplate template, Resource? resource,
		McpPrimitiveRouting routing)
	{
		_primitive = primitive;
		Template = template;
		Resource = resource;
		Routing = routing;
	}

	/// <summary>A detached copy of the protocol metadata; a concrete resource has no template variables.</summary>
	public ResourceTemplate Template
	{
		get;
	}

	/// <summary>
	///     A detached copy of a concrete resource's listing entry, including its <c>size</c>; <see langword="null" /> for a
	///     template.
	/// </summary>
	public Resource? Resource
	{
		get;
	}

	/// <summary>Whether the gateway serves it locally or routes it to an instance.</summary>
	public McpPrimitiveRouting Routing
	{
		get;
	}

	/// <summary>Whether the URI template has variables.</summary>
	public bool IsTemplated => Template.IsTemplated;

	/// <summary>Whether a URI names this resource, exactly as the backend would decide it.</summary>
	/// <param name="uri">The URI in the resource's own space, such as <c>cheatengine://instance/…</c>.</param>
	/// <returns><see langword="true" /> when the backend would serve the URI with this primitive.</returns>
	public bool IsMatch(string uri)
	{
		ArgumentNullException.ThrowIfNull(uri);
		return _primitive.IsMatch(uri);
	}
}
