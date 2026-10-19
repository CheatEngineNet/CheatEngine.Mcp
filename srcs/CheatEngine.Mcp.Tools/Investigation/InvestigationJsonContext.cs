using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Tools.Modules;

namespace CheatEngine.Mcp.Tools.Investigation;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InvestigationNote))]
[JsonSerializable(typeof(InvestigationNotePage))]
[JsonSerializable(typeof(InvestigationExportResult))]
[JsonSerializable(typeof(ModuleRvaBinding))]
[JsonSerializable(typeof(ModuleBuildIdentity))]
public sealed partial class InvestigationJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InvestigationNoteFile))]
[JsonSerializable(typeof(InvestigationExportFile))]
internal sealed partial class InvestigationFileJsonContext : JsonSerializerContext;
