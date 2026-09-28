using System.Text.Json;
using System.Threading.Channels;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Hosting;

/// <summary>
///     <c>notifications/resources/list_changed</c> from the gateway: raised once per settled change of the verified
///     instances after a client listed resources, through the SDK's resource collection, and delivered by the stdio
///     executable as its initialize result advertises.
/// </summary>
[Collection(nameof(SerialTestGroup))]
public sealed class GatewayResourceListChangedTests
{
	private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(20);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Poll_WithoutAnyListing_ReadsNothingAndNeverNotifies()
	{
		await using ListFixture fixture = await ListFixture.StartAsync();
		await fixture.Verifier.VerifyAsync(fixture.Backend.Descriptor, Token);

		Assert.False(fixture.Monitor.Poll(Token));
		Assert.False(fixture.Monitor.Poll(Token));
		Assert.Equal(0, fixture.Changes);
	}

	[Fact]
	public async Task Poll_VerifiedInstanceAfterAListing_NotifiesOnceWhenSettledAndLeavesTheCollectionUnchanged()
	{
		await using ListFixture fixture = await ListFixture.StartAsync();
		Assert.Empty(fixture.Live.ListForClient(Token));
		await fixture.Verifier.VerifyAsync(fixture.Backend.Descriptor, Token);

		bool first = fixture.Monitor.Poll(Token);
		bool second = fixture.Monitor.Poll(Token);
		bool third = fixture.Monitor.Poll(Token);

		// The first poll sees the change, the second confirms it lasted; then nothing is outstanding.
		Assert.Equal((false, true, false), (first, second, third));
		Assert.Equal(1, fixture.Changes);
		Assert.Equal([ListFixture.DocumentUri],
			fixture.Collection.Select(static resource => resource.ProtocolResourceTemplate.UriTemplate));
	}

	[Fact]
	public async Task Poll_WithdrawnInstance_NotifiesAgainOnlyAfterTheNextListing()
	{
		await using ListFixture fixture = await ListFixture.StartAsync();
		await fixture.Verifier.VerifyAsync(fixture.Backend.Descriptor, Token);
		Assert.Single(fixture.Live.ListForClient(Token));
		fixture.Withdraw();

		Assert.False(fixture.Monitor.Poll(Token));
		Assert.True(fixture.Monitor.Poll(Token));
		fixture.Republish();
		await fixture.Verifier.VerifyAsync(fixture.Backend.Descriptor, Token);
		Assert.False(fixture.Monitor.Poll(Token));
		Assert.False(fixture.Monitor.Poll(Token));
		Assert.Equal(1, fixture.Changes);
	}

	[Fact]
	public async Task Poll_ChangeRevertedBeforeItSettled_SendsNothing()
	{
		await using ListFixture fixture = await ListFixture.StartAsync();
		Assert.Empty(fixture.Live.ListForClient(Token));
		await fixture.Verifier.VerifyAsync(fixture.Backend.Descriptor, Token);

		Assert.False(fixture.Monitor.Poll(Token));
		fixture.Withdraw();
		Assert.False(fixture.Monitor.Poll(Token));
		Assert.False(fixture.Monitor.Poll(Token));
		Assert.Equal(0, fixture.Changes);
	}

	[Fact]
	public async Task Poll_ListedAgainBeforeTheChangeSettled_NotifiesOnceForTheOlderListing()
	{
		await using ListFixture fixture = await ListFixture.StartAsync();
		Assert.Empty(fixture.Live.ListForClient(Token));
		await fixture.Verifier.VerifyAsync(fixture.Backend.Descriptor, Token);
		Assert.False(fixture.Monitor.Poll(Token));

		// The latest listing is current, but the older one differs and a client may still hold it.
		Assert.Single(fixture.Live.ListForClient(Token));
		bool settled = fixture.Monitor.Poll(Token);
		bool after = fixture.Monitor.Poll(Token);
		Assert.Single(fixture.Live.ListForClient(Token));
		bool relisted = fixture.Monitor.Poll(Token);

		Assert.Equal((true, false, false), (settled, after, relisted));
		Assert.Equal(1, fixture.Changes);
	}

	[Fact]
	public async Task Verify_FailedCheckOrChangedRecord_IsNoLongerConfirmed()
	{
		await using ListFixture fixture = await ListFixture.StartAsync();
		InstanceDescriptor record = fixture.Backend.Descriptor;
		await fixture.Verifier.VerifyAsync(record, Token);
		bool confirmed = fixture.Verifier.IsConfirmed(record);
		bool otherVersion = fixture.Verifier.IsConfirmed(record with
		{
			PluginVersion = "9.9.9"
		});
		fixture.Backend.ReportedIdentity = record with
		{
			ActivationId = Guid.NewGuid()
		};

		await Assert.ThrowsAsync<InstanceUnavailableException>(() => fixture.Verifier.VerifyAsync(record, Token));

		Assert.Equal((true, false), (confirmed, otherVersion));
		Assert.False(fixture.Verifier.IsConfirmed(record));
		Assert.Empty(fixture.Live.Read(Token));
	}

