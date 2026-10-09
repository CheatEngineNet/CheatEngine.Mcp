using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Runtime;

/// <summary>Runtime identity, retained-resource and retained-job tools for one plugin activation.</summary>
[McpServerToolType]
public sealed class RuntimeTools
{
	private readonly ToolDispatch _dispatch;
	private readonly JobRegistry _jobs;
	private readonly TargetResources _resources;
	private readonly McpRuntimeInfo _runtime;

	/// <summary>Creates the runtime tools without calling the Cheat Engine host.</summary>
	public RuntimeTools(ToolDispatch dispatch, McpRuntimeInfo runtime, TargetResources resources, JobRegistry jobs)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(runtime);
		ArgumentNullException.ThrowIfNull(resources);
		ArgumentNullException.ThrowIfNull(jobs);
		_dispatch = dispatch;
		_runtime = runtime;
		_resources = resources;
		_jobs = jobs;
	}

	/// <summary>Gets the activation identity, Client capability evidence and exposure switches.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RuntimeGetInfo, Title = "Get runtime information", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read the active Client epoch, host version, plugin identity, capability evidence and the Mcp:Enable* " +
		"gates (gates: autoAssembler, unsafeLua, targetCodeExecution, kernelAccess) before using optional features; " +
		"a tool whose gate is off is refused with capability_disabled.")]
	public RuntimeInfoResult GetInfo(CancellationToken cancellationToken = default)
	{
		return _dispatch.Run(CheatEngineToolNames.RuntimeGetInfo,
			token => CreateInfo(_dispatch.Client.Runtime.GetSnapshot(token)),
			cancellationToken);
	}

	/// <summary>Gets a compact orientation summary without changing the selected target.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RuntimeGetOverview, Title = "Get runtime overview", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read the host capability snapshot, the Mcp:Enable* gates (runtime.gates), the current target and the " +
		"retained resource and job counts before starting target work.")]
	public RuntimeOverviewResult GetOverview(CancellationToken cancellationToken = default)
	{
		return _dispatch.Run(CheatEngineToolNames.RuntimeGetOverview, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			RuntimeCurrentProcess process = Current(client, token);
			int resources = _resources.ListAll(token).Count;
			return new RuntimeOverviewResult(CreateInfo(client.Runtime.GetSnapshot(token)), process, resources,
				_jobs.Count);
		}, cancellationToken);
	}

	/// <summary>Lists resources that can block a target switch or still hold host state.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RuntimeListResources, Title = "List retained resources", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"List activation-owned resources and orphaned Lua state that still hold Cheat Engine state. Release them before changing targets.")]
	public RuntimeResourceList ListResources(CancellationToken cancellationToken = default)
	{
		return _dispatch.Run(CheatEngineToolNames.RuntimeListResources,
			token => new RuntimeResourceList([.. _resources.ListAll(token)]), cancellationToken);
	}

	/// <summary>Releases retained state in the order required by its dependencies.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RuntimeReleaseResources, Title = "Release retained resources",
		ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.MayPrompt)]
	[Description(
		"Release retained resources newest first. Releasing Auto Assembler patches runs their DISABLE sections, which can block Cheat Engine or show a dialog. includeOrphans also releases retained Lua state from earlier activations. acknowledgeIds only forgets resources already recovered manually; an incomplete cleanup is reported as partial_effect with its actual host effect.")]
	public RuntimeReleaseResourcesResult ReleaseResources(
		[Description("Also release orphaned or unmanaged Lua state from earlier plugin activations.")]
		bool includeOrphans = false,
		[Description(
			"Resource ids already recovered manually to acknowledge and forget first, 1 to the documented limit when supplied.")]
		string[]? acknowledgeIds = null,
		CancellationToken cancellationToken = default)
	{
		return _dispatch.Run(CheatEngineToolNames.RuntimeReleaseResources, token =>
		{
			TargetResourceDescriptor[] acknowledged = acknowledgeIds is { Length: > 0 }
				? [.. _resources.Acknowledge(acknowledgeIds, token)]
				: [];
			ReleaseAllResult release = _resources.ReleaseAll(token, includeOrphans).ThrowIfIncomplete();
			return new RuntimeReleaseResourcesResult(acknowledged, release);
		}, cancellationToken);
	}

	/// <summary>Lists the current activation's jobs without changing them.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RuntimeListJobs, Title = "List retained jobs", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"List running, stopping and completed jobs retained until their TTL ends. Job status is live when a Lua job is present.")]
	public RuntimeJobList ListJobs(CancellationToken cancellationToken = default)
	{
		return new RuntimeJobList([.. _jobs.List(cancellationToken)]);
	}

	/// <summary>Stops one job and discards its retained items.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RuntimeStopJob, Title = "Stop a job", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Stop one retained job and discard its items. A managed job may report partial_effect while it is still ending; repeat only when the error says retryable.")]
	public JobStopResult StopJob(
		[Description("The job id returned by a start tool.")]
		string jobId,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(jobId))
		{
			throw CheatEngineToolException.InvalidArgument("jobId", "is required.");
		}

		return _jobs.Stop(jobId, cancellationToken);
	}

	private RuntimeInfoResult CreateInfo(CheatEngineRuntimeSnapshot snapshot)
	{
		RuntimeCapability[] capabilities =
		[
			.. snapshot.Capabilities.Entries.ToArray().Select(capability =>
				new RuntimeCapability(capability.Capability.Value, capability.State.ToString(), capability.IsAvailable,
					capability.Reason, capability.Evidence.ToString()))
		];
		McpFeatureSummary features = _dispatch.Features.Snapshot();
		RuntimeGates gates = new(features.AutoAssembler, features.UnsafeLua, features.TargetCodeExecution,
			features.KernelAccess);
		return new RuntimeInfoResult(snapshot.Epoch, snapshot.Version.ToString(), snapshot.Platform.ToString(),
			_runtime.Version, FileName(_runtime.Location), FileName(_runtime.RuntimeLocation), _runtime.ApplicationName,
			capabilities, gates);
	}

	private static RuntimeCurrentProcess Current(ICheatEngineClient client, CancellationToken cancellationToken)
	{
		if (!client.Processes.TryGetCurrentProcess(out ProcessSnapshot process, out CheatEngineFailure failure,
				cancellationToken))
		{
			if (failure.Kind is CheatEngineFailureKind.TargetNotAttached)
			{
				return new RuntimeCurrentProcess(false);
			}

			throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
		}

		return new RuntimeCurrentProcess(true, process.Id.Value, process.Name, process.ConfiguredPointerSizeBytes,
			process.SelectionEpoch);
	}

	private static string FileName(string? location)
	{
		if (string.IsNullOrWhiteSpace(location))
		{
			return "unknown";
		}

		string name = Path.GetFileName(location);
		return string.IsNullOrEmpty(name) ? "unknown" : name;
	}
}
