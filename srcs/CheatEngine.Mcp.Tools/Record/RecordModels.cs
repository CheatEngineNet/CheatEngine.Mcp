using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using CheatEngine.Client.Tables;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Mcp.Tools.Record;

/// <summary>A copied, handle-free address-list record.</summary>
public sealed record RecordEntry(
	[property: Description("The Cheat Engine record identifier, valid until a table load.")]
	int Id,
	[property: Description(
		"The record's current zero-based position in the whole address list, where each record is followed by the " +
		"records nested below it.")]
	int Index,
	[property: Description("The display description stored in the table.")]
	string Description,
	[property: Description("The unresolved Cheat Engine address expression.")]
	string Address,
	[property: Description(
		"The record's value text as Cheat Engine shows it. A byte array record shows hexadecimal byte pairs such " +
		"as 48 8B 05 once these tools created it or wrote its value; one that a loaded table set to decimal display " +
		"shows decimal numbers until then.")]
	string Value,
	[property: Description("The value type Cheat Engine stores, as an integer: " + RecordArguments.StoredVariableTypes +
		".")]
	VariableType VariableType,
	[property: Description("The stored Auto Assembler text, when Cheat Engine copied it.")]
	string? Script,
	[property: Description("The number of pointer offsets; 0 for a plain address.")]
	int OffsetCount,
	[property: Description(
		"The pointer offsets in dereference order, nearest the base first, as signed hexadecimal such as 10, -8 or " +
		"4C8, the form that record_create, record_update and pointer_read_chain take; omitted when offsetCount is 0. " +
		"An offset that Cheat Engine keeps as a symbol or Lua expression, which only a loaded table or Cheat Engine " +
		"itself sets, is copied as that text without being evaluated. The list is shorter than offsetCount only when " +
		"the copy bound of one call, 65536 offsets and 1048576 bytes of offset text, cut it.")]
	string[]? Offsets,
	[property: Description("The current resolved target address, when Cheat Engine could resolve it.")]
	string? CurrentAddress,
	[property: Description("Whether the record is active or frozen.")]
	bool Active,
	[property: Description("The number of immediate child records.")]
	int ChildCount,
	[property: Description("Whether Cheat Engine is still processing an asynchronous activation.")]
	bool AsyncProcessing,
	[property: Description(
		"The record's dropdown list and options; copied only by record_get with includeDropdown true and by " +
		"record_set_dropdown, and omitted by every other tool.")]
	RecordDropdown? Dropdown = null);

/// <summary>
///     One choice of a record's dropdown list, which Cheat Engine stores as a <c>value:description</c> line.
/// </summary>
public sealed record RecordDropdownItem(
	[property: Description(
		"The value text Cheat Engine writes when the item is chosen and compares with the record's displayed value, " +
		"ignoring the case of ASCII letters; it never contains a colon, and it is empty for a line without one. As " +
		"input: 0 to 1024 characters without a colon or control characters; a non-empty value must differ from the " +
		"other values in the list even when the case of ASCII letters is ignored.")]
	string Value,
	[property: Description(
		"The text shown for the value; empty is allowed. As input: up to 1024 characters without control characters.")]
	string Description = "");

/// <summary>The dropdown list and options that Cheat Engine uses for one record.</summary>
public sealed record RecordDropdown(
	[property: Description(
		"The copied items in list order: at most 1024 per record, and at most 4096 items and 1048576 bytes of " +
		"value and description text per record_get call, in request order.")]
	RecordDropdownItem[] Items,
	[property: Description("The number of items in the list this record uses, including items that were not copied.")]
	int ItemCount,
	[property: Description(
		"Whether items stops before itemCount because of a copy bound; read fewer records to copy more.")]
	bool Truncated,
	[property: Description(
		"Cheat Engine's DropDownReadOnly option, shown as 'Disallow manual user input': the user can only choose a " +
		"listed value.")]
	bool DisallowManualInput,
	[property: Description("Cheat Engine's DropDownDescriptionOnly option: the list shows only the descriptions.")]
	bool DescriptionOnly,
	[property: Description(
		"Cheat Engine's DisplayAsDropDownListItem option: the record displays its value as the matching list item.")]
	bool DisplayAsListItem,
	[property: Description(
		"The description of the record whose list and options this record uses instead of its own; omitted when " +
		"the list is not linked. Items and options are empty and false while Cheat Engine finds no such record.")]
	string? LinkedTo = null);

