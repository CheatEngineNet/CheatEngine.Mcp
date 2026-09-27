using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using CheatEngine.Mcp.Plugin;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Hosting;

/// <summary>
///     The source-generated registry record, <c>/instance</c> body and <c>instance_list</c> result are byte-identical
///     to the reflection output with Web defaults that they replaced. The literals are that previous output.
/// </summary>
[Collection(nameof(SerialTestGroup))]
public sealed class HostingJsonContextTests
{
	private const string InstanceId = "ce-4242-0123456789abcdef0123456789abcdef";

	// The default encoder escapes HTML-sensitive and non-ASCII characters; the name proves both paths agree.
	private const string EscapedName = @"Cheat Engine \u003Cmain\u003E \u0026 \u0022\u00E9\u0022";

	private const string DescriptorJson =
		"{\"instanceId\":\"" + InstanceId + "\",\"name\":\"" + EscapedName + "\","
		+ "\"activationId\":\"01234567-89ab-cdef-0123-456789abcdef\",\"processId\":4242,"
		+ "\"processStartUtcTicks\":638000000000000000,\"endpoint\":\"http://127.0.0.1:40001/\","
		+ "\"accessToken\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"pluginVersion\":\"2.0.0.0\"}";

	private const string IdentityJson =
		"{\"instanceId\":\"" + InstanceId + "\",\"activationId\":\"01234567-89ab-cdef-0123-456789abcdef\","
		+ "\"processId\":4242,\"processStartUtcTicks\":638000000000000000,\"pluginVersion\":\"2.0.0.0\"}";

	private const string ListJson =
		"{\"instances\":[{\"instanceId\":\"" + InstanceId + "\",\"name\":\"" + EscapedName + "\","
		+ "\"processId\":4242,\"pluginVersion\":\"2.0.0.0\"}],\"discoveryIncomplete\":true}";

	private static readonly InstanceDescriptor Descriptor = new(InstanceId, "Cheat Engine <main> & \"\u00E9\"",
		Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"), 4242, 638000000000000000, "http://127.0.0.1:40001/",
		new string('a', 64), "2.0.0.0");

	[Fact]
	public void InstanceDescriptor_SourceGeneratedJson_MatchesTheRegistryFileFormat()
	{
		string json = JsonSerializer.Serialize(Descriptor, HostingJsonContext.Default.InstanceDescriptor);

		Assert.Equal(DescriptorJson, json);
		Assert.Equal(Descriptor, JsonSerializer.Deserialize(json, HostingJsonContext.Default.InstanceDescriptor));
	}

	[Fact]
	public void InstanceIdentity_BackendAndGateway_ShareOneWireShape()
	{
		InstanceIdentity identity = InstanceIdentity.From(Descriptor);
		string json = JsonSerializer.Serialize(identity, HostingJsonContext.Default.InstanceIdentity);

		Assert.Equal(IdentityJson, json);
		Assert.DoesNotContain(Descriptor.AccessToken, json, StringComparison.Ordinal);
		Assert.DoesNotContain("endpoint", json, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(identity, JsonSerializer.Deserialize(json, HostingJsonContext.Default.InstanceIdentity));
		// A backend that reports more fields, such as a full record, still yields the same identity.
		Assert.Equal(identity, JsonSerializer.Deserialize(DescriptorJson, HostingJsonContext.Default.InstanceIdentity));
	}

	[Fact]
	public void InstanceListResult_SourceGeneratedJson_KeepsTheInstanceListShape()
	{
		InstanceListResult result = new(
			[new InstanceListEntry(InstanceId, Descriptor.Name, Descriptor.ProcessId, Descriptor.PluginVersion)], true);

		Assert.Equal(ListJson,
			JsonSerializer.SerializeToElement(result, HostingJsonContext.Default.InstanceListResult).GetRawText());
	}

	/// <remarks>
	///     Compares with the reflection serializer these types replaced. A reflection-free test run cannot execute it;
	///     the literals above pin the same bytes.
	/// </remarks>
	[Fact]
	public void SourceGeneratedJson_PreviousReflectionOutput_IsByteIdentical()
	{
		JsonSerializerOptions reflection = new(JsonSerializerDefaults.Web);
		InstanceListEntry[] entries =
			[new(InstanceId, Descriptor.Name, Descriptor.ProcessId, Descriptor.PluginVersion)];

		// The registry record, the backend's former anonymous /instance body and the former list_instances payload.
		string descriptor = JsonSerializer.Serialize(Descriptor, reflection);
		string identity = JsonSerializer.Serialize(
			new
			{
				Descriptor.InstanceId,
				Descriptor.ActivationId,
				Descriptor.ProcessId,
				Descriptor.ProcessStartUtcTicks,
				Descriptor.PluginVersion
			}, reflection);
		string list = JsonSerializer.Serialize(new { instances = entries, discoveryIncomplete = true }, reflection);

		Assert.Equal(DescriptorJson, descriptor);
		Assert.Equal(IdentityJson, identity);
		Assert.Equal(ListJson, list);
	}

	[Fact]
	public void Publish_RegistryFile_HoldsTheSourceGeneratedRecordAndReadsBack()
	{
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.JsonContextTests-{Guid.NewGuid():N}");
		try
		{
			InstanceRegistry registry = new(directory);
			using InstancePublication publication = new(registry, "Cheat Engine <main> & \"\u00E9\"");
			publication.Publish("http://127.0.0.1:40001/");

			byte[] file = File.ReadAllBytes(Path.Combine(directory,
				publication.Descriptor.ActivationId.ToString("N") + ".json"));
			Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(publication.Descriptor,
				HostingJsonContext.Default.InstanceDescriptor), file);
			Assert.Equal(publication.Descriptor,
				Assert.Single(registry.ReadActive(TestContext.Current.CancellationToken)));
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
	public async Task Instance_PublishedBackend_ServesTheIdentityJson()
	{
		string root = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.JsonContextTests-{Guid.NewGuid():N}");
		using TestActivation activation = new(ClientTestDouble.Client());
		using PluginLog log = new(Path.Combine(root, "logs"));
		InstancePublication publication = new(new InstanceRegistry(Path.Combine(root, "instances")), "identity");
		McpBackendHost server = new(new McpBackendOptions(), log, TestRuntime.Info, activation.Manifest,
			activation.Targets, CancellationToken.None, publication);
		try
		{
			await server.StartAsync();
			using HttpClient http = new();
			using HttpRequestMessage request = new(HttpMethod.Get, new Uri(new Uri(server.Endpoint!), "instance"));
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publication.Descriptor.AccessToken);
			using HttpResponseMessage response = await http.SendAsync(request, TestContext.Current.CancellationToken);

			Assert.Equal(HttpStatusCode.OK, response.StatusCode);
			Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
			Assert.Equal(JsonSerializer.Serialize(InstanceIdentity.From(publication.Descriptor),
					HostingJsonContext.Default.InstanceIdentity),
				await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		}
		finally
		{
			await server.StopAsync();
			await TestLog.ReleaseAsync(log);
			if (Directory.Exists(root))
			{
				Directory.Delete(root, true);
			}
		}
	}
}
