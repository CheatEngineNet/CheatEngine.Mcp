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
		IReadOnlyList<ResourceTemplate> resources)
	{
		Tools = tools;
		Prompts = prompts;
		Resources = resources;
	}

	/// <summary>The tools, ordered by name.</summary>
	public IReadOnlyList<Tool> Tools
	{
		get;
	}

	/// <summary>The prompts, ordered by name.</summary>
	public IReadOnlyList<Prompt> Prompts
	{
		get;
	}

	/// <summary>The resources and resource templates, ordered by URI template.</summary>
	public IReadOnlyList<ResourceTemplate> Resources
	{
		get;
	}

	/// <summary>Builds the catalog in a validated, disposable container that never registers primitive types.</summary>
	/// <param name="manifest">The primitives a composition declared.</param>
	/// <param name="strictJson">
	///     Whether the serializer options keep only source-generated metadata, as the backend's must for the schemas to
	///     match; see <see cref="CheatEngineMcpJson.CreateOptions" />.
	/// </param>
	/// <returns>Detached copies of the protocol metadata.</returns>
	public static McpPrimitiveCatalog Create(CheatEngineMcpPrimitiveOptions manifest,
		bool strictJson = CheatEngineMcpJson.StrictByDefault)
	{
		ArgumentNullException.ThrowIfNull(manifest);
		ServiceCollection services = new();
		services.AddLogging();
		services.AddMcpServer().WithCheatEnginePrimitives(manifest, McpPrimitiveBinding.Catalog, strictJson);
		using ServiceProvider provider =
			services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
		// Materializing the options runs the duplicate-identifier and contract validation.
		_ = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
		return new McpPrimitiveCatalog(
			provider.GetServices<McpServerTool>().Select(static tool => Detach(tool.ProtocolTool))
				.OrderBy(static tool => tool.Name, StringComparer.Ordinal).ToArray(),
			provider.GetServices<McpServerPrompt>().Select(static prompt => Detach(prompt.ProtocolPrompt))
				.OrderBy(static prompt => prompt.Name, StringComparer.Ordinal).ToArray(),
			provider.GetServices<McpServerResource>()
				.Select(static resource => Detach(resource.ProtocolResourceTemplate))
				.OrderBy(static resource => resource.UriTemplate, StringComparer.Ordinal).ToArray());
	}

	// The protocol types are in the SDK's source-generated context, so no reflection-based overload is needed.
	private static T Detach<T>(T value)
	{
		JsonTypeInfo<T> typeInfo = McpJsonUtilities.DefaultOptions.GetTypeInfo<T>();
		return JsonSerializer.SerializeToElement(value, typeInfo).Deserialize(typeInfo)!;
	}
}
