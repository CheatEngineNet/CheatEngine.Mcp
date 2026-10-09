using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tests.Tools.Modules;
using CheatEngine.Mcp.Tools.Runtime;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Hosting;

/// <summary>
///     The gateway's completion of routed live templates: <c>[AllowedValues]</c> from its own catalog, and a marked
///     variable forwarded to exactly the recently verified instance the client selected, in the backend's template
///     form, bounded in time and size and never an error.
/// </summary>
[Collection(nameof(SerialTestGroup))]
public sealed class GatewayCompletionTests
{
	private const string Items = "cheatengine://instances/{instanceId}/probe-items/{item}{?format}";
	private const string ItemIds = "cheatengine://instances/{instanceId}/probe-items/{item}/ids/{id}";
	private const string BackendItems = "cheatengine://instance/probe-items/{item}{?format}";

	private static readonly CheatEngineMcpPrimitive[] RoutedProbe =
	[
		new(CheatEngineMcpPrimitiveKind.Resource, typeof(RoutedCompletionProbe))
	];

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Complete_AllowedValuesOfARoutedTemplate_AreServedByTheGatewayWithoutAnyBackend()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(extraPrimitives: RoutedProbe);
		await using McpClient client = await gateway.ConnectAsync();

		CompleteResult concise = await CompleteAsync(client, Items, "format", "C", null);
		CompleteResult both = await CompleteAsync(client, Items, "format", "", null);

