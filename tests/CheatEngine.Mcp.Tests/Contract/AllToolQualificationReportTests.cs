using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Xml.Linq;

using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>Exercises cumulative live-evidence boundaries through the real report script and reviewed maps.</summary>
public sealed class AllToolQualificationReportTests
{
	[Fact]
	public async Task Report_DisjointPassingSameBundleSessions_VerifiesTheirReceiptUnion()
	{
		await using ReportFixture fixture = new();
		string first = fixture.CreateSession("first", ["all_tools_process_create", "live_process_create_cleanup"]);
		string second = fixture.CreateSession("second", ["all_tools_dotnet_get_object", "live_managed_object_cleanup"]);

		RunResult run = await fixture.RunAsync(first, second);

		Assert.Equal(0, run.ExitCode);
		JsonObject report = fixture.ReadReport();
		Assert.Null(report["liveSession"]);
		Assert.Equal(2, report["liveSessions"]!.AsArray().Count);
		Assert.Equal("verified", Tool(report, "process_create")["live"]!.GetValue<string>());
		Assert.Equal("verified", Tool(report, "dotnet_get_object")["live"]!.GetValue<string>());
		Assert.Equal(2, Tool(report, "process_create")["liveEvidence"]!["sessions"]!.AsArray().Count);
	}

	[Fact]
	public async Task Report_ReceiptAndCleanupInDifferentSessions_DoesNotPoolEvidence()
	{
		await using ReportFixture fixture = new();
		string receipt = fixture.CreateSession("receipt", ["all_tools_process_create"]);
		string cleanup = fixture.CreateSession("cleanup", ["live_process_create_cleanup"]);

		RunResult run = await fixture.RunAsync(receipt, cleanup);

		Assert.Equal(0, run.ExitCode);
		Assert.Equal("not_verified", Tool(fixture.ReadReport(), "process_create")["live"]!.GetValue<string>());
	}

	[Fact]
	public async Task Report_DuplicateReceiptOrCleanupInPassingSession_RemainsUnverified()
	{
		await using ReportFixture fixture = new();
		string duplicateReceipt = fixture.CreateSession("duplicate-receipt",
			["all_tools_process_create", "all_tools_process_create", "live_process_create_cleanup"]);
		string duplicateCleanup = fixture.CreateSession("duplicate-cleanup",
			["all_tools_process_create", "live_process_create_cleanup", "live_process_create_cleanup"]);

		RunResult run = await fixture.RunAsync(duplicateReceipt, duplicateCleanup);

		Assert.Equal(0, run.ExitCode);
		Assert.Equal("not_verified", Tool(fixture.ReadReport(), "process_create")["live"]!.GetValue<string>());
	}

	[Fact]
	public async Task Report_FailedAndDiagnosticSessions_DoNotOverridePassingEvidence()
	{
		await using ReportFixture fixture = new();
		string passing = fixture.CreateSession("passing", ["all_tools_process_create", "live_process_create_cleanup"]);
		string failed = fixture.CreateSession("failed", ["all_tools_dotnet_get_object", "live_managed_object_cleanup"], passed: false);
		string diagnostic = fixture.CreateSession("diagnostic", ["all_tools_dotnet_get_type"], diagnosticOnly: true);
		string diagnosticDuplicate = fixture.CreateSession("diagnostic-duplicate", ["all_tools_process_create", "live_process_create_cleanup"], diagnosticOnly: true);

		RunResult run = await fixture.RunAsync(passing, failed, diagnostic, diagnosticDuplicate);

		Assert.Equal(0, run.ExitCode);
		JsonObject report = fixture.ReadReport();
		Assert.Equal("verified", Tool(report, "process_create")["live"]!.GetValue<string>());
		Assert.Equal("observed_in_failed_session", Tool(report, "dotnet_get_object")["live"]!.GetValue<string>());
		Assert.Equal("diagnostic_only", Tool(report, "dotnet_get_type")["live"]!.GetValue<string>());
	}

	[Theory]
	[InlineData("duplicate path", "Duplicate live summary path")]
	[InlineData("duplicate run", "Duplicate live runId")]
	[InlineData("product mismatch", "different candidate bundles")]
	[InlineData("plugin mismatch", "different candidate bundles")]
	[InlineData("gateway mismatch", "different candidate bundles")]
	public async Task Report_InvalidLiveSessionSet_RejectsBeforeWritingReport(string condition, string message)
	{
		await using ReportFixture fixture = new();
		string first = fixture.CreateSession("first", []);
		string second = condition == "duplicate path" ? first : fixture.CreateSession(
			condition == "duplicate run" ? "first" : "second", [], passed: condition != "plugin mismatch",
			diagnosticOnly: condition == "product mismatch",
			productVersion: condition == "product mismatch" ? "2.0.1" : "2.0.0",
			pluginSha256: condition == "plugin mismatch" ? new string('C', 64) : new string('A', 64),
			gatewaySha256: condition == "gateway mismatch" ? new string('D', 64) : new string('B', 64));

		RunResult run = await fixture.RunAsync(first, second);

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains(message, run.Output, StringComparison.OrdinalIgnoreCase);
		Assert.False(File.Exists(fixture.ReportPath));
	}

