using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>Source-generated JSON metadata for <c>memory_find_strings</c>.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MemoryStringFindResult))]
[JsonSerializable(typeof(MemoryStringSearchEncoding))]
public sealed partial class MemoryStringJsonContext : JsonSerializerContext;
