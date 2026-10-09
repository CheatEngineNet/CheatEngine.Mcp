using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools.Exec;

/// <summary>Source-generated JSON metadata for <c>exec_compile_csharp</c>.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ExecCSharpCompilerOutput))]
[JsonSerializable(typeof(ExecCSharpCompilerDiagnostic))]
[JsonSerializable(typeof(ExecCSharpCompileResult))]
[JsonSerializable(typeof(ExecCSharpExportFailure))]
internal sealed partial class ExecCSharpJsonContext : JsonSerializerContext;
