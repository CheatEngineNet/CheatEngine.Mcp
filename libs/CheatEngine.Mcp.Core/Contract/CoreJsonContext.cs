using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Core.Contract;

/// <summary>
///     Source-generated metadata for the Core contract types. It is always first in the composed serializer options
///     (<see cref="Composition.CheatEngineMcpJson" />), so Core types never need reflection.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ToolErrorEnvelope))]
[JsonSerializable(typeof(ToolError))]
[JsonSerializable(typeof(ToolErrorKind))]
[JsonSerializable(typeof(ToolHostEffect))]
[JsonSerializable(typeof(ToolParameterDetails))]
[JsonSerializable(typeof(McpValueType))]
[JsonSerializable(typeof(ResultFormat))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
