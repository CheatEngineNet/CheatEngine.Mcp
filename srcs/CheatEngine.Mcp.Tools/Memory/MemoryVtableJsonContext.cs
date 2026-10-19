using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>Source-generated metadata for the read-only vtable inspection result.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MemoryVtableResult))]
[JsonSerializable(typeof(MemoryVtableSlot[]))]
public sealed partial class MemoryVtableJsonContext : JsonSerializerContext;
