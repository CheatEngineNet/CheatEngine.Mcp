using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

using CheatEngine.Client;
using CheatEngine.Client.Dispatching;
using CheatEngine.Mcp.Plugin;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Plugin;

[Collection(nameof(SerialTestGroup))]
public sealed class McpModuleLifecycleTests
{
	private static readonly string[] AllowedClientMembers = ["get_Stopping", "get_Dispatcher", "get_IsMainThread"];

	[Fact]
	public async Task EnableDisable_StrictClient_PublishesWithdrawsAndNeverDispatches()
	{
		string root = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		List<string> accessed = [];
		ICheatEngineClient client = StrictClient(accessed);
		string instances = Path.Combine(root, "instances");
		using TestActivation activation = new(client,
			new Dictionary<string, string?>
			{
				["Mcp:InstanceName"] = "lifecycle",
				["Mcp:InstanceDirectory"] = instances
			});
		McpServerModule module = activation.Module;
		InstanceRegistry registry = new(instances);
		using HttpClient http = new();
		try
		{
			module.OnEnabled(client);
			InstanceDescriptor published = Assert.Single(registry.ReadActive(TestContext.Current.CancellationToken));
			Uri identity = new(new Uri(published.Endpoint), "instance");
			using (HttpResponseMessage anonymous = await http.GetAsync(identity, TestContext.Current.CancellationToken))
			{
				Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
			}

			using (HttpRequestMessage request = new(HttpMethod.Get, identity))
			{
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", published.AccessToken);
				using HttpResponseMessage authorized =
					await http.SendAsync(request, TestContext.Current.CancellationToken);
				Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
				JsonNode body =
					JsonNode.Parse(await authorized.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
				Assert.Equal(published.InstanceId, body["instanceId"]!.GetValue<string>());
			}

			module.OnDisabling(client);
			Assert.Empty(registry.ReadActive(TestContext.Current.CancellationToken));
			using (HttpResponseMessage stopping =
				   await http.GetAsync(published.Endpoint, TestContext.Current.CancellationToken))
			{
				Assert.Equal(HttpStatusCode.ServiceUnavailable, stopping.StatusCode);
			}

			Stopwatch elapsed = Stopwatch.StartNew();
			module.Dispose();
			Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1),
				"Module disposal must not wait for the HTTP shutdown.");
			await WaitUntilStoppedAsync(http, published.Endpoint);
		}
		finally
		{
			module.Dispose();
			if (Directory.Exists(root))
			{
				Directory.Delete(root, true);
			}
		}

