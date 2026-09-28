using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Table;

/// <summary>Source-generated metadata for the <c>table_*</c> result contracts.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TableLoadResult))]
[JsonSerializable(typeof(TableSaveResult))]
[JsonSerializable(typeof(TableFilePage))]
[JsonSerializable(typeof(TableInspectionResult))]
[JsonSerializable(typeof(TableFileEntry))]
[JsonSerializable(typeof(string[]))]
public sealed partial class TableJsonContext : JsonSerializerContext;
