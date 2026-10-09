using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>Source-generated metadata for supplied pointer-access analysis.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PointerAccessInfo))]
[JsonSerializable(typeof(PointerAccessStatus))]
[JsonSerializable(typeof(PointerAccessContextPhase))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
internal sealed partial class PointerAccessJsonContext : JsonSerializerContext;
