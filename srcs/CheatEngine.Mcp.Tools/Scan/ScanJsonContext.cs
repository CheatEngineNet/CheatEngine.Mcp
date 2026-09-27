using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Scan;

/// <summary>Source-generated JSON metadata for the <c>scan_*</c> contract and its fixed Lua results.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ScanStatusResult))]
[JsonSerializable(typeof(ScanResultsResult))]
[JsonSerializable(typeof(ScanScannerList))]
[JsonSerializable(typeof(ScanReleaseResult))]
[JsonSerializable(typeof(ScanStopResult))]
[JsonSerializable(typeof(ScanReleaseFailure))]
[JsonSerializable(typeof(ScanUiStatus))]
[JsonSerializable(typeof(ScanUiResults))]
[JsonSerializable(typeof(bool))]
public sealed partial class ScanJsonContext : JsonSerializerContext;
