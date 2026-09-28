using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     One resource or resource template of a schema-only catalog, with its routing and a URI matcher. The matcher is the
///     SDK's own template parser, which only evaluates the URI and never invokes or constructs the primitive.
/// </summary>
public sealed class McpCatalogResource
{
	private readonly FrozenDictionary<string, IReadOnlyList<string>> _allowedValues;
	private readonly FrozenSet<string> _instanceCompletions;
	private readonly McpServerResource _primitive;

	internal McpCatalogResource(McpServerResource primitive, ResourceTemplate template, Resource? resource,
		McpPrimitiveRouting routing)
	{
		_primitive = primitive;
		Template = template;
		Resource = resource;
		Routing = routing;
		ParameterInfo[] parameters = McpPrimitiveOrigin.MethodOf(primitive)?.GetParameters() ?? [];
		_allowedValues = parameters
			.Where(static parameter => parameter.Name is not null &&
									   parameter.GetCustomAttribute<AllowedValuesAttribute>() is not null)
			.ToFrozenDictionary(static parameter => parameter.Name!, static parameter =>
				(IReadOnlyList<string>) Array.AsReadOnly(
					parameter.GetCustomAttribute<AllowedValuesAttribute>()!.Values.OfType<string>().ToArray()),
				StringComparer.Ordinal);
		_instanceCompletions = parameters
			.Where(static parameter => parameter.Name is not null &&
									   parameter.GetCustomAttribute<McpCompletionAttribute>() is not null)
			.Select(static parameter => parameter.Name!).ToFrozenSet(StringComparer.Ordinal);
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

	/// <summary>
	///     The values a template variable or query parameter declares with <c>[AllowedValues]</c> on its string
	///     parameter, in declaration order, which the SDK completes on a backend by itself.
	/// </summary>
	/// <param name="variable">The variable's name.</param>
	/// <returns>The declared string values; empty when the variable declares none.</returns>
	public IReadOnlyList<string> AllowedValues(string variable)
	{
		ArgumentNullException.ThrowIfNull(variable);
		return _allowedValues.TryGetValue(variable, out IReadOnlyList<string>? values) ? values : [];
	}

	/// <summary>
	///     Whether the instance serving this live resource completes a template variable from its own state, because
	///     the variable is marked with <see cref="McpCompletionAttribute" />.
	/// </summary>
	/// <param name="variable">The variable's name.</param>
	/// <returns><see langword="true" /> for a variable a backend completes dynamically.</returns>
	public bool IsCompletedByInstance(string variable)
	{
		ArgumentNullException.ThrowIfNull(variable);
		return _instanceCompletions.Contains(variable);
	}

	/// <summary>Whether a URI names this resource, exactly as the backend would decide it.</summary>
	/// <param name="uri">The URI in the resource's own space, such as <c>cheatengine://instance/…</c>.</param>
	/// <returns><see langword="true" /> when the backend would serve the URI with this primitive.</returns>
	public bool IsMatch(string uri)
	{
		ArgumentNullException.ThrowIfNull(uri);
		return _primitive.IsMatch(uri);
	}
}
