using System.ComponentModel;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>
///     Lists, reads, creates, renames and deletes Cheat Engine's Structure Dissect definitions. Structures are Cheat
///     Engine's global state, saved with the cheat table; they outlive the plugin and are never target resources.
/// </summary>
[McpServerToolType]
public sealed class StructureTools
{
	internal const int MaxListLimit = 1000;
	internal const int MaxScannedStructures = 65536;
	internal const int MaxGetLimit = 1024;
	internal const int MaxCreateElements = 1024;
	internal const int MaxPdbElements = 4096;
	internal const int MaxGuessSize = 65536;

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tools; nothing is dispatched until a call.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public StructureTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Pages Cheat Engine's global structures.</summary>
	[McpServerTool(Name = CheatEngineToolNames.StructureList, Title = "List structures", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Pages Cheat Engine's global Structure Dissect definitions: name, size and element count. Structures are shared " +
		"with the user and saved in the cheat table; check a name here before creating one.")]
	public StructurePage List(
		[Description("Only structures whose name contains this text, ignoring case; omit for all.")]
		string? nameContains = null,
		[Description("The zero-based index of the first structure to return.")]
		int offset = 0,
		[Description("The most structures to return (1-1000).")]
		int limit = 100,
		CancellationToken cancellationToken = default)
	{
		string? filter = nameContains is null ? null : StructureArguments.Name(nameContains, "nameContains");
		StructureArguments.Page(offset, limit, MaxListLimit);
		return _dispatch.RunLua(CheatEngineToolNames.StructureList, StructureLuaScripts.List,
			StructuresJsonContext.Default.StructurePage, cancellationToken, filter, offset, limit,
			MaxScannedStructures);
	}

	/// <summary>Reads a structure definition and a page of its elements.</summary>
	[McpServerTool(Name = CheatEngineToolNames.StructureGet, Title = "Get a structure", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Reads one structure, internal ones included, and a page of its elements ordered by offset: index, hexadecimal " +
		"offset, name, valueType, integer display, byteSize and child structure. The detailed format adds the child " +
		"start, custom type and bit fields.")]
	public StructureDefinition Get(
		[Description("The structure's case-sensitive name.")]
		string name,
		[Description("The zero-based index of the first element to return.")]
		int offset = 0,
		[Description("The most elements to return (1-1024).")]
		int limit = 256,
		[Description("concise, or detailed for the child start, custom type and bit fields.")]
		ResultFormat format = ResultFormat.Concise,
		CancellationToken cancellationToken = default)
	{
		string structure = StructureArguments.Name(name, "name");
		StructureArguments.Page(offset, limit, MaxGetLimit);
		bool detailed = format is ResultFormat.Detailed;
		StructureLuaDefinition definition = _dispatch.RunLua(CheatEngineToolNames.StructureGet,
			StructureLuaScripts.Elements, StructureLuaJsonContext.Default.StructureLuaDefinition, cancellationToken,
			structure, null, null, offset, limit, detailed);
		return new StructureDefinition(definition.Name, definition.Size, definition.ElementCount, definition.Internal,
			[.. definition.Elements.Select(element => Element(element, detailed))], definition.Total,
			definition.NextOffset);
	}

	/// <summary>Creates a global structure.</summary>
	[McpServerTool(Name = CheatEngineToolNames.StructureCreate, Title = "Create a structure", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Creates a global structure from at most one source: explicit elements, a copy of cloneFrom, or the PDB type " +
		"pdbTypeName of the loaded symbols; with none it is empty. It is all or nothing and refuses an existing name. " +
		"The structure persists in Cheat Engine and is saved with the table; delete what you created with " +
		"structure_delete.")]
	public StructureSummary Create(
		[Description("The new structure's case-sensitive name; it must not exist yet.")]
		string name,
		[Description("The elements to create (at most 1024); offsets are signed hexadecimal.")]
		StructureElementSpec[]? elements = null,
		[Description("The name of an existing structure to copy.")]
		string? cloneFrom = null,
		[Description("The name of a type in the loaded PDB symbols; check it with structure_get_pdb_layout first.")]
		string? pdbTypeName = null,
		[Description(
			"Create an internal structure: selectable as a child structure, but hidden from the structure list and " +
			"not saved with the table.")]
		bool @internal = false,
		CancellationToken cancellationToken = default)
	{
		string structure = StructureArguments.Name(name, "name");
		int sources = (elements is null ? 0 : 1) + (cloneFrom is null ? 0 : 1) + (pdbTypeName is null ? 0 : 1);
		if (sources > 1)
		{
			throw CheatEngineToolException.InvalidArgument(elements is null ? "cloneFrom" : "elements",
				"pass at most one of elements, cloneFrom and pdbTypeName.");
		}

		if (cloneFrom is not null)
		{
			string source = StructureArguments.Name(cloneFrom, "cloneFrom");
			return _dispatch.RunLua(CheatEngineToolNames.StructureCreate, StructureLuaScripts.CreateClone,
				StructuresJsonContext.Default.StructureSummary, cancellationToken, structure, @internal, source);
		}

		if (pdbTypeName is not null)
		{
			string type = StructureArguments.Name(pdbTypeName, "pdbTypeName");
			return _dispatch.RunLua(CheatEngineToolNames.StructureCreate, StructureLuaScripts.CreateFromPdb,
				StructuresJsonContext.Default.StructureSummary, cancellationToken, structure, @internal, type,
				MaxPdbElements);
		}

		object?[][] specs = StructureArguments.Specs(
			StructureArguments.Batch(elements, "elements", 0, MaxCreateElements), "elements");
		return _dispatch.RunLua(CheatEngineToolNames.StructureCreate, StructureLuaScripts.CreateFromElements,
			StructuresJsonContext.Default.StructureSummary, cancellationToken, structure, @internal, specs);
	}

