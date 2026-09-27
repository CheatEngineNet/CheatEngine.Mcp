using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Client.Tables;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Mcp.Tools.Record;

/// <summary>A copied, handle-free address-list record.</summary>
public sealed record RecordEntry(
	[property: Description("The Cheat Engine record identifier, valid until a table load.")]
	int Id,
	[property: Description("The record's current zero-based address-list position.")]
	int Index,
	[property: Description("The display description stored in the table.")]
	string Description,
	[property: Description("The unresolved Cheat Engine address expression.")]
	string Address,
	[property: Description("The record's stored value text.")]
	string Value,
	[property: Description("The Cheat Engine type of the record.")]
	VariableType VariableType,
	[property: Description("The stored Auto Assembler text, when Cheat Engine copied it.")]
	string? Script,
	[property: Description("The number of pointer offsets.")]
	int OffsetCount,
	[property: Description("The current resolved target address, when Cheat Engine could resolve it.")]
	string? CurrentAddress,
	[property: Description("Whether the record is active or frozen.")]
	bool Active,
	[property: Description("The number of immediate child records.")]
	int ChildCount,
	[property: Description("Whether Cheat Engine is still processing an asynchronous activation.")]
	bool AsyncProcessing);

/// <summary>A bounded page of top-level address-list records.</summary>
public sealed record RecordPage(
	[property: Description("The number of top-level records at the instant Cheat Engine counted them.")]
	int Total,
	[property: Description("The copied top-level records in address-list order.")]
	RecordEntry[] Records,
	[property: Description("The offset for the next page, or null when this page reaches the count.")]
	int? NextOffset);

/// <summary>The records copied by one identifier lookup.</summary>
public sealed record RecordGetResult(
	[property: Description("The copied records in the same order as the requested ids.")]
	RecordEntry[] Records);

/// <summary>The records found by a bounded address-list search.</summary>
public sealed record RecordFindResult(
	[property: Description("The number of matching top-level records before this result was limited.")]
	int Total,
	[property: Description("Whether more matching records exist beyond records.")]
	bool Truncated,
	[property: Description("The matching records in address-list order.")]
	RecordEntry[] Records);

/// <summary>The current address-list selection.</summary>
public sealed record RecordSelection(
	[property: Description("Whether Cheat Engine currently selects an address-list record.")]
	bool Selected,
	[property: Description("The selected record when selected is true.")]
	RecordEntry? Record);

/// <summary>One record to create.</summary>
public sealed record RecordCreateSpec(
	[property: Description("The display description; empty is allowed, up to 1024 characters.")]
	string Description,
	[property: Description("The Cheat Engine address expression; use 0 for group and script records.")]
	string Address,
	[property: Description("The initial value text, up to 65536 characters.")]
	string Value,
	[property: Description("The Cheat Engine value type.")]
	VariableType VariableType = VariableType.Dword,
	[property: Description("The existing parent group id; omit for a top-level record.")]
	int? ParentId = null,
	[property: Description("The Auto Assembler text for an AutoAssembler record, up to 1048576 characters.")]
	string? Script = null);

/// <summary>The records a create operation added.</summary>
public sealed record RecordCreateResult(
	[property: Description("The created records in the order requested.")]
	RecordEntry[] Records);

/// <summary>One partial update of a record.</summary>
public sealed record RecordUpdateSpec(
	[property: Description("The record id to update.")]
	int Id,
	[property: Description("The replacement display description; omit to keep it.")]
	string? Description = null,
	[property: Description("The replacement Cheat Engine address expression; omit to keep it.")]
	string? Address = null,
	[property: Description("The replacement value text; omit to keep it.")]
	string? Value = null,
	[property: Description("The replacement Cheat Engine value type; omit to keep it.")]
	VariableType? VariableType = null,
	[property: Description("Pointer offsets in dereference order, nearest the base first; omit to keep them.")]
	long[]? Offsets = null);

/// <summary>The records a batch update changed.</summary>
public sealed record RecordUpdateResult(
	[property: Description("The updated records in the order requested.")]
	RecordEntry[] Records);

