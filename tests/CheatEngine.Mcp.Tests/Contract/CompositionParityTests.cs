using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Gateway;
using CheatEngine.Mcp.Prompts;
using CheatEngine.Mcp.Resources.Docs;
using CheatEngine.Mcp.Resources.Live;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>The plugin backend and the gateway must advertise one composition.</summary>
public sealed class CompositionParityTests
{
	private static readonly JsonSerializerOptions SnapshotOptions =
		new(McpJsonUtilities.DefaultOptions)
		{
			WriteIndented = true
		};

	[Fact]
	public void PluginAndGatewayExecutable_ComposeTheSamePrimitivesInOrder()
	{
		Assert.Equal(TestComposition.BackendManifest.Primitives, TestComposition.GatewayManifest.Primitives);
		// The tool set itself is pinned by the tools golden; each primitive type is declared once.
		Assert.Equal(TestComposition.BackendManifest.Primitives.Count,
			TestComposition.BackendManifest.Primitives.Distinct().Count());
		Assert.Equal(
		[
			typeof(CheatEngineDocResources), typeof(RuntimeLiveResources), typeof(ProcessLiveResources),
			typeof(ModuleLiveResources), typeof(MemoryLiveResources), typeof(RecordLiveResources),
			typeof(StructureLiveResources)
		], Types(CheatEngineMcpPrimitiveKind.Resource));
		Assert.Equal([typeof(CheatEngineWorkflowPrompts)], Types(CheatEngineMcpPrimitiveKind.Prompt));
	}

	[Fact]
	public void PluginAndGatewayExecutable_RegisterTheSameJsonResolvers()
	{
		// Schemas depend on the resolvers, so both compositions must register the same metadata in the same order.
		Assert.Equal(TestComposition.BackendManifest.JsonResolvers.Select(static resolver => resolver.GetType()),
			TestComposition.GatewayManifest.JsonResolvers.Select(static resolver => resolver.GetType()));
	}

	[Fact]
	public void GatewayExecutable_InstanceDirectoryArgument_IsBound()
	{
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		HostApplicationBuilder builder = GatewayProgram.CreateBuilder(["--instance-directory", directory]);
		using ServiceProvider provider = builder.Services.BuildServiceProvider();
		Assert.Equal(directory, provider.GetRequiredService<GatewayOptions>().InstanceDirectory);
	}

	[Fact]
	public void SchemaOnlyCatalog_MatchesTheLiveBackendListing()
	{
		GoldenFile.AssertMatches("backend-tools.json",
			JsonSerializer.Serialize(McpPrimitiveCatalog.Create(TestComposition.BackendManifest).Tools,
				SnapshotOptions));
	}

	[Fact]
	public void GatewayTools_AreTheBackendToolsWithOnlyTheRoutingArgumentAdded()
	{
		Dictionary<string, string> backend = McpPrimitiveCatalog.Create(TestComposition.BackendManifest).Tools
			.ToDictionary(static tool => tool.Name,
				static tool => JsonSerializer.Serialize(tool, McpJsonUtilities.DefaultOptions), StringComparer.Ordinal);
		Tool[] routed = TestComposition.GatewayTools
			.Where(static tool => tool.Name != GatewayToolCatalog.InstanceListToolName).ToArray();

		Assert.Equal(backend.Keys.Order(StringComparer.Ordinal),
			routed.Select(static tool => tool.Name).Order(StringComparer.Ordinal));
		foreach (Tool tool in routed)
		{
			JsonObject gateway = JsonSerializer.SerializeToNode(tool, McpJsonUtilities.DefaultOptions)!.AsObject();
			JsonObject schema = gateway["inputSchema"]!.AsObject();
			Assert.True(schema["properties"]!.AsObject().Remove(GatewayToolCatalog.InstanceIdArgumentName));
			JsonArray required = schema["required"]!.AsArray();
			JsonNode routing = Assert.Single(required,
				static value => value!.GetValue<string>() == GatewayToolCatalog.InstanceIdArgumentName)!;
			required.Remove(routing);
			if (required.Count == 0)
			{
				schema.Remove("required");
			}

			Assert.True(JsonNode.DeepEquals(JsonNode.Parse(backend[tool.Name]), gateway),
				$"Gateway schema drifted for {tool.Name}.");
		}
	}

