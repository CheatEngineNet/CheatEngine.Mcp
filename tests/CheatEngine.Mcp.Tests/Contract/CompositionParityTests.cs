using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Gateway;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>The plugin backend and the gateway must advertise one composition.</summary>
public sealed class CompositionParityTests
{
	private static readonly JsonSerializerOptions SnapshotOptions =
		new(McpJsonUtilities.DefaultOptions) { WriteIndented = true };

	[Fact]
	public void PluginAndGatewayExecutable_ComposeTheSamePrimitivesInOrder()
	{
		Assert.Equal(TestComposition.BackendManifest.Primitives, TestComposition.GatewayManifest.Primitives);
		Assert.Equal(28, TestComposition.BackendManifest.Primitives.Count);
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
			.Where(static tool => tool.Name != GatewayToolCatalog.ListInstancesToolName).ToArray();

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
	public void ComposedPrimitives_AreAllRoutedByTheGateway()
	{
		// The gateway forwards tools only. Resources or prompts need a routing design before they are composed.
		Assert.All(TestComposition.GatewayManifest.Primitives,
			static primitive => Assert.Equal(CheatEngineMcpPrimitiveKind.Tool, primitive.Kind));
	}
}
