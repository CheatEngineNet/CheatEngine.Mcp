using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Modules;

/// <summary>Source-generated metadata for the <c>module_*</c> tool results; no reflection.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ModuleList))]
[JsonSerializable(typeof(ModuleDetails))]
[JsonSerializable(typeof(ExportList))]
[JsonSerializable(typeof(PatchScanResult))]
[JsonSerializable(typeof(PeMachine))]
[JsonSerializable(typeof(PeSubsystem))]
public sealed partial class ModuleJsonContext : JsonSerializerContext;
