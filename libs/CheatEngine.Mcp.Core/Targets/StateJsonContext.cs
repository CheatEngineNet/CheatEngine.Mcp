using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>
///     Source-generated metadata for the results of the fixed state and job scripts and for the details that target
///     resources and jobs attach to their errors; no reflection.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LuaStateSnapshot))]
[JsonSerializable(typeof(LuaStateRelease))]
[JsonSerializable(typeof(LuaStateAcknowledgement))]
[JsonSerializable(typeof(LuaResourceRelease))]
[JsonSerializable(typeof(LuaJobPage))]
[JsonSerializable(typeof(LuaJobStatuses))]
[JsonSerializable(typeof(LuaJobStop))]
[JsonSerializable(typeof(LuaStateSweep))]
[JsonSerializable(typeof(ReleaseAllResult))]
[JsonSerializable(typeof(ReleasedResource))]
[JsonSerializable(typeof(TargetTransitionRefusal))]
[JsonSerializable(typeof(TargetTransitionRefusals))]
[JsonSerializable(typeof(JobStatus))]
internal sealed partial class StateJsonContext : JsonSerializerContext;
