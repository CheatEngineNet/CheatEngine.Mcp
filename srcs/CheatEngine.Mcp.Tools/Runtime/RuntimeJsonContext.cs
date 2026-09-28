using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Runtime;

/// <summary>Source-generated JSON metadata for the <c>runtime_*</c> contract.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RuntimeInfoResult))]
[JsonSerializable(typeof(RuntimeGates))]
[JsonSerializable(typeof(RuntimeOverviewResult))]
[JsonSerializable(typeof(RuntimeResourceList))]
[JsonSerializable(typeof(RuntimeReleaseResourcesResult))]
[JsonSerializable(typeof(RuntimeJobList))]
[JsonSerializable(typeof(JobStopResult))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(TargetResourceDescriptor[]))]
[JsonSerializable(typeof(ReleaseAllResult))]
public sealed partial class RuntimeJsonContext : JsonSerializerContext;