/// <summary>
///     A bounded page of the whole address list, nested records included, or of one record's immediate children.
/// </summary>
public sealed record RecordPage(
	[property: Description(
		"The number of records in the whole address list, nested records included, or of the parent's immediate " +
		"children, when Cheat Engine counted them.")]
	int Total,
	[property: Description(
		"The copied records in address-list order, where each record is followed by the records nested below it, or " +
		"the parent's immediate children in their order.")]
	RecordEntry[] Records,
	[property: Description("The offset of the next page; omitted when this page reaches total.")]
	int? NextOffset);

/// <summary>The records copied by one identifier lookup.</summary>
public sealed record RecordGetResult(
	[property: Description("The copied records in the same order as the requested ids.")]
	RecordEntry[] Records);

/// <summary>The records found by a bounded search of the whole address list.</summary>
public sealed record RecordFindResult(
	[property: Description(
		"The number of matching records, nested records included, before this result was limited.")]
	int Total,
	[property: Description("Whether more matching records exist beyond records.")]
	bool Truncated,
	[property: Description(
		"The matching records in address-list order, where each record is followed by the records nested below it.")]
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
	[property: Description(RecordArguments.CreateValueText)]
	string Value = "",
	[property: Description("The Cheat Engine value type as an integer, 2 by default: " + RecordArguments.VariableTypes +
		".")]
	VariableType VariableType = VariableType.Dword,
	[property: Description("The existing parent group id; omit for a top-level record.")]
	int? ParentId = null,
	[property: Description(
		"The Auto Assembler text for an AutoAssembler record, up to 1048576 characters and 1048576 UTF-8 bytes.")]
	string? Script = null,
	[property: Description(
		"Optional pointer offsets that make address the base of a pointer chain, in dereference order, nearest the " +
		"base first: " + RecordArguments.OffsetBounds + ". Not allowed on an Auto Assembler record.")]
	string[]? Offsets = null,
	[property: Description(RecordArguments.LengthText + " Omit it to let a written value set the length.")]
	int? Length = null,
	[property: Description(
		"For a string record (6): true for UTF-16 text, false or omitted for single-byte text. Other types refuse " +
		"it.")]
	bool? Unicode = null);

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
	[property: Description(RecordArguments.UpdateValueText)]
	string? Value = null,
	[property: Description("The replacement Cheat Engine value type as an integer (" + RecordArguments.VariableTypes +
		"); omit to keep it.")]
	VariableType? VariableType = null,
	[property: Description(
		"Replacement pointer offsets in dereference order, nearest the base first: " + RecordArguments.OffsetBounds +
		". [] removes the chain. Omit to keep them, except that Cheat Engine clears them when address changes.")]
	string[]? Offsets = null,
	[property: Description(RecordArguments.LengthText + " Omit it to keep the current length.")]
	int? Length = null,
	[property: Description(
		"For a string record (6): true for UTF-16 text, false for single-byte text; omit it to keep the setting. " +
		"Other types refuse it.")]
	bool? Unicode = null);

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
	bool Pending,
	[property: Description(
		"Why Cheat Engine left the record in the other state; present only when active differs from " +
		"requestedActive and pending is false.")]
	RecordActivationFailure? Failure = null);

/// <summary>Why Cheat Engine refused to change the state of one record.</summary>
public sealed record RecordActivationFailure(
	[property: Description(
		"Cheat Engine's reason for a failed activation: unknown, inaccessible (a frozen value that Cheat Engine " +
		"could not write), auto_assembler_error, allocation_failed, syntax_error, lua_syntax_error, " +
		"structure_definition_error, assert_failed, aob_module_not_found, aob_not_found, include_not_found or " +
		"dll_injection_failed; not_reported when Cheat Engine refused without a reason, as it does for every " +
		"refused deactivation and for an activation that an OnActivate handler stopped, or when the record's " +
		"OnActivationFailure handler is neither missing nor a Lua function, so the tool did not read the reason.")]
	RecordActivationFailureReason Reason,
	[property: Description(
		"Cheat Engine's own description of the failure, such as the Auto Assembler error message, cut at 4096 " +
		"bytes; omitted when Cheat Engine gave none.")]
	string? Text = null);

