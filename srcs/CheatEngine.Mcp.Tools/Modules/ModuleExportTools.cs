using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Inspection;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Modules;

/// <summary>The <c>module_list_exports</c> tool: a module's export directory, parsed from target memory.</summary>
[McpServerToolType]
public sealed class ModuleExportTools
{
	/// <summary>The most bytes one call reads from the module to parse its export directory.</summary>
	internal const long MaximumExportBytes = 8 * 1024 * 1024;

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tool; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public ModuleExportTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Pages a module's exports.</summary>
	/// <param name="module">The module name, or an address inside it.</param>
	/// <param name="nameContains">A case-insensitive substring of the export name.</param>
	/// <param name="offset">The index of the first export to return.</param>
	/// <param name="limit">The most exports to return.</param>
	/// <param name="cancellationToken">The request's token.</param>
	/// <returns>The page.</returns>
	[McpServerTool(Name = CheatEngineToolNames.ModuleListExports, Title = "List module exports", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Page the exports of one module of the attached process, parsed from its export directory in memory: name (omitted for ordinal-only exports), ordinal and address, or the forwarder such as NTDLL.RtlAllocateHeap for a forwarded export. The whole directory is read in one dispatch before filtering and paging, holding Cheat Engine while it runs; large directories can take seconds. Filter with nameContains, then page with offset and limit (at most 1000). A module whose export data exceeds 8 MiB or 65536 entries is refused.")]
	public ExportList ListExports(
		[Description("The module name, such as kernel32.dll, or an address expression inside it.")]
		string module,
		[Description("A case-insensitive substring of the export name; at most 256 characters.")]
		string? nameContains = null,
		[Description("The index of the first export to return.")]
		int offset = 0,
		[Description("The most exports to return, 1 to 1000.")]
		int limit = 200,
		CancellationToken cancellationToken = default)
	{
		string wanted = ModuleLocator.RequireModule(module);
		string? filter = ModuleLocator.OptionalFilter(nameContains, "nameContains");
		_ = Paging.Slice(Array.Empty<PeExport>(), offset, limit, ModuleTools.MaximumLimit);
		ICheatEngineClient client = _dispatch.Client;
		(ModuleInfo found, List<PeExport> exports) = _dispatch.Run(CheatEngineToolNames.ModuleListExports,
			token =>
			{
				ModuleInfo located = ModuleLocator.Find(client, wanted, token);
				byte[] headerBytes = ModuleLocator.ReadHeaderBytes(client, located, token);
				PeHeaders headers = ParseOrRefuse(located, headerBytes);
				ModuleMemoryImage image = new(client, located.Name, located.BaseAddress.ToUInt64(),
					ModuleLocator.MappedSize(located, headers.SizeOfImage), MaximumExportBytes, token);
				try
				{
					return (located, PeImageReader.ReadExports(headers, image));
				}
				catch (InvalidDataException exception)
				{
					throw Malformed(located, exception);
				}
			}, cancellationToken);

		PeExport[] matching = filter is null
			? [.. exports]
			: [.. exports.Where(export => export.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true)];
		PageSlice<PeExport> page = Paging.Slice(matching, offset, limit, ModuleTools.MaximumLimit);
		ulong baseAddress = found.BaseAddress.ToUInt64();
		ModuleExport[] items =
		[
			.. page.Items.Select(export => new ModuleExport(export.Ordinal, export.Name,
				export.Forwarder is null ? HexFormat.Address(unchecked(baseAddress + export.Rva)) : null,
				export.Forwarder))
		];
		return new ExportList(found.Name, page.Total, items, page.NextOffset);
	}

	internal static PeHeaders ParseOrRefuse(ModuleInfo module, byte[] headerBytes)
	{
		try
		{
			return PeImageReader.ParseHeaders(headerBytes);
		}
		catch (InvalidDataException exception)
		{
			throw Malformed(module, exception);
		}
	}

	internal static CheatEngineToolException Malformed(ModuleInfo module, InvalidDataException exception)
	{
		return new CheatEngineToolException(new ToolError(ToolErrorKind.InvalidState,
				$"The mapped PE image of {module.Name} is malformed or unreadable: {exception.Message}", null,
				ToolHostEffect.Completed, false, "Check that the module is fully loaded, or read it with memory_read."),
			exception);
	}
}
