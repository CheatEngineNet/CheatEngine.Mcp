using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Processes;

/// <summary>Source-generated JSON metadata for the <c>process_*</c> arguments and results.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProcessListResult))]
[JsonSerializable(typeof(ProcessAttachResult))]
[JsonSerializable(typeof(ProcessCurrentResult))]
[JsonSerializable(typeof(ProcessCreateResult))]
[JsonSerializable(typeof(ProcessOpenFileResult))]
[JsonSerializable(typeof(ProcessSaveFileResult))]
[JsonSerializable(typeof(ProcessSaveFilePublishFailure))]
[JsonSerializable(typeof(ProcessPausedResult))]
[JsonSerializable(typeof(ReleasedResource))]
[JsonSerializable(typeof(ProcessThreadListResult))]
[JsonSerializable(typeof(ProcessPointerSizeResult))]
[JsonSerializable(typeof(string[]))]
public sealed partial class ProcessJsonContext : JsonSerializerContext;
