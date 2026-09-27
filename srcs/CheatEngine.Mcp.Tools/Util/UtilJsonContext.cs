using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Util;

/// <summary>Source-generated metadata for the public <c>util_*</c> arguments and results.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(UtilConvertValueResult))]
[JsonSerializable(typeof(UtilCalculateResult))]
[JsonSerializable(typeof(McpValueType))]
[JsonSerializable(typeof(ValueByteOrder))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
public sealed partial class UtilJsonContext : JsonSerializerContext;
