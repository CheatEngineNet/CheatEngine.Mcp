using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>
///     The source-generated JSON metadata of the <c>pointer_*</c> tools: their results, error details and parameter
///     types.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PointerChainResult))]
[JsonSerializable(typeof(PointerHopFailure))]
[JsonSerializable(typeof(PointerReferenceResult))]
[JsonSerializable(typeof(PointerMapInfo))]
[JsonSerializable(typeof(PointerMapList))]
[JsonSerializable(typeof(PointerCaptureCompleteness))]
[JsonSerializable(typeof(PointerDeleteResult))]
[JsonSerializable(typeof(PointerScanInfo))]
[JsonSerializable(typeof(PointerScanList))]
[JsonSerializable(typeof(PointerRescanResult))]
[JsonSerializable(typeof(PointerPathPage))]
[JsonSerializable(typeof(PointerCleanupDetails))]
[JsonSerializable(typeof(PointerMapFileResult))]
[JsonSerializable(typeof(PointerScanFileResult))]
[JsonSerializable(typeof(PointerPathSort))]
[JsonSerializable(typeof(McpValueType?))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int?))]
public sealed partial class PointerJsonContext : JsonSerializerContext;
