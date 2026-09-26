using System.Net;
using System.Net.Sockets;

namespace CheatEngine.Mcp.Tests;

[Collection(nameof(SerialTestGroup))]
public sealed class McpServerTests
{
	[Fact]
	public async Task Start_InvalidConfiguration_ReleasesTheLoggingLease()
	{
		using PluginLog log = new PluginLog(Path.GetTempPath());
		McpServer server = new McpServer(ClientTestDouble.Client(), new McpOptions { Port = -1 }, log);
		await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync());
		Assert.False(server.IsRunning);
		log.Dispose();
		Assert.Throws<ObjectDisposedException>(() => log.Acquire());
		Assert.True(ReferenceEquals(server.StopAsync(), server.StopAsync()), "Repeated shutdown must share one task.");
	}

	[Fact]
	public async Task Start_ToolDiscoveryAndDisabledLua_WorkOverRealHttp()
	{
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		using PluginLog log = new PluginLog(directory);
		McpOptions options = new McpOptions { Port = FreePort() };
		McpServer server = new McpServer(ClientTestDouble.Client(), options, log);
		try
		{
			await server.StartAsync();
			Assert.True(server.IsRunning);
			await using LiveMcpClient client = await LiveMcpClient.ConnectAsync(options.BaseUrl, TestContext.Current.CancellationToken);
			string[] actual = (await client.ListToolsAsync()).Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray();
			Assert.Equal(ToolContractTests.GetToolNames().ToArray(), actual);
			System.Text.Json.Nodes.JsonNode? result = await client.CallToolAsync("execute_lua",
				new Dictionary<string, object?> { ["script"] = "error('must not run')" });
			Assert.False(result!["success"]!.GetValue<bool>());
			Assert.Contains("disabled", result["error"]!.GetValue<string>());
			server.StopAccepting();
			using HttpClient http = new HttpClient();
			using HttpResponseMessage response = await http.GetAsync(options.BaseUrl, TestContext.Current.CancellationToken);
			Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
		}
		finally
		{
			await server.StopAsync();
			log.Dispose();
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
		Assert.False(server.IsRunning);
	}

	[Fact]
	public async Task Start_EmptyNativeHostBaseDirectory_BindsConfiguredEndpoint()
	{
		const string baseDirectoryKey = "APP_CONTEXT_BASE_DIRECTORY";
		object? originalBaseDirectory = AppContext.GetData(baseDirectoryKey);
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		PluginLog? log = null;
		McpServer? server = null;
		try
		{
			AppContext.SetData(baseDirectoryKey, string.Empty);
			Assert.Equal(string.Empty, AppContext.BaseDirectory);

			log = new PluginLog(directory);
			McpOptions options = new McpOptions { Port = FreePort() };
			server = new McpServer(ClientTestDouble.Client(), options, log);
			await server.StartAsync();
			await using LiveMcpClient client = await LiveMcpClient.ConnectAsync(options.BaseUrl, TestContext.Current.CancellationToken);
			string[] actual = (await client.ListToolsAsync()).Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray();
			Assert.Equal(ToolContractTests.GetToolNames().ToArray(), actual);
		}
		finally
		{
			try
			{
				if (server is not null)
				{
					await server.StopAsync();
				}
			}
			finally
			{
				try
				{
					log?.Dispose();
				}
				finally
				{
					try
					{
						AppContext.SetData(baseDirectoryKey, originalBaseDirectory);
					}
					finally
					{
						if (Directory.Exists(directory))
						{
							Directory.Delete(directory, recursive: true);
						}
					}
				}
			}
		}
	}

	[Fact]
	public async Task Start_OccupiedPort_FailsAndDoesNotReportRunning()
	{
		using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		McpOptions options = new McpOptions { Port = ((IPEndPoint) listener.LocalEndpoint).Port };
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		using PluginLog log = new PluginLog(directory);
		McpServer server = new McpServer(ClientTestDouble.Client(), options, log);
		try
		{
			await Assert.ThrowsAsync<IOException>(() => server.StartAsync());
			Assert.False(server.IsRunning);
		}
		finally
		{
			await server.StopAsync();
			log.Dispose();
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
	}

	private static int FreePort()
	{
		using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		return ((IPEndPoint) listener.LocalEndpoint).Port;
	}
}