	/// <summary>Deletes a global structure.</summary>
	[McpServerTool(Name = CheatEngineToolNames.StructureDelete, Title = "Delete a structure", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Deletes a structure from Cheat Engine; pointer elements of other structures that pointed to it lose their " +
		"child structure. Delete only structures you created, unless the user asks. An unknown name is not_found.")]
	public StructureDeleted Delete(
		[Description("The structure's case-sensitive name.")]
		string name,
		CancellationToken cancellationToken = default)
	{
		string structure = StructureArguments.Name(name, "name");
		return _dispatch.RunLua(CheatEngineToolNames.StructureDelete, StructureLuaScripts.Delete,
			StructuresJsonContext.Default.StructureDeleted, cancellationToken, structure);
	}

	/// <summary>Renames a structure, global or internal, in place.</summary>
	[McpServerTool(Name = CheatEngineToolNames.StructureSetName, Title = "Rename a structure", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Renames a structure in place, internal ones included; pointer elements of other structures keep pointing " +
		"to it, unlike a copy with structure_create(cloneFrom) and a delete. A newName that any structure already " +
		"has is invalid_state; the current name is a no-op. A global structure is saved with the table under its " +
		"new name; an internal structure is never saved.")]
	public StructureSummary SetName(
		[Description("The structure's current case-sensitive name.")]
		string name,
		[Description("The new case-sensitive name (1-256 characters); no other structure may have it.")]
		string newName,
		CancellationToken cancellationToken = default)
	{
		string structure = StructureArguments.Name(name, "name");
		string renamed = StructureArguments.Name(newName, "newName");
		return _dispatch.RunLua(CheatEngineToolNames.StructureSetName, StructureLuaScripts.Rename,
			StructuresJsonContext.Default.StructureSummary, cancellationToken, structure, renamed);
	}

	/// <summary>Lets Cheat Engine guess a structure's fields from memory.</summary>
	[McpServerTool(Name = CheatEngineToolNames.StructureAutoguess, Title = "Auto-guess a structure", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Lets Cheat Engine guess element types from the current bytes at address plus offset, like the dissect " +
		"window's automatic guess, creating the structure when missing. The guessed elements are added from offset " +
		"on and existing elements are kept, so guess into a range that has none yet. Guesses are unconfirmed: " +
		"pointers can read as integers and padding as fields. Keep size to the object, usually 1024-4096 bytes.")]
	public StructureSummary Autoguess(
		[Description("The structure's case-sensitive name.")]
		string name,
		[Description(
			"The object's base address or symbol expression, such as 7FF6A1B2C3D0 or [game.exe+1C]+10; the bytes " +
			"are read from this address plus offset.")]
		string address,
		[Description(
			"The structure offset where guessing starts, as hexadecimal, zero or greater: Cheat Engine reads from " +
			"address plus offset and places the first guessed element at this offset.")]
		string offset = "0",
		[Description("The bytes to guess from, a decimal count (1-65536).")]
		int size = 4096,
		[Description("Create the structure when no structure has this name.")]
		bool createIfMissing = true,
		CancellationToken cancellationToken = default)
	{
		string structure = StructureArguments.Name(name, "name");
		string expression = StructureArguments.Expression(address, "address");
		int start = StructureArguments.Offset(offset, "offset");
		if (start < 0)
		{
			throw CheatEngineToolException.InvalidArgument("offset", "must be zero or greater.");
		}

		GuessSize(size);
		return _dispatch.Run(CheatEngineToolNames.StructureAutoguess, token =>
		{
			ulong resolved = StructureArguments.Resolve(_dispatch.Client, expression, "address", token);
			// Cheat Engine reads from its base argument and only labels the elements from its offset argument, as the
			// dissect window's own guess passes the column address plus the start offset.
			ulong first = StructureArguments.Add(resolved, start) ??
						  throw CheatEngineToolException.InvalidArgument("address",
							  "plus offset leaves the address space.");
			return _dispatch.ExecuteLua(CheatEngineToolNames.StructureAutoguess, StructureLuaScripts.AutoGuess,
				StructuresJsonContext.Default.StructureSummary, token, structure, "0x" + HexFormat.Address(first),
				start, size, createIfMissing);
		}, cancellationToken);
	}

