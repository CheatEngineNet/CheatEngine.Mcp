using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

using CheatEngine.Mcp.Hosting.Discovery;
using CheatEngine.Mcp.Hosting.Gateway;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Hosting;

/// <summary>Discovery keeps verified candidates, omits unavailable records and labels an incomplete deadline honestly.</summary>
[Collection(nameof(SerialTestGroup))]
public sealed class GatewayInstanceDiscoveryTests
{
	[Theory]
	[InlineData(false, "missing")]
	[InlineData(true, "ready")]
	public async Task ListAsync_MissingOrEmptyRegistry_ExplainsEmptyDiscovery(bool createDirectory, string status)
	{
		string directory = NewDirectory();
		if (createDirectory)
		{
			Directory.CreateDirectory(directory);
		}

		using InstanceIdentityVerifier verifier = new(TimeProvider.System);
		try
		{
			GatewayInstanceTool tool = new(new InstanceRegistry(directory), verifier);
			InstanceListResult result = await tool.ListAsync(TestContext.Current.CancellationToken);

			Assert.Empty(result.Instances);
			Assert.False(result.DiscoveryIncomplete);
			Assert.Equal(status, result.Diagnostics.RegistryStatus);
			Assert.Equal(0, result.Diagnostics.RecordsFound);
			Assert.Equal(0, result.Diagnostics.Candidates);
			Assert.Equal(0, result.Diagnostics.RejectedRecords);
			Assert.Equal(0, result.Diagnostics.ExpiredRecords);
			Assert.Equal(0, result.Diagnostics.UnobservableProcesses);
			Assert.Equal(0, result.Diagnostics.UnavailableInstances);
			Assert.Equal(0, result.Diagnostics.PendingInstances);
			string payload = JsonSerializer.Serialize(result, HostingJsonContext.Default.InstanceListResult);
			Assert.DoesNotContain(directory, payload, StringComparison.Ordinal);
			using JsonDocument serialized = JsonDocument.Parse(payload);
			Assert.Equal(status, serialized.RootElement.GetProperty("diagnostics")
				.GetProperty("registryStatus").GetString());
		}
		finally
		{
			await GatewayTestDirectory.DeleteAsync(directory);
		}
	}

	[Fact]
	public async Task ListAsync_InvalidAndExpiredRecords_ReportsReasonsWithoutRecordContents()
	{
		string directory = NewDirectory();
		InstanceRegistry registry = new(directory);
		FakeBackend healthy = await FakeBackend.StartAsync(registry, "healthy");
		using InstanceIdentityVerifier verifier = new(TimeProvider.System);
		InstanceDescriptor expired = CreateDescriptor("expired-secret-name", healthy.Descriptor.Endpoint) with
		{
			ProcessStartUtcTicks = healthy.Descriptor.ProcessStartUtcTicks - TimeSpan.TicksPerSecond
		};
		string expiredPath = Path.Combine(directory, expired.ActivationId.ToString("N") + ".json");
		try
		{
			await File.WriteAllTextAsync(expiredPath,
				JsonSerializer.Serialize(expired, HostingJsonContext.Default.InstanceDescriptor),
				TestContext.Current.CancellationToken);
			await File.WriteAllTextAsync(Path.Combine(directory, "malformed.json"), "{broken-secret-record",
				TestContext.Current.CancellationToken);
			await File.WriteAllTextAsync(Path.Combine(directory, "oversized.json"), new string('x', 16385),
				TestContext.Current.CancellationToken);
			await File.WriteAllTextAsync(Path.Combine(directory, "invalid.json"), "null",
				TestContext.Current.CancellationToken);
			using FileStream unreadable = new(Path.Combine(directory, "unreadable.json"), FileMode.Create,
				FileAccess.ReadWrite, FileShare.None);
			GatewayInstanceTool tool = new(registry, verifier);
			InstanceListResult result = await tool.ListAsync(TestContext.Current.CancellationToken);

			Assert.Equal(healthy.Descriptor.InstanceId, Assert.Single(result.Instances).InstanceId);
			Assert.False(result.DiscoveryIncomplete);
			Assert.Equal("ready", result.Diagnostics.RegistryStatus);
			Assert.Equal(6, result.Diagnostics.RecordsFound);
			Assert.Equal(1, result.Diagnostics.Candidates);
			Assert.Equal(4, result.Diagnostics.RejectedRecords);
			Assert.Equal(1, result.Diagnostics.ExpiredRecords);
			Assert.Equal(0, result.Diagnostics.UnobservableProcesses);
			Assert.Equal(0, result.Diagnostics.UnavailableInstances);
			Assert.Equal(0, result.Diagnostics.PendingInstances);
			Assert.False(File.Exists(expiredPath));
			string payload = JsonSerializer.Serialize(result, HostingJsonContext.Default.InstanceListResult);
			Assert.DoesNotContain("expired-secret-name", payload, StringComparison.Ordinal);
			Assert.DoesNotContain("broken-secret-record", payload, StringComparison.Ordinal);
			Assert.DoesNotContain(expired.AccessToken, payload, StringComparison.Ordinal);
			Assert.DoesNotContain(healthy.Descriptor.AccessToken, payload, StringComparison.Ordinal);
			Assert.DoesNotContain(healthy.Descriptor.Endpoint, payload, StringComparison.Ordinal);
			Assert.DoesNotContain(JsonSerializer.Serialize(directory), payload, StringComparison.Ordinal);
		}
		finally
		{
			await healthy.DisposeAsync();
			await GatewayTestDirectory.DeleteAsync(directory);
		}
	}

