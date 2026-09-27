using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Core.Features;

/// <summary>Source-generated metadata for the results of the fixed scripts that feature checks run.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(MonoAutoAttachState))]
internal sealed partial class FeaturesJsonContext : JsonSerializerContext;
