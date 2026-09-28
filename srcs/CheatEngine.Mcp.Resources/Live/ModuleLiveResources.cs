using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Execution;
using CheatEngine.Mcp.Tools.Modules;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>
///     The read-only module projections of one Cheat Engine instance: the loaded modules, one module's details and one
///     module's exports. The <c>module</c> variable completes from the attached process's module names.
/// </summary>
[McpServerResourceType]
public sealed class ModuleLiveResources(ModuleTools modules, ModuleExportTools exports, ToolDispatch dispatch)
	: IMcpCompletionSource
{
	private const string ModulesPath = McpResourceUris.InstancePrefix + "modules";
	private const string ModuleVariable = "module";
	private const int DefaultModules = 200;
	private const int MaximumModules = 1000;
	private const int DefaultExports = 200;
	private const int MaximumExports = 1000;

	/// <summary>Reads a page of the v2 module list.</summary>
	/// <param name="offset">The index of the first module; 0 when absent.</param>
	/// <param name="limit">The most modules to return; 200 when absent.</param>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>module_list</c>.</returns>
	[McpServerResource(UriTemplate = ModulesPath + "{?offset,limit}", Name = "instance_modules",
		Title = "Loaded modules", MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(ModuleTools), CheatEngineToolNames.ModuleList)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("A page of the modules loaded in the attached target: 200 from offset 0 by default, or " +
				 "?offset=..&limit=.. in that order (limit 1 to 1000). Its JSON is the structured result of " +
				 CheatEngineToolNames.ModuleList + "; use that tool to filter by name or read another process.")]
	public ReadResourceResult Modules(
		[Description("The index of the first module, 0 by default.")]
		string? offset = null,
		[Description("The most modules to return, 1 to 1000, 200 by default.")]
		string? limit = null,
		CancellationToken cancellationToken = default)
	{
		int? first = McpResourceQuery.Number(offset, nameof(offset), 0, LiveResourceResults.MaximumOffset);
		int? count = McpResourceQuery.Number(limit, nameof(limit), 1, MaximumModules);
		ModuleList page = modules.List(offset: first ?? 0, limit: count ?? DefaultModules,
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
	[McpSourceTool(typeof(ModuleTools), CheatEngineToolNames.ModuleGet)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("One module of the attached target, by name such as game.exe or by an address expression inside it " +
				 "(percent-encoded): base, size, path, sections and PE header fields, whose time stamp identifies " +
				 "the build. Its JSON is the structured result of " + CheatEngineToolNames.ModuleGet + ".")]
	public ReadResourceResult Module(
		[Description("The module name, such as game.exe, or an address expression inside it.")]
		[McpCompletion(McpCompletionCost.Dispatch)]
		string module,
		CancellationToken cancellationToken = default)
	{
		string wanted = McpResourceQuery.Required(module, nameof(module));
		return LiveResourceResults.Json(ModulesPath + "/" + McpResourceQuery.Segment(wanted),
			JsonSerializer.Serialize(modules.Get(wanted, cancellationToken), ModuleJsonContext.Default.ModuleDetails));
	}

	/// <summary>Reads a page of one module's exports.</summary>
	/// <param name="module">The module name, or an address expression inside it.</param>
	/// <param name="offset">The index of the first export; 0 when absent.</param>
	/// <param name="limit">The most exports to return; 200 when absent.</param>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>module_list_exports</c>.</returns>
	[McpServerResource(UriTemplate = ModulesPath + "/{module}/exports{?offset,limit}", Name = "instance_module_exports",
		Title = "Module exports", MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(ModuleExportTools), CheatEngineToolNames.ModuleListExports)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("A page of one module's exports parsed from its export directory: 200 from offset 0 by default, or " +
				 "?offset=..&limit=.. in that order (limit 1 to 1000). Its JSON is the structured result of " +
				 CheatEngineToolNames.ModuleListExports + "; use that tool to filter by name.")]
	public ReadResourceResult Exports(
		[Description("The module name, such as kernel32.dll, or an address expression inside it.")]
		[McpCompletion(McpCompletionCost.Dispatch)]
		string module,
		[Description("The index of the first export, 0 by default.")]
		string? offset = null,
		[Description("The most exports to return, 1 to 1000, 200 by default.")]
		string? limit = null,
		CancellationToken cancellationToken = default)
	{
		string wanted = McpResourceQuery.Required(module, nameof(module));
		int? first = McpResourceQuery.Number(offset, nameof(offset), 0, LiveResourceResults.MaximumOffset);
		int? count = McpResourceQuery.Number(limit, nameof(limit), 1, MaximumExports);
		ExportList page = exports.ListExports(wanted, null, first ?? 0, count ?? DefaultExports, cancellationToken);
		return LiveResourceResults.Json(
			McpResourceQuery.WithQuery(ModulesPath + "/" + McpResourceQuery.Segment(wanted) + "/exports",
				("offset", first), ("limit", count)),
			JsonSerializer.Serialize(page, ModuleJsonContext.Default.ExportList));
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
			? McpCompletionReads.ModuleNames(dispatch, cancellationToken)
			: throw new ArgumentOutOfRangeException(nameof(variable), variable, "Only module is completed.");
	}
}
