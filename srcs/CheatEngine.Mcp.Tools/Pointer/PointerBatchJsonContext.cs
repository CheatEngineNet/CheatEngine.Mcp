using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>Source-generated JSON metadata for the batch pointer-chain tool.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PointerChainBatchResult))]
[JsonSerializable(typeof(PointerChainCandidate[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
public sealed partial class PointerBatchJsonContext : JsonSerializerContext;
