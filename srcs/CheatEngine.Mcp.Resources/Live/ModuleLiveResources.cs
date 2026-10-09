using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Execution;
using CheatEngine.Mcp.Core.Inspection;
using CheatEngine.Mcp.Tools.Modules;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>
///     The read-only module projections of one Cheat Engine instance: the loaded modules and one module's details.
///     The <c>module</c> variable completes from the attached process's module names.
/// </summary>
[McpServerResourceType]
public sealed class ModuleLiveResources(ModuleTools modules, ToolDispatch dispatch, PreparedInspectionStore prepared)
	: IMcpCompletionSource
{
	private const string ModulesPath = McpResourceUris.InstancePrefix + "modules";
	private const string ModuleVariable = "module";
	private const int DefaultModules = 200;
	private const int MaximumModules = 1000;

	/// <summary>Reads a page of the v2 module list.</summary>
	/// <param name="offset">The index of the first module; 0 when absent.</param>
	/// <param name="limit">The most modules to return; 200 when absent.</param>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>module_list</c>.</returns>
	[McpServerResource(UriTemplate = ModulesPath + "{?offset,limit}", Name = "instance_modules",
		Title = "Loaded modules", MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(ModuleTools), CheatEngineToolNames.ModuleList, PreparedProjection = nameof(ModuleTools.ListPrepared))]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("A page of the latest modules explicitly prepared for the attached target: 200 from offset 0 by default, or " +
				 "?offset=..&limit=.. in that order (limit 1 to 1000). The preparation expires after five seconds. Its JSON is the structured result of " +
				 CheatEngineToolNames.ModuleList + "; run that tool without processId before reading this resource.")]
	public ReadResourceResult Modules(
		[Description("The index of the first module, 0 by default.")]
		string? offset = null,
		[Description("The most modules to return, 1 to 1000, 200 by default.")]
		string? limit = null,
		CancellationToken cancellationToken = default)
	{
		int? first = McpResourceQuery.Number(offset, nameof(offset), 0, LiveResourceResults.MaximumOffset);
		int? count = McpResourceQuery.Number(limit, nameof(limit), 1, MaximumModules);
		ModuleList page = modules.ListPrepared(offset: first ?? 0, limit: count ?? DefaultModules,
			cancellationToken: cancellationToken);
		return LiveResourceResults.Json(McpResourceQuery.WithQuery(ModulesPath, ("offset", first), ("limit", count)),
			JsonSerializer.Serialize(page, ModuleJsonContext.Default.ModuleList));
	}

	/// <summary>Reads one module's details.</summary>
	/// <param name="module">The module name, or an address expression inside it.</param>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>module_get</c>.</returns>
	[McpServerResource(UriTemplate = ModulesPath + "/{module}", Name = "instance_module", Title = "Module details",
		MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(ModuleTools), CheatEngineToolNames.ModuleGet, PreparedProjection = nameof(ModuleTools.GetPrepared))]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("The latest module details explicitly prepared for the attached target, by the exact module_get selector or " +
				 "its canonical name: base, size, path, sections and PE header fields. The latest result expires after five seconds; " +
				 "run module_get before reading this resource. Its JSON is the structured result of " + CheatEngineToolNames.ModuleGet + ".")]
	public ReadResourceResult Module(
		[Description("The module name, such as game.exe, or an address expression inside it.")]
		[McpCompletion(McpCompletionCost.DispatchFresh)]
		string module,
		CancellationToken cancellationToken = default)
	{
		string wanted = McpResourceQuery.Required(module, nameof(module));
		return LiveResourceResults.Json(ModulesPath + "/" + McpResourceQuery.Segment(wanted),
			JsonSerializer.Serialize(modules.GetPrepared(wanted, cancellationToken), ModuleJsonContext.Default.ModuleDetails));
	}

	/// <summary>
	///     Lists the attached process's module names for <c>module</c>, with the selection epoch they were read in, in
	///     one short dispatch.
	/// </summary>
	/// <param name="variable">The completed variable: <c>module</c>.</param>
	/// <param name="cancellationToken">The listing's cancellation.</param>
	/// <returns>The distinct module names in Cheat Engine's order.</returns>
	/// <exception cref="CheatEngineToolException">No process is attached, or the dispatch failed.</exception>
	public McpCompletionValues ListCompletionValues(string variable, CancellationToken cancellationToken)
	{
		return string.Equals(variable, ModuleVariable, StringComparison.Ordinal)
			? McpCompletionReads.ModuleNames(dispatch, prepared, cancellationToken)
			: throw new ArgumentOutOfRangeException(nameof(variable), variable, "Only module is completed.");
	}
}
