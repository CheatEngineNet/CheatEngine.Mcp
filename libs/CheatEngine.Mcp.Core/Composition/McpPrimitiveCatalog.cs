using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>The protocol metadata of a manifest, produced without constructing any primitive or touching Cheat Engine.</summary>
public sealed class McpPrimitiveCatalog
{
	private McpPrimitiveCatalog(IReadOnlyList<Tool> tools, IReadOnlyList<Prompt> prompts,
		IReadOnlyList<McpCatalogResource> entries)
	{
		Tools = tools;
		Prompts = prompts;
		Entries = entries;
		Resources = entries.Where(static entry => entry.Resource is not null)
			.Select(static entry => entry.Resource!).ToArray();
		ResourceTemplates = entries.Where(static entry => entry.IsTemplated)
			.Select(static entry => entry.Template).ToArray();
		LocalResources = entries.Where(static entry => entry.Routing is McpPrimitiveRouting.Local).ToArray();
		InstanceResources = entries.Where(static entry => entry.Routing is McpPrimitiveRouting.Instance).ToArray();
	}

	/// <summary>The tools, ordered by name.</summary>
	public IReadOnlyList<Tool> Tools
	{
		get;
	}

	/// <summary>The prompts, ordered by name; every prompt is Local.</summary>
	public IReadOnlyList<Prompt> Prompts
	{
		get;
	}

	/// <summary>The concrete resources (<c>resources/list</c>), ordered by URI.</summary>
	public IReadOnlyList<Resource> Resources
	{
		get;
	}

	/// <summary>The resource templates (<c>resources/templates/list</c>), ordered by URI template.</summary>
	public IReadOnlyList<ResourceTemplate> ResourceTemplates
	{
		get;
	}

	/// <summary>The static resources and templates every host serves itself, ordered by URI template.</summary>
	public IReadOnlyList<McpCatalogResource> LocalResources
	{
		get;
	}

	/// <summary>The live resources and templates the gateway routes by instance, ordered by URI template.</summary>
	public IReadOnlyList<McpCatalogResource> InstanceResources
	{
		get;
	}

	/// <summary>Every resource and template with its routing, ordered by URI template.</summary>
	public IReadOnlyList<McpCatalogResource> Entries
	{
		get;
	}

	/// <summary>Builds the catalog in a validated, disposable container that never registers primitive types.</summary>
	/// <param name="manifest">The primitives a composition declared.</param>
	/// <returns>Detached copies of the protocol metadata.</returns>
	public static McpPrimitiveCatalog Create(CheatEngineMcpPrimitiveOptions manifest)
	{
		ArgumentNullException.ThrowIfNull(manifest);
		JsonSerializerOptions json = CheatEngineMcpJson.CreateOptions(manifest);
		ServiceCollection services = new();
		services.AddLogging();
		services.AddMcpServer().WithCheatEnginePrimitives(manifest, McpPrimitiveBinding.Catalog);
		using ServiceProvider provider =
			services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
		// Materializing the options runs the duplicate-identifier and contract validation.
		_ = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
		// The resources are kept only as URI matchers: a schema-only instance method refuses invocation, and a static
		// one needs no service, so they stay valid after the container is gone.
		return new McpPrimitiveCatalog(
			provider.GetServices<McpServerTool>().Select(tool => Detach(tool.ProtocolTool, json))
				.OrderBy(static tool => tool.Name, StringComparer.Ordinal).ToArray(),
			provider.GetServices<McpServerPrompt>().Select(prompt => Detach(prompt.ProtocolPrompt, json))
				.OrderBy(static prompt => prompt.Name, StringComparer.Ordinal).ToArray(),
			provider.GetServices<McpServerResource>().Select(resource => new McpCatalogResource(resource,
					Detach(resource.ProtocolResourceTemplate, json),
					resource.ProtocolResource is { } concrete ? Detach(concrete, json) : null,
					McpPrimitiveOrigin.Of(resource)?.Routing ?? McpPrimitiveRouting.Instance))
				.OrderBy(static entry => entry.Template.UriTemplate, StringComparer.Ordinal).ToArray());
	}

	// The protocol types are resolved through the strict composition options, so missing metadata fails at catalog build.
	private static T Detach<T>(T value, JsonSerializerOptions json)
	{
		JsonTypeInfo<T> typeInfo = json.GetTypeInfo<T>();
		return JsonSerializer.SerializeToElement(value, typeInfo).Deserialize(typeInfo)!;
	}
}