	[Fact]
	public void ComposedResourcesAndPrompts_AreLocalOrRoutedByTheGateway()
	{
		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(TestComposition.GatewayManifest);
		IReadOnlyList<ResourceTemplate> routed = new GatewayResourceCatalog(Primitives(TestComposition.GatewayManifest))
			.RoutedTemplates;

		// Every resource is either served locally (docs) or routed by instance; every prompt is Local.
		Assert.All(catalog.LocalResources,
			static entry => Assert.True(McpResourceUris.IsDocs(entry.Template.UriTemplate)));
		Assert.All(catalog.InstanceResources, static entry =>
			Assert.True(McpResourceUris.IsInstance(entry.Template.UriTemplate)));
		Assert.Equal(catalog.LocalResources.Count + catalog.InstanceResources.Count, catalog.Entries.Count);
		Assert.Equal(catalog.InstanceResources.Count, routed.Count);
		foreach ((McpCatalogResource backend, ResourceTemplate gateway) in catalog.InstanceResources.Zip(routed))
		{
			JsonObject reversed = JsonSerializer.SerializeToNode(gateway, McpJsonUtilities.DefaultOptions)!.AsObject();
			Assert.True(McpResourceUris.TryParseGateway(
				gateway.UriTemplate.Replace("{instanceId}", "ce-1-0123456789abcdef0123456789abcdef",
					StringComparison.Ordinal), out _, out string? backendTemplate));
			reversed["uriTemplate"] = backendTemplate;
			Assert.True(JsonNode.DeepEquals(
				JsonSerializer.SerializeToNode(backend.Template, McpJsonUtilities.DefaultOptions), reversed));
		}
	}

	[Fact]
	public void GatewayLocalPrimitives_EqualTheBackendsResourcesAndPrompts()
	{
		McpPrimitiveCatalog backend = McpPrimitiveCatalog.Create(TestComposition.BackendManifest);
		McpServerOptions gateway = new();
		using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
		McpLocalPrimitives.AddTo(gateway, TestComposition.GatewayManifest, services);

		Assert.Equal(
			Json(backend.LocalResources.Select(static entry => (object?) entry.Resource ?? entry.Template)),
			Json(gateway.ResourceCollection!.OrderBy(static resource => resource.ProtocolResourceTemplate.UriTemplate,
					StringComparer.Ordinal)
				.Select(static resource => (object?) resource.ProtocolResource ?? resource.ProtocolResourceTemplate)));
		Assert.Equal(Json(backend.Prompts),
			Json(gateway.PromptCollection!.Select(static prompt => prompt.ProtocolPrompt)
				.OrderBy(static prompt => prompt.Name, StringComparer.Ordinal)));
	}

	private static Type[] Types(CheatEngineMcpPrimitiveKind kind)
	{
		return TestComposition.BackendManifest.Primitives.Where(primitive => primitive.Kind == kind)
			.Select(static primitive => primitive.Type).ToArray();
	}

	private static GatewayPrimitiveCatalog Primitives(CheatEngineMcpPrimitiveOptions manifest)
	{
		return new GatewayPrimitiveCatalog(Options.Create(manifest));
	}

	private static string Json<T>(IEnumerable<T> values)
	{
		return string.Join('\n', values.Select(static value => value switch
		{
			Resource resource => JsonSerializer.Serialize(resource, McpJsonUtilities.DefaultOptions),
			ResourceTemplate template => JsonSerializer.Serialize(template, McpJsonUtilities.DefaultOptions),
			Prompt prompt => JsonSerializer.Serialize(prompt, McpJsonUtilities.DefaultOptions),
			_ => throw new InvalidOperationException($"Unexpected {value?.GetType().Name}.")
		}));
	}
}
