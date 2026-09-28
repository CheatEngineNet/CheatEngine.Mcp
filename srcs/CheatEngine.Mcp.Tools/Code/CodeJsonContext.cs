using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Code;

/// <summary>Source-generated metadata for public <c>code_*</c> results and fixed Lua copies.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CodeDisassembly))]
[JsonSerializable(typeof(CodeDecodeResult))]
[JsonSerializable(typeof(CodeByteDisassembly))]
[JsonSerializable(typeof(CodeFunction))]
[JsonSerializable(typeof(CodeJobStart))]
[JsonSerializable(typeof(CodeJobPage))]
[JsonSerializable(typeof(CodeReferencePage))]
[JsonSerializable(typeof(CodeStringPage))]
[JsonSerializable(typeof(CodeFunctionPage))]
[JsonSerializable(typeof(CodeComments))]
[JsonSerializable(typeof(CodeComment))]
[JsonSerializable(typeof(CodeClearResult))]
[JsonSerializable(typeof(CodeJobEvent))]
[JsonSerializable(typeof(CodeFunctionGraph))]
[JsonSerializable(typeof(CodeBlockTerminator))]
[JsonSerializable(typeof(JobStatus))]
[JsonSerializable(typeof(JobState))]
[JsonSerializable(typeof(string[]))]
public sealed partial class CodeJsonContext : JsonSerializerContext;

/// <summary>Source-generated metadata for the fixed Lua results that remain internal to the code tools.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CodeLuaByteDisassembly))]
[JsonSerializable(typeof(CodeLuaFunction))]
[JsonSerializable(typeof(CodeLuaReferencePage))]
[JsonSerializable(typeof(CodeLuaStringPage))]
[JsonSerializable(typeof(CodeLuaFunctionPage))]
[JsonSerializable(typeof(CodeLuaComments))]
[JsonSerializable(typeof(CodeLuaCleared))]
[JsonSerializable(typeof(CodeLuaDissected))]
internal sealed partial class CodeLuaJsonContext : JsonSerializerContext;
