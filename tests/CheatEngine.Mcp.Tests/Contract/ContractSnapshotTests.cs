using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Plugin;
using CheatEngine.Mcp.Tests.LiveQualification;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>Pins the externally observable MCP contract so the architecture refactor cannot change it silently.</summary>
[Collection(nameof(SerialTestGroup))]
public sealed class ContractSnapshotTests
{
	private static readonly JsonSerializerOptions SnapshotOptions =
		new(McpJsonUtilities.DefaultOptions) { WriteIndented = true };

	[Fact]
	public void GatewayCatalog_CurrentBuild_MatchesGoldenSnapshot()
	{
		GoldenFile.AssertMatches("gateway-tools.json", SerializeTools(TestComposition.GatewayTools));
	}

	[Fact]
	public async Task Backend_ToolsAndInitialize_MatchGoldenSnapshots()
	{
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		using TestActivation activation = new(ClientTestDouble.Client());
		using PluginLog log = new(directory);
		McpBackendHost server = new(new McpBackendOptions(), log, TestRuntime.Info, activation.Manifest,
			activation.Targets,
			CancellationToken.None);
		try
		{
			await server.StartAsync();
			await using McpClient client = await McpClient.CreateAsync(
				new HttpClientTransport(new HttpClientTransportOptions
				{
					Endpoint = new Uri(server.Endpoint!),
					Name = "CheatEngine.Mcp.ContractTests",
					TransportMode = HttpTransportMode.StreamableHttp,
					ConnectionTimeout = TimeSpan.FromSeconds(10)
				}), CreateClientOptions(), cancellationToken: TestContext.Current.CancellationToken);

			IList<McpClientTool> tools =
				await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
			GoldenFile.AssertMatches("backend-tools.json",
				SerializeTools(tools.Select(static tool => tool.ProtocolTool)));
			GoldenFile.AssertMatches("backend-initialize.json", SerializeInitialize(client));
		}
		finally
		{
			await server.StopAsync();
			log.Dispose();
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, true);
			}
		}
	}

	[Fact]
	public async Task GatewayExecutable_StdioWithoutInstances_MatchesGoldenSnapshots()
	{
		string executable = Path.Combine(AppContext.BaseDirectory, "CheatEngine.Mcp.Gateway.exe");
		Assert.True(File.Exists(executable), $"The gateway executable is missing: {executable}");
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		try
		{
			await using McpClient client = await McpClient.CreateAsync(
				new StdioClientTransport(new StdioClientTransportOptions
				{
					Command = executable,
					Arguments = ["--instance-directory", Path.Combine(directory, "instances")],
					Name = "CheatEngine.Mcp.ContractTests.Gateway",
					WorkingDirectory = directory,
					ShutdownTimeout = TimeSpan.FromSeconds(10)
				}), CreateClientOptions(), cancellationToken: TestContext.Current.CancellationToken);

			IList<McpClientTool> tools =
				await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
			GoldenFile.AssertMatches("gateway-tools.json",
				SerializeTools(tools.Select(static tool => tool.ProtocolTool)));
			GoldenFile.AssertMatches("gateway-initialize.json", SerializeInitialize(client));
		}
		finally
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, true);
			}
		}
	}

	[Fact]
	public void PluginOutput_StagedDeploymentFiles_MatchGoldenManifest()
	{
		// PrepareCheatEnginePluginDeployment copies exactly the plugin output's top-level *.dll, *.json and *.pdb files.
		string output = Path.GetDirectoryName(typeof(CheatEngineMcpPlugin).Assembly.Location)!;
		string pluginOutput = Path.Combine(RepositoryPaths.Root, "artifacts", "bin", "CheatEngine.Mcp.Plugin",
			Path.GetFileName(output));
		Assert.True(Directory.Exists(pluginOutput), $"The plugin build output is missing: {pluginOutput}");
		string[] files = Directory.EnumerateFiles(pluginOutput)
			.Select(static path => Path.GetFileName(path))
			.Where(static name => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
			                      || name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
			                      name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
			.Order(StringComparer.Ordinal).ToArray();
		GoldenFile.AssertMatches("plugin-files.txt", string.Join('\n', files));
	}

	[Fact]
	public void GetPluginVersion_Result_KeepsDocumentedShape()
	{
		JsonObject result =
			JsonSerializer.SerializeToNode(
				new ProcessTool(ClientTestDouble.Client(), TestRuntime.Info).GetPluginVersion())!.AsObject();
		Assert.Equal(["location", "runtimeLocation", "success", "version"],
			result.Select(static property => property.Key).Order(StringComparer.Ordinal));
		Assert.True(result["success"]!.GetValue<bool>());
		Assert.Equal("CheatEngine.Mcp.Plugin.dll", Path.GetFileName(result["runtimeLocation"]!.GetValue<string>()));
		Assert.False(string.IsNullOrWhiteSpace(result["version"]!.GetValue<string>()));
		Assert.False(string.IsNullOrWhiteSpace(result["location"]!.GetValue<string>()));
	}

	private static McpClientOptions CreateClientOptions()
	{
		return new McpClientOptions
		{
			ClientInfo = new Implementation { Name = "CheatEngine.Mcp.ContractTests", Version = "2.0.0" },
			Capabilities = new ClientCapabilities(),
			ProtocolVersion = LiveMcpClient.ProtocolVersion
		};
	}

	private static string SerializeTools(IEnumerable<Tool> tools)
	{
		return JsonSerializer.Serialize(tools.OrderBy(static tool => tool.Name, StringComparer.Ordinal).ToArray(),
			SnapshotOptions);
	}

	private static string SerializeInitialize(McpClient client)
	{
		return JsonSerializer.Serialize(
			new JsonObject
			{
				["protocolVersion"] = client.NegotiatedProtocolVersion,
				["serverInfo"] = JsonSerializer.SerializeToNode(client.ServerInfo, McpJsonUtilities.DefaultOptions),
				["capabilities"] =
					JsonSerializer.SerializeToNode(client.ServerCapabilities, McpJsonUtilities.DefaultOptions),
				["instructions"] = client.ServerInstructions
			}, SnapshotOptions);
	}
}
