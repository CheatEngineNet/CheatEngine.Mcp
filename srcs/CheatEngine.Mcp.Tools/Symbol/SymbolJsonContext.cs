using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Symbol;

/// <summary>Source-generated metadata for the <c>symbol_*</c> tool arguments and results; no reflection.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SymbolResolveResult))]
[JsonSerializable(typeof(RegisteredSymbol))]
[JsonSerializable(typeof(SymbolReleaseResult))]
[JsonSerializable(typeof(RegisteredSymbolList))]
[JsonSerializable(typeof(ModulePreference))]
[JsonSerializable(typeof(SymbolLoadState))]
[JsonSerializable(typeof(SymbolModuleLoad))]
[JsonSerializable(typeof(SymbolSourcesEnabled))]
[JsonSerializable(typeof(SymbolReloadScope))]
[JsonSerializable(typeof(string[]))]
public sealed partial class SymbolJsonContext : JsonSerializerContext;

/// <summary>
///     Source-generated metadata for the copies the fixed <c>symbol_*</c> scripts return; they never leave the tools, so
///     the context is internal and not part of the composed serializer options.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LuaRegisteredSymbols))]
[JsonSerializable(typeof(LuaSymbolReload))]
[JsonSerializable(typeof(LuaSymbolModuleLoad))]
internal sealed partial class SymbolLuaJsonContext : JsonSerializerContext;