	[Fact]
	public async Task ForgetInactive_KeepsAConfirmationNewerThanTheRegistryRead()
	{
		await using ListFixture fixture = await ListFixture.StartAsync();
		long readBefore = fixture.Time.GetTimestamp();
		fixture.Time.Advance(TimeSpan.FromSeconds(1));
		await fixture.Verifier.VerifyAsync(fixture.Backend.Descriptor, Token);

		fixture.Verifier.ForgetInactive(new HashSet<string>(StringComparer.Ordinal), readBefore);
		bool keptWhenNewer = fixture.Verifier.IsConfirmed(fixture.Backend.Descriptor);
		fixture.Time.Advance(TimeSpan.FromSeconds(1));
		fixture.Verifier.ForgetInactive(new HashSet<string>(StringComparer.Ordinal), fixture.Time.GetTimestamp());

		Assert.True(keptWhenNewer);
		Assert.False(fixture.Verifier.IsConfirmed(fixture.Backend.Descriptor));
	}

	[Fact]
	public async Task GatewayExecutable_VerifiedInstancesChange_SendsResourceListChangedAndListsTheLiveResources()
	{
		await using GatewayProcess gateway = await GatewayProcess.StartAsync("2025-06-18");
		Channel<JsonRpcNotification> changes = Channel.CreateUnbounded<JsonRpcNotification>();
		await using IAsyncDisposable handler = gateway.Client.RegisterNotificationHandler(
			NotificationMethods.ResourceListChangedNotification,
			(notification, _) => changes.Writer.WriteAsync(notification, CancellationToken.None));
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "notified");
		string prefix = $"cheatengine://instances/{backend.Descriptor.InstanceId}/";
		string[] unverified = await ListUrisAsync(gateway.Client);

		await gateway.Client.CallToolAsync(CheatEngineToolNames.InstanceList, new Dictionary<string, object?>(),
			cancellationToken: Token);
		await changes.Reader.ReadAsync(Token).AsTask().WaitAsync(NotificationTimeout, Token);
		string[] verified = await ListUrisAsync(gateway.Client);
		// The plugin is disabled: its record is withdrawn.
		File.Delete(Path.Combine(gateway.Registry.DirectoryPath,
			backend.Descriptor.ActivationId.ToString("N") + ".json"));
		await changes.Reader.ReadAsync(Token).AsTask().WaitAsync(NotificationTimeout, Token);
		string[] withdrawn = await ListUrisAsync(gateway.Client);

