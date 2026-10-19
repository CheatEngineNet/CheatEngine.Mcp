using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Structures;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(StructureInterchangeResult))]
[JsonSerializable(typeof(StructureInterchangeLuaExport))]
[JsonSerializable(typeof(StructureInterchangeLuaImport))]
internal sealed partial class StructureInterchangeJsonContext : JsonSerializerContext;
