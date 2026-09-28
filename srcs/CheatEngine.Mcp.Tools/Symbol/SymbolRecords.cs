using System.ComponentModel;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Symbol;

/// <summary>The resolution of each requested expression, in request order.</summary>
/// <param name="Items">One entry per expression.</param>
public sealed record SymbolResolveResult(
	[property: Description("One entry per expression, in request order; check each entry's error.")]
	SymbolResolution[] Items);

/// <summary>One entry of <see cref="SymbolResolveResult" />.</summary>
/// <param name="Expression">The expression as given.</param>
/// <param name="Address">The resolved address.</param>
/// <param name="Name">Cheat Engine's best name for the address.</param>
/// <param name="Symbol">The symbol record, when the expression names a symbol.</param>
/// <param name="Error">Why the expression did not resolve.</param>
public sealed record SymbolResolution(
	[property: Description("The expression as given.")]
	string Expression,
	[property:
		Description("The resolved address as uppercase hexadecimal without 0x; omitted when it did not resolve.")]
	string? Address = null,
	[property: Description(
		"Cheat Engine's best name for the address: a registered symbol, module.export or module+offset.")]
	string? Name = null,
	[property: Description("The symbol record, when the expression names a symbol Cheat Engine knows.")]
	SymbolDetails? Symbol = null,
	[property: Description("Why this expression did not resolve; the other entries are unaffected.")]
	SymbolItemError? Error = null);

/// <summary>A symbol record as Cheat Engine's <c>getSymbolInfo</c> copies it.</summary>
/// <param name="Module">The module that defines the symbol.</param>
/// <param name="Key">The symbol's search key.</param>
/// <param name="Address">The symbol's address.</param>
/// <param name="Size">The symbol's size.</param>
public sealed record SymbolDetails(
	[property: Description("The module that defines the symbol.")]
	string Module,
	[property: Description("The symbol's search key, its name without the module.")]
	string Key,
	[property: Description("The symbol's address as uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The symbol's size in bytes, zero when unknown.")]
	long Size);

/// <summary>The failure of one expression of a batch.</summary>
/// <param name="Kind">The failure class.</param>
/// <param name="Message">What failed.</param>
public sealed record SymbolItemError(
	[property: Description("The failure class, such as not_found.")]
	ToolErrorKind Kind,
	[property: Description("What failed.")]
	string Message);

/// <summary>One page of the symbols whose names contain the searched text.</summary>
/// <param name="Total">The number of matches collected.</param>
/// <param name="Truncated">Whether the collection stopped at its cap.</param>
/// <param name="SymbolsLoaded">
///     Whether Cheat Engine reported every symbol loaded, and its IL2CPP method list complete, before the search.
/// </param>
/// <param name="Symbols">The matches of this page.</param>
/// <param name="NextOffset">The offset of the next page.</param>
public sealed record SymbolFindResult(
	[property: Description("The number of matches collected, at most 10000; see truncated.")]
	int Total,
	[property: Description(
		"Whether the search stopped at 10000 matches, so more symbols match and the collected ones are an arbitrary subset; narrow nameContains or pass module.")]
	bool Truncated,
	[property: Description(
		"Whether Cheat Engine reported every symbol loaded, and any IL2CPP method list fully enumerated, before the search; while false a name missing now may appear later.")]
	bool SymbolsLoaded,
	[property: Description("The matches of this page, sorted by lowercase name, then name, then address.")]
	SymbolMatch[] Symbols,
	[property: Description("The offset of the next page; omitted on the last page.")]
	int? NextOffset = null);

/// <summary>One symbol of <see cref="SymbolFindResult" />.</summary>
/// <param name="Name">The symbol name as Cheat Engine lists it.</param>
/// <param name="Address">The symbol's address.</param>
/// <param name="Module">The module that defines the symbol.</param>
/// <param name="Size">The symbol's size, when Cheat Engine knows it.</param>
/// <param name="Registered">Whether the symbol is a registered symbol or comes from a registered symbol list.</param>
public sealed record SymbolMatch(
	[property: Description("The symbol name as Cheat Engine lists it; use it in any address expression.")]
	string Name,
	[property: Description("The symbol's address as uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The module that defines the symbol, as Cheat Engine names it; omitted when it does not say.")]
	string? Module = null,
	[property: Description(
		"The symbol's size in bytes, or a registered allocation's size; omitted when unknown or zero, and for the size 1 Cheat Engine gives symbols of registered symbol lists by default.")]
	long? Size = null,
	[property: Description(
		"True for a symbol registered by a table, a script (including {$C} code and other registered symbol lists) or symbol_register; omitted for loaded symbols.")]
	bool? Registered = null);

