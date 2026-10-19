using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Il2Cpp;

/// <summary>Source-generated JSON metadata for the offline IL2CPP tool results.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Il2CppDumpImport))]
[JsonSerializable(typeof(Il2CppTypePage))]
[JsonSerializable(typeof(Il2CppMemberPage))]
[JsonSerializable(typeof(Il2CppMethodResolution))]
[JsonSerializable(typeof(Il2CppDeleteResult))]
public sealed partial class Il2CppJsonContext : JsonSerializerContext;
