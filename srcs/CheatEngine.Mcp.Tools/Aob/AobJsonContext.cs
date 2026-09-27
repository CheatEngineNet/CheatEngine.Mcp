using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Tools.Memory;

namespace CheatEngine.Mcp.Tools.Aob;

/// <summary>
///     Source-generated metadata for the <c>aob_*</c> arguments, results and fixed Lua results, so the domain never needs
///     reflection-based JSON.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AobFindResult))]
[JsonSerializable(typeof(AobSignature))]
[JsonSerializable(typeof(ProtectionRequirement))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(UniqueAobProbe))]
public sealed partial class AobJsonContext : JsonSerializerContext;
