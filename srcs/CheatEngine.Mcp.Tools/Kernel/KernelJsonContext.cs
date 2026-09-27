using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Kernel;

/// <summary>Source-generated JSON metadata for the <c>kernel_*</c> tools and their fixed Lua results.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(KernelStatus))]
[JsonSerializable(typeof(KernelDbvmInitialization))]
[JsonSerializable(typeof(KernelAddressTranslation))]
[JsonSerializable(typeof(KernelPhysicalRead))]
[JsonSerializable(typeof(KernelPhysicalWrite))]
[JsonSerializable(typeof(KernelWatchAccess))]
[JsonSerializable(typeof(KernelWatchStart))]
[JsonSerializable(typeof(KernelWatchEvent))]
[JsonSerializable(typeof(KernelWatchEvent[]))]
[JsonSerializable(typeof(KernelWatchPoll))]
[JsonSerializable(typeof(JobStatus))]
[JsonSerializable(typeof(LuaKernelWatchArmed))]
public sealed partial class KernelJsonContext : JsonSerializerContext;
