using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Lua;

/// <summary>Source-generated metadata for the <c>lua_*</c> tool arguments and results; no reflection.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LuaExecuteResult))]
[JsonSerializable(typeof(LuaApiSearchResult))]
[JsonSerializable(typeof(LuaApiSearchLine[]))]
[JsonSerializable(typeof(JsonElement[]))]
public sealed partial class LuaJsonContext : JsonSerializerContext;

/// <summary>Source-generated metadata for the fixed stage that copies caller-authored Lua return values.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LuaExecuteOutcome))]
internal sealed partial class LuaUnsafeJsonContext : JsonSerializerContext;
