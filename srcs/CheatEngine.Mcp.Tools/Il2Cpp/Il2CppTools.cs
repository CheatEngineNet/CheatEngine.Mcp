using System.ComponentModel;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tools.Modules;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Il2Cpp;

/// <summary>Offline-only tools for a strictly bounded, documented dump.cs import.</summary>
[McpServerToolType]
public sealed class Il2CppTools
{
	private readonly ToolDispatch _dispatch;
	private readonly McpFilePaths _paths;
	private readonly Il2CppDumpStore _store;
	/// <summary>Creates the tools without reading a target or file.</summary>
	public Il2CppTools(ToolDispatch dispatch, McpFilePaths paths, Il2CppDumpStore store)
	{
		_dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
		_store = store ?? throw new ArgumentNullException(nameof(store));
	}

	/// <summary>Imports the supported strict UTF-8 dump_cs_v1 subset into an immutable offline dataset.</summary>
	[McpServerTool(Name = CheatEngineToolNames.Il2CppImportDump, Title = "Import IL2CPP dump", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Import a local dump.cs using only the documented dump_cs_v1 text subset. This reads no target process. modulePath is an optional original PE file used only to bind imported method RVAs for later validation; it is never loaded or executed.")]
	public Il2CppDumpImport ImportDump([Description("Absolute local UTF-8 dump.cs path, at most 16 MiB.")] string path, [Description("Optional absolute path of the original PE module that produced the dump.")] string? modulePath = null, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using HeldFile dump = _paths.OpenRead(path, CheatEngineToolNames.Il2CppImportDump, Il2CppDumpStore.MaximumBytes);
		using HeldFile? module = modulePath is null ? null : _paths.OpenRead(modulePath, CheatEngineToolNames.Il2CppImportDump, 0, "modulePath");
		return _store.Import(dump, module, cancellationToken);
	}

	/// <summary>Finds imported types without contacting Cheat Engine.</summary>
	[McpServerTool(Name = CheatEngineToolNames.Il2CppFindTypes, Title = "Find IL2CPP types", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Find types in one imported offline dataset by optional case-insensitive name substring, then page stable image and TypeDefIndex order.")]
	public Il2CppTypePage FindTypes([Description("An active imported dataset id.")] string datasetId, [Description("Optional case-insensitive type-name substring.")] string? nameContains = null, [Description("Zero-based filtered offset.")] int offset = 0, [Description("Maximum returned records, 1 to 100.")] int limit = 100, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return _store.FindTypes(datasetId, nameContains, offset, limit);
	}

	/// <summary>Finds imported fields or methods without contacting Cheat Engine.</summary>
	[McpServerTool(Name = CheatEngineToolNames.Il2CppFindMembers, Title = "Find IL2CPP members", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Find fields or methods in one imported offline dataset, optionally within one type and by case-insensitive name substring, then page stable source order.")]
	public Il2CppMemberPage FindMembers([Description("An active imported dataset id.")] string datasetId, [Description("method or field.")] string kind = "method", [Description("Optional imported type id.")] string? typeId = null, [Description("Optional case-insensitive member-name substring.")] string? nameContains = null, [Description("Zero-based filtered offset.")] int offset = 0, [Description("Maximum returned records, 1 to 100.")] int limit = 100, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return _store.FindMembers(datasetId, kind, typeId, nameContains, offset, limit);
	}

	/// <summary>Validates an imported method RVA against the currently attached matching module.</summary>
	[McpServerTool(Name = CheatEngineToolNames.Il2CppResolveMethod, Title = "Resolve IL2CPP method", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Validate an imported method RVA only against the matching currently attached module. This returns an address observation and PE section classification; it neither proves the method is callable nor executes it.")]
	public Il2CppMethodResolution ResolveMethod([Description("An active imported dataset id with an original module binding.")] string datasetId, [Description("An imported method id with a non-negative RVA.")] string methodId, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ModuleRvaBinding binding = _store.ResolveBinding(datasetId, methodId);
		ModuleRvaResolution resolution = _dispatch.Run(CheatEngineToolNames.Il2CppResolveMethod, token => ModuleBuildBindings.Validate(_dispatch.Client, binding, token), cancellationToken);
		return new(methodId, binding, resolution);
	}

	/// <summary>Explicitly releases an imported offline dataset.</summary>
	[McpServerTool(Name = CheatEngineToolNames.Il2CppDeleteDump, Title = "Delete IL2CPP dump", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Explicitly release an imported IL2CPP offline dataset. Import never replaces an existing dataset automatically.")]
	public Il2CppDeleteResult DeleteDump([Description("The active imported dataset id to release.")] string datasetId, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return _store.Delete(datasetId);
	}
}
