using System.ComponentModel;
using System.IO.Pipelines;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Hosting;

/// <summary>
///     Every <c>internal</c> failure carries a random <c>errorId</c> (the tool envelope's <c>details</c>, a resource
///     error's <c>data</c>) that the host's log event records, and never the exception text; other kinds carry none.
/// </summary>
public sealed class ErrorCorrelationTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task CallTool_UnexpectedException_CarriesAnErrorIdThatItsLogEventRecords()
	{
		LogCapture logs = new();
		await using LoggedPipeline pipeline = await LoggedPipeline.StartAsync(TestMcpPipeline.ProbeManifest, logs);

		CallToolResult result = await pipeline.CallAsync(ContractProbeTool.FailName, """{"failure":"boom"}""");

		ToolError error = TestMcpPipeline.AssertError(result, ToolErrorKind.Internal);
		string errorId = AssertErrorId(error.Details);
		CapturedLog entry = Assert.Single(logs.Entries,
			entry => entry.Text.Contains(errorId, StringComparison.Ordinal));
		Assert.Equal(LogLevel.Error, entry.Level);
		Assert.Contains(
			$"MCP tool {ContractProbeTool.FailName} failed with an unexpected exception " +
			$"({nameof(InvalidOperationException)})", entry.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(ContractProbeTool.SecretMessage, entry.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(ContractProbeTool.SecretMessage,
			Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
	}

	[Fact]
	public async Task CallTool_ReportedInternalError_KeepsItsDetailsAndLogsTheSameErrorId()
	{
		LogCapture logs = new();
		await using LoggedPipeline pipeline = await LoggedPipeline.StartAsync(CorrelationProbes.Manifest, logs);

		CallToolResult result = await pipeline.CallAsync(CorrelationProbeTool.Name, """{"failure":"details"}""");

		ToolError error = TestMcpPipeline.AssertError(result, ToolErrorKind.Internal);
		string errorId = AssertErrorId(error.Details);
		Assert.Equal(2, error.Details!.Value.GetProperty("step").GetInt32());
		CapturedLog entry = Assert.Single(logs.Entries,
			entry => entry.Text.Contains(errorId, StringComparison.Ordinal));
		Assert.Contains(
			$"MCP tool {CorrelationProbeTool.Name} reported an internal error in operation unknown " +
			$"({nameof(CheatEngineToolException)}; errorId {errorId})", entry.Text, StringComparison.Ordinal);
	}

	[Fact]
	public async Task CallTool_ReportedInternalErrorWrappingAFault_LogsItsOperationAndTheFaultTypeOnly()
	{
		LogCapture logs = new();
		await using LoggedPipeline pipeline = await LoggedPipeline.StartAsync(CorrelationProbes.Manifest, logs);

		CallToolResult result = await pipeline.CallAsync(CorrelationProbeTool.Name, """{"failure":"wrapped"}""");

		string errorId = AssertErrorId(TestMcpPipeline.AssertError(result, ToolErrorKind.Internal).Details);
		CapturedLog entry = Assert.Single(logs.Entries,
			entry => entry.Text.Contains(errorId, StringComparison.Ordinal));
		Assert.Contains(
			$"reported an internal error in operation {CorrelationProbeTool.Operation} " +
			$"({nameof(FormatException)}; errorId {errorId})", entry.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(CorrelationProbeResource.Secret, entry.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(CorrelationProbeResource.Secret,
			Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
	}

	[Fact]
	public async Task CallTool_OtherKinds_CarryNoErrorIdAndLogNothing()
	{
		LogCapture logs = new();
		await using LoggedPipeline pipeline = await LoggedPipeline.StartAsync(TestMcpPipeline.ProbeManifest, logs);

		CallToolResult notFound = await pipeline.CallAsync(ContractProbeTool.FailName, """{"failure":"not_found"}""");
		CallToolResult partial = await pipeline.CallAsync(ContractProbeTool.FailName, """{"failure":"partial"}""");

		Assert.Null(TestMcpPipeline.AssertError(notFound, ToolErrorKind.NotFound).Details);
		ToolError partialError = TestMcpPipeline.AssertError(partial, ToolErrorKind.PartialEffect);
		Assert.False(partialError.Details!.Value.TryGetProperty(McpErrorCorrelation.Key, out _));
		Assert.DoesNotContain(logs.Entries, static entry => entry.Text.Contains("errorId", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("fault", "failed with an unexpected exception (InvalidOperationException)")]
	[InlineData("reported", "reported an internal error in operation unknown (CheatEngineToolException;")]
	public async Task ReadResource_InternalFailure_CarriesTheErrorIdInDataAndItsLogEvent(string kind, string logged)
	{
		LogCapture logs = new();
		await using GatewayTestHost gateway =
			await GatewayTestHost.StartAsync(logs: logs, extraPrimitives: CorrelationProbes.Resources);
		await using McpClient client = await gateway.ConnectAsync();

		McpProtocolException failure = await Assert.ThrowsAsync<McpProtocolException>(async () =>
			await client.ReadResourceAsync(CorrelationProbeResource.Prefix + kind, cancellationToken: Token));

		Assert.Equal(McpErrorCode.InternalError, failure.ErrorCode);
		Assert.Equal("internal", failure.Data["kind"]);
		string errorId = Assert.IsType<string>(failure.Data[McpErrorCorrelation.Key]);
		Assert.Matches("^[0-9a-f]{16}$", errorId);
		CapturedLog entry = Assert.Single(logs.Entries,
			entry => entry.Text.Contains(errorId, StringComparison.Ordinal));
		Assert.Contains($"MCP resource {CorrelationProbeResource.Name} {logged}", entry.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(CorrelationProbeResource.Secret, entry.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(CorrelationProbeResource.Secret, failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ReadResource_NotFound_CarriesNoErrorId()
	{
		await using GatewayTestHost gateway =
			await GatewayTestHost.StartAsync(extraPrimitives: CorrelationProbes.Resources);
		await using McpClient client = await gateway.ConnectAsync();

		McpProtocolException failure = await Assert.ThrowsAsync<McpProtocolException>(async () =>
			await client.ReadResourceAsync(CorrelationProbeResource.Prefix + "missing", cancellationToken: Token));

		Assert.Equal("not_found", failure.Data["kind"]);
		Assert.False(failure.Data.Contains(McpErrorCorrelation.Key));
	}

	[Fact]
	public async Task ReadResource_BackendErrorId_PassesThroughTheGateway()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "correlated");
		await using McpClient client = await gateway.ConnectAsync();
		ToolError correlated = McpErrorCorrelation.Correlate(
			CheatEngineToolException.Internal("The probe failed.").Error, out string? errorId);
		backend.RespondToRead = _ => throw McpResourceErrors.Create(correlated, "2025-06-18");

		McpProtocolException failure = await Assert.ThrowsAsync<McpProtocolException>(async () =>
			await client.ReadResourceAsync($"cheatengine://instances/{backend.Descriptor.InstanceId}/runtime",
				cancellationToken: Token));

		Assert.Equal(errorId, failure.Data[McpErrorCorrelation.Key]);
	}

	[Fact]
	public void Correlate_ExistingIdOrDetailsThatAreNotAnObject_AreKept()
	{
		ToolError first = McpErrorCorrelation.Correlate(CheatEngineToolException.Internal("Failed.").Error,
			out string? firstId);
		ToolError again = McpErrorCorrelation.Correlate(first, out string? againId);
		using JsonDocument array = JsonDocument.Parse("[1,2]");
		ToolError listed = CheatEngineToolException.Internal("Failed.").Error with
		{
			Details = array.RootElement.Clone()
		};
		ToolError unchanged = McpErrorCorrelation.Correlate(listed, out string? listedId);
		ToolError other = McpErrorCorrelation.Correlate(
			CheatEngineToolException.NotFound("Missing.", "List first.").Error, out string? otherId);

		Assert.Equal(firstId, againId);
		Assert.Same(first, again);
		Assert.Matches("^[0-9a-f]{16}$", listedId);
		Assert.Equal("[1,2]", unchanged.Details!.Value.GetRawText());
		Assert.Null(McpErrorCorrelation.Read(unchanged));
		Assert.Null(otherId);
		Assert.Null(other.Details);
	}

	private static string AssertErrorId(JsonElement? details)
	{
		Assert.NotNull(details);
		string errorId = details.Value.GetProperty(McpErrorCorrelation.Key).GetString()!;
		Assert.Matches("^[0-9a-f]{16}$", errorId);
		return errorId;
	}

	/// <summary>The correlation probes' compositions.</summary>
	private static class CorrelationProbes
	{
		internal static CheatEngineMcpPrimitiveOptions Manifest
		{
			get;
		} = CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog, static builder => builder
			.AddToolType<CorrelationProbeTool>().AddJsonTypeInfoResolver(TestJsonContext.Default));

		internal static CheatEngineMcpPrimitive[] Resources
		{
			get;
		} = [new(CheatEngineMcpPrimitiveKind.Resource, typeof(CorrelationProbeResource))];
	}

	/// <summary>
	///     A real MCP server with the Core filters over in-memory pipes whose logs a <see cref="LogCapture" /> records,
	///     like <see cref="TestMcpPipeline" />.
	/// </summary>
	private sealed class LoggedPipeline : IAsyncDisposable
	{
		private readonly McpServer _server;
		private readonly Task _serverRun;
		private readonly ServiceProvider _services;
		private readonly CancellationTokenSource _stop;

		private LoggedPipeline(ServiceProvider services, McpServer server, Task serverRun, CancellationTokenSource stop,
			McpClient client)
		{
			_services = services;
			_server = server;
			_serverRun = serverRun;
			_stop = stop;
			Client = client;
		}

		private McpClient Client
		{
			get;
		}

		public async ValueTask DisposeAsync()
		{
			await Client.DisposeAsync();
			await _stop.CancelAsync();
			try
			{
				await _serverRun;
			}
			catch (OperationCanceledException)
			{
				// The server stops through its token.
			}

			await _server.DisposeAsync();
			_stop.Dispose();
			await _services.DisposeAsync();
		}

		internal static async Task<LoggedPipeline> StartAsync(CheatEngineMcpPrimitiveOptions manifest, LogCapture logs)
		{
			ServiceCollection services = new();
			services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Information).AddProvider(logs));
			services.AddMcpServer().WithCheatEnginePrimitives(manifest, McpPrimitiveBinding.Catalog);
			ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
			{
				ValidateOnBuild = true,
				ValidateScopes = true
			});
			CancellationTokenSource stop = new();
			try
			{
				ILoggerFactory loggers = provider.GetRequiredService<ILoggerFactory>();
				Pipe clientToServer = new();
				Pipe serverToClient = new();
				McpServer server = McpServer.Create(
					new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream(),
						"CheatEngine.Mcp.Tests", loggers),
					provider.GetRequiredService<IOptions<McpServerOptions>>().Value, loggers, provider);
				Task serverRun = server.RunAsync(stop.Token);
				McpClient client = await McpClient.CreateAsync(
					new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
					new McpClientOptions
					{
						ClientInfo = new Implementation { Name = "CheatEngine.Mcp.Tests", Version = "2.0.0" },
						ProtocolVersion = TestMcpPipeline.DefaultProtocolVersion
					}, cancellationToken: Token);
				return new LoggedPipeline(provider, server, serverRun, stop, client);
			}
			catch
			{
				await stop.CancelAsync();
				stop.Dispose();
				await provider.DisposeAsync();
				throw;
			}
		}

		internal async Task<CallToolResult> CallAsync(string tool, string argumentsJson)
		{
			using JsonDocument arguments = JsonDocument.Parse(argumentsJson);
			Dictionary<string, JsonElement> values = arguments.RootElement.EnumerateObject()
				.ToDictionary(static property => property.Name, static property => property.Value.Clone(),
					StringComparer.Ordinal);
			return await Client.CallToolAsync(new CallToolRequestParams { Name = tool, Arguments = values }, Token);
		}
	}
}

/// <summary>A static tool that raises the internal failure its argument names.</summary>
[McpServerToolType]
public sealed class CorrelationProbeTool
{
	// A frozen catalog name: the startup validator admits no other v2 tool name.
	internal const string Name = CheatEngineToolNames.UtilCalculate;

	// The operation of the wrapped internal error.
	internal const string Operation = "probe_step";

	[McpServerTool(Name = Name, Title = "Raise an internal probe failure", ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Raises the internal failure its argument names.")]
	public static ContractProbeResult Fail([Description("The failure to raise.")] string failure)
	{
		using JsonDocument details = JsonDocument.Parse("""{"step":2}""");
		throw failure switch
		{
			"details" => new CheatEngineToolException(new ToolError(ToolErrorKind.Internal,
				"The probe stopped at step 2.", null, ToolHostEffect.Unknown, false, null,
				details.RootElement.Clone())),
			"wrapped" => new CheatEngineToolException(new ToolError(ToolErrorKind.Internal,
				"The probe's step failed.", Operation, ToolHostEffect.Unknown, false),
				new FormatException(CorrelationProbeResource.Secret)),
			_ => CheatEngineToolException.Internal("The probe reported an internal fault.")
		};
	}
}

/// <summary>A static (Local) document whose URI selects the failure it raises.</summary>
[McpServerResourceType]
public sealed class CorrelationProbeResource
{
	internal const string Prefix = "cheatengine://docs/correlation/";
	internal const string Name = "doc_correlation";
	internal const string Secret = "correlation-probe-secret";

	[McpServerResource(UriTemplate = Prefix + "{kind}", Name = Name, Title = "Correlation probe",
		MimeType = "text/markdown")]
	[Description("Raises the failure its kind names.")]
	public static string Read(string kind)
	{
		return kind switch
		{
			"fault" => throw new InvalidOperationException(Secret),
			"reported" => throw CheatEngineToolException.Internal("The probe reported an internal fault."),
			_ => throw CheatEngineToolException.NotFound("No such probe.", "Read fault or reported.")
		};
	}
}
