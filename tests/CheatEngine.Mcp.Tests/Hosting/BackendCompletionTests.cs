using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tests.Tools.Modules;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Hosting;

/// <summary>
///     The completion of live templates on a real backend, whose stateless HTTP transport creates the server options
///     again for every request: the handler, its cache and its rate limit belong to the transport container instead.
/// </summary>
[Collection(nameof(SerialTestGroup))]
public sealed class BackendCompletionTests
{
	private const string Modules = McpResourceUris.InstancePrefix + "modules/{module}";
	private const string Exports = McpResourceUris.InstancePrefix + "modules/{module}/exports{?offset,limit}";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Complete_ModuleTemplatesOverStatelessHttp_ShareOneListingAcrossRequests()
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
		target.AddModule("game.exe", 0x140000000, 0x5000);
		using TestActivation activation = new(target.Client);
		McpBackendHost backend = new(new McpBackendOptions(), activation.Log, TestRuntime.Info, activation.Manifest,
			activation.Targets, CancellationToken.None);
		try
		{
			await backend.StartAsync();
			await using McpClient client = await McpClient.CreateAsync(new HttpClientTransport(
				new HttpClientTransportOptions
				{
					Endpoint = new Uri(backend.Endpoint!),
					Name = "CheatEngine.Mcp.Tests.BackendCompletion",
					TransportMode = HttpTransportMode.StreamableHttp
				}), cancellationToken: Token);

			CompleteResult module = await CompleteAsync(client, Modules, "SA");
			CompleteResult exports = await CompleteAsync(client, Exports, "");
			CompleteResult narrowed = await CompleteAsync(client, Modules, "g");

			Assert.NotNull(client.ServerCapabilities.Completions);
			Assert.Equal(["sample.dll"], module.Completion.Values);
			Assert.Equal(["sample.dll", "game.exe"], exports.Completion.Values);
			Assert.Equal(["game.exe"], narrowed.Completion.Values);
			// Three HTTP requests, three server option sets, one handler: one dispatch for the module names.
			Assert.Equal(1, target.Calls("TryGetModules"));
		}
		finally
		{
			await backend.StopAsync();
		}
	}

	private static async Task<CompleteResult> CompleteAsync(McpClient client, string template, string value)
	{
		return await client.CompleteAsync(new ResourceTemplateReference { Uri = template }, "module", value,
			cancellationToken: Token);
	}
}
