using System.Collections.Immutable;
using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Modules;

/// <summary>
///     The <c>module_list_imports</c> tool: a module's import and delay-load directories, parsed from target memory,
///     with the live value of every import address table slot.
/// </summary>
[McpServerToolType]
public sealed class ModuleImportTools
{
	/// <summary>The most bytes one call reads from the module to parse its import directories.</summary>
	internal const long MaximumImportBytes = 8 * 1024 * 1024;

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tool; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public ModuleImportTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Pages a module's imported functions with their slots, live values and targets.</summary>
	/// <param name="module">The module name, or an address inside it.</param>
	/// <param name="dllContains">A case-insensitive substring of the imported DLL's name.</param>
	/// <param name="nameContains">A case-insensitive substring of the imported function's name.</param>
	/// <param name="includeDelayLoaded">Whether to list the delay-load directory too.</param>
	/// <param name="offset">The index of the first import to return.</param>
	/// <param name="limit">The most imports to return.</param>
	/// <param name="cancellationToken">The request's token.</param>
	/// <returns>The page.</returns>
	[McpServerTool(Name = CheatEngineToolNames.ModuleListImports, Title = "List module imports", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Page the imported functions of one module of the attached process, parsed from its import directory (and delay-load directory) in memory: the DLL, the IAT slot address the code calls through, the name and hint or the ordinal, the pointer the slot holds now, and the loaded module and Cheat Engine symbol that pointer lands in. A bound IAT without a lookup table gives slots without names; a delay-loaded slot points at this module's loader stub until its first call. A pointer outside the named DLL is not proof of a hook: api-ms-win-* sets resolve into kernelbase.dll and others. The whole directory is read in one dispatch before filtering and paging, holding Cheat Engine while it runs; large directories can take seconds. Filter with dllContains and nameContains, then page with offset and limit (at most 1000). A module whose import data exceeds 8 MiB or 65536 entries is refused.")]
	public ImportList ListImports(
		[Description("The module name, such as game.exe, or an address expression inside it.")]
		string module,
		[Description("A case-insensitive substring of the imported DLL's name, such as kernel32; at most 256 characters.")]
		string? dllContains = null,
		[Description(
			"A case-insensitive substring of the imported function's name, such as QueryPerformance; at most 256 characters. Imports without a name never match.")]
		string? nameContains = null,
		[Description("Whether to list the delay-load directory after the import directory.")]
		bool includeDelayLoaded = true,
		[Description("The index of the first import to return.")]
		int offset = 0,
		[Description("The most imports to return, 1 to 1000.")]
		int limit = 200,
		CancellationToken cancellationToken = default)
	{
		string wanted = ModuleLocator.RequireModule(module);
		string? dllFilter = ModuleLocator.OptionalFilter(dllContains, "dllContains");
		string? nameFilter = ModuleLocator.OptionalFilter(nameContains, "nameContains");
		_ = Paging.Slice(Array.Empty<PeImport>(), offset, limit, ModuleTools.MaximumLimit);
		ICheatEngineClient client = _dispatch.Client;
		return _dispatch.Run(CheatEngineToolNames.ModuleListImports, token =>
		{
			ModuleInfo located = ModuleLocator.Find(client, wanted, out ImmutableArray<ModuleInfo> modules, token);
			byte[] headerBytes = ModuleLocator.ReadHeaderBytes(client, located, token);
			PeHeaders headers = ModuleExportTools.ParseOrRefuse(located, headerBytes);
			ulong baseAddress = located.BaseAddress.ToUInt64();
			ModuleMemoryImage image = new(client, located.Name, baseAddress,
				ModuleLocator.MappedSize(located, headers.SizeOfImage), MaximumImportBytes, token);
			List<PeImport> imports;
			try
			{
				imports = PeImageReader.ReadImports(headers, image, includeDelayLoaded, baseAddress);
			}
			catch (InvalidDataException exception)
			{
				throw ModuleExportTools.Malformed(located, exception);
			}

			PeImport[] matching = [.. imports.Where(import => Matches(import, dllFilter, nameFilter))];
			PageSlice<PeImport> page = Paging.Slice(matching, offset, limit, ModuleTools.MaximumLimit);
			ModuleImport[] items =
			[
				.. page.Items.Select(import => Describe(client, modules, baseAddress, import, token))
			];
			return new ImportList(located.Name, page.Total, items, page.NextOffset);
		}, cancellationToken);
	}

	private static bool Matches(PeImport import, string? dllFilter, string? nameFilter)
	{
		return (dllFilter is null || import.Dll.Contains(dllFilter, StringComparison.OrdinalIgnoreCase)) &&
			   (nameFilter is null || import.Name?.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) == true);
	}

	/// <summary>Builds one result entry and names its value for the returned page only. Inside the dispatch.</summary>
	private static ModuleImport Describe(ICheatEngineClient client, ImmutableArray<ModuleInfo> modules,
		ulong baseAddress, PeImport import, CancellationToken token)
	{
		string? targetModule = null;
		string? targetSymbol = null;
		if (import.Value != 0)
		{
			foreach (ModuleInfo candidate in modules)
			{
				if (ModuleLocator.Contains(candidate, import.Value))
				{
					targetModule = candidate.Name;
					break;
				}
			}

			targetSymbol = Symbol(client, import.Value, token);
		}

		return new ModuleImport(import.Dll, HexFormat.Address(unchecked(baseAddress + import.SlotRva)),
			import.DelayLoaded, HexFormat.Address(import.Value), import.Name, import.Ordinal, import.Hint,
			targetModule, targetSymbol);
	}

	/// <summary>Cheat Engine's name for an address, or <see langword="null" /> when it only echoes the address.</summary>
	private static string? Symbol(ICheatEngineClient client, ulong value, CancellationToken token)
	{
		if (client.Inspection.TryResolveName(new Address(value), out string? name, out CheatEngineFailure failure,
				token))
		{
			return string.IsNullOrWhiteSpace(name) || (HexParse.TryAddress(name, out ulong echoed) && echoed == value)
				? null
				: name;
		}

		// A missing name is data; a lost target or a cancelled call is the whole request's failure.
		if (failure.Kind is CheatEngineFailureKind.TargetNotAttached or CheatEngineFailureKind.TargetChanged
			or CheatEngineFailureKind.TargetIdentityUnavailable or CheatEngineFailureKind.ActivationExpired
			or CheatEngineFailureKind.Cancelled or CheatEngineFailureKind.RuntimeChanged)
		{
			throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
		}

		return null;
	}
}
