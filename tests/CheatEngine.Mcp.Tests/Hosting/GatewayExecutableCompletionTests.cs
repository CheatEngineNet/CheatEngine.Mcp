using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Hosting;

/// <summary>
///     The gateway executable, whose JSON has no reflection fallback like its Native AOT build, completes a routed live
///     template over stdio by forwarding to a real backend: only the SDK's generated protocol metadata is involved.
/// </summary>
[Collection(nameof(SerialTestGroup))]
public sealed class GatewayExecutableCompletionTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task GatewayExecutable_RoutedScannerTemplate_CompletesFromTheSelectedBackend()
	{
		string executable = Path.Combine(AppContext.BaseDirectory, "CheatEngine.Mcp.Gateway.exe");
		Assert.True(File.Exists(executable), $"The gateway executable is missing: {executable}");
		string directory = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		string instances = Path.Combine(directory, "instances");
		Directory.CreateDirectory(directory);
		using TestActivation activation = new(ClientTestDouble.Client());
		InstancePublication publication = new(new InstanceRegistry(instances), "completion");
		McpBackendHost backend = new(new McpBackendOptions(), activation.Log, TestRuntime.Info, activation.Manifest,
			activation.Targets, CancellationToken.None, publication);
		try
		{
			await backend.StartAsync();
			await using McpClient client = await McpClient.CreateAsync(
				new StdioClientTransport(new StdioClientTransportOptions
				{
					Command = executable,
					Arguments = ["--instance-directory", instances],
					Name = "CheatEngine.Mcp.Tests.GatewayCompletion",
					// Outside the deleted tree: a gateway still exiting holds its working directory.
					WorkingDirectory = AppContext.BaseDirectory,
					ShutdownTimeout = TimeSpan.FromSeconds(10)
				}), new McpClientOptions
				{
					ClientInfo = new Implementation { Name = "CheatEngine.Mcp.Tests", Version = "2.0.0" },
					ProtocolVersion = "2025-06-18"
				}, cancellationToken: Token);
			const string template = "cheatengine://instances/{instanceId}/scanners/{scannerName}";
			string instanceId = publication.Descriptor.InstanceId;

			CallToolResult listed = await client.CallToolAsync(CheatEngineToolNames.InstanceList,
				new Dictionary<string, object?>(), cancellationToken: Token);
			CompleteResult ids = await client.CompleteAsync(new ResourceTemplateReference { Uri = template },
				McpResourceUris.InstanceIdVariable, "ce-", cancellationToken: Token);
			CompleteResult scanners = await client.CompleteAsync(new CompleteRequestParams
			{
				Ref = new ResourceTemplateReference { Uri = template },
				Argument = new Argument { Name = "scannerName", Value = "ma" },
				Context = new CompleteContext
				{
					Arguments = new Dictionary<string, string> { [McpResourceUris.InstanceIdVariable] = instanceId }
				}
			}, Token);

			Assert.NotEqual(true, listed.IsError);
			Assert.NotNull(client.ServerCapabilities.Completions);
			Assert.Equal([instanceId], ids.Completion.Values);
			Assert.Equal(["main"], scanners.Completion.Values);
			Assert.Equal((1, false), (scanners.Completion.Total, scanners.Completion.HasMore));
		}
		finally
		{
			await backend.StopAsync();
			await GatewayTestDirectory.DeleteAsync(directory);
		}
	}
}
