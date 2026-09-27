using System.Reflection;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Processes;
using CheatEngine.Mcp.Tools.Runtime;
using CheatEngine.Mcp.Tools.Scan;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Tools;

/// <summary>Contract and lifecycle tests shared by the runtime, process and scan v2 domain containers.</summary>
public sealed class RuntimeProcessScanV2ContractTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void V2Domains_RegisterEveryReviewedNameWithStructuredContent()
	{
		Type[] types = [typeof(RuntimeTools), typeof(ProcessTools), typeof(ScanTools)];
		string[] expected =
		[
			CheatEngineToolNames.RuntimeGetInfo, CheatEngineToolNames.RuntimeGetOverview,
			CheatEngineToolNames.RuntimeListResources, CheatEngineToolNames.RuntimeReleaseResources,
			CheatEngineToolNames.RuntimeListJobs, CheatEngineToolNames.RuntimeStopJob,
			CheatEngineToolNames.ProcessList, CheatEngineToolNames.ProcessAttach,
			CheatEngineToolNames.ProcessGetCurrent, CheatEngineToolNames.ProcessCreate,
			CheatEngineToolNames.ProcessOpenFile, CheatEngineToolNames.ProcessSaveFile,
			CheatEngineToolNames.ProcessSetPaused, CheatEngineToolNames.ProcessListThreads,
			CheatEngineToolNames.ProcessSetPointerSize,
			CheatEngineToolNames.ScanFirst, CheatEngineToolNames.ScanNext, CheatEngineToolNames.ScanGetStatus,
			CheatEngineToolNames.ScanListResults, CheatEngineToolNames.ScanListScanners,
			CheatEngineToolNames.ScanReset, CheatEngineToolNames.ScanDelete, CheatEngineToolNames.ScanStop
		];
		McpServerToolAttribute[] attributes =
		[
			.. types.SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance |
															   BindingFlags.DeclaredOnly))
				.Select(static method => method.GetCustomAttribute<McpServerToolAttribute>())
				.Where(static attribute => attribute is not null)
				.Select(static attribute => attribute!)
		];

		Assert.Equal(expected.Order(StringComparer.Ordinal), attributes.Select(static attribute => attribute.Name)
			.Order(StringComparer.Ordinal));
		Assert.All(attributes, static attribute => Assert.True(attribute.UseStructuredContent));
		Assert.All(types, type => Assert.Contains(TestComposition.BackendManifest.Primitives,
			primitive => primitive.Kind == CheatEngineMcpPrimitiveKind.Tool && primitive.Type == type));
	}

	[Fact]
	public void V2Contexts_SerializeRepresentativeResultsWithoutReflection()
	{
		RuntimeInfoResult runtime = new(7, "7.7", "windows", "2.0.0", "plugin.dll", "runtime.dll", "plugin",
			[new RuntimeCapability("memory", "Available", true, Evidence: "verified")]);
		ProcessListResult processes = new([new ProcessSummary(42, "target.exe")], false);
		ProcessCurrentResult current = new(true, 42, "target.exe", 8, 7);
		ScanResultsResult scan = new("named", "independent", 1,
			[new ScanMatch("0x401000", "25")], null, false);

		string runtimeJson = JsonSerializer.Serialize(runtime, RuntimeJsonContext.Default.RuntimeInfoResult);
		string processJson = JsonSerializer.Serialize(processes, ProcessJsonContext.Default.ProcessListResult);
		Assert.Contains("capabilities", runtimeJson);
		Assert.Contains("pluginFileName", runtimeJson, StringComparison.Ordinal);
		Assert.Contains("runtimeFileName", runtimeJson, StringComparison.Ordinal);
		Assert.DoesNotContain("pluginPath", runtimeJson, StringComparison.Ordinal);
		Assert.DoesNotContain("runtimePath", runtimeJson, StringComparison.Ordinal);
		Assert.Contains("target.exe", processJson);
		Assert.DoesNotContain("executablePath", processJson, StringComparison.Ordinal);
		Assert.DoesNotContain("executablePath",
			JsonSerializer.Serialize(current, ProcessJsonContext.Default.ProcessCurrentResult), StringComparison.Ordinal);
		Assert.Contains("0x401000", JsonSerializer.Serialize(scan, ScanJsonContext.Default.ScanResultsResult));
	}

	[Fact]
	public void RuntimeReleaseResources_ReleasesTrackedStateAndRuntimeStopJobRoutesToRegistry()
	{
		StateTestHarness harness = new(onMainThread: false, withLedger: false);
		ReleasableResource resource = new();
		harness.Resources.Track(resource);
		RuntimeTools tools = new(harness.Dispatch, TestRuntime.Info, harness.Resources, harness.Jobs);

		RuntimeResourceList listed = tools.ListResources(Token);
		Assert.Contains(listed.Resources, item => item.Id == resource.Descriptor.Id);
		RuntimeReleaseResourcesResult released = tools.ReleaseResources(cancellationToken: Token);
		Assert.True(released.Release.IsComplete);
		Assert.Equal(1, resource.ReleaseCalls);

		ManagedJob<long> job = harness.Jobs.StartManaged<long>("probe", TimeSpan.FromSeconds(60), 4,
			static async (_, cancellationToken) => await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
		Assert.Contains(tools.ListJobs(Token).Jobs, status => status.JobId == job.Id);
		JobStopResult stopped = tools.StopJob(job.Id, Token);
		Assert.True(stopped.Released);
		Assert.DoesNotContain(tools.ListJobs(Token).Jobs, status => status.JobId == job.Id);
	}

	[Fact]
	public void ProcessValidation_RefusesInvalidEffectsBeforeHostDispatch()
	{
		StateTestHarness harness = new();
		ProcessTools tools = new(harness.Dispatch, harness.Resources, new TargetTransitionGuards([]), CreateFiles());

		Assert.Equal(ToolErrorKind.InvalidArgument,
			Assert.Throws<CheatEngineToolException>(() => tools.Attach(string.Empty, Token)).Error.Kind);
		Assert.Equal(ToolErrorKind.InvalidArgument,
			Assert.Throws<CheatEngineToolException>(() => tools.SetPointerSize(5, Token)).Error.Kind);
		Assert.Equal(ToolErrorKind.InvalidArgument,
			Assert.Throws<CheatEngineToolException>(() => tools.Create("target.exe", breakOnEntryPoint: true,
				cancellationToken: Token)).Error.Kind);
		Assert.Equal(0, harness.Dispatches);
	}

	[Fact]
	public void ProcessFileTools_RefuseUnverifiedInputAndUnconfiguredOutputBeforeHostDispatch()
	{
		StateTestHarness harness = new();
		ProcessTools tools = new(harness.Dispatch, harness.Resources, new TargetTransitionGuards([]), CreateFiles());

		Assert.Equal(ToolErrorKind.InvalidArgument,
			Assert.Throws<CheatEngineToolException>(() => tools.OpenFile("relative.bin", cancellationToken: Token)).Error.Kind);
		Assert.Equal(ToolErrorKind.InvalidArgument,
			Assert.Throws<CheatEngineToolException>(() => tools.SaveFile(null!, Token)).Error.Kind);
		Assert.Equal(ToolErrorKind.InvalidArgument,
			Assert.Throws<CheatEngineToolException>(() => tools.SaveFile(Path.Combine(Path.GetTempPath(), "outside.bin"), Token)).Error.Kind);
		Assert.Equal(0, harness.Dispatches);
	}

	private static McpFilePaths CreateFiles()
	{
		string temporary = Path.GetTempPath();
		return new McpFilePaths(new McpFileOptions(), Path.Combine(temporary, "ce-mcp-test-registry"),
			Path.Combine(temporary, "ce-mcp-test-data"));
	}

	private sealed class ReleasableResource : ITargetResource
	{
		public int ReleaseCalls
		{
			get;
			private set;
		}

		public TargetResourceDescriptor Descriptor
		{
			get;
		} = new("probe-ns-1", "probe",
			TargetResourceCategory.ClientLease, TargetResourceState.Active, DateTimeOffset.UnixEpoch);

		public bool HoldsHostState => !IsEnded;

		public bool IsEnded
		{
			get;
			private set;
		}

		public ResourceReleaseOutcome Release(CancellationToken cancellationToken)
		{
			ReleaseCalls++;
			IsEnded = true;
			return ResourceReleaseOutcome.Released();
		}
	}
}
