using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Tools.Memory;

namespace CheatEngine.Mcp.Tools.Scan;

/// <summary>Source-generated JSON metadata for the <c>scan_*</c> arguments, results and fixed Lua results.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ScanState))]
[JsonSerializable(typeof(ScanStatusResult))]
[JsonSerializable(typeof(ScanResultsResult))]
[JsonSerializable(typeof(ScanScannerList))]
[JsonSerializable(typeof(ScanReleaseResult))]
[JsonSerializable(typeof(ScanStopResult))]
[JsonSerializable(typeof(ScanReleaseFailure))]
[JsonSerializable(typeof(ScanUiStatus))]
[JsonSerializable(typeof(ScanUiResults))]
[JsonSerializable(typeof(ScanUiStop))]
[JsonSerializable(typeof(ScanMainSettings))]
[JsonSerializable(typeof(ScanRounding))]
[JsonSerializable(typeof(ScanMappedOverrideFailure))]
[JsonSerializable(typeof(ProtectionRequirement))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(bool))]
public sealed partial class ScanJsonContext : JsonSerializerContext;
