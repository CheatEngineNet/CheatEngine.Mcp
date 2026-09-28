using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tests.Support;

/// <summary>Source-generated metadata for the job items and Lua results that the state and job tests read.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(JobProbeItem))]
internal sealed partial class StateTestJsonContext : JsonSerializerContext;

/// <summary>A structured job item.</summary>
/// <param name="Sequence">The producer's own counter.</param>
/// <param name="Text">A label.</param>
internal sealed record JobProbeItem(long Sequence, string Text);
