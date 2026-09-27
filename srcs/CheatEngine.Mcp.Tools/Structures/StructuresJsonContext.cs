using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>Source-generated metadata for the structure tools' arguments and results; no reflection.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(StructurePage))]
[JsonSerializable(typeof(StructureDefinition))]
[JsonSerializable(typeof(StructureSummary))]
[JsonSerializable(typeof(StructureDeleted))]
[JsonSerializable(typeof(StructureChange))]
[JsonSerializable(typeof(StructureBatchFailure))]
[JsonSerializable(typeof(PdbLayout))]
[JsonSerializable(typeof(StructureReadResult))]
[JsonSerializable(typeof(StructureWriteResult))]
[JsonSerializable(typeof(StructureComparison))]
[JsonSerializable(typeof(StructureElementSpec[]))]
[JsonSerializable(typeof(StructureElementUpdate[]))]
[JsonSerializable(typeof(StructureCompareMode))]
[JsonSerializable(typeof(StructureCompareFormat))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(int?))]
public sealed partial class StructuresJsonContext : JsonSerializerContext;

/// <summary>
///     Source-generated metadata for the internal copies of the fixed scripts' results. They never reach the wire, and
///     the public context's generated members would expose them, so they have their own internal context.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(StructureLuaDefinition))]
[JsonSerializable(typeof(StructureLuaElementRef))]
[JsonSerializable(typeof(StructureLuaBatch))]
[JsonSerializable(typeof(StructureLuaPdbLayout))]
[JsonSerializable(typeof(StructureLuaValues))]
[JsonSerializable(typeof(StructureLuaWritten))]
internal sealed partial class StructureLuaJsonContext : JsonSerializerContext;