	[Theory]
	[InlineData("plugin")]
	[InlineData("gateway")]
	public async Task Report_NonSha256CandidateIdentity_RejectsSession(string field)
	{
		await using ReportFixture fixture = new();
		string summary = fixture.CreateSession("invalid-" + field, [],
			pluginSha256: field == "plugin" ? "not-a-hash" : new string('A', 64),
			gatewaySha256: field == "gateway" ? "not-a-hash" : new string('B', 64));

		RunResult run = await fixture.RunAsync(summary);

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains("invalid selected_distribution." + field + "Sha256", run.Output, StringComparison.Ordinal);
		Assert.False(File.Exists(fixture.ReportPath));
	}

	[Fact]
	public async Task Report_SingleLiveSummary_PreservesCompatibilityProjection()
	{
		await using ReportFixture fixture = new();
		string summary = fixture.CreateSession("single", ["all_tools_process_create", "live_process_create_cleanup"]);

		RunResult run = await fixture.RunSingleAsync(summary);

		Assert.Equal(0, run.ExitCode);
		JsonObject report = fixture.ReadReport();
		Assert.Equal("single", report["liveSession"]!["runId"]!.GetValue<string>());
		Assert.Single(report["liveSessions"]!.AsArray());
	}

	[Fact]
	public async Task Report_SingleAndCumulativeLiveSummarySwitches_RejectsAmbiguousInput()
	{
		await using ReportFixture fixture = new();
		string summary = fixture.CreateSession("single", []);

		RunResult run = await fixture.RunBothAsync(summary);

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains("either LiveSummary or LiveSummaries", run.Output, StringComparison.Ordinal);
		Assert.False(File.Exists(fixture.ReportPath));
	}

	[Fact]
	public async Task Report_WithoutLiveSummary_PreservesPortableReport()
	{
		await using ReportFixture fixture = new();

		RunResult run = await fixture.RunAsync();

		Assert.Equal(0, run.ExitCode);
		JsonObject report = fixture.ReadReport();
		Assert.Null(report["liveSession"]);
		Assert.Empty(report["liveSessions"]!.AsArray());
	}

	private static JsonObject Tool(JsonObject report, string name) => report["tools"]!.AsArray()
		.Select(static node => node!.AsObject()).Single(tool => tool["name"]!.GetValue<string>() == name);

	private sealed record RunResult(int ExitCode, string Output);

	private sealed class ReportFixture : IAsyncDisposable
	{
		private readonly string _root = Path.Combine(RepositoryPaths.Root, "artifacts", "test-results", "all-tool-report",
			Guid.NewGuid().ToString("N"));

		internal ReportFixture()
		{
			Directory.CreateDirectory(_root);
			GatewayTrx = Path.Combine(_root, "gateway.trx");
			InvocationScriptPath = Path.Combine(_root, "invoke-report.ps1");
			File.WriteAllText(GatewayTrx, GatewayTrxXml());
			File.WriteAllText(InvocationScriptPath, InvocationScript);
		}

		private string GatewayTrx
		{
			get;
		}
		private string InvocationScriptPath
		{
			get;
		}
		internal string ReportPath => Path.Combine(_root, "report", "tool-results.json");

