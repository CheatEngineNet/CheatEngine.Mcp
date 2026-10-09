using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Speedhack;

/// <summary>Source-generated metadata for the <c>speedhack_*</c> arguments and results.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SpeedhackState))]
[JsonSerializable(typeof(SpeedhackSetResult))]
[JsonSerializable(typeof(double))]
public sealed partial class SpeedhackJsonContext : JsonSerializerContext;

/// <summary>Source-generated metadata for the fixed Lua copies used only inside the speedhack tool.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LuaSpeedhackState))]
[JsonSerializable(typeof(SpeedhackRestoreFailure))]
internal sealed partial class SpeedhackLuaJsonContext : JsonSerializerContext;
