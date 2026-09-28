using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Mono;

/// <summary>Source-generated JSON metadata for the <c>mono_*</c> tools and fixed Lua results.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MonoStatus))]
[JsonSerializable(typeof(MonoAttachResult))]
[JsonSerializable(typeof(MonoDetachResult))]
[JsonSerializable(typeof(MonoAssemblyPage))]
[JsonSerializable(typeof(MonoClassPage))]
[JsonSerializable(typeof(MonoClass))]
[JsonSerializable(typeof(MonoFieldList))]
[JsonSerializable(typeof(MonoMethodPage))]
[JsonSerializable(typeof(MonoMethod))]
[JsonSerializable(typeof(MonoStaticFieldAddress))]
[JsonSerializable(typeof(MonoCompiledMethod))]
[JsonSerializable(typeof(MonoMethodInvocation))]
[JsonSerializable(typeof(MonoObject))]
[JsonSerializable(typeof(MonoInstanceSearch))]
[JsonSerializable(typeof(MonoInstanceSearchPage))]
[JsonSerializable(typeof(MonoInstanceBatch))]
[JsonSerializable(typeof(JobPoll<MonoInstance>))]
[JsonSerializable(typeof(MonoInvokeArgument[]))]
[JsonSerializable(typeof(string[]))]
public sealed partial class MonoJsonContext : JsonSerializerContext;