		internal string CreateSession(string runId, IReadOnlyList<string> receipts, bool passed = true,
			bool diagnosticOnly = false, string productVersion = "2.0.0", string? pluginSha256 = null,
			string? gatewaySha256 = null)
		{
			string directory = Path.Combine(_root, runId + "-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			JsonObject summary = new()
			{
				["schema"] = "cheatengine-mcp-live-multi-instance/v1",
				["runId"] = runId,
				["passed"] = passed,
				["userStateRestored"] = true,
				["sourceInstallationUnchanged"] = true,
				["cleanupFailures"] = new JsonArray(),
				["diagnosticOnly"] = diagnosticOnly,
				["selected_distribution"] = new JsonObject
				{
					["productVersion"] = productVersion,
					["pluginSha256"] = pluginSha256 ?? new string('A', 64),
					["gatewaySha256"] = gatewaySha256 ?? new string('B', 64)
				},
				["host"] = new JsonObject()
			};
			string summaryPath = Path.Combine(directory, "summary.json");
			File.WriteAllText(summaryPath, summary.ToJsonString());
			File.WriteAllLines(Path.Combine(directory, "receipts.jsonl"), receipts.Select(static name =>
				new JsonObject { ["name"] = name }.ToJsonString()));
			return summaryPath;
		}

		internal async Task<RunResult> RunSingleAsync(string summary) => await RunCoreAsync(summary, null);

		internal async Task<RunResult> RunAsync(params string[] summaries)
		{
			return await RunCoreAsync(null, summaries);
		}

		internal async Task<RunResult> RunBothAsync(string summary) =>
			await RunCoreAsync(summary, [summary]);

		private async Task<RunResult> RunCoreAsync(string? liveSummary, IReadOnlyList<string>? liveSummaries)
		{
			string configuration = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json");
			JsonObject invocation = new()
			{
				["script"] = Path.Combine(RepositoryPaths.Root, "eng", "Write-AllToolQualificationReport.ps1"),
				["gatewayTrx"] = GatewayTrx,
				["outputDirectory"] = Path.GetDirectoryName(ReportPath)!
			};
			if (liveSummary is not null)
			{
				invocation["liveSummary"] = liveSummary;
			}
			if (liveSummaries is not null)
			{
				invocation["liveSummaries"] = new JsonArray(liveSummaries.Select(static value => (JsonNode?) JsonValue.Create(value)).ToArray());
			}
			File.WriteAllText(configuration, invocation.ToJsonString());
			ProcessStartInfo start = new("pwsh")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				WorkingDirectory = RepositoryPaths.Root
			};
			foreach (string argument in new[] { "-NoProfile", "-File", InvocationScriptPath, "-Configuration", configuration })
			{
				start.ArgumentList.Add(argument);
			}
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
			timeout.CancelAfter(TimeSpan.FromSeconds(60));
			using Process process = Process.Start(start)!;
			Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
			Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
			try
			{
				await process.WaitForExitAsync(timeout.Token);
				return new RunResult(process.ExitCode, await output + await error);
			}
			finally
			{
				if (!process.HasExited)
				{
					process.Kill(true);
					await process.WaitForExitAsync(CancellationToken.None);
				}
			}
		}

		internal JsonObject ReadReport() => JsonNode.Parse(File.ReadAllText(ReportPath))!.AsObject();

		public ValueTask DisposeAsync() => new(TestDirectory.DeleteAsync(_root));

		private static string GatewayTrxXml()
		{
			JsonArray catalog = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryPaths.Root,
				"tests", "CheatEngine.Mcp.Tests", "Contract", "Golden", "backend-tools.json")))!.AsArray();
			const string prefix = "CheatEngine.Mcp.Tests.Hosting.AllToolGatewayQualificationTests.";
			string[] methods = ["CallTool_MissingInstanceId_RejectsEveryBackendToolBeforeAnyBackendContact",
				"CallTool_UnknownInstanceId_RejectsEveryBackendToolWithoutFallback", "CallTool_StaleIdentity_RejectsEveryBackendToolBeforeMutation",
				"CallTool_TwoInstances_RoutesEveryBackendToolWithOnlyInstanceIdRemoved"];
			IEnumerable<string> names = catalog.SelectMany(tool =>
			{
				string toolName = tool!["name"]!.GetValue<string>();
				return methods.Select(method => $"{prefix}{method}(tool: \"{toolName}\")");
			}).Concat([
				prefix + "InstanceList_EmptyRegistry_ReturnsLocalEmptyDiscoveryResult",
				prefix + "InstanceList_PublishedDoubles_ReturnsBothVerifiedInstancesWithoutTokens"]);
			XNamespace test = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
			return new XDocument(new XElement(test + "TestRun", new XElement(test + "Results", names.Select(name =>
				new XElement(test + "UnitTestResult", new XAttribute("testName", name),
					new XAttribute("outcome", "Passed")))))).ToString();
		}


		private const string InvocationScript = """
			param([Parameter(Mandatory)][string] $Configuration)
			$config = Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json -AsHashtable
			$splat = @{ GatewayTrx = $config['gatewayTrx']; OutputDirectory = $config['outputDirectory'] }
			if ($config.ContainsKey('liveSummary')) { $splat.LiveSummary = $config['liveSummary'] }
			if ($config.ContainsKey('liveSummaries')) { $splat.LiveSummaries = @($config['liveSummaries']) }
			& $config['script'] @splat
			exit $LASTEXITCODE
			""";
	}
}