	[Fact]
	public async Task ListAsync_RegistryPathIsAFile_ReportsUnavailable()
	{
		string path = NewDirectory();
		await File.WriteAllTextAsync(path, "not-a-registry", TestContext.Current.CancellationToken);
		using InstanceIdentityVerifier verifier = new(TimeProvider.System);
		try
		{
			GatewayInstanceTool tool = new(new InstanceRegistry(path), verifier);
			InstanceListResult result = await tool.ListAsync(TestContext.Current.CancellationToken);

			Assert.Empty(result.Instances);
			Assert.False(result.DiscoveryIncomplete);
			Assert.Equal("unavailable", result.Diagnostics.RegistryStatus);
			Assert.Equal(0, result.Diagnostics.RecordsFound);
			Assert.Equal(0, result.Diagnostics.Candidates);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public async Task ListAsync_StaleCandidateAlongsideHealthyCandidate_ReturnsOnlyVerifiedInstanceWithoutSecrets()
	{
		string directory = NewDirectory();
		InstanceRegistry registry = new(directory);
		FakeBackend healthy = await FakeBackend.StartAsync(registry, "healthy");
		FakeBackend stale = await FakeBackend.StartAsync(registry, "stale");
		InstanceIdentityVerifier verifier = new(TimeProvider.System);
		try
		{
			stale.ReportedIdentity = stale.Descriptor with
			{
				ActivationId = Guid.NewGuid()
			};
			GatewayInstanceTool tool = new(registry, verifier);

			InstanceListResult result = await tool.ListAsync(TestContext.Current.CancellationToken);
			string payload = JsonSerializer.Serialize(result, HostingJsonContext.Default.InstanceListResult);

			Assert.Equal([healthy.Descriptor.InstanceId], result.Instances.Select(static instance => instance.InstanceId));
			Assert.False(result.DiscoveryIncomplete);
			Assert.Equal(2, result.Diagnostics.Candidates);
			Assert.Equal(1, result.Diagnostics.UnavailableInstances);
			Assert.Equal(0, result.Diagnostics.PendingInstances);
			Assert.DoesNotContain(healthy.Descriptor.AccessToken, payload, StringComparison.Ordinal);
			Assert.DoesNotContain(stale.Descriptor.AccessToken, payload, StringComparison.Ordinal);
			Assert.DoesNotContain(healthy.Descriptor.Endpoint, payload, StringComparison.Ordinal);
			Assert.DoesNotContain(stale.Descriptor.Endpoint, payload, StringComparison.Ordinal);
			Assert.True(verifier.IsConfirmed(healthy.Descriptor));
			Assert.False(verifier.IsConfirmed(stale.Descriptor));
		}
		finally
		{
			verifier.Dispose();
			await stale.DisposeAsync();
			await healthy.DisposeAsync();
			await GatewayTestDirectory.DeleteAsync(directory);
		}
	}

	[Fact]
	public async Task ListAsync_DeadlineExpiresWithOneBlockedCandidate_ReturnsVerifiedCandidatesAndMarksIncomplete()
	{
		string directory = NewDirectory();
		InstanceRegistry registry = new(directory);
		FakeBackend healthy = await FakeBackend.StartAsync(registry, "healthy");
		HangingIdentityEndpoint blocked = HangingIdentityEndpoint.Start();
		ManualClock time = new();
		InstanceIdentityVerifier verifier = new(time);
		try
		{
			InstanceDescriptor stalled = CreateDescriptor("stalled", blocked.Endpoint);
			registry.Publish(stalled);
			GatewayInstanceTool tool = new(registry, verifier, time, TimeSpan.FromHours(1));

			Task<InstanceListResult> listing = tool.ListAsync(CancellationToken.None);
			await WaitUntilAsync(() => verifier.IsConfirmed(healthy.Descriptor));
			await blocked.WaitForConnectionAsync();
			Assert.False(listing.IsCompleted);

			time.Advance(TimeSpan.FromHours(1));
			InstanceListResult result = await listing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

			Assert.Equal([healthy.Descriptor.InstanceId], result.Instances.Select(static instance => instance.InstanceId));
			Assert.True(result.DiscoveryIncomplete);
			Assert.Equal(2, result.Diagnostics.Candidates);
			Assert.Equal(0, result.Diagnostics.UnavailableInstances);
			Assert.Equal(1, result.Diagnostics.PendingInstances);
			Assert.True(verifier.IsConfirmed(healthy.Descriptor));
			Assert.False(verifier.IsConfirmed(stalled));
		}
		finally
		{
			verifier.Dispose();
			await blocked.DisposeAsync();
			await healthy.DisposeAsync();
			await GatewayTestDirectory.DeleteAsync(directory);
		}
	}

	[Fact]
	public async Task ListAsync_CallerCancellationDuringBlockedDiscovery_RethrowsCallerCancellation()
	{
		string directory = NewDirectory();
		InstanceRegistry registry = new(directory);
		HangingIdentityEndpoint blocked = HangingIdentityEndpoint.Start();
		ManualClock time = new();
		InstanceIdentityVerifier verifier = new(time);
		using CancellationTokenSource caller = new();
		try
		{
			registry.Publish(CreateDescriptor("stalled", blocked.Endpoint));
			GatewayInstanceTool tool = new(registry, verifier, time, TimeSpan.FromHours(1));

			Task<InstanceListResult> listing = tool.ListAsync(caller.Token);
			await blocked.WaitForConnectionAsync();
			caller.Cancel();

			await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
				await listing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
			Assert.False(verifier.RecentlyVerified(InstanceIdentityVerifier.VerificationWindow).Any());
		}
		finally
		{
			verifier.Dispose();
			await blocked.DisposeAsync();
			await GatewayTestDirectory.DeleteAsync(directory);
		}
	}

	private static string NewDirectory()
	{
		return Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.InstanceDiscovery-{Guid.NewGuid():N}");
	}

	private static InstanceDescriptor CreateDescriptor(string name, string endpoint)
	{
		using Process process = Process.GetCurrentProcess();
		Guid activation = Guid.NewGuid();
		return new InstanceDescriptor($"ce-{process.Id}-{activation:N}", name, activation, process.Id,
			process.StartTime.ToUniversalTime().Ticks, endpoint, RandomNumberGenerator.GetHexString(64), "2.0.0");
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		using CancellationTokenSource setup = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		setup.CancelAfter(TimeSpan.FromSeconds(10));
		while (!condition())
		{
			// Wait for loopback setup; the discovery deadline itself advances only through ManualClock.
			await Task.Delay(TimeSpan.FromMilliseconds(10), setup.Token);
		}
	}

	private sealed class HangingIdentityEndpoint : IAsyncDisposable
	{
		private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
		private readonly Task<TcpClient> _connection;

		private HangingIdentityEndpoint()
		{
			_listener.Start();
			_connection = _listener.AcceptTcpClientAsync(CancellationToken.None).AsTask();
		}

		internal string Endpoint => $"http://127.0.0.1:{((IPEndPoint) _listener.LocalEndpoint).Port}/";

		internal static HangingIdentityEndpoint Start()
		{
			return new HangingIdentityEndpoint();
		}

		internal async Task WaitForConnectionAsync()
		{
			await _connection.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
		}

		public async ValueTask DisposeAsync()
		{
			_listener.Stop();
			try
			{
				using TcpClient client = await _connection;
			}
			catch (SocketException)
			{
				// Stopping a listener with no connection is expected during test cleanup.
			}
		}
	}
}