/// <summary>A symbol this activation registered.</summary>
/// <param name="Name">The symbol name.</param>
/// <param name="Address">The address it names.</param>
/// <param name="DoNotSave">Whether saved tables omit it.</param>
/// <param name="ResourceId">The tracked resource that owns it.</param>
public sealed record RegisteredSymbol(
	[property: Description("The symbol name; use it in any address expression.")]
	string Name,
	[property: Description("The address it names, as uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("Whether a saved cheat table omits the symbol.")]
	bool DoNotSave,
	[property: Description(
		"The id runtime_list_resources reports for it; runtime_release_resources and symbol_unregister release it.")]
	string ResourceId);

/// <summary>What unregistering a symbol did.</summary>
/// <param name="Name">The symbol name.</param>
/// <param name="Address">The address it named.</param>
/// <param name="Release">The release outcome.</param>
public sealed record SymbolReleaseResult(
	[property: Description("The symbol name.")]
	string Name,
	[property: Description("The address it named, as uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("What the release did.")]
	ResourceReleaseOutcome Release);

/// <summary>One page of Cheat Engine's registered symbols.</summary>
/// <param name="Total">The number of symbols that match the filter.</param>
/// <param name="NextOffset">The offset of the next page.</param>
/// <param name="Truncated">Whether Cheat Engine holds more symbols than were copied.</param>
/// <param name="Symbols">The symbols of this page.</param>
public sealed record RegisteredSymbolList(
	[property: Description("The number of copied symbols that match nameContains.")]
	int Total,
	[property: Description("Whether Cheat Engine holds more than the 8192 symbols that were copied.")]
	bool Truncated,
	[property: Description("The symbols of this page, in Cheat Engine's order.")]
	RegisteredSymbolEntry[] Symbols,
	[property: Description("The offset of the next page; omitted on the last page.")]
	int? NextOffset = null);

/// <summary>One registered symbol of <see cref="RegisteredSymbolList" />.</summary>
/// <param name="Name">The symbol name.</param>
/// <param name="Address">The address it names.</param>
/// <param name="OwnedByMcp">Whether this activation registered it.</param>
/// <param name="DoNotSave">Whether saved tables omit it.</param>
/// <param name="AllocSize">The size of the allocation it names.</param>
/// <param name="ProcessId">The process of that allocation.</param>
/// <param name="ResourceId">The tracked resource of an owned symbol.</param>
public sealed record RegisteredSymbolEntry(
	[property: Description("The symbol name.")]
	string Name,
	[property:
		Description("Whether this plugin activation registered it (symbol_register); only those can be unregistered.")]
	bool OwnedByMcp,
	[property: Description("The address it names, as uppercase hexadecimal without 0x.")]
	string? Address = null,
	[property: Description("Whether a saved cheat table omits the symbol; omitted when false.")]
	bool? DoNotSave = null,
	[property: Description("The size of the allocation it names, for an Auto Assembler alloc; omitted otherwise.")]
	long? AllocSize = null,
	[property: Description("The process of that allocation; omitted otherwise.")]
	int? ProcessId = null,
	[property: Description("The resource id of an owned symbol, as runtime_list_resources reports it.")]
	string? ResourceId = null);

/// <summary>Cheat Engine's symbol lookup module precedence.</summary>
/// <param name="Modules">The modules, first first.</param>
/// <param name="Truncated">Whether the list is longer than was copied.</param>
public sealed record ModulePreference(
	[property: Description(
		"Extensionless module names; a symbol present in several modules resolves in the first listed one.")]
	string[] Modules,
	[property: Description("Whether Cheat Engine's list is longer than the 1024 names copied.")]
	bool Truncated);

/// <summary>What a symbol reload started and whether symbols are loaded.</summary>
/// <param name="Done">Whether Cheat Engine reports every symbol loaded.</param>
/// <param name="Scope">The reload that was started.</param>
public sealed record SymbolLoadState(
	[property: Description(
		"Whether Cheat Engine reports every symbol loaded; false while loading continues in the background.")]
	bool Done,
	[property: Description("The reload that was started: new_modules, all or dotnet.")]
	SymbolReloadScope Scope);

/// <summary>A symbol file loaded at a base address.</summary>
/// <param name="Path">The loaded file.</param>
/// <param name="Base">The base address.</param>
/// <param name="EnumStructures">Whether structures were enumerated.</param>
public sealed record SymbolModuleLoad(
	[property: Description("The normalized path of the loaded file.")]
	string Path,
	[property: Description("The base address the symbols were loaded at, as uppercase hexadecimal without 0x.")]
	string Base,
	[property: Description("Whether PDB structures were enumerated for structure_get_pdb_layout.")]
	bool EnumStructures);

/// <summary>The requested system symbol sources enabled in Cheat Engine.</summary>
public sealed record SymbolSourcesEnabled(bool WindowsEnabled, bool KernelEnabled, bool ExternalAccess);

/// <summary>Which symbols <c>symbol_reload</c> reloads; the wire value is the <c>snake_case</c> member name.</summary>
[JsonConverter(typeof(ContractEnumConverter<SymbolReloadScope>))]
public enum SymbolReloadScope
{
	/// <summary>Load the symbols of modules loaded since the last scan (<c>loadNewSymbols</c>).</summary>
	NewModules,

	/// <summary>Reinitialize the whole symbol handler without waiting (<c>reinitializeSymbolhandler(false)</c>).</summary>
	All,

	/// <summary>Reinitialize the .NET symbols, optionally of one module (<c>reinitializeDotNetSymbolhandler</c>).</summary>
	Dotnet
}

/// <summary>One registered symbol as the fixed script copies it from <c>enumRegisteredSymbols</c>.</summary>
/// <param name="Name">The symbol name.</param>
/// <param name="Address">The address as uppercase hexadecimal.</param>
/// <param name="AllocSize">The allocation size, when the symbol names an allocation.</param>
/// <param name="ProcessId">The allocation's process.</param>
/// <param name="DoNotSave">Whether saved tables omit it.</param>
internal sealed record LuaRegisteredSymbol(
	[property: Description("The symbol name.")]
	string Name,
	[property: Description("The address as uppercase hexadecimal.")]
	string? Address = null,
	[property: Description("The allocation size, when the symbol names an allocation.")]
	long? AllocSize = null,
	[property: Description("The allocation's process.")]
	int? ProcessId = null,
	[property: Description("Whether saved tables omit it.")]
	bool? DoNotSave = null);

/// <summary>The bounded copy of <c>enumRegisteredSymbols</c>.</summary>
/// <param name="Symbols">The copied symbols.</param>
/// <param name="Count">How many symbols Cheat Engine holds.</param>
/// <param name="Truncated">Whether fewer were copied.</param>
internal sealed record LuaRegisteredSymbols(
	[property: Description("The copied symbols.")]
	LuaRegisteredSymbol[] Symbols,
	[property: Description("How many symbols Cheat Engine holds.")]
	int Count,
	[property: Description("Whether fewer were copied.")]
	bool Truncated);

/// <summary>What the fixed search script collected and the page it returned.</summary>
/// <param name="Total">How many matches were collected.</param>
/// <param name="Truncated">Whether the collection stopped at its cap.</param>
/// <param name="SymbolsLoaded">
///     Whether <c>symbolsDoneLoading</c> reported true, and no IL2CPP method list was still being filled, before the
///     copies.
/// </param>
/// <param name="Symbols">The requested page of the sorted matches.</param>
internal sealed record LuaSymbolFind(
	[property: Description("How many matches were collected, at most the cap.")]
	int Total,
	[property: Description("Whether the collection stopped at its cap.")]
	bool Truncated,
	[property: Description(
		"Whether symbolsDoneLoading reported true and the IL2CPP method list was complete before the copies.")]
	bool SymbolsLoaded,
	[property: Description("The requested page of the sorted matches.")]
	SymbolMatch[] Symbols);

/// <summary>What the fixed reload script observed.</summary>
/// <param name="Done">Whether <c>symbolsDoneLoading</c> reported true.</param>
internal sealed record LuaSymbolReload(
	[property: Description("Whether symbolsDoneLoading reported true.")]
	bool Done);

/// <summary>What the fixed module-load script observed.</summary>
/// <param name="Loaded">Whether <c>symbolHandlerAddModule</c> did not report failure.</param>
internal sealed record LuaSymbolModuleLoad(
	[property: Description("Whether symbolHandlerAddModule did not report failure.")]
	bool Loaded);
