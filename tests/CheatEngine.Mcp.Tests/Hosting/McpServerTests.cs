using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json.Nodes;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Plugin;
using CheatEngine.Mcp.Tests.Contract;
using CheatEngine.Mcp.Tests.LiveQualification;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Hosting;

[Collection(nameof(SerialTestGroup))]
public sealed class McpServerTests
{
	[Fact]
	public async Task Start_InvalidConfiguration_ReleasesTheLoggingLease()
	{
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		using TestActivation activation = new(ClientTestDouble.Client());
		using PluginLog log = new(directory);
		McpBackendHost server = CreateHost(activation, new McpBackendOptions { Port = -1 }, log);
		// The log's own reference and the backend's provider, which is the backend's lease.
		Assert.Equal(2, log.Sink.References);
		OptionsValidationException failure =
			await Assert.ThrowsAsync<OptionsValidationException>(() => server.StartAsync());
		Assert.Contains("Mcp:Port must be between 0 and 65535 (0 selects a free port).", failure.Message,
			StringComparison.Ordinal);
		Assert.False(server.IsRunning);
		Assert.Equal(1, log.Sink.References);
		await TestLog.ReleaseAsync(log);
		Assert.Equal(0, log.Sink.References);
		Assert.Throws<ObjectDisposedException>(() => log.CreateProvider());
		Assert.True(ReferenceEquals(server.StopAsync(), server.StopAsync()), "Repeated shutdown must share one task.");
		Assert.Equal(0, log.Sink.References);
		if (Directory.Exists(directory))
		{
			Directory.Delete(directory, true);
		}
	}

	[Fact]
	public async Task Start_ToolDiscoveryAndDisabledLua_WorkOverRealHttp()
	{
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		using PluginLog log = new(directory);
		McpBackendOptions options = new()
		{
			Port = FreePort()
		};
		using TestActivation activation = new(ClientTestDouble.Client(),
			new Dictionary<string, string?> { ["Mcp:EnableUnsafeLua"] = "false" });
		McpBackendHost server = CreateHost(activation, options, log);
		try
		{
			await server.StartAsync();
			Assert.True(server.IsRunning);
			await using LiveMcpClient client =
				await LiveMcpClient.ConnectAsync(options.BaseUrl, TestContext.Current.CancellationToken);
			string[] actual = (await client.ListToolsAsync()).Select(tool => tool.Name).Order(StringComparer.Ordinal)
				.ToArray();
			Assert.Equal(ToolContractTests.GetToolNames().ToArray(), actual);
			InvalidOperationException disabled = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
				await client.CallToolAsync("lua_execute",
					new Dictionary<string, object?> { ["source"] = "error('must not run')" }));
			Assert.Contains("capability_disabled", disabled.Message, StringComparison.Ordinal);
			server.StopAccepting();
			using HttpClient http = new();
			using HttpResponseMessage response =
				await http.GetAsync(options.BaseUrl, TestContext.Current.CancellationToken);
			Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
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

		Assert.False(server.IsRunning);
	}