		Assert.True(gateway.Client.ServerCapabilities.Resources?.ListChanged);
		Assert.DoesNotContain(unverified, uri => uri.StartsWith(prefix, StringComparison.Ordinal));
		Assert.Contains(prefix + "process", verified);
		Assert.Equal(11, verified.Count(uri => uri.StartsWith(prefix, StringComparison.Ordinal)));
		Assert.DoesNotContain(withdrawn, uri => uri.StartsWith(prefix, StringComparison.Ordinal));
		Assert.Empty(backend.ReadUris);
	}

	[Fact]
	public async Task GatewayExecutable_July2026Client_ReceivesListChangedOnlyOverItsSubscription()
	{
		await using GatewayProcess gateway = await GatewayProcess.StartAsync("2026-07-28");
		Channel<JsonRpcNotification> changes = Channel.CreateUnbounded<JsonRpcNotification>();
		await using IAsyncDisposable handler = gateway.Client.RegisterNotificationHandler(
			NotificationMethods.ResourceListChangedNotification,
			(notification, _) => changes.Writer.WriteAsync(notification, CancellationToken.None));
		await using FakeBackend first = await FakeBackend.StartAsync(gateway.Registry, "first");
		await using FakeBackend second = await FakeBackend.StartAsync(gateway.Registry, "second");

		// Without a subscription, the 2026-07-28 session must receive no list_changed.
		await ListUrisAsync(gateway.Client);
		await gateway.Client.CallToolAsync(CheatEngineToolNames.InstanceList, new Dictionary<string, object?>(),
			cancellationToken: Token);
		await Task.Delay(GatewayResourceListMonitor.PollInterval * 4, Token);
		bool unsolicited = changes.Reader.TryRead(out _);

		using CancellationTokenSource listen = CancellationTokenSource.CreateLinkedTokenSource(Token);
		Task<JsonRpcResponse> subscription = gateway.Client.SendRequestAsync(new JsonRpcRequest
		{
			Method = RequestMethods.SubscriptionsListen,
			Params = JsonSerializer.SerializeToNode(new SubscriptionsListenRequestParams
			{
				Notifications = new SubscriptionsListenNotifications { ResourcesListChanged = true }
			}, McpJsonUtilities.DefaultOptions)
		}, listen.Token);
		await ListUrisAsync(gateway.Client);
		File.Delete(Path.Combine(gateway.Registry.DirectoryPath,
			second.Descriptor.ActivationId.ToString("N") + ".json"));
		JsonRpcNotification subscribed =
			await changes.Reader.ReadAsync(Token).AsTask().WaitAsync(NotificationTimeout, Token);
		await listen.CancelAsync();
		try
		{
			await subscription;
		}
		catch (OperationCanceledException)
		{
			// The listen stream ends with its request.
		}

		Assert.False(unsolicited);
		Assert.NotNull(subscribed.Params?["_meta"]?["io.modelcontextprotocol/subscriptionId"]);
	}

	private static async Task<string[]> ListUrisAsync(McpClient client)
	{
		ListResourcesResult result = await client.ListResourcesAsync(new ListResourcesRequestParams(), Token);
		return [.. result.Resources.Select(static resource => resource.Uri)];
	}

	/// <summary>
	///     A registry with one published fake backend, and the gateway's listing, verifier and monitor over a server
	///     options whose resource collection counts its changes.
	/// </summary>
	private sealed class ListFixture : IAsyncDisposable
	{
		internal const string DocumentUri = "cheatengine://docs/listed";

		private readonly string _directory;
		private int _changes;

		private ListFixture(string directory, InstanceRegistry registry, FakeBackend backend)
		{
			_directory = directory;
			Registry = registry;
			Backend = backend;
			Verifier = new InstanceIdentityVerifier(Time);
			Live = new GatewayLiveInstances(registry, Verifier, Time, NullLogger<GatewayLiveInstances>.Instance);
			Collection =
			[
				McpServerResource.Create(static () => "listed",
					new McpServerResourceCreateOptions { UriTemplate = DocumentUri, Name = "doc_listed" })
			];
			Collection.Changed += (_, _) => Interlocked.Increment(ref _changes);
			Monitor = new GatewayResourceListMonitor(Live,
				Options.Create(new McpServerOptions { ResourceCollection = Collection }), Time,
				NullLogger<GatewayResourceListMonitor>.Instance);
		}

		internal ManualTimeProvider Time
		{
			get;
		} = new();

		internal InstanceRegistry Registry
		{
			get;
		}

		internal FakeBackend Backend
		{
			get;
		}

		internal InstanceIdentityVerifier Verifier
		{
			get;
		}

		internal GatewayLiveInstances Live
		{
			get;
		}

		internal McpServerResourceCollection Collection
		{
			get;
		}

		internal GatewayResourceListMonitor Monitor
		{
			get;
		}

		internal int Changes => Volatile.Read(ref _changes);

		public async ValueTask DisposeAsync()
		{
			await Backend.DisposeAsync();
			Verifier.Dispose();
			Monitor.Dispose();
			if (Directory.Exists(_directory))
			{
				Directory.Delete(_directory, true);
			}
		}

		internal static async Task<ListFixture> StartAsync()
		{
			string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.ListTests-{Guid.NewGuid():N}");
			InstanceRegistry registry = new(directory);
			return new ListFixture(directory, registry, await FakeBackend.StartAsync(registry, "listed"));
		}

		/// <summary>Deletes the backend's registry record, as a disabled plugin would.</summary>
		internal void Withdraw()
		{
			File.Delete(RecordPath());
		}

		/// <summary>Publishes the same record again.</summary>
		internal void Republish()
		{
			Registry.Publish(Backend.Descriptor);
		}

		private string RecordPath()
		{
			return Path.Combine(Registry.DirectoryPath, Backend.Descriptor.ActivationId.ToString("N") + ".json");
		}
	}

	/// <summary>The gateway executable over stdio with a fresh instance directory, as an AI client starts it.</summary>
	private sealed class GatewayProcess : IAsyncDisposable
	{
		private readonly string _directory;

		private GatewayProcess(string directory, InstanceRegistry registry, McpClient client)
		{
			_directory = directory;
			Registry = registry;
			Client = client;
		}

		internal InstanceRegistry Registry
		{
			get;
		}

		internal McpClient Client
		{
			get;
		}

		public async ValueTask DisposeAsync()
		{
			await Client.DisposeAsync();
			await GatewayTestDirectory.DeleteAsync(_directory);
		}

		internal static async Task<GatewayProcess> StartAsync(string protocolVersion)
		{
			string executable = Path.Combine(AppContext.BaseDirectory, "CheatEngine.Mcp.Gateway.exe");
			Assert.True(File.Exists(executable), $"The gateway executable is missing: {executable}");
			string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
			string instances = Path.Combine(directory, "instances");
			Directory.CreateDirectory(directory);
			McpClient client = await McpClient.CreateAsync(
				new StdioClientTransport(new StdioClientTransportOptions
				{
					Command = executable,
					Arguments = ["--instance-directory", instances],
					Name = "CheatEngine.Mcp.Tests.GatewayListChanged",
					// Outside the deleted tree: a gateway still exiting holds its working directory.
					WorkingDirectory = AppContext.BaseDirectory,
					ShutdownTimeout = TimeSpan.FromSeconds(10)
				}), new McpClientOptions
				{
					ClientInfo = new Implementation { Name = "CheatEngine.Mcp.Tests", Version = "2.0.0" },
					ProtocolVersion = protocolVersion
				}, cancellationToken: Token);
			return new GatewayProcess(directory, new InstanceRegistry(instances), client);
		}
	}
}