		Assert.NotNull(client.ServerCapabilities.Completions);
		Assert.Equal(["concise"], concise.Completion.Values);
		Assert.Equal((1, false), (concise.Completion.Total, concise.Completion.HasMore));
		Assert.Equal(["concise", "detailed"], both.Completion.Values);
	}

	[Fact]
	public async Task Complete_MarkedVariable_IsForwardedToTheSelectedVerifiedInstanceInItsBackendForm()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(extraPrimitives: RoutedProbe);
		await using CompletingBackend first = await CompletingBackend.StartAsync(gateway.Registry, "first");
		await using CompletingBackend second = await CompletingBackend.StartAsync(gateway.Registry, "second");
		await using McpClient client = await gateway.ConnectAsync();
		first.Respond = static _ => new Completion { Values = ["first-item"], Total = 1 };
		second.Respond = static request => new Completion
		{
			Values = ["second-" + request.Argument.Value],
			Total = 1,
			HasMore = false
		};
		await VerifyAsync(client);
		const string traceparent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

		CompleteResult result = await client.CompleteAsync(new CompleteRequestParams
		{
			Ref = new ResourceTemplateReference { Uri = ItemIds },
			Argument = new Argument { Name = "item", Value = "al" },
			Context = new CompleteContext
			{
				Arguments = new Dictionary<string, string>
				{
					[McpResourceUris.InstanceIdVariable] = second.Descriptor.InstanceId,
					["id"] = "7"
				}
			},
			// Only W3C trace context reaches the backend: the upstream progress token and protocol fields do not.
			Meta = new JsonObject
			{
				["traceparent"] = traceparent,
				["progressToken"] = "upstream-token",
				["io.modelcontextprotocol/related-task"] = "upstream-task",
				["vendor.example/key"] = "upstream-vendor"
			}
		}, Token);

		Assert.Equal(["second-al"], result.Completion.Values);
		Assert.Equal((1, false), (result.Completion.Total, result.Completion.HasMore));
		Assert.Empty(first.Requests);
		CompleteRequestParams forwarded = Assert.Single(second.Requests);
		Assert.Equal("cheatengine://instance/probe-items/{item}/ids/{id}",
			Assert.IsType<ResourceTemplateReference>(forwarded.Ref).Uri);
		Assert.Equal(("item", "al"), (forwarded.Argument.Name, forwarded.Argument.Value));
		IDictionary<string, string> context = Assert.IsAssignableFrom<IDictionary<string, string>>(
			forwarded.Context?.Arguments);
		Assert.Equal(["id"], context.Keys);
		Assert.Equal("7", context["id"]);
		JsonObject meta = Assert.IsType<JsonObject>(forwarded.Meta);
		Assert.Equal(["traceparent"], meta.Select(static entry => entry.Key));
		Assert.Equal(traceparent, meta["traceparent"]?.GetValue<string>());
	}

	[Fact]
	public async Task Complete_InstanceNotSelectedNotVerifiedOrMalformed_OffersNothingWithoutContactingIt()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(extraPrimitives: RoutedProbe);
		await using CompletingBackend backend = await CompletingBackend.StartAsync(gateway.Registry, "unverified");
		await using McpClient client = await gateway.ConnectAsync();

		CompleteResult unverified = await CompleteAsync(client, Items, "item", "", Selecting(backend));
		CompleteResult unselected = await CompleteAsync(client, Items, "item", "", null);
		CompleteResult malformed = await CompleteAsync(client, Items, "item", "",
			new Dictionary<string, string> { [McpResourceUris.InstanceIdVariable] = "ce-1-zz" });

		Assert.Empty(unverified.Completion.Values);
		Assert.Empty(unselected.Completion.Values);
		Assert.Empty(malformed.Completion.Values);
		Assert.Empty(backend.Requests);
		Assert.Equal(0, backend.IdentityProbeCount);
		Assert.Equal(0, backend.InitializeCount);
	}

	[Fact]
	public async Task Complete_UnmarkedVariable_IsNeverForwarded_AndInstanceIdCompletesAsBefore()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(extraPrimitives: RoutedProbe);
		await using CompletingBackend backend = await CompletingBackend.StartAsync(gateway.Registry, "unmarked");
		await using McpClient client = await gateway.ConnectAsync();
		await VerifyAsync(client);

		CompleteResult id = await CompleteAsync(client, ItemIds, "id", "", Selecting(backend));
		CompleteResult instanceId = await CompleteAsync(client, ItemIds, McpResourceUris.InstanceIdVariable, "ce-",
			null);

		Assert.Empty(id.Completion.Values);
		Assert.Equal([backend.Descriptor.InstanceId], instanceId.Completion.Values);
		Assert.Equal(1, instanceId.Completion.Total);
		Assert.Empty(backend.Requests);
	}

	[Fact]
	public async Task Complete_RepublishedCredentials_AreIneligibleUntilVerifiedAgain()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(extraPrimitives: RoutedProbe);
		await using CompletingBackend backend = await CompletingBackend.StartAsync(gateway.Registry, "republished");
		await using McpClient client = await gateway.ConnectAsync();
		backend.Respond = static _ => new Completion { Values = ["refreshed"] };
		await VerifyAsync(client);

		backend.RepublishWithNewToken(gateway.Registry);
		CompleteResult instanceId = await CompleteAsync(client, Items, McpResourceUris.InstanceIdVariable, "ce-", null);
		CompleteResult forwarded = await CompleteAsync(client, Items, "item", "re", Selecting(backend));

		Assert.Empty(instanceId.Completion.Values);
		Assert.Empty(forwarded.Completion.Values);
		Assert.Empty(backend.Requests);
		Assert.Equal(0, backend.InitializeCount);

		await VerifyAsync(client);
		CompleteResult refreshed = await CompleteAsync(client, Items, "item", "re", Selecting(backend));

		Assert.Equal(["refreshed"], refreshed.Completion.Values);
		Assert.Single(backend.Requests);
	}

	[Fact]
	public async Task Complete_SlowBackend_OffersNothingWithinTheTimeoutAndKeepsThePooledConnection()
	{
		await using GatewayTestHost gateway =
			await GatewayTestHost.StartAsync(TimeSpan.FromMilliseconds(500), extraPrimitives: RoutedProbe);
		await using CompletingBackend backend = await CompletingBackend.StartAsync(gateway.Registry, "slow");
		await using McpClient client = await gateway.ConnectAsync();
		await VerifyAsync(client);
		backend.HangUntilCancelled = true;

		Stopwatch elapsed = Stopwatch.StartNew();
		CompleteResult slow = await CompleteAsync(client, Items, "item", "", Selecting(backend));
		elapsed.Stop();
		backend.HangUntilCancelled = false;
		backend.Respond = static _ => new Completion { Values = ["recovered"] };
		CompleteResult recovered = await CompleteAsync(client, Items, "item", "", Selecting(backend));

		Assert.Empty(slow.Completion.Values);
		Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), $"The completion took {elapsed.Elapsed}.");
		Assert.Equal(2, backend.Requests.Count);
		Assert.Equal(["recovered"], recovered.Completion.Values);
		// A slow answer is not a broken connection: the second completion reused the first handshake.
		Assert.Equal(1, backend.InitializeCount);
	}

	[Fact]
	public async Task Complete_BackendErrorOversizedAnswerOrStoppedBackend_IsBoundedAndNeverAnError()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(extraPrimitives: RoutedProbe);
		await using CompletingBackend backend = await CompletingBackend.StartAsync(gateway.Registry, "bounded");
		await using McpClient client = await gateway.ConnectAsync();
		await VerifyAsync(client);

		backend.Respond = static _ => throw new McpProtocolException("No completion here.", McpErrorCode.InvalidParams);
		CompleteResult refused = await CompleteAsync(client, Items, "item", "", Selecting(backend));
		backend.Respond = static _ => new Completion
		{
			Values = [.. Enumerable.Range(0, 150).Select(static index => $"item-{index:D3}")]
		};
		CompleteResult oversized = await CompleteAsync(client, Items, "item", "", Selecting(backend));
		await backend.StopListeningAsync();
		CompleteResult stopped = await CompleteAsync(client, Items, "item", "", Selecting(backend));

		Assert.Empty(refused.Completion.Values);
		Assert.Equal(McpCompletions.MaximumValues, oversized.Completion.Values.Count);
		Assert.Equal((150, true), (oversized.Completion.Total, oversized.Completion.HasMore));
		Assert.Empty(stopped.Completion.Values);
	}

	[Fact]
	public async Task Complete_ThroughARealBackend_OffersTheLiveScannerAndModuleNames()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
		target.AddModule("game.exe", 0x140000000, 0x5000);
		using TestActivation activation = new(target.Client);
		InstancePublication publication = new(gateway.Registry, "completion");
		McpBackendHost backend = new(new McpBackendOptions(), activation.Log, TestRuntime.Info, activation.Manifest,
			activation.Targets, CancellationToken.None, publication);
		try
		{
			await backend.StartAsync();
			await using McpClient client = await gateway.ConnectAsync();
			await VerifyAsync(client);
			Dictionary<string, string> selected = new()
			{
				[McpResourceUris.InstanceIdVariable] = publication.Descriptor.InstanceId
			};
			CallToolResult prepared = await client.CallToolAsync(CheatEngineToolNames.ModuleList,
				new Dictionary<string, object?> { [McpResourceUris.InstanceIdVariable] = publication.Descriptor.InstanceId },
				cancellationToken: Token);
			Assert.NotEqual(true, prepared.IsError);
			Assert.Equal(1, target.Calls("TryGetModules"));

			CompleteResult scanners = await CompleteAsync(client,
				"cheatengine://instances/{instanceId}/scanners/{scannerName}", "scannerName", "M", selected);
			CompleteResult records = await CompleteAsync(client,
				"cheatengine://instances/{instanceId}/records/{recordId}", "recordId", "1", selected);
			CompleteResult modules = await CompleteAsync(client,
				"cheatengine://instances/{instanceId}/modules/{module}", "module", "G", selected);
			CompleteResult all = await CompleteAsync(client,
				"cheatengine://instances/{instanceId}/modules/{module}", "module", "", selected);

			Assert.Equal(["main"], scanners.Completion.Values);
			Assert.Empty(records.Completion.Values);
			Assert.Equal(["game.exe"], modules.Completion.Values);
			Assert.Equal(["sample.dll", "game.exe"], all.Completion.Values);
			// Forwarded keystrokes check the prepared snapshot; only the explicit tool enumerates modules.
			Assert.Equal(1, target.Calls("TryGetModules"));
		}
		finally
		{
			await backend.StopAsync();
		}
	}

	private static async Task VerifyAsync(McpClient client)
	{
		// instance_list verifies every candidate, which is what makes an instance eligible for completion.
		CallToolResult listed = await client.CallToolAsync(CheatEngineToolNames.InstanceList,
			new Dictionary<string, object?>(), cancellationToken: Token);
		Assert.NotEqual(true, listed.IsError);
	}

	private static Dictionary<string, string> Selecting(CompletingBackend backend)
	{
		return new Dictionary<string, string> { [McpResourceUris.InstanceIdVariable] = backend.Descriptor.InstanceId };
	}

	private static async Task<CompleteResult> CompleteAsync(McpClient client, string template, string argument,
		string value, Dictionary<string, string>? context)
	{
		return await client.CompleteAsync(
			new CompleteRequestParams
			{
				Ref = new ResourceTemplateReference { Uri = template },
				Argument = new Argument { Name = argument, Value = value },
				Context = context is null ? null : new CompleteContext { Arguments = context }
			}, Token);
	}

	/// <summary>Routed live templates the gateway completes; the gateway never constructs or invokes it.</summary>
	[McpServerResourceType]
	public sealed class RoutedCompletionProbe : IMcpCompletionSource
	{
		private readonly string _json = "{}";

		public McpCompletionValues ListCompletionValues(string variable, CancellationToken cancellationToken)
		{
			throw new InvalidOperationException("The gateway never lists a routed probe itself.");
		}

		[McpServerResource(UriTemplate = "cheatengine://instance/probe-items/{item}{?format}",
			Name = "instance_probe_items", Title = "Probe items", MimeType = "application/json")]
		[McpSourceTool(typeof(RuntimeTools), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("Probe items.")]
		public string Items([McpCompletion(McpCompletionCost.Memory)] string item,
			[AllowedValues("concise", "detailed")] string? format = null)
		{
			return _json;
		}

		[McpServerResource(UriTemplate = "cheatengine://instance/probe-items/{item}/ids/{id}",
			Name = "instance_probe_item_ids", Title = "Probe item ids", MimeType = "application/json")]
		[McpSourceTool(typeof(RuntimeTools), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("Probe item ids.")]
		public string ItemIds([McpCompletion(McpCompletionCost.Memory)] string item, string id)
		{
			return _json;
		}
	}

	/// <summary>
	///     A published backend double that answers <c>completion/complete</c> as scripted and records what the gateway
	///     forwarded, the identity probes and the <c>initialize</c> handshakes.
	/// </summary>
	private sealed class CompletingBackend : IAsyncDisposable
	{
		private readonly WebApplication _application;
		private int _identityProbes;
		private int _initializeRequests;
		private string? _recordPath;

		private CompletingBackend(WebApplication application)
		{
			_application = application;
		}

		internal InstanceDescriptor Descriptor
		{
			get;
			private set;
		} = null!;

		internal ConcurrentQueue<CompleteRequestParams> Requests
		{
			get;
		} = new();

		internal Func<CompleteRequestParams, Completion> Respond
		{
			get;
			set;
		} = static _ => new Completion();

		internal bool HangUntilCancelled
		{
			get;
			set;
		}

		internal int IdentityProbeCount => Volatile.Read(ref _identityProbes);

		internal int InitializeCount => Volatile.Read(ref _initializeRequests);

		public async ValueTask DisposeAsync()
		{
			string? path = Interlocked.Exchange(ref _recordPath, null);
			if (path is not null)
			{
				File.Delete(path);
			}

			await _application.StopAsync(Token);
			await _application.DisposeAsync();
		}

		internal async Task StopListeningAsync()
		{
			await _application.StopAsync(Token);
		}

		internal void RepublishWithNewToken(InstanceRegistry registry)
		{
			ArgumentNullException.ThrowIfNull(registry);
			string path = _recordPath ?? throw new InvalidOperationException("The backend is not published.");
			File.Delete(path);
			Descriptor = Descriptor with
			{
				AccessToken = RandomNumberGenerator.GetHexString(64)
			};
			_recordPath = registry.Publish(Descriptor);
		}

		internal static async Task<CompletingBackend> StartAsync(InstanceRegistry registry, string name)
		{
			using Process process = Process.GetCurrentProcess();
			Guid activation = Guid.NewGuid();
			InstanceDescriptor descriptor = new($"ce-{process.Id}-{activation:N}", name, activation, process.Id,
				process.StartTime.ToUniversalTime().Ticks, "http://127.0.0.1:0/",
				RandomNumberGenerator.GetHexString(64), "2.0.0");
			WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
			builder.Logging.ClearProviders();
			CompletingBackend? backend = null;
			builder.Services.AddMcpServer()
				.WithHttpTransport(static transport => transport.Stateless = true)
				.WithMessageFilters(filters => filters.AddIncomingFilter(next => async (context, cancellationToken) =>
				{
					if (context.JsonRpcMessage is JsonRpcRequest { Method: RequestMethods.Initialize })
					{
						Interlocked.Increment(ref backend!._initializeRequests);
					}

					await next(context, cancellationToken);
				}))
				.WithCompleteHandler((context, cancellationToken) => backend!.CompleteAsync(context.Params!,
					cancellationToken));
			WebApplication application = builder.Build();
			application.Use(async (context, next) =>
			{
				if (!string.Equals(context.Request.Headers.Authorization,
						$"Bearer {backend!.Descriptor.AccessToken}", StringComparison.Ordinal))
				{
					context.Response.StatusCode = StatusCodes.Status401Unauthorized;
					return;
				}

				await next(context);
			});
			application.MapGet("/instance", context =>
			{
				Interlocked.Increment(ref backend!._identityProbes);
				return context.Response.WriteAsJsonAsync(InstanceIdentity.From(backend.Descriptor),
					HostingJsonContext.Default.InstanceIdentity, cancellationToken: context.RequestAborted);
			});
			application.MapMcp();
			application.Urls.Add(LoopbackEndpoints.NewEndpoint());
			backend = new CompletingBackend(application) { Descriptor = descriptor };
			await application.StartAsync(Token);
			backend.Descriptor = descriptor with
			{
				Endpoint = new Uri(application.Urls.Single()).AbsoluteUri
			};
			backend._recordPath = registry.Publish(backend.Descriptor);
			return backend;
		}

		private async ValueTask<CompleteResult> CompleteAsync(CompleteRequestParams request,
			CancellationToken cancellationToken)
		{
			Requests.Enqueue(request);
			if (HangUntilCancelled)
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			}

			return new CompleteResult { Completion = Respond(request) };
		}
	}
}
