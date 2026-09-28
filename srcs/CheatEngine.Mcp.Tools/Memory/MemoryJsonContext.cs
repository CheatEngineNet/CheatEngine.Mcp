using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>
///     Source-generated metadata for the <c>memory_*</c> arguments, results, error details and fixed Lua results, so the
///     domain never needs reflection-based JSON.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MemoryReadResult))]
[JsonSerializable(typeof(MemoryReadItem[]))]
[JsonSerializable(typeof(MemoryReadBatchResult))]
[JsonSerializable(typeof(MemoryWriteResult))]
[JsonSerializable(typeof(MemoryWriteItem[]))]
[JsonSerializable(typeof(MemoryWriteBatchResult))]
[JsonSerializable(typeof(MemoryWriteBatchFailure))]
[JsonSerializable(typeof(AddressInfoResult))]
[JsonSerializable(typeof(RegionList))]
[JsonSerializable(typeof(ProtectionChange))]
[JsonSerializable(typeof(AllocationInfo))]
[JsonSerializable(typeof(ReleaseResult))]
[JsonSerializable(typeof(MemoryCopyResult))]
[JsonSerializable(typeof(MemoryCompareResult))]
[JsonSerializable(typeof(MemoryHashResult))]
[JsonSerializable(typeof(FileDumpResult))]
[JsonSerializable(typeof(FileLoadResult))]
[JsonSerializable(typeof(FileLoadFailure))]
[JsonSerializable(typeof(MemorySnapshotInfo))]
[JsonSerializable(typeof(MemorySnapshotDiff))]
[JsonSerializable(typeof(MemorySnapshotList))]
[JsonSerializable(typeof(MemorySnapshotDeleted))]
[JsonSerializable(typeof(MemorySampleResult))]
[JsonSerializable(typeof(McpValueType))]
[JsonSerializable(typeof(FixedValueType))]
[JsonSerializable(typeof(MemoryByteOrder))]
[JsonSerializable(typeof(RegionStateFilter))]
[JsonSerializable(typeof(RegionTypeFilter))]
[JsonSerializable(typeof(ProtectionRequirement))]
[JsonSerializable(typeof(ResultFormat))]
[JsonSerializable(typeof(MemoryHashAlgorithm))]
[JsonSerializable(typeof(UnreadableMemory))]
[JsonSerializable(typeof(SnapshotChangeFilter))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(long?))]
[JsonSerializable(typeof(AddressExtras))]
[JsonSerializable(typeof(ProtectionProbe))]
public sealed partial class MemoryJsonContext : JsonSerializerContext;
