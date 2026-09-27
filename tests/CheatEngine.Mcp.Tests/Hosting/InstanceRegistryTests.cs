using System.Text.Json;

using CheatEngine.Mcp.Tests.Contract;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Hosting;

public sealed class InstanceRegistryTests : IDisposable
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly string _directory =
		Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.RegistryTests-{Guid.NewGuid():N}");

	public void Dispose()
	{
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, true);
		}
	}

	[Fact]
	public void Publish_SameLabels_HaveDistinctImmutableRoutesAndIndependentWithdrawal()
	{
		InstanceRegistry registry = new(_directory);
		using InstancePublication first = new(registry, "same-label");
		using InstancePublication second = new(registry, "same-label");
		first.Publish("http://127.0.0.1:40001/");
		second.Publish("http://127.0.0.1:40002/");
		Assert.NotEqual(first.Descriptor.InstanceId, second.Descriptor.InstanceId);
		Assert.Equal(2, registry.ReadActive(TestContext.Current.CancellationToken).Count);
		Assert.Equal(first.Descriptor, registry.Find(first.Descriptor.InstanceId));
		first.Dispose();
		Assert.Equal(second.Descriptor, Assert.Single(registry.ReadActive(TestContext.Current.CancellationToken)));
		Assert.Throws<InvalidOperationException>(() => registry.Find(first.Descriptor.InstanceId));
	}

	[Fact]
	public void ReadActive_ReusedPidWithDifferentStartTime_IsNotReturned()
	{
		InstanceRegistry registry = new(_directory);
		using InstancePublication publication = new(registry, "expired");
		publication.Publish("http://127.0.0.1:40001/");
		InstanceDescriptor replaced = publication.Descriptor with
		{
			ProcessStartUtcTicks = publication.Descriptor.ProcessStartUtcTicks - TimeSpan.TicksPerSecond
		};
		WriteRecord(replaced);
		Assert.Empty(registry.ReadActive(TestContext.Current.CancellationToken));
	}

	[Theory]
	[InlineData("http://example.com:40001/")]
	[InlineData("http://localhost:40001/")]
	[InlineData("https://127.0.0.1:40001/")]
	[InlineData("http://127.0.0.1:0/")]
	[InlineData("http://127.0.0.1:40001/path")]
	[InlineData("http://user:password@127.0.0.1:40001/")]
	public void ReadActive_NoncanonicalOrRemoteEndpoint_IsNotReturned(string endpoint)
	{
		InstanceRegistry registry = new(_directory);
		using InstancePublication publication = new(registry, "bad-endpoint");
		WriteRecord(publication.Descriptor with
		{
			Endpoint = endpoint
		});
		Assert.Empty(registry.ReadActive(TestContext.Current.CancellationToken));
	}

	[Fact]
	public void ReadActive_CorruptedRecord_DoesNotHideValidInstance()
	{
		InstanceRegistry registry = new(_directory);
		using InstancePublication publication = new(registry, "good");
		publication.Publish("http://127.0.0.1:40001/");
		File.WriteAllText(Path.Combine(_directory, "broken.json"), "{broken");
		Assert.Equal(publication.Descriptor, Assert.Single(registry.ReadActive(TestContext.Current.CancellationToken)));
	}

	[Fact]
	public void Catalog_WithoutCheatEngine_PreservesCompleteBackendSurface()
	{
		Assert.Equal(ToolContractTests.GetToolNames(),
			McpPrimitiveCatalog.Create(TestComposition.BackendManifest).Tools.Select(tool => tool.Name));
		Assert.All(McpPrimitiveCatalog.Create(TestComposition.BackendManifest).Tools,
			tool => Assert.False(tool.InputSchema.GetProperty("properties").TryGetProperty("instanceId", out _)));
	}

	private void WriteRecord(InstanceDescriptor instance)
	{
		Directory.CreateDirectory(_directory);
		File.WriteAllText(Path.Combine(_directory, instance.ActivationId.ToString("N") + ".json"),
			JsonSerializer.Serialize(instance, JsonOptions));
	}
}
