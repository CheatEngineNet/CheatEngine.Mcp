using System.ComponentModel;
using System.Text.Json.Serialization;

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