/// <summary>
///     The reason of a refused state change. The members up to <see cref="DllInjectionFailed" /> are Cheat Engine 7.7's
///     <c>TFailReason</c> values in their order, which its <c>OnActivationFailure</c> event passes as integers 0 to 11;
///     the wire value is the <c>snake_case</c> member name.
/// </summary>
/// <remarks>
///     Cheat Engine's <c>defines.lua</c> numbers <c>afInaccessible</c> from 0, but the 7.7 executable's enumeration
///     starts with <c>afUnknown</c>, so every event value is one above the matching <c>defines.lua</c> constant.
/// </remarks>
[JsonConverter(typeof(ContractEnumConverter<RecordActivationFailureReason>))]
public enum RecordActivationFailureReason
{
	/// <summary><c>afUnknown</c>: the script failed without an Auto Assembler error, or with another error.</summary>
	Unknown,

	/// <summary><c>afInaccessible</c>: the frozen value of a value record could not be written.</summary>
	Inaccessible,

	/// <summary><c>afGenericAutoAssembler</c>: an Auto Assembler error without a more specific class.</summary>
	AutoAssemblerError,

	/// <summary><c>afAllocateFailure</c>: an alloc or globalalloc failed.</summary>
	AllocationFailed,

	/// <summary><c>afSyntaxError</c>: the Auto Assembler text did not assemble.</summary>
	SyntaxError,

	/// <summary><c>afSyntaxErrorInLua</c>: a {$lua} block did not compile or run.</summary>
	LuaSyntaxError,

	/// <summary><c>afStructureDefinitionError</c>: a struct definition failed.</summary>
	StructureDefinitionError,

	/// <summary><c>afAssertFailure</c>: an assert found other bytes than expected.</summary>
	AssertFailed,

	/// <summary><c>afAOBMobuleNotFound</c>: the module of an aobscanmodule was not found.</summary>
	AobModuleNotFound,

	/// <summary><c>afAOBNotFound</c>: an AOB scan found no match.</summary>
	AobNotFound,

	/// <summary><c>afIncludeNotFound</c>: an {$include} file was not found.</summary>
	IncludeNotFound,

	/// <summary><c>afDLLInjectionFailure</c>: a loadlibrary injection failed.</summary>
	DllInjectionFailed,

	/// <summary>Cheat Engine refused the change without reporting a reason.</summary>
	NotReported
}

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

/// <summary>A record after its dropdown list and options changed.</summary>
public sealed record RecordDropdownResult(
	[property: Description("The record copied after Cheat Engine stored the dropdown, with its dropdown.")]
	RecordEntry Record);

/// <summary>The retained records when a create-batch rollback could not fully restore the table.</summary>
public sealed record RecordCreateRollback(
	[property: Description("The number of records created before the failed step.")]
	int Created,
	[property: Description("The number of created records removed during rollback.")]
	int RolledBack);

/// <summary>
///     The fixed Lua response used after creating a record or changing an Auto Assembler script, a string or byte array
///     layout, pointer offsets or a dropdown.
/// </summary>
public sealed record RecordLuaChanged(int Id);

/// <summary>The fixed Lua copy of one page of a record's immediate children.</summary>
/// <param name="Total">The parent's current number of immediate children.</param>
/// <param name="Ids">The ids of the children on the page, in address-list order.</param>
internal sealed record RecordLuaChildren(int Total, int[] Ids);

/// <summary>The fixed Lua copy of the dropdowns of the requested records, in request order.</summary>
/// <param name="Records">One entry per requested record.</param>
internal sealed record RecordLuaDropdowns(RecordLuaDropdown[] Records);

/// <summary>The dropdown of one record as the fixed Lua copy reports it.</summary>
/// <param name="Id">The record id Cheat Engine returned for the requested id.</param>
/// <param name="Dropdown">The bounded dropdown copy.</param>
internal sealed record RecordLuaDropdown(int Id, RecordDropdown Dropdown);

/// <summary>The fixed Lua copy of the pointer offsets of the requested records, in request order.</summary>
/// <param name="Records">One entry per requested record.</param>
internal sealed record RecordLuaOffsets(RecordLuaOffsetList[] Records);

/// <summary>The stored offset texts of one record, in dereference order.</summary>
/// <param name="Id">The record id Cheat Engine returned for the requested id.</param>
/// <param name="Offsets">The stored offset texts, nearest the base first, cut by the copy bounds.</param>
internal sealed record RecordLuaOffsetList(int Id, string[] Offsets);

