using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Values;

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
	public async Task IndependentScan_HttpCallsAndReconnect_KeepSessionUntilExplicitResetOrServerShutdown()
	{
		HttpScanSessionProbe resetSession = new(new Address(0x1234), "initial");
		HttpScanSessionProbe shutdownSession = new(new Address(0x5678), "remaining");
		Queue<HttpScanSessionProbe> sessions = new([resetSession, shutdownSession]);
		IValueScanner scanner = ClientTestDouble.Create<IValueScanner>((method, _) => method.Name == nameof(IValueScanner.CreateSession) &&
			sessions.TryDequeue(out HttpScanSessionProbe? session)
			? session.Session
			: throw new Xunit.Sdk.XunitException($"Unexpected value-scanner call: {method.Name}."));
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		using PluginLog log = new PluginLog(directory);
		McpOptions options = new McpOptions { Port = FreePort() };
		McpServer server = new McpServer(ClientTestDouble.Client((nameof(ICheatEngineClient.ValueScans), scanner)), options, log);
		try
		{
			await server.StartAsync();
			await using (LiveMcpClient firstClient = await LiveMcpClient.ConnectAsync(options.BaseUrl, TestContext.Current.CancellationToken))
			{
				await AssertSuccessfulCallAsync(firstClient, "memory_scan", new Dictionary<string, object?>
				{
					["scannerName"] = "reset-me",
					["valueType"] = "int32",
					["value"] = "10"
				});
				JsonNode? results = await firstClient.CallToolAsync("get_memory_scan_results", new Dictionary<string, object?>
				{
					["scannerName"] = "reset-me"
				});
				Assert.True(results!["success"]!.GetValue<bool>());
				Assert.Equal("initial", results["results"]![0]!["value"]!.GetValue<string>());
			}

			Assert.Equal(0, resetSession.ReleaseCalls);
			await using (LiveMcpClient reconnectingClient = await LiveMcpClient.ConnectAsync(options.BaseUrl, TestContext.Current.CancellationToken))
			{
				await AssertSuccessfulCallAsync(reconnectingClient, "next_memory_scan", new Dictionary<string, object?>
				{
					["scannerName"] = "reset-me",
					["value"] = "11"
				});
				Assert.Equal(1, resetSession.NextCalls);
				Assert.Equal(0, resetSession.ReleaseCalls);

				await AssertSuccessfulCallAsync(reconnectingClient, "reset_memory_scan", new Dictionary<string, object?>
				{
					["scannerName"] = "reset-me"
				});
				Assert.Equal(1, resetSession.ReleaseCalls);

				await AssertSuccessfulCallAsync(reconnectingClient, "memory_scan", new Dictionary<string, object?>
				{
					["scannerName"] = "release-on-shutdown",
					["valueType"] = "int32",
					["value"] = "20"
				});
			}

			Assert.Equal(0, shutdownSession.ReleaseCalls);
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

		Assert.Equal(1, resetSession.ReleaseCalls);
		Assert.Equal(1, shutdownSession.ReleaseCalls);
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

	private static async Task AssertSuccessfulCallAsync(LiveMcpClient client, string name, IReadOnlyDictionary<string, object?> arguments)
	{
		JsonNode? result = await client.CallToolAsync(name, arguments);
		Assert.True(result?["success"]?.GetValue<bool>() ?? false, result?.ToJsonString());
	}

	private sealed class HttpScanSessionProbe
	{
		private ValueScanSessionState state = ValueScanSessionState.Created;

		public HttpScanSessionProbe(Address address, string value)
		{
			Address = address;
			Value = value;
			Session = ClientTestDouble.Create<IValueScanSession>(Handle);
		}

		public Address Address
		{
			get;
		}
		public int NextCalls
		{
			get; private set;
		}
		public int ReleaseCalls
		{
			get; private set;
		}
		public IValueScanSession Session
		{
			get;
		}
		public string Value
		{
			get;
		}

		private object? Handle(System.Reflection.MethodInfo method, object?[]? arguments) => method.Name switch
		{
			"get_State" => state,
			"FirstScan" => FirstScan(),
			"NextScan" => NextScan(),
			"GetResultCount" => 1UL,
			"Read" => new ValueScanPage(0, 1, ImmutableArray.Create(new ValueScanMatch(Address, Value))),
			"Release" => Release(),
			_ => throw new Xunit.Sdk.XunitException($"Unexpected scan-session call: {method.Name}.")
		};

		private object? FirstScan()
		{
			state = ValueScanSessionState.ResultsReady;
			return null;
		}

		private object? NextScan()
		{
			NextCalls++;
			return null;
		}

		private LeaseReleaseOutcome Release()
		{
			ReleaseCalls++;
			return new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed);
		}
	}
}
