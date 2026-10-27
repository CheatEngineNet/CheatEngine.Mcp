using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Mcp.Tools.Record;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RecordClearResult))]
[JsonSerializable(typeof(RecordPage))]
[JsonSerializable(typeof(RecordGetResult))]
[JsonSerializable(typeof(RecordFindResult))]
[JsonSerializable(typeof(RecordSelection))]
[JsonSerializable(typeof(RecordCreateResult))]
[JsonSerializable(typeof(RecordUpdateResult))]
[JsonSerializable(typeof(RecordActivationResult))]
[JsonSerializable(typeof(RecordDeleteResult))]
[JsonSerializable(typeof(RecordMoveResult))]
[JsonSerializable(typeof(RecordGroupResult))]
[JsonSerializable(typeof(RecordScriptResult))]
[JsonSerializable(typeof(RecordDropdownResult))]
[JsonSerializable(typeof(RecordDropdown))]
[JsonSerializable(typeof(RecordCreateRollback))]
[JsonSerializable(typeof(RecordCreateSpec[]))]
[JsonSerializable(typeof(RecordUpdateSpec[]))]
[JsonSerializable(typeof(RecordDropdownItem[]))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(VariableType))]
[JsonSerializable(typeof(RecordActivationFailureReason))]
[JsonSerializable(typeof(RecordLuaChanged))]
public sealed partial class RecordJsonContext : JsonSerializerContext;

/// <summary>Source-generated metadata for fixed Lua results that remain inside the record tools.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(RecordLuaChildren))]
[JsonSerializable(typeof(RecordLuaDropdowns))]
[JsonSerializable(typeof(RecordLuaOffsets))]
[JsonSerializable(typeof(RecordLuaReaches))]
[JsonSerializable(typeof(RecordLuaActivation))]
internal sealed partial class RecordLuaJsonContext : JsonSerializerContext;
