using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.DotNet;

/// <summary>Source-generated JSON metadata for the <c>dotnet_*</c> tools and their bounded Lua results.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DotNetStatus))]
[JsonSerializable(typeof(DotNetDomainPage))]
[JsonSerializable(typeof(DotNetModulePage))]
[JsonSerializable(typeof(DotNetTypePage))]
[JsonSerializable(typeof(DotNetTypeDetails))]
[JsonSerializable(typeof(DotNetMethodPage))]
[JsonSerializable(typeof(DotNetObject))]
[JsonSerializable(typeof(DotNetInstanceSearch))]
[JsonSerializable(typeof(DotNetInstanceSearchPage))]
[JsonSerializable(typeof(DotNetInstanceBatch))]
[JsonSerializable(typeof(JobPoll<DotNetInstance>))]
[JsonSerializable(typeof(string[]))]
public sealed partial class DotNetJsonContext : JsonSerializerContext;