/// <summary>Whether a change of each requested record would reach a nested record of one of the given types.</summary>
/// <param name="Reaches">One flag per requested record, in request order.</param>
internal sealed record RecordLuaReaches(bool[] Reaches);

/// <summary>The state of one record after the fixed Lua state change.</summary>
/// <param name="Active">The record's state after the change.</param>
/// <param name="Pending">Whether an asynchronous activation is still running.</param>
/// <param name="Reason">The last reason Cheat Engine passed to <c>OnActivationFailure</c>, if it called it.</param>
/// <param name="Text">The last reason text Cheat Engine passed with it.</param>
internal sealed record RecordLuaActivation(bool Active, bool Pending, int? Reason = null, string? Text = null);

/// <summary>Shared validation and copied-record mapping for the record domain.</summary>
internal static partial class RecordArguments
{
	internal const int MaximumBatch = 256;
	internal const int MaximumListLimit = 1000;
	internal const int MaximumScannedRecords = 4096;
	internal const int MaximumDescriptionLength = 1024;
	internal const int MaximumAddressLength = 1024;
	internal const int MaximumValueLength = 65536;
	internal const int MaximumScriptLength = 1_048_576;

	/// <summary>
	///     The UTF-8 size bound of a script, the largest string a fixed Lua body receives as an argument; checked
	///     before any host change instead of failing when the Lua source is built.
	/// </summary>
	internal const int MaximumScriptBytes = 1_048_576;

	internal const int MaximumOffsets = 128;
	internal const int MaximumHierarchyDepth = 64;

	/// <summary>The largest string length in characters, or byte array length in bytes, a record is given.</summary>
	internal const int MaximumLength = 4096;

	/// <summary>
	///     The value type integers a record accepts as input, for parameter descriptions. Cheat Engine keeps 7, 10 and
	///     12 unchanged on a record, but its value code has no case for them, so they are refused.
	/// </summary>
	internal const string VariableTypes =
		"0 byte, 1 2-byte integer, 2 4-byte integer, 3 8-byte integer, 4 float, 5 double, 6 string, 8 byte array, " +
		"9 binary, 11 Auto Assembler script, 13 custom, 14 group header; 7 (UTF-16 string), 10 (all types) and 12 " +
		"(pointer-sized value) are refused because Cheat Engine can neither read nor write the value of a record " +
		"with one of them; use 6 with unicode true for UTF-16 text";

	/// <summary>
	///     The value type integers a record can carry, for output and search descriptions: Cheat Engine stores the type
	///     it is given, including 7, 10 and 12 when a Lua script or a loaded table gives one.
	/// </summary>
	internal const string StoredVariableTypes =
		"0 byte, 1 2-byte integer, 2 4-byte integer, 3 8-byte integer, 4 float, 5 double, 6 string, " +
		"7 UTF-16 string, 8 byte array, 9 binary, 10 all types, 11 Auto Assembler script, 12 pointer-sized " +
		"hexadecimal value (not a pointer chain), 13 custom, 14 group header; only a record created outside these " +
		"tools, such as by a Lua script or a loaded table, has 7, 10 or 12, and Cheat Engine reads its value as " +
		"empty text";

	/// <summary>The form and bounds of a pointer-offset list, for parameter descriptions.</summary>
	internal const string OffsetBounds =
		"up to 128 signed hexadecimal offsets from -80000000 to 7FFFFFFF such as \"10\", \"-8\" or \"4C8\", with an " +
		"optional + sign or 0x prefix, the form pointer_list_paths and pointer_read_chain use";

	/// <summary>The length option shared by the create and update descriptions.</summary>
	internal const string LengthText =
		"For a string record (6) its length in characters, for a byte array record (8) its number of bytes, from 1 " +
		"to 4096; other types refuse it. A longer written value lengthens the record: Cheat Engine lengthens a " +
		"string record, and the tool sets a byte array record to the number of bytes in the value.";