	[Fact]
	public async Task Start_EmptyNativeHostBaseDirectory_BindsConfiguredEndpoint()
	{
		const string baseDirectoryKey = "APP_CONTEXT_BASE_DIRECTORY";
		object? originalBaseDirectory = AppContext.GetData(baseDirectoryKey);
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		using TestActivation activation = new(ClientTestDouble.Client());
		PluginLog? log = null;
		McpBackendHost? server = null;
		try
		{
			AppContext.SetData(baseDirectoryKey, string.Empty);
			Assert.Equal(string.Empty, AppContext.BaseDirectory);

			log = new PluginLog(directory);
			McpBackendOptions options = new()
			{
				Port = FreePort()
			};
			server = CreateHost(activation, options, log);
			await server.StartAsync();
			await using LiveMcpClient client =
				await LiveMcpClient.ConnectAsync(options.BaseUrl, TestContext.Current.CancellationToken);
			string[] actual = (await client.ListToolsAsync()).Select(tool => tool.Name).Order(StringComparer.Ordinal)
				.ToArray();
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
					if (log is not null)
					{
						await TestLog.ReleaseAsync(log);
					}
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
							Directory.Delete(directory, true);
						}
					}
				}
			}
		}
	}

	[Fact]
	public async Task Start_AmbientHostingSettings_CannotRebindOrReconfigureTheBackend()
	{
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		int port = FreePort();
		int kestrelPort = FreePort();
		int urlsPort = FreePort();
		// What a default web builder would honour: the content root's appsettings.json and process variables.
		File.WriteAllText(Path.Combine(directory, "appsettings.json"),
			new JsonObject
			{
				["Kestrel"] = new JsonObject
				{
					["Endpoints"] = new JsonObject
					{
						["File"] = new JsonObject { ["Url"] = $"http://127.0.0.1:{kestrelPort}" }
					}
				},
				["urls"] = $"http://127.0.0.1:{urlsPort}"
			}.ToJsonString());
		Dictionary<string, string?> ambient = new()
		{
			["ASPNETCORE_ENVIRONMENT"] = "Development",
			["DOTNET_ENVIRONMENT"] = "Development",
			["ASPNETCORE_URLS"] = $"http://127.0.0.1:{urlsPort}",
			["Kestrel__Endpoints__Ambient__Url"] = $"http://127.0.0.1:{kestrelPort}"
		};
		Dictionary<string, string?> original = ambient.Keys.ToDictionary(static key => key,
			static key => Environment.GetEnvironmentVariable(key));
		McpRuntimeInfo runtime = TestRuntime.Info with
		{
			RuntimeLocation = Path.Combine(directory, "CheatEngine.Mcp.Plugin.dll")
		};
		McpBackendOptions options = new()
		{
			Port = port
		};
		using TestActivation activation = new(ClientTestDouble.Client());
		PluginLog log = new(directory);
		McpBackendHost server = new(options, log, runtime, activation.Manifest, activation.Targets,
			CancellationToken.None);
		try
		{
			foreach ((string key, string? value) in ambient)
			{
				Environment.SetEnvironmentVariable(key, value);
			}

			await server.StartAsync();

			Assert.Equal(options.BaseUrl, server.Endpoint);
			IServiceProvider services = server.Services!;
			Assert.Equal(Environments.Production, services.GetRequiredService<IHostEnvironment>().EnvironmentName);
			Assert.IsType<ActivationHostLifetime>(services.GetRequiredService<IHostLifetime>());
			Assert.False(await IsListeningAsync(kestrelPort), "Kestrel configuration must not add an endpoint.");
			Assert.False(await IsListeningAsync(urlsPort), "Ambient URLs must not add an endpoint.");
		}
		finally
		{
			foreach ((string key, string? value) in original)
			{
				Environment.SetEnvironmentVariable(key, value);
			}

			await server.StopAsync();
			await TestLog.ReleaseAsync(log);
			Directory.Delete(directory, true);
		}
	}

	[Fact]
	public async Task IndependentScan_HttpCallsAndReconnect_KeepSessionUntilExplicitResetOrActivationEnd()
	{
		HttpScanSessionProbe resetSession = new(new Address(0x1234), "initial");
		HttpScanSessionProbe shutdownSession = new(new Address(0x5678), "remaining");
		Queue<HttpScanSessionProbe> sessions = new([resetSession, shutdownSession]);
		IValueScanner scanner = ClientTestDouble.Create<IValueScanner>((method, _) =>
			method.Name == nameof(IValueScanner.CreateSession) &&
			sessions.TryDequeue(out HttpScanSessionProbe? session)
				? session.Session
				: throw new XunitException($"Unexpected value-scanner call: {method.Name}."));
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		using PluginLog log = new(directory);
		McpBackendOptions options = new()
		{
			Port = FreePort()
		};
		using TestActivation activation =
			new(ClientTestDouble.Client((nameof(ICheatEngineClient.ValueScans), scanner)));
		McpBackendHost server = CreateHost(activation, options, log);
		try
		{
			await server.StartAsync();
			await using (LiveMcpClient firstClient =
						 await LiveMcpClient.ConnectAsync(options.BaseUrl, TestContext.Current.CancellationToken))
			{
				await AssertSuccessfulCallAsync(firstClient, "scan_first",
					new Dictionary<string, object?>
					{
						["scannerName"] = "reset-me",
						["valueType"] = "int32",
						["value"] = "10"
					});
				JsonObject results = Assert.IsType<JsonObject>(await firstClient.CallToolAsync("scan_list_results",
					new Dictionary<string, object?> { ["scannerName"] = "reset-me" }));
				Assert.Equal("initial", results["results"]![0]!["value"]!.GetValue<string>());
			}

			Assert.Equal(0, resetSession.ReleaseCalls);
			await using (LiveMcpClient reconnectingClient =
						 await LiveMcpClient.ConnectAsync(options.BaseUrl, TestContext.Current.CancellationToken))
			{
				await AssertSuccessfulCallAsync(reconnectingClient, "scan_next",
					new Dictionary<string, object?> { ["scannerName"] = "reset-me", ["value"] = "11" });
				Assert.Equal(1, resetSession.NextCalls);
				Assert.Equal(0, resetSession.ReleaseCalls);

				await AssertSuccessfulCallAsync(reconnectingClient, "scan_reset",
					new Dictionary<string, object?> { ["scannerName"] = "reset-me" });
				Assert.Equal(1, resetSession.ReleaseCalls);

				await AssertSuccessfulCallAsync(reconnectingClient, "scan_first",
					new Dictionary<string, object?>
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
			await TestLog.ReleaseAsync(log);
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, true);
			}
		}

		// The transport never owns tools: stopping it keeps the session until the activation scope ends.
		Assert.Equal(1, resetSession.ReleaseCalls);
		Assert.Equal(0, shutdownSession.ReleaseCalls);
		activation.DisposeScope();
		Assert.Equal(1, shutdownSession.ReleaseCalls);
	}

	[Fact]
	public async Task Start_OccupiedPort_FailsAndDoesNotReportRunning()
	{
		using TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		McpBackendOptions options = new()
		{
			Port = ((IPEndPoint) listener.LocalEndpoint).Port
		};
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		using PluginLog log = new(directory);
		using TestActivation activation = new(ClientTestDouble.Client());
		McpBackendHost server = CreateHost(activation, options, log);
		try
		{
			await Assert.ThrowsAsync<IOException>(() => server.StartAsync());
			Assert.False(server.IsRunning);
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

	private static McpBackendHost CreateHost(TestActivation activation, McpBackendOptions options, PluginLog log)
	{
		return new McpBackendHost(options, log, TestRuntime.Info, activation.Manifest, activation.Targets,
			CancellationToken.None);
	}

	private static async Task<bool> IsListeningAsync(int port)
	{
		using TcpClient client = new();
		try
		{
			await client.ConnectAsync(IPAddress.Loopback, port, TestContext.Current.CancellationToken);
			return true;
		}
		catch (SocketException)
		{
			return false;
		}
	}

	private static int FreePort()
	{
		using TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		return ((IPEndPoint) listener.LocalEndpoint).Port;
	}

	private static async Task AssertSuccessfulCallAsync(LiveMcpClient client, string name,
		IReadOnlyDictionary<string, object?> arguments)
	{
		JsonNode? result = await client.CallToolAsync(name, arguments);
		Assert.NotNull(result);
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
			get;
			private set;
		}

		public int ReleaseCalls
		{
			get;
			private set;
		}

		public IValueScanSession Session
		{
			get;
		}

		public string Value
		{
			get;
		}

		private object? Handle(MethodInfo method, object?[]? arguments)
		{
			return method.Name switch
			{
				"get_State" => state,
				"FirstScan" => FirstScan(),
				"NextScan" => NextScan(),
				"GetResultCount" => 1UL,
				"Read" => new ValueScanPage(0, 1, ImmutableArray.Create(new ValueScanMatch(Address, Value))),
				"Release" => Release(),
				_ => throw new XunitException($"Unexpected scan-session call: {method.Name}.")
			};
		}

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