	/// <summary>Fills a structure from a .NET object's layout.</summary>
	[McpServerTool(Name = CheatEngineToolNames.StructureFillFromDotNet, Title = "Fill a structure from .NET",
		ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[Description(
		"Adds the fields of the managed object at address, as Cheat Engine's .NET data collector reports them, to a " +
		"structure, creating it when missing; give the object's address, not a field's. It refuses with busy while " +
		"the target is paused or stopped in the debugger, and with not_found when no .NET layout is found. The query " +
		"runs out of process and can block Cheat Engine for a few seconds.")]
	public StructureSummary FillFromDotNet(
		[Description("The structure's case-sensitive name.")]
		string name,
		[Description("The managed object's address or symbol expression.")]
		string address,
		[Description("Rename the structure to the object's .NET class name.")]
		bool rename = false,
		[Description("Create the structure when no structure has this name.")]
		bool createIfMissing = true,
		CancellationToken cancellationToken = default)
	{
		string structure = StructureArguments.Name(name, "name");
		string expression = StructureArguments.Expression(address, "address");
		return _dispatch.Run(CheatEngineToolNames.StructureFillFromDotNet, token =>
		{
			ulong resolved = StructureArguments.Resolve(_dispatch.Client, expression, "address", token);
			return _dispatch.ExecuteLua(CheatEngineToolNames.StructureFillFromDotNet,
				StructureLuaScripts.FillFromDotNet, StructuresJsonContext.Default.StructureSummary, token, structure,
				"0x" + HexFormat.Address(resolved), rename, createIfMissing);
		}, cancellationToken);
	}

	/// <summary>Reads a PDB type's field layout.</summary>
	[McpServerTool(Name = CheatEngineToolNames.StructureGetPdbLayout, Title = "Get a PDB structure layout",
		ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Reads the fields of a type from the loaded PDB symbols: offset, name and value type. found is false when the " +
		"symbols do not describe it; load a PDB with symbol_add_module(enumStructures=true). Create a structure from it " +
		"with structure_create(pdbTypeName).")]
	public PdbLayout GetPdbLayout(
		[Description("The PDB type name, such as _PEB.")]
		string typeName,
		[Description("The most fields to return (1-4096).")]
		int limit = 1024,
		CancellationToken cancellationToken = default)
	{
		string type = StructureArguments.Name(typeName, "typeName");
		StructureArguments.Page(0, limit, MaxPdbElements);
		StructureLuaPdbLayout layout = _dispatch.RunLua(CheatEngineToolNames.StructureGetPdbLayout,
			StructureLuaScripts.PdbLayout, StructureLuaJsonContext.Default.StructureLuaPdbLayout, cancellationToken,
			type, limit);
		return new PdbLayout(type, layout.Found,
		[
			.. layout.Elements.Select(static field => new PdbLayoutElement(HexFormat.Offset(field.Offset),
				field.Name ?? string.Empty,
				field.Vartype is { } vartype ? StructureValueCodec.ElementType(vartype, null) : null))
		], layout.Truncated);
	}

	/// <summary>Maps a copied element to the contract.</summary>
	internal static StructureElement Element(StructureLuaElement element, bool detailed)
	{
		StructureElementType type = StructureValueCodec.ElementType(element.Vartype, element.Display);
		return new StructureElement(element.Index, HexFormat.Offset(element.Offset), element.Name ?? string.Empty,
			type, StructureValueCodec.Display(element.Vartype, element.Display), element.ByteSize,
			element.ChildStructure,
			detailed && element.ChildStructure is not null ? HexFormat.Offset(element.ChildStructureStart ?? 0) : null,
			detailed ? element.CustomType : null, detailed ? element.BitStart : null,
			detailed ? element.BitSize : null);
	}

	private static void GuessSize(int size)
	{
		if (size < 1)
		{
			throw CheatEngineToolException.InvalidArgument("size", "must be at least 1.");
		}

		if (size > MaxGuessSize)
		{
			throw CheatEngineToolException.LimitExceeded("size", $"must be at most {MaxGuessSize}.");
		}
	}
}
