using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Runtime;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>
///     The read-only runtime projections of one Cheat Engine instance: the overview, the retained resources and the
///     retained jobs.
/// </summary>
[McpServerResourceType]
public sealed class RuntimeLiveResources(RuntimeTools runtime)
{
	private const string RuntimePath = McpResourceUris.InstancePrefix + "runtime";
	private const string ResourcesPath = McpResourceUris.InstancePrefix + "resources";
	private const string JobsPath = McpResourceUris.InstancePrefix + "jobs";

	/// <summary>Reads the v2 runtime overview.</summary>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>runtime_get_overview</c>.</returns>
	[McpServerResource(UriTemplate = RuntimePath, Name = "instance_runtime", Title = "Runtime overview",
		MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(RuntimeTools), CheatEngineToolNames.RuntimeGetOverview)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("The runtime overview of this Cheat Engine instance: host capabilities, the Mcp:Enable* gates " +
				 "(runtime.gates), the current target and the retained resource and job counts. Its JSON is the " +
				 "structured result of " + CheatEngineToolNames.RuntimeGetOverview + ".")]
	public ReadResourceResult Runtime(CancellationToken cancellationToken = default)
	{
		return LiveResourceResults.Json(RuntimePath, JsonSerializer.Serialize(runtime.GetOverview(cancellationToken),
			RuntimeJsonContext.Default.RuntimeOverviewResult));
	}

	/// <summary>Reads the resources this activation retains.</summary>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>runtime_list_resources</c>.</returns>
	[McpServerResource(UriTemplate = ResourcesPath, Name = "instance_resources", Title = "Retained resources",
		MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(RuntimeTools), CheatEngineToolNames.RuntimeListResources)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("The activation-owned resources and orphaned Lua state that still hold Cheat Engine state and block " +
				 "a target change. Its JSON is the structured result of " +
				 CheatEngineToolNames.RuntimeListResources + "; release them with runtime_release_resources.")]
	public ReadResourceResult Resources(CancellationToken cancellationToken = default)
	{
		return LiveResourceResults.Json(ResourcesPath, JsonSerializer.Serialize(
			runtime.ListResources(cancellationToken), RuntimeJsonContext.Default.RuntimeResourceList));
	}

	/// <summary>Reads the jobs this activation retains.</summary>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>runtime_list_jobs</c>.</returns>
	[McpServerResource(UriTemplate = JobsPath, Name = "instance_jobs", Title = "Retained jobs",
		MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(RuntimeTools), CheatEngineToolNames.RuntimeListJobs)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("The running, stopping and completed jobs of this activation, kept until their TTL ends. Its JSON " +
				 "is the structured result of " + CheatEngineToolNames.RuntimeListJobs + ".")]
	public ReadResourceResult Jobs(CancellationToken cancellationToken = default)
	{
		return LiveResourceResults.Json(JobsPath, JsonSerializer.Serialize(runtime.ListJobs(cancellationToken),
			RuntimeJsonContext.Default.RuntimeJobList));
	}
}
