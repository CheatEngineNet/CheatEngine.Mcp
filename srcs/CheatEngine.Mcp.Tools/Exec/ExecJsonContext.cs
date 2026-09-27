using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Exec;

/// <summary>Source-generated JSON metadata for the <c>exec_*</c> tool inputs and results.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ExecCallArgument[]))]
[JsonSerializable(typeof(ExecCallingConvention))]
[JsonSerializable(typeof(ExecArgumentType))]
[JsonSerializable(typeof(ExecLibraryInjection))]
[JsonSerializable(typeof(ExecDotNetInjection))]
[JsonSerializable(typeof(ExecCallResult))]
[JsonSerializable(typeof(ExecCompiledSymbol[]))]
[JsonSerializable(typeof(ExecCompileResult))]
public sealed partial class ExecJsonContext : JsonSerializerContext;
