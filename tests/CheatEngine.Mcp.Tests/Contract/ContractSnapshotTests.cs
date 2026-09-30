using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Plugin;
using CheatEngine.Mcp.Tests.LiveQualification;
using CheatEngine.Mcp.Tests.Support;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>Pins the externally observable MCP contract so the architecture refactor cannot change it silently.</summary>
[Collection(nameof(SerialTestGroup))]
public sealed class ContractSnapshotTests
{
	private static readonly JsonSerializerOptions SnapshotOptions =
		new(McpJsonUtilities.DefaultOptions)
		{
			WriteIndented = true
		};

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
			GoldenFile.AssertMatches("backend-resources.json", await SerializeResourcesAsync(client));
			GoldenFile.AssertMatches("backend-prompts.json", await SerializePromptsAsync(client));
		}
		finally
		{
			await server.StopAsync();
			await TestLog.ReleaseAsync(log);
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, true);
			}
		}
	}

	/// <summary>
	///     The gateway pins its backend hop to 2025-06-18. This guard keeps a later switch to 2026-07-28 a one-line change:
	///     the backend already lists exactly the same tools to both versions.
	/// </summary>
	[Fact]
	public async Task Backend_ToolsList_IsIdenticalFor2025And2026Clients()
	{
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		using TestActivation activation = new(ClientTestDouble.Client());
		using PluginLog log = new(directory);
		McpBackendHost server = new(new McpBackendOptions(), log, TestRuntime.Info, activation.Manifest,
			activation.Targets, CancellationToken.None);
		try
		{
			await server.StartAsync();
			string june2025 = await ListBackendToolsAsync(server.Endpoint!, "2025-06-18");
			string july2026 = await ListBackendToolsAsync(server.Endpoint!, "2026-07-28");

			Assert.Equal(june2025, july2026);
		}
		finally
		{
			await server.StopAsync();
			await TestLog.ReleaseAsync(log);
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
					// Outside the deleted tree: a gateway still exiting holds its working directory.
					WorkingDirectory = AppContext.BaseDirectory,
					ShutdownTimeout = TimeSpan.FromSeconds(10)
				}), CreateClientOptions(), cancellationToken: TestContext.Current.CancellationToken);

			IList<McpClientTool> tools =
				await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
			GoldenFile.AssertMatches("gateway-tools.json",
				SerializeTools(tools.Select(static tool => tool.ProtocolTool)));
			GoldenFile.AssertMatches("gateway-initialize.json", SerializeInitialize(client));
			GoldenFile.AssertMatches("gateway-resources.json", await SerializeResourcesAsync(client));
			GoldenFile.AssertMatches("gateway-prompts.json", await SerializePromptsAsync(client));

			// The executable serves the embedded knowledge itself: a document, a workflow body and a rendered prompt.
			ReadResourceResult safety = await client.ReadResourceAsync("cheatengine://docs/safety",
				cancellationToken: TestContext.Current.CancellationToken);
			Assert.StartsWith("# Safety and responsible use",
				Assert.IsType<TextResourceContents>(Assert.Single(safety.Contents)).Text, StringComparison.Ordinal);
			ReadResourceResult body = await client.ReadResourceAsync("cheatengine://docs/workflows/nop-patch",
				cancellationToken: TestContext.Current.CancellationToken);
			Assert.StartsWith("# Replace code with NOPs",
				Assert.IsType<TextResourceContents>(Assert.Single(body.Contents)).Text, StringComparison.Ordinal);
			GetPromptResult prompt = await client.GetPromptAsync("speedhack",
				new Dictionary<string, object?> { ["speed"] = "0.5" },
				cancellationToken: TestContext.Current.CancellationToken);
			Assert.Contains("`0.5`", Assert.IsType<TextContentBlock>(prompt.Messages[0].Content).Text,
				StringComparison.Ordinal);
		}
		finally
		{
			// The gateway may still hold a registry file while it exits.
			await TestDirectory.DeleteAsync(directory);
		}
	}

	[Fact]
	public void PluginOutput_BundledDeploymentFiles_MatchGoldenManifest()
	{
		// Publish installs only the woven DLL; build-only component manifests are not deployment dependencies.
		// Build outputs can retain compiler manifests and copy-local files. Publish installs only this bundled assembly;
		// PluginBundleTests verifies its dependency closure, and ReleaseScriptTests verifies the complete ZIP layout.
		string file = Path.GetFileName(typeof(CheatEngineMcpPlugin).Assembly.Location);
		GoldenFile.AssertMatches("plugin-files.txt", file);
	}

	private static async Task<string> ListBackendToolsAsync(string endpoint, string protocolVersion)
	{
		await using McpClient client = await McpClient.CreateAsync(
			new HttpClientTransport(new HttpClientTransportOptions
			{
				Endpoint = new Uri(endpoint),
				Name = "CheatEngine.Mcp.ContractTests",
				TransportMode = HttpTransportMode.StreamableHttp,
				ConnectionTimeout = TimeSpan.FromSeconds(10)
			}),
			new McpClientOptions
			{
				ClientInfo = new Implementation { Name = "CheatEngine.Mcp.ContractTests", Version = "2.0.0" },
				Capabilities = new ClientCapabilities(),
				ProtocolVersion = protocolVersion
			}, cancellationToken: TestContext.Current.CancellationToken);
		Assert.Equal(protocolVersion, client.NegotiatedProtocolVersion);
		IList<McpClientTool> tools =
			await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
		return SerializeTools(tools.Select(static tool => tool.ProtocolTool));
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

	/// <summary>
	///     The resources and resource templates of one host, ordered by URI: the SDK lists its collections in hash order,
	///     which differs between processes.
	/// </summary>
	private static async Task<string> SerializeResourcesAsync(McpClient client)
	{
		IList<McpClientResource> resources =
			await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
		IList<McpClientResourceTemplate> templates =
			await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);
		return JsonSerializer.Serialize(new JsonObject
		{
			["resources"] = JsonSerializer.SerializeToNode(
				resources.Select(static resource => resource.ProtocolResource)
					.OrderBy(static resource => resource.Uri, StringComparer.Ordinal).ToArray(), SnapshotOptions),
			["resourceTemplates"] = JsonSerializer.SerializeToNode(
				templates.Select(static template => template.ProtocolResourceTemplate)
					.OrderBy(static template => template.UriTemplate, StringComparer.Ordinal).ToArray(),
				SnapshotOptions)
		}, SnapshotOptions);
	}

	private static async Task<string> SerializePromptsAsync(McpClient client)
	{
		IList<McpClientPrompt> prompts =
			await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);
		return JsonSerializer.Serialize(prompts.Select(static prompt => prompt.ProtocolPrompt)
			.OrderBy(static prompt => prompt.Name, StringComparer.Ordinal).ToArray(), SnapshotOptions);
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