		Assert.All(accessed, member => Assert.Contains(member, AllowedClientMembers));
	}

	[Fact]
	public async Task EnableDisable_TwoActivations_ShareTheRegistryWithoutAffectingEachOther()
	{
		string root = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		string instances = Path.Combine(root, "instances");
		List<string> firstAccessed = [];
		List<string> secondAccessed = [];
		ICheatEngineClient firstClient = StrictClient(firstAccessed);
		ICheatEngineClient secondClient = StrictClient(secondAccessed);
		using TestActivation firstActivation = new(firstClient,
			new Dictionary<string, string?>
			{
				["Mcp:InstanceName"] = "first",
				["Mcp:InstanceDirectory"] = instances
			});
		using TestActivation secondActivation = new(secondClient,
			new Dictionary<string, string?>
			{
				["Mcp:InstanceName"] = "second",
				["Mcp:InstanceDirectory"] = instances
			});
		McpServerModule first = firstActivation.Module;
		McpServerModule second = secondActivation.Module;
		InstanceRegistry registry = new(instances);
		using HttpClient http = new();
		string? firstEndpoint = null;
		string? secondEndpoint = null;
		try
		{
			first.OnEnabled(firstClient);
			second.OnEnabled(secondClient);

			InstanceDescriptor[] published = registry.ReadActive(TestContext.Current.CancellationToken).ToArray();
			Assert.Equal(2, published.Length);
			InstanceDescriptor firstInstance = Assert.Single(published, instance => instance.Name == "first");
			InstanceDescriptor secondInstance = Assert.Single(published, instance => instance.Name == "second");
			firstEndpoint = firstInstance.Endpoint;
			secondEndpoint = secondInstance.Endpoint;
			Assert.NotEqual(firstInstance.InstanceId, secondInstance.InstanceId);
			Assert.True(firstInstance.AccessToken != secondInstance.AccessToken);
			Assert.NotEqual(firstInstance.Endpoint, secondInstance.Endpoint);
			await AssertPublishedIdentityAsync(http, firstInstance);
			await AssertPublishedIdentityAsync(http, secondInstance);

			first.OnDisabling(firstClient);

			InstanceDescriptor remaining = Assert.Single(registry.ReadActive(TestContext.Current.CancellationToken));
			Assert.Equal(secondInstance.InstanceId, remaining.InstanceId);
			await AssertPublishedIdentityAsync(http, remaining);
			first.Dispose();
			await WaitUntilStoppedAsync(http, firstEndpoint);
		}
		finally
		{
			first.OnDisabling(firstClient);
			second.OnDisabling(secondClient);
			first.Dispose();
			second.Dispose();
			if (firstEndpoint is not null)
			{
				await WaitUntilStoppedAsync(http, firstEndpoint);
			}

			if (secondEndpoint is not null)
			{
				await WaitUntilStoppedAsync(http, secondEndpoint);
			}

			if (Directory.Exists(root))
			{
				Directory.Delete(root, true);
			}
		}

		Assert.Empty(registry.ReadActive(TestContext.Current.CancellationToken));
		Assert.All(firstAccessed, member => Assert.Contains(member, AllowedClientMembers));
		Assert.All(secondAccessed, member => Assert.Contains(member, AllowedClientMembers));
	}

	[Fact]
	public async Task Stopping_CancelledBeforeDisabling_RefusesRequestsUntilTheRecordIsWithdrawn()
	{
		string root = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		List<string> accessed = [];
		using CancellationTokenSource stopping = new();
		ICheatEngineClient client = StrictClient(accessed, stopping);
		string instances = Path.Combine(root, "instances");
		using TestActivation activation = new(client,
			new Dictionary<string, string?>
			{
				["Mcp:InstanceName"] = "stopping",
				["Mcp:InstanceDirectory"] = instances
			});
		McpServerModule module = activation.Module;
		InstanceRegistry registry = new(instances);
		using HttpClient http = new();
		try
		{
			module.OnEnabled(client);
			InstanceDescriptor published = Assert.Single(registry.ReadActive(TestContext.Current.CancellationToken));

			// Client Hosting cancels the stopping token before it runs the disable callbacks.
			await stopping.CancelAsync();
			using (HttpRequestMessage request = new(HttpMethod.Get, new Uri(new Uri(published.Endpoint), "instance")))
			{
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", published.AccessToken);
				using HttpResponseMessage
					refused = await http.SendAsync(request, TestContext.Current.CancellationToken);
				Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
			}

			Assert.Single(registry.ReadActive(TestContext.Current.CancellationToken));
			module.OnDisabling(client);
			Assert.Empty(registry.ReadActive(TestContext.Current.CancellationToken));
		}
		finally
		{
			module.Dispose();
			if (Directory.Exists(root))
			{
				Directory.Delete(root, true);
			}
		}

		Assert.All(accessed, member => Assert.Contains(member, AllowedClientMembers));
	}

	[Fact]
	public async Task Enable_TraceLogging_LogsDebugButNeitherTransportDetailNorTheToken()
	{
		string root = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		List<string> accessed = [];
		ICheatEngineClient client = StrictClient(accessed);
		string instances = Path.Combine(root, "instances");
		using TestActivation activation = new(client,
			new Dictionary<string, string?>
			{
				["Mcp:InstanceName"] = "logging",
				["Mcp:InstanceDirectory"] = instances,
				["Mcp:Logging:MinimumLevel"] = "Trace"
			});
		Assert.Equal(LogLevel.Trace, activation.Log.MinimumLevel);
		McpServerModule module = activation.Module;
		InstanceRegistry registry = new(instances);
		using HttpClient http = new();
		string token;
		try
		{
			module.OnEnabled(client);
			InstanceDescriptor published = Assert.Single(registry.ReadActive(TestContext.Current.CancellationToken));
			token = published.AccessToken;
			using (HttpRequestMessage identity = new(HttpMethod.Get, new Uri(new Uri(published.Endpoint), "instance")))
			{
				identity.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
				using HttpResponseMessage response =
					await http.SendAsync(identity, TestContext.Current.CancellationToken);
				Assert.Equal(HttpStatusCode.OK, response.StatusCode);
			}

			using (HttpRequestMessage initialize = new(HttpMethod.Post, published.Endpoint))
			{
				initialize.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
				initialize.Headers.Accept.ParseAdd("application/json");
				initialize.Headers.Accept.ParseAdd("text/event-stream");
				initialize.Content = new StringContent(
					"""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"logging","version":"0"}}}""",
					Encoding.UTF8, "application/json");
				using HttpResponseMessage response =
					await http.SendAsync(initialize, TestContext.Current.CancellationToken);
				Assert.Equal(HttpStatusCode.OK, response.StatusCode);
				Assert.Contains("serverInfo",
					await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
					StringComparison.Ordinal);
			}

			module.OnDisabling(client);
		}
		finally
		{
			module.Dispose();
		}

		string log = await activation.EndAndReadLogAsync();
		if (Directory.Exists(root))
		{
			Directory.Delete(root, true);
		}

		Assert.DoesNotContain(token, log, StringComparison.Ordinal);
		// The configured level reaches the backend's web host and the activation's own loggers.
		Assert.Contains("|DEBUG|Microsoft.Extensions.Hosting.", log, StringComparison.Ordinal);
		Assert.Contains("|CheatEngine.Mcp.Plugin.McpStatusIndicator|", log, StringComparison.Ordinal);
		Assert.DoesNotMatch(@"\|(TRACE|DEBUG)\|(Microsoft\.AspNetCore|System\.Net\.Http|ModelContextProtocol)[.|]",
			log);
		Assert.All(accessed, member => Assert.Contains(member, AllowedClientMembers));
	}

	[Theory]
	[InlineData("Mcp:Port", "65536", "Mcp:Port must be between 0 and 65535 (0 selects a free port).")]
	[InlineData("Mcp:Host", "0.0.0.0", "Mcp:Host must be 127.0.0.1; the gateway connects to private local backends.")]
	[InlineData("Mcp:InstanceDirectory", "instances", "Mcp:InstanceDirectory must be an absolute directory.")]
	public void Enable_InvalidSettings_FailWhileTheActivationIsBuilt(string key, string value, string message)
	{
		List<string> accessed = [];
		using TestActivation activation = new(StrictClient(accessed),
			new Dictionary<string, string?> { [key] = value });

		OptionsValidationException failure = Assert.Throws<OptionsValidationException>(() => activation.Module);

		Assert.Contains(message, failure.Message, StringComparison.Ordinal);
		Assert.Empty(accessed);
	}

	[Fact]
	public void Enable_OccupiedEndpoint_RefusesAndLeavesNoDiscoveryRecordOrActiveBackend()
	{
		string root = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		string instances = Path.Combine(root, "instances");
		List<string> accessed = [];
		ICheatEngineClient client = StrictClient(accessed);
		using System.Net.Sockets.TcpListener occupied = new(IPAddress.Loopback, 0);
		occupied.Start();
		int port = ((IPEndPoint) occupied.LocalEndpoint).Port;
		using TestActivation activation = new(client,
			new Dictionary<string, string?>
			{
				["Mcp:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
				["Mcp:InstanceDirectory"] = instances
			});
		McpServerModule module = activation.Module;
		InstanceRegistry registry = new(instances);
		try
		{
			InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => module.OnEnabled(client));

			Assert.Contains("Could not start the MCP server", failure.Message, StringComparison.Ordinal);
			Assert.Empty(registry.ReadActive(TestContext.Current.CancellationToken));

			occupied.Stop();
			using System.Net.Sockets.TcpListener replacement = new(IPAddress.Loopback, port);
			replacement.Start();
		}
		finally
		{
			module.Dispose();
			if (Directory.Exists(root))
			{
				Directory.Delete(root, true);
			}
		}

		Assert.Empty(registry.ReadActive(TestContext.Current.CancellationToken));
		Assert.All(accessed, member => Assert.Contains(member, AllowedClientMembers));
	}

	[Fact]
	public async Task Enable_UnavailableLogLocation_KeepsTheActualModuleRunningAndDiscoverable()
	{
		string root = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		string instances = Path.Combine(root, "instances");
		Directory.CreateDirectory(root);
		string unavailableDirectory = Path.Combine(root, "not-a-directory");
		await File.WriteAllTextAsync(unavailableDirectory, "occupied", TestContext.Current.CancellationToken);
		List<string> accessed = [];
		ICheatEngineClient client = StrictClient(accessed);
		using TestActivation activation = new(client,
			new Dictionary<string, string?>
			{
				["Mcp:InstanceDirectory"] = instances
			});
		PluginLog unavailableLog = new(unavailableDirectory);
		McpServerModule? module = null;
		string? endpoint = null;
		string logLocationContents = string.Empty;
		try
		{
			IOptions<McpBackendOptions> backend = activation.Services.GetRequiredService<IOptions<McpBackendOptions>>();
			IOptions<McpDiscoveryOptions> discovery = activation.Services.GetRequiredService<IOptions<McpDiscoveryOptions>>();
			McpBackendHostFactory factory = new(backend, discovery, unavailableLog,
				activation.Services.GetRequiredService<McpRuntimeInfo>(),
				activation.Services.GetRequiredService<IOptions<CheatEngineMcpPrimitiveOptions>>());
			McpStatusIndicator status = new(discovery, unavailableLog,
				activation.Services.GetRequiredService<ILogger<McpStatusIndicator>>());
			module = new McpServerModule(factory, activation.Targets, status);
			InstanceRegistry registry = new(instances);
			using HttpClient http = new();

			module.OnEnabled(client);
			InstanceDescriptor published = Assert.Single(registry.ReadActive(TestContext.Current.CancellationToken));
			endpoint = published.Endpoint;
			await AssertPublishedIdentityAsync(http, published);

			module.OnDisabling(client);
			Assert.Empty(registry.ReadActive(TestContext.Current.CancellationToken));
			module.Dispose();
			await WaitUntilStoppedAsync(http, endpoint);
		}
		finally
		{
			module?.OnDisabling(client);
			module?.Dispose();
			await TestLog.ReleaseAsync(unavailableLog);
			logLocationContents = await File.ReadAllTextAsync(unavailableDirectory, TestContext.Current.CancellationToken);
			if (Directory.Exists(root))
			{
				Directory.Delete(root, true);
			}
		}

		Assert.Equal("occupied", logLocationContents);
		Assert.All(accessed, member => Assert.Contains(member, AllowedClientMembers));
	}

	[Fact]
	public void Enable_PartiallyStartedBackendStopsBeforeItRethrowsTheStartupFailure()
	{
		List<string> accessed = [];
		ICheatEngineClient client = StrictClient(accessed);
		using TestActivation activation = new(client);
		PartiallyStartedBackend backend = new();
		McpServerModule module = new(new FailingBackendFactory(backend), activation.Targets,
			activation.Services.GetRequiredService<McpStatusIndicator>());

		InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
			module.OnEnabled(client));

		Assert.Contains("Could not start the MCP server", failure.Message, StringComparison.Ordinal);
		Assert.IsType<InvalidOperationException>(failure.InnerException);
		Assert.Equal((1, 1), (backend.StopAcceptingCalls, backend.StopCalls));
		Assert.All(accessed, member => Assert.Contains(member, AllowedClientMembers));
	}

	[Fact]
	public void Enable_SecondCall_IsRejectedBeforeCreatingAnotherBackend()
	{
		List<string> accessed = [];
		ICheatEngineClient client = StrictClient(accessed);
		using TestActivation activation = new(client);
		StartedBackend backend = new();
		CountingBackendFactory factory = new(backend);
		McpServerModule module = new(factory, activation.Targets,
			activation.Services.GetRequiredService<McpStatusIndicator>());
		try
		{
			module.OnEnabled(client);

			InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => module.OnEnabled(client));

			Assert.Equal("The MCP server module can only be enabled once.", failure.Message);
			Assert.Equal(1, factory.CreateCalls);
			Assert.Equal(1, backend.StartCalls);
		}
		finally
		{
			module.Dispose();
		}

		Assert.Equal((1, 1), (backend.StopAcceptingCalls, backend.StopCalls));
		Assert.All(accessed, member => Assert.Contains(member, AllowedClientMembers));
	}

	[Fact]
	public async Task Disabling_DuringEnable_StopsTheBackendBeforeItCanRun()
	{
		List<string> accessed = [];
		ICheatEngineClient client = StrictClient(accessed);
		using TestActivation activation = new(client);
		BlockingBackend backend = new();
		McpServerModule module = new(new BlockingBackendFactory(backend), activation.Targets,
			activation.Services.GetRequiredService<McpStatusIndicator>());
		try
		{
			Task enabling = Task.Run(() => module.OnEnabled(client), TestContext.Current.CancellationToken);
			await backend.WaitForStartAsync(TestContext.Current.CancellationToken);

			module.OnDisabling(client);
			backend.CompleteStart();

			InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => enabling);
			Assert.Contains("Could not start the MCP server", failure.Message, StringComparison.Ordinal);
			Assert.Equal((1, 1), (backend.StopAcceptingCalls, backend.StopCalls));
		}
		finally
		{
			module.Dispose();
		}

		Assert.All(accessed, member => Assert.Contains(member, AllowedClientMembers));
	}

	private static ICheatEngineClient StrictClient(List<string> accessed, CancellationTokenSource? stopping = null)
	{
		ICheatEngineDispatcher dispatcher = ClientTestDouble.Create<ICheatEngineDispatcher>((method, _) =>
		{
			lock (accessed)
			{
				accessed.Add(method.Name);
			}

			return method.Name == "get_IsMainThread"
				? true
				: throw new NotSupportedException($"Lifecycle callbacks must not dispatch Client work: {method.Name}.");
		});
		return ClientTestDouble.Create<ICheatEngineClient>((method, _) =>
		{
			lock (accessed)
			{
				accessed.Add(method.Name);
			}

			return method.Name switch
			{
				"get_Stopping" => stopping?.Token ?? CancellationToken.None,
				"get_Dispatcher" => dispatcher,
				_ => throw new NotSupportedException($"Lifecycle callbacks must not use Client member {method.Name}.")
			};
		});
	}

	private static async Task WaitUntilStoppedAsync(HttpClient http, string endpoint)
	{
		Stopwatch elapsed = Stopwatch.StartNew();
		while (elapsed.Elapsed < TimeSpan.FromSeconds(10))
		{
			try
			{
				using HttpResponseMessage response =
					await http.GetAsync(endpoint, TestContext.Current.CancellationToken);
			}
			catch (HttpRequestException)
			{
				return;
			}

			await Task.Delay(50, TestContext.Current.CancellationToken);
		}

		throw new TimeoutException("The disposed module left its MCP listener running.");
	}

	private static async Task AssertPublishedIdentityAsync(HttpClient http, InstanceDescriptor instance)
	{
		using HttpRequestMessage request = new(HttpMethod.Get, new Uri(new Uri(instance.Endpoint), "instance"));
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", instance.AccessToken);
		using HttpResponseMessage response = await http.SendAsync(request, TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		JsonNode body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
		Assert.Equal(instance.InstanceId, body["instanceId"]!.GetValue<string>());
	}

	private sealed class FailingBackendFactory(PartiallyStartedBackend backend) : IMcpBackendHostFactory
	{
		public string BaseUrl => "http://127.0.0.1:45678/";

		public IMcpBackendHost Create(McpPrimitiveTargets _, CancellationToken __)
		{
			return backend;
		}
	}

	private sealed class CountingBackendFactory(StartedBackend backend) : IMcpBackendHostFactory
	{
		public int CreateCalls
		{
			get;
			private set;
		}

		public string BaseUrl => "http://127.0.0.1:45678/";

		public IMcpBackendHost Create(McpPrimitiveTargets _, CancellationToken __)
		{
			CreateCalls++;
			return backend;
		}
	}

	private sealed class BlockingBackendFactory(BlockingBackend backend) : IMcpBackendHostFactory
	{
		public string BaseUrl => "http://127.0.0.1:45678/";

		public IMcpBackendHost Create(McpPrimitiveTargets _, CancellationToken __)
		{
			return backend;
		}
	}

	private sealed class PartiallyStartedBackend : IMcpBackendHost
	{
		public int StopAcceptingCalls
		{
			get;
			private set;
		}

		public int StopCalls
		{
			get;
			private set;
		}

		public string? Endpoint => null;

		public Task StartAsync()
		{
			return Task.FromException(new InvalidOperationException("listener startup failed after binding"));
		}

		public void StopAccepting()
		{
			StopAcceptingCalls++;
		}

		public Task StopAsync()
		{
			StopCalls++;
			return Task.CompletedTask;
		}
	}

	private sealed class StartedBackend : IMcpBackendHost
	{
		public int StartCalls
		{
			get;
			private set;
		}

		public int StopAcceptingCalls
		{
			get;
			private set;
		}

		public int StopCalls
		{
			get;
			private set;
		}

		public string? Endpoint => "http://127.0.0.1:45678/";

		public Task StartAsync()
		{
			StartCalls++;
			return Task.CompletedTask;
		}

		public void StopAccepting()
		{
			StopAcceptingCalls++;
		}

		public Task StopAsync()
		{
			StopCalls++;
			return Task.CompletedTask;
		}
	}

	private sealed class BlockingBackend : IMcpBackendHost
	{
		private readonly TaskCompletionSource<object?> _start =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<object?> _startEntered =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public int StopAcceptingCalls
		{
			get;
			private set;
		}

		public int StopCalls
		{
			get;
			private set;
		}

		public string? Endpoint => "http://127.0.0.1:45678/";

		public void CompleteStart()
		{
			_start.TrySetResult(null);
		}

		public Task StartAsync()
		{
			_startEntered.TrySetResult(null);
			return _start.Task;
		}

		public void StopAccepting()
		{
			StopAcceptingCalls++;
		}

		public Task StopAsync()
		{
			StopCalls++;
			return Task.CompletedTask;
		}

		public Task<object?> WaitForStartAsync(CancellationToken cancellationToken)
		{
			return _startEntered.Task.WaitAsync(cancellationToken);
		}
	}
}