	/// <summary>The value rules of record_create.</summary>
	internal const string CreateValueText =
		"The value text written once the record is complete, at its final address after parentId and offsets apply, " +
		"up to 65536 characters. Empty, the default, writes nothing and leaves memory unchanged for every type; a " +
		"string or byte array record then needs length. Types 0 to 5 and 13 take a number: decimal such as 100, -5 " +
		"or 1.5, optionally with an unsigned exponent such as 1.5e3, or hexadecimal such as 0x64; other text, " +
		"including a + or - after the first character, is refused because Cheat Engine would evaluate it as a Lua " +
		"expression. A string record (6) takes its text. A byte array record (8) takes hexadecimal byte pairs such " +
		"as 48 8B 05, where ?? leaves a byte unchanged, and refuses Cheat Engine's (description) notation; the " +
		"record is lengthened to the number of bytes in the value. When Cheat Engine refuses a value, the call " +
		"deletes the records it created.";

	/// <summary>The value rules of record_update.</summary>
	internal const string UpdateValueText =
		"The replacement value text, written last at the record's final address, up to 65536 characters; omit it to " +
		"keep the current value. The number and byte array rules of record_create apply to the record's type after " +
		"the update. The number rule also applies to a record of any type whose Cheat Engine option " +
		"moRecursiveSetValue passes the value on to a nested record of type 0 to 5 or 13. An empty value is accepted " +
		"only for a string record (6), where it writes an empty string. A byte array record (8) is switched to " +
		"hexadecimal display and lengthened to the number of bytes in the value before the value is written.";

	/// <summary>
	///     Cheat Engine's record option that makes SetValue write the same text to every child record, which passes it
	///     on to its own children when it has the option too.
	/// </summary>
	internal const string RecursiveSetValueOption = "moRecursiveSetValue";

	/// <summary>The value types whose SetValue evaluates bracketed or arithmetic text as a Lua expression.</summary>
	internal static readonly ImmutableArray<int> NumberTypes =
	[
		(int) VariableType.Byte, (int) VariableType.Word, (int) VariableType.Dword, (int) VariableType.Qword,
		(int) VariableType.Single, (int) VariableType.Double, (int) VariableType.Custom
	];

	/// <summary>Maps a copied record with its copied pointer offsets and, when one was copied, its dropdown.</summary>
	internal static RecordEntry Entry(MemoryRecordSnapshot record, string[]? offsets, RecordDropdown? dropdown)
	{
		return new RecordEntry(record.Id.Value, record.Index, record.Content.Description,
			record.Content.AddressExpression, record.Content.Value, record.Content.VariableType, record.Content.Script,
			record.Content.OffsetCount, offsets,
			record.State.CurrentAddress is { } address ? HexFormat.Address(address) : null,
			record.State.IsActive, record.State.ChildCount, record.State.IsAsyncProcessing, dropdown);
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

	/// <summary>
	///     Checks a value type a record is given: a defined type other than 7 (UTF-16 string), 10 (all types) and 12
	///     (pointer-sized value). Cheat Engine's setVarType keeps those unchanged on the record, but its byte-size,
	///     read and write code has no case for them, so the record's value could never be read or written.
	/// </summary>
	internal static void Type(VariableType value, string parameter)
	{
		SearchType(value, parameter);
		switch (value)
		{
			case VariableType.WideString:
				throw CheatEngineToolException.InvalidArgument(parameter,
					"7 (UTF-16 string) gives a record whose value Cheat Engine can neither read nor write.",
					"Use 6 with unicode true for a UTF-16 string record.");
			case VariableType.All:
				throw CheatEngineToolException.InvalidArgument(parameter,
					"10 (all types) is a scan-only value type whose value Cheat Engine can neither read nor write on " +
					"a record.",
					"Use the type of the value, such as 2 for a 4-byte integer.");
			case VariableType.Pointer:
				throw CheatEngineToolException.InvalidArgument(parameter,
					"12 (pointer-sized value) gives a record whose value Cheat Engine can neither read nor write.",
					"Use 3 on a 64-bit process or 2 otherwise; pass offsets for a pointer chain.");
		}
	}

	/// <summary>
	///     Checks a value type a search compares with the type records carry: any defined type, because a Lua script or
	///     a loaded table can give a record even a type these tools refuse.
	/// </summary>
	internal static void SearchType(VariableType value, string parameter)
	{
		if (!Enum.IsDefined(value))
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must be a defined Cheat Engine value type.");
		}
	}

	/// <summary>Checks the size of Auto Assembler text that a fixed Lua body stores on a record.</summary>
	internal static void Script(string script, string parameter)
	{
		if (script.Length > MaximumScriptLength)
		{
			throw CheatEngineToolException.LimitExceeded(parameter,
				$"must be at most {MaximumScriptLength} characters.");
		}

		if (Encoding.UTF8.GetByteCount(script) > MaximumScriptBytes)
		{
			throw CheatEngineToolException.LimitExceeded(parameter,
				$"must be at most {MaximumScriptBytes} bytes when encoded as UTF-8.");
		}
	}

