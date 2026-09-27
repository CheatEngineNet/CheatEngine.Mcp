using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

using CheatEngine.Client;
using CheatEngine.Client.Dispatching;
using CheatEngine.Mcp.Plugin;
using CheatEngine.Mcp.Tests.Support;

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
				["Mcp:InstanceName"] = "lifecycle", ["Mcp:InstanceDirectory"] = instances
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
				["Mcp:InstanceName"] = "stopping", ["Mcp:InstanceDirectory"] = instances
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
}
