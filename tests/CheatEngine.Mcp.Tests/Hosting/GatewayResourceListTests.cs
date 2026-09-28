using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Hosting;

/// <summary>
///     The gateway's <c>resources/list</c>: the concrete live resources of every instance whose identity the gateway
///     verified, in their gateway form, read from the registry and the identity cache without contacting any backend.
/// </summary>
public sealed class GatewayResourceListTests
{
	/// <summary>Every concrete (untemplated) live resource, after <c>cheatengine://instance/</c>, by URI.</summary>
	private static readonly string[] ConcretePaths =
	[
		"debugger", "jobs", "patches", "pointer-maps", "pointer-scans", "process", "resources", "runtime", "scanners",
		"speedhack", "threads"
	];

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ListResources_UnverifiedInstance_ListsNoLiveResourceAndContactsNoBackend()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "unverified");
		await using McpClient client = await gateway.ConnectAsync();

		ListResourcesResult result = await client.ListResourcesAsync(new ListResourcesRequestParams(), Token);

		Assert.Empty(Live(result));
		Assert.Equal(McpResourceUris.GatewayInstances, result.Resources[0].Uri);
		Assert.Equal((TimeSpan.Zero, CacheScope.Private), (result.TimeToLive, result.CacheScope));
		Assert.Equal(0, backend.IdentityProbeCount);
		Assert.Equal(0, backend.InitializeCount);
	}

	[Fact]
	public async Task ListResources_TwoVerifiedInstances_ListEachConcreteLiveResourceInGatewayForm()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend first = await FakeBackend.StartAsync(gateway.Registry, "first");
		await using FakeBackend second = await FakeBackend.StartAsync(gateway.Registry, "second");
		await using McpClient client = await gateway.ConnectAsync();
		await client.CallToolAsync(CheatEngineToolNames.InstanceList, new Dictionary<string, object?>(),
			cancellationToken: Token);
		int probes = first.IdentityProbeCount + second.IdentityProbeCount;

		ListResourcesResult result = await client.ListResourcesAsync(new ListResourcesRequestParams(), Token);

		// Ordered like instance_list (by name, then id), each instance's resources by URI, before the documents.
		Resource[] live = Live(result);
		Assert.Equal(
		[
			.. ConcretePaths.Select(path => $"cheatengine://instances/{first.Descriptor.InstanceId}/{path}"),
			.. ConcretePaths.Select(path => $"cheatengine://instances/{second.Descriptor.InstanceId}/{path}")
		], live.Select(static resource => resource.Uri));
		Assert.Equal(live, result.Resources.Skip(1).Take(live.Length));
		foreach (FakeBackend backend in new[] { first, second })
		{
			string instance = $" (CE '{backend.Descriptor.Name}', pid {backend.Descriptor.ProcessId})";
			Assert.All(live.Where(resource => resource.Uri.Contains(backend.Descriptor.InstanceId,
					StringComparison.Ordinal)),
				resource => Assert.EndsWith(instance, resource.Title, StringComparison.Ordinal));
		}

		Assert.All(live, static resource =>
		{
			Assert.StartsWith("instance_", resource.Name, StringComparison.Ordinal);
			Assert.Equal(McpResourceUris.JsonMimeType, resource.MimeType);
			Assert.Equal([Role.Assistant], resource.Annotations!.Audience);
			Assert.Equal(0.3f, resource.Annotations.Priority);
			Assert.Contains(resource.Meta![McpSourceToolAttribute.MetaKey]!.GetValue<string>(),
				(IEnumerable<string>) CheatEngineToolNames.Backend);
		});
		Resource process = Assert.Single(live, resource =>
			resource.Uri == $"cheatengine://instances/{first.Descriptor.InstanceId}/process");
		Assert.Equal("instance_process", process.Name);
		Assert.Equal($"Current process (CE 'first', pid {first.Descriptor.ProcessId})", process.Title);
		Assert.Equal(CheatEngineToolNames.ProcessGetCurrent,
			process.Meta![McpSourceToolAttribute.MetaKey]!.GetValue<string>());
		Assert.StartsWith("The process currently selected", process.Description, StringComparison.Ordinal);
		// Listing reused the identities instance_list confirmed: it probed nothing.
		Assert.Equal(probes, first.IdentityProbeCount + second.IdentityProbeCount);
	}

	[Fact]
	public async Task ListResources_IdentityOlderThanTheCompletionWindow_StaysListedWhileTheRecordIsActive()
	{
		ManualTimeProvider time = new();
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.ListTests-{Guid.NewGuid():N}");
		InstanceRegistry registry = new(directory);
		using InstanceIdentityVerifier verifier = new(time);
		try
		{
			await using FakeBackend backend = await FakeBackend.StartAsync(registry, "kept");
			GatewayLiveInstances live = new(registry, verifier, time, NullLogger<GatewayLiveInstances>.Instance);
			await verifier.VerifyAsync(backend.Descriptor, Token);
			time.Advance(InstanceIdentityVerifier.VerificationWindow + TimeSpan.FromMinutes(10));

			Assert.Empty(verifier.RecentlyVerified(InstanceIdentityVerifier.VerificationWindow));
			Assert.Equal([backend.Descriptor.InstanceId], live.Read(Token).Select(static entry => entry.InstanceId));
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
	public async Task ListResources_WithdrawnOrStaleInstance_IsNoLongerListed()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend withdrawn = await FakeBackend.StartAsync(gateway.Registry, "withdrawn");
		await using FakeBackend stale = await FakeBackend.StartAsync(gateway.Registry, "stale");
		await using FakeBackend kept = await FakeBackend.StartAsync(gateway.Registry, "kept");
		await using McpClient client = await gateway.ConnectAsync();
		await client.CallToolAsync(CheatEngineToolNames.InstanceList, new Dictionary<string, object?>(),
			cancellationToken: Token);
		File.Delete(Path.Combine(gateway.Registry.DirectoryPath,
			withdrawn.Descriptor.ActivationId.ToString("N") + ".json"));
		stale.ReportedIdentity = stale.Descriptor with
		{
			ActivationId = Guid.NewGuid()
		};
		CallToolResult refused = await client.CallToolAsync("runtime_get_info", stale.RoutedArguments(),
			cancellationToken: Token);

		ListResourcesResult result = await client.ListResourcesAsync(new ListResourcesRequestParams(), Token);

		Assert.True(refused.IsError);
		Assert.Equal(ConcretePaths.Select(path => $"cheatengine://instances/{kept.Descriptor.InstanceId}/{path}"),
			Live(result).Select(static resource => resource.Uri));
	}

	[Fact]
	public async Task ReadResource_ListedLiveResource_IsRoutedToItsInstance()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "read");
		await using McpClient client = await gateway.ConnectAsync();
		await client.ReadResourceAsync(McpResourceUris.GatewayInstances, cancellationToken: Token);
		ListResourcesResult listed = await client.ListResourcesAsync(new ListResourcesRequestParams(), Token);
		string runtime = Assert.Single(Live(listed), static resource => resource.Name == "instance_runtime").Uri;

		ReadResourceResult result = await client.ReadResourceAsync(runtime, cancellationToken: Token);

		Assert.Equal(["cheatengine://instance/runtime"], backend.ReadUris);
		Assert.Equal(runtime, Assert.IsType<TextResourceContents>(result.Contents[0]).Uri);
	}

	[Fact]
	public async Task ListResources_InstanceList_IsAnnotatedForAssistantAndUserWithItsSourceTool()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using McpClient client = await gateway.ConnectAsync();

		ListResourcesResult result = await client.ListResourcesAsync(new ListResourcesRequestParams(), Token);

		Resource instances = result.Resources[0];
		Assert.Equal(McpResourceUris.GatewayInstances, instances.Uri);
		Assert.Equal([Role.Assistant, Role.User], instances.Annotations!.Audience);
		Assert.Equal(GatewayResourceCatalog.InstancesPriority, instances.Annotations.Priority);
		Assert.Equal(CheatEngineToolNames.InstanceList,
			instances.Meta![McpSourceToolAttribute.MetaKey]!.GetValue<string>());
	}

	[Fact]
	public async Task Initialize_StatelessTransport_AdvertisesNoResourceListChanged()
	{
		// The SDK drops listChanged where it cannot deliver it; the stdio gateway advertises it (gateway-initialize).
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using McpClient client = await gateway.ConnectAsync();

		Assert.NotEqual(true, client.ServerCapabilities.Resources?.ListChanged);
	}

	private static Resource[] Live(ListResourcesResult result)
	{
		return
		[
			.. result.Resources.Where(static resource =>
				resource.Uri.StartsWith(McpResourceUris.GatewayInstancesPrefix, StringComparison.Ordinal))
		];
	}
}