	/// <summary>
	///     Parses a pointer-offset list in the pointer tools' signed hexadecimal form: Cheat Engine stores at most 128
	///     signed 32-bit offsets per record.
	/// </summary>
	/// <param name="offsets">The offsets in dereference order.</param>
	/// <param name="parameter">The parameter name for errors; items are reported as <c>parameter[i]</c>.</param>
	/// <returns>The offsets in the same order.</returns>
	internal static int[] Offsets(string[] offsets, string parameter)
	{
		long[] parsed = HexParse.Offsets(offsets, parameter, MaximumOffsets);
		int[] values = new int[parsed.Length];
		for (int index = 0; index < parsed.Length; index++)
		{
			if (parsed[index] is < int.MinValue or > int.MaxValue)
			{
				throw CheatEngineToolException.InvalidArgument($"{parameter}[{index}]",
					"must be a signed 32-bit offset from -80000000 to 7FFFFFFF.",
					"Write a negative offset with a minus sign, such as -8 rather than FFFFFFF8.");
			}

			values[index] = (int) parsed[index];
		}

		return values;
	}

	/// <summary>
	///     Formats an offset text that Cheat Engine stores: a plain signed hexadecimal number, which is how Cheat
	///     Engine stores an offset given as a number, becomes the signed 32-bit value Cheat Engine applies, in the form
	///     <see cref="HexFormat.Offset" /> writes; an empty text is the offset 0; any other text, a symbol or Lua
	///     expression, is kept unchanged.
	/// </summary>
	/// <param name="text">The stored offset text.</param>
	/// <returns>The contract form of the offset.</returns>
	internal static string CopiedOffset(string? text)
	{
		ReadOnlySpan<char> span = (text ?? string.Empty).AsSpan().Trim();
		if (span.IsEmpty)
		{
			return "0";
		}

		bool negative = span[0] == '-';
		ReadOnlySpan<char> digits = span[0] is '+' or '-' ? span[1..] : span;
		if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			digits = digits[2..];
		}
		else if (digits.StartsWith('$'))
		{
			digits = digits[1..];
		}

		if (digits.IsEmpty || digits.Length > 16 || !IsHexDigits(digits))
		{
			return text!;
		}