/// <summary>The activation state of one record after a batch request.</summary>
public sealed record RecordActivation(
	[property: Description("The record id.")]
	int Id,
	[property: Description("The state requested by the caller.")]
	bool RequestedActive,
	[property: Description("The current state copied after the request.")]
	bool Active,
	[property: Description("Whether the final state is still processing asynchronously.")]
	bool Pending);

/// <summary>The records whose activation state was changed.</summary>
public sealed record RecordActivationResult(
	[property: Description("One state result per requested record, in order.")]
	RecordActivation[] Records);

/// <summary>The records deleted by a batch delete.</summary>
public sealed record RecordDeleteResult(
	[property: Description("The number of records removed.")]
	int Deleted);

/// <summary>A record after its parent changed.</summary>
public sealed record RecordMoveResult(
	[property: Description("The copied record after moving it.")]
	RecordEntry Record);

/// <summary>A newly created group and its member records after grouping.</summary>
public sealed record RecordGroupResult(
	[property: Description("The newly created group record.")]
	RecordEntry Group,
	[property: Description("The records moved under the group, in the order requested.")]
	RecordEntry[] Records);

/// <summary>A record after its Auto Assembler text changed.</summary>
public sealed record RecordScriptResult(
	[property: Description("The record copied after Cheat Engine stored the script.")]
	RecordEntry Record);

/// <summary>The retained records when a create-batch rollback could not fully restore the table.</summary>
public sealed record RecordCreateRollback(
	[property: Description("The number of records created before the failed step.")]
	int Created,
	[property: Description("The number of created records removed during rollback.")]
	int RolledBack);

/// <summary>The fixed Lua response used after changing an Auto Assembler script or pointer offsets.</summary>
public sealed record RecordLuaChanged(int Id);

/// <summary>Shared validation and copied-record mapping for the record domain.</summary>
internal static class RecordArguments
{
	internal const int MaximumBatch = 256;
	internal const int MaximumListLimit = 1000;
	internal const int MaximumScannedRecords = 4096;
	internal const int MaximumDescriptionLength = 1024;
	internal const int MaximumAddressLength = 1024;
	internal const int MaximumValueLength = 65536;
	internal const int MaximumScriptLength = 1_048_576;
	internal const int MaximumOffsets = 128;

	internal static RecordEntry Entry(MemoryRecordSnapshot record)
	{
		return new RecordEntry(record.Id.Value, record.Index, record.Content.Description,
			record.Content.AddressExpression, record.Content.Value, record.Content.VariableType, record.Content.Script,
			record.Content.OffsetCount,
			record.State.CurrentAddress is { } address ? HexFormat.Address(address) : null,
			record.State.IsActive, record.State.ChildCount, record.State.IsAsyncProcessing);
	}

	internal static void Id(int id, string parameter)
	{
		if (id < 0)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must be a non-negative record id.");
		}
	}

	internal static void Batch<T>(T[]? items, string parameter)
	{
		if (items is not { Length: > 0 })
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must contain at least one item.");
		}

		if (items.Length > MaximumBatch)
		{
			throw CheatEngineToolException.LimitExceeded(parameter, $"accepts at most {MaximumBatch} items.");
		}
	}

	internal static void Text(string? value, string parameter, int maximum, bool required = false)
	{
		if (value is null || (required && string.IsNullOrWhiteSpace(value)) || value.Length > maximum ||
			value.Any(char.IsControl))
		{
			string requirement = required ? "must be non-empty" : "must be supplied";
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"{requirement}, at most {maximum} characters, and contain no control characters.");
		}
	}

	internal static void Type(VariableType value, string parameter)
	{
		if (!Enum.IsDefined(value))
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must be a defined Cheat Engine value type.");
		}
	}
}

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
[JsonSerializable(typeof(RecordCreateRollback))]
[JsonSerializable(typeof(RecordCreateSpec[]))]
[JsonSerializable(typeof(RecordUpdateSpec[]))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(long[]))]
[JsonSerializable(typeof(VariableType))]
[JsonSerializable(typeof(RecordLuaChanged))]
public sealed partial class RecordJsonContext : JsonSerializerContext;
