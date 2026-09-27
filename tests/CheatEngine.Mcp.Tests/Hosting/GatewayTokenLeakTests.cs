using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.Logging;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Hosting;

/// <summary>
///     A real authenticated backend and the gateway, both logging at Trace: no bearer token may appear in any log line,
///     tool result, error text or exception the test observes.
/// </summary>
[Collection(nameof(SerialTestGroup))]
public sealed class GatewayTokenLeakTests
{
	[Fact]
	public async Task TraceLogging_ListCallAndErrorPaths_NeverExposeAToken()
	{
		LogCapture gatewayLogs = new();
		LogCapture backendLogs = new();
		LogCapture clientLogs = new();
		List<string> observed = [];
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(TimeSpan.FromSeconds(30), gatewayLogs,
			LogLevel.Trace);
		using TestActivation activation = new(ClientTestDouble.Client());
		InstancePublication publication = new(gateway.Registry, "leak-check");
		McpBackendHost backend = new(new McpBackendOptions(), backendLogs.ForBackend(LogLevel.Trace), TestRuntime.Info,
			activation.Manifest, activation.Targets, CancellationToken.None, publication);
		InstanceDescriptor forged;
		try
		{
			await backend.StartAsync();
			InstanceDescriptor record = publication.Descriptor;
			// A second record for the same backend with a token the backend never issued: its calls must fail with 401.
			forged = Forge(record);
			gateway.Registry.Publish(forged);
			using ILoggerFactory clientLoggers = LoggerFactory.Create(logging =>
				logging.SetMinimumLevel(LogLevel.Trace).AddProvider(clientLogs));
			await using McpClient client = await gateway.ConnectAsync(loggers: clientLoggers);

			IList<McpClientTool> tools =
				await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
			observed.Add(string.Join('\n', tools.Select(static tool => tool.ProtocolTool.Name)));
			CallToolResult listed = await CallAsync(client, observed, GatewayToolCatalog.InstanceListToolName, null);
			Assert.Contains(record.InstanceId, listed.StructuredContent!.Value.GetRawText(), StringComparison.Ordinal);
			Assert.DoesNotContain(forged.InstanceId, listed.StructuredContent!.Value.GetRawText(),
				StringComparison.Ordinal);

			Assert.NotEqual(true, (await CallAsync(client, observed, "get_plugin_version", record.InstanceId)).IsError);
			Assert.NotEqual(true, (await CallAsync(client, observed, "get_plugin_version", record.InstanceId)).IsError);
			Assert.True((await CallAsync(client, observed, "get_plugin_version", null)).IsError);
			Assert.True((await CallAsync(client, observed, "get_plugin_version", forged.InstanceId)).IsError);
			Assert.True((await CallAsync(client, observed, "get_plugin_version",
				"ce-1-00000000000000000000000000000000")).IsError);
			try
			{
				await CallAsync(client, observed, "no_such_tool", record.InstanceId);
			}
			catch (McpException exception)
			{
				observed.Add(exception.ToString());
			}

			await backend.StopAsync();
			// Stopping withdraws the record, so the old instanceId is now unknown.
			Assert.True((await CallAsync(client, observed, "get_plugin_version", record.InstanceId)).IsError);
		}
		finally
		{
			await backend.StopAsync();
		}

		// Trace really was on: the backend dumped MCP messages, and the gateway logged its own Debug diagnostics.
		Assert.Contains(backendLogs.Entries, static entry => entry.Level == LogLevel.Trace
															 && entry.Category.StartsWith("ModelContextProtocol",
																 StringComparison.Ordinal));
		Assert.Contains(gatewayLogs.Entries, static entry => entry.Level == LogLevel.Debug
															 && entry.Category.StartsWith(
																 "CheatEngine.Mcp.Hosting.Gateway",
																 StringComparison.Ordinal));
		Assert.Contains(clientLogs.Entries, static entry => entry.Level == LogLevel.Trace);
		// The gateway's floor held its token-handling framework categories at Information or above.
		Assert.DoesNotContain(gatewayLogs.Entries, static entry => entry.Level < LogLevel.Information
																   && TokenSafeLogging.GuardedCategories.Any(guarded =>
																	   entry.Category.StartsWith(guarded,
																		   StringComparison.Ordinal)));
		string[] tokens = [publication.Descriptor.AccessToken, forged.AccessToken];
		CapturedLog[] everything = [.. gatewayLogs.Entries, .. backendLogs.Entries, .. clientLogs.Entries];
		foreach (string token in tokens)
		{
			Assert.DoesNotContain(everything, entry => entry.Text.Contains(token, StringComparison.OrdinalIgnoreCase));
			Assert.DoesNotContain(observed, text => text.Contains(token, StringComparison.OrdinalIgnoreCase));
		}
	}

	[Fact]
	public void InstanceDescriptor_ToString_RedactsTheAccessToken()
	{
		InstanceDescriptor record = new("ce-1-00000000000000000000000000000001", "printed",
			Guid.Parse("00000000-0000-0000-0000-000000000001"), 1, 2, "http://127.0.0.1:40001/",
			RandomNumberGenerator.GetHexString(64), "2.0.0");

		string printed = record.ToString();

		Assert.DoesNotContain(record.AccessToken, printed, StringComparison.OrdinalIgnoreCase);
		Assert.Equal("InstanceDescriptor { InstanceId = ce-1-00000000000000000000000000000001, Name = printed, " +
					 "ActivationId = 00000000-0000-0000-0000-000000000001, ProcessId = 1, ProcessStartUtcTicks = 2, " +
					 "Endpoint = http://127.0.0.1:40001/, AccessToken = [redacted], PluginVersion = 2.0.0 }", printed);
	}

	private static async Task<CallToolResult> CallAsync(McpClient client, List<string> observed, string tool,
		string? instanceId)
	{
		Dictionary<string, object?> arguments = new(StringComparer.Ordinal);
		if (instanceId is not null)
		{
			arguments[GatewayToolCatalog.InstanceIdArgumentName] = instanceId;
		}

		CallToolResult result = await client.CallToolAsync(tool, arguments,
			cancellationToken: TestContext.Current.CancellationToken);
		observed.Add(JsonSerializer.Serialize(result,
			McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(CallToolResult))));
		return result;
	}

	private static InstanceDescriptor Forge(InstanceDescriptor record)
	{
		using Process process = Process.GetCurrentProcess();
		Guid activation = Guid.NewGuid();
		return record with
		{
			InstanceId = $"ce-{process.Id}-{activation:N}",
			Name = "forged",
			ActivationId = activation,
			AccessToken = RandomNumberGenerator.GetHexString(64)
		};
	}
}