		ulong magnitude = ulong.Parse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
		// Cheat Engine keeps an offset as a signed 32-bit integer and drops the higher bits of a longer number.
		int value = unchecked((int) (negative ? 0UL - magnitude : magnitude));
		return HexFormat.Offset(value);
	}

	/// <summary>
	///     Checks the length and unicode options against the type the record has once the request applies.
	/// </summary>
	/// <param name="type">The record's type after the request.</param>
	/// <param name="length">The requested length, if any.</param>
	/// <param name="unicode">The requested encoding, if any.</param>
	/// <param name="prefix">The parameter path of the record, such as <c>records[0]</c>.</param>
	internal static void Layout(VariableType type, int? length, bool? unicode, string prefix)
	{
		if (length is { } count)
		{
			if (type is not (VariableType.String or VariableType.ByteArray))
			{
				throw CheatEngineToolException.InvalidArgument($"{prefix}.length",
					"applies only to a string (6) or byte array (8) record.");
			}

			if (count is < 1 or > MaximumLength)
			{
				throw CheatEngineToolException.InvalidArgument($"{prefix}.length",
					$"must be from 1 to {MaximumLength}.");
			}
		}

		if (unicode is not null && type is not VariableType.String)
		{
			throw CheatEngineToolException.InvalidArgument($"{prefix}.unicode", "applies only to a string (6) record.");
		}
	}

	/// <summary>
	///     Whether a record needs the layout step: a string record given a length or encoding, and every byte array
	///     record that is created, retyped, resized or written, so that its value text is hexadecimal byte pairs.
	/// </summary>
	internal static bool NeedsLayout(VariableType type, int? length, bool? unicode, bool bytesChange)
	{
		return type switch
		{
			VariableType.String => length is not null || unicode is not null,
			VariableType.ByteArray => bytesChange || length is not null,
			_ => false
		};
	}

	/// <summary>Whether Cheat Engine's SetValue can evaluate a value of this type as a Lua expression.</summary>
	internal static bool IsNumberType(VariableType type)
	{
		return type is VariableType.Byte or VariableType.Word or VariableType.Dword or VariableType.Qword or
			VariableType.Single or VariableType.Double or VariableType.Custom;
	}

	/// <summary>
	///     Refuses a value that Cheat Engine would hand to Lua or that the tool cannot size. For types 0 to 5 and 13
	///     Cheat Engine evaluates text in brackets, or with a <c>+ - * /</c> after its first character, as a Lua
	///     expression that also embeds the record's current value text, so only a plain decimal or hexadecimal number
	///     without such a character is accepted there. For a byte array Cheat Engine replaces a whole
	///     <c>(description)</c> value by another record's value, whose byte count the tool cannot know beforehand.
	/// </summary>
	/// <param name="type">The record's type when the value is written.</param>
	/// <param name="value">The value text.</param>
	/// <param name="parameter">The parameter name for errors.</param>
	internal static void Value(VariableType type, string value, string parameter)
	{
		if (value.Length == 0)
		{
			return;
		}

		if (IsNumberType(type) && !NumberPattern().IsMatch(value))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"must be a number for value type {(int) type}: decimal such as 100, -5 or 1.5, optionally with an " +
				"unsigned exponent such as 1.5e3, or hexadecimal such as 0x64; Cheat Engine would evaluate other " +
				"text, including a + or - after the first character, as a Lua expression.");
		}

		ReadOnlySpan<char> trimmed = value.AsSpan().Trim(' ');
		if (type is VariableType.ByteArray && trimmed.Length > 2 && trimmed[0] == '(' && trimmed[^1] == ')')
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"cannot use Cheat Engine's (description) notation on a byte array record (8), because the tool sets " +
				"the record's byte count from the value text.",
				"Write the bytes as hexadecimal pairs such as 48 8B 05.");
		}
	}

	/// <summary>
	///     Refuses a value that a record passes on to a nested record of a number type through Cheat Engine's
	///     <c>moRecursiveSetValue</c> option: that nested record would evaluate anything but a plain number as a Lua
	///     expression, whatever the type of the record written.
	/// </summary>
	/// <param name="value">The value text.</param>
	/// <param name="parameter">The parameter name for errors.</param>
	internal static void RecursiveValue(string value, string parameter)
	{
		if (value.Length > 0 && !NumberPattern().IsMatch(value))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"must be a number such as 100, -5, 1.5 or 0x64, because the record's moRecursiveSetValue option " +
				"passes the value on to a nested record of type 0 to 5 or 13, and Cheat Engine would evaluate other " +
				"text there as a Lua expression.",
				"Update the nested records one at a time, each with a value its own type takes.");
		}
	}

	/// <summary>
	///     Counts the bytes Cheat Engine writes for a byte array value shown as hexadecimal: it splits the value at
	///     spaces, commas and dashes and reads every word as consecutive pairs of characters, one byte per pair and a
	///     last single character, where a pair that is not hexadecimal, such as <c>??</c>, is a byte left unchanged.
	///     Cheat Engine counts characters as UTF-8 bytes.
	/// </summary>
	/// <param name="value">The byte array value text.</param>
	/// <returns>The number of bytes the value covers.</returns>
	internal static int ByteCount(string value)
	{
		int count = 0;
		foreach (string word in value.Split([' ', ',', '-'], StringSplitOptions.RemoveEmptyEntries))
		{
			count += (Encoding.UTF8.GetByteCount(word) + 1) / 2;
		}

		return count;
	}

	private static bool IsHexDigits(ReadOnlySpan<char> text)
	{
		foreach (char character in text)
		{
			if (!char.IsAsciiHexDigit(character))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	///     A signed decimal or hexadecimal integer, or a signed decimal number with an optional unsigned exponent: no
	///     <c>+ - * /</c> after the first character, which would make Cheat Engine evaluate the text as Lua.
	/// </summary>
	[GeneratedRegex(
		@"^ *[+-]?(?:(?:0[xX]|\$)?[0-9A-Fa-f]+|(?:[0-9]+(?:[.,][0-9]*)?|[.,][0-9]+)(?:[eE][0-9]+)?) *$",
		RegexOptions.CultureInvariant)]
	private static partial Regex NumberPattern();
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
