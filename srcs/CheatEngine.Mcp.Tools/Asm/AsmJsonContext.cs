using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Client.Assembly;

namespace CheatEngine.Mcp.Tools.Asm;

/// <summary>Source-generated metadata for public <c>asm_*</c> results.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AsmAssembleResult))]
[JsonSerializable(typeof(AsmCheckResult))]
[JsonSerializable(typeof(AsmPatchApplied))]
[JsonSerializable(typeof(AsmPatchReleased))]
[JsonSerializable(typeof(AsmPatchList))]
[JsonSerializable(typeof(AsmGeneratedScript))]
[JsonSerializable(typeof(AsmPatchInfo))]
[JsonSerializable(typeof(InstructionEncodingPreference))]
public sealed partial class AsmJsonContext : JsonSerializerContext;

/// <summary>Source-generated metadata for fixed Lua results that remain inside the assembler tools.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AsmLuaScript))]
internal sealed partial class AsmLuaJsonContext : JsonSerializerContext;
