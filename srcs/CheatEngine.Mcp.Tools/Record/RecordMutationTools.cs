using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Tables;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Record;

/// <summary>Changes address-list records through the typed Client table API and fixed bounded Lua where Client has no API.</summary>
[McpServerToolType]
public sealed class RecordMutationTools(ToolDispatch dispatch)
{
	/// <summary>The bytes of Cheat Engine's failure text that <c>record_set_active</c> copies per record.</summary>
	internal const int MaximumFailureTextBytes = 4096;

	/// <summary>
	///     How often a record's own <c>OnActivationFailure</c> handler runs during one activation: Cheat Engine
	///     repeats an activation while the handler returns true, which never ends for a handler that always does.
	/// </summary>
	internal const int MaximumFailureHandlerCalls = 8;

	/// <summary>
	///     The nested records one check examines for a change that Cheat Engine passes on to children; a larger
	///     subtree is treated as reaching a record of the checked type.
	/// </summary>
	internal const int MaximumReachedRecords = RecordArguments.MaximumScannedRecords;

	/// <summary>Cheat Engine's record option that makes an activation activate every child record too.</summary>
	internal const string ActivateChildrenOption = "moActivateChildrenAsWell";

	/// <summary>Cheat Engine's record option that makes a deactivation deactivate every child record too.</summary>
	internal const string DeactivateChildrenOption = "moDeactivateChildrenAsWell";

	/// <summary>Selects one address-list record.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordSelect, Title = "Select address-list record", ReadOnly = false,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Select one current address-list record in the Cheat Engine UI. This changes the shared Cheat Engine selection, which is visible to the user and other plugins.")]
	public RecordEntry Select(
		[Description("The current record id to select.")]
		int id,
		CancellationToken cancellationToken = default)
	{
		RecordArguments.Id(id, "id");
		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run(CheatEngineToolNames.RecordSelect, token =>
			RecordEntries.One(dispatch, CheatEngineToolNames.RecordSelect,
				client.Tables.SelectRecord(new MemoryRecordId(id), token), token), cancellationToken);
	}

	/// <summary>Creates a bounded batch of records.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordCreate, Title = "Create address-list records", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Create 1 to 256 value, group or Auto Assembler records in order. Each record is built without touching " +
		"memory, in this order: description, address and type, then parentId, the string or byte array length and " +
		"encoding, script and pointer offsets, and only then value, which is written at the record's final address; " +
		"an empty value writes nothing. offsets make address the base of a pointer chain, as signed hexadecimal in " +
		"dereference order, nearest the base first, the form pointer_list_paths and pointer_read_chain give. A byte " +
		"array record shows and takes hexadecimal byte pairs. Each parentId must already identify a record, so one " +
		"request cannot parent a record under another record it creates. Auto Assembler records and script text " +
		"require Mcp:EnableAutoAssembler. If a later step fails, the tool deletes the records this call created; a " +
		"rollback failure is reported as partial_effect with the retained count.")]
	public RecordCreateResult Create(
		[Description("The records to create, from 1 to 256 entries.")]
		RecordCreateSpec[] records,
		CancellationToken cancellationToken = default)
	{
		RecordArguments.Batch(records, "records");
		int[]?[] offsets = ValidateCreate(records);
		if (records.Any(static record =>
				record.VariableType is VariableType.AutoAssembler || record.Script is not null))
		{
			dispatch.Features.Require(McpFeature.AutoAssembler, CheatEngineToolNames.RecordCreate);
		}

		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run(CheatEngineToolNames.RecordCreate, token =>
		{
			MemoryRecordSnapshot[] created = new MemoryRecordSnapshot[records.Length];
			List<MemoryRecordId> createdIds = [];
			try
			{
				for (int index = 0; index < records.Length; index++)
				{
					created[index] = CreateOne(client, records[index], offsets[index], createdIds, token);
				}

				return new RecordCreateResult(RecordEntries.Map(dispatch, CheatEngineToolNames.RecordCreate, created,
					token));
			}
			catch
			{
				int rolledBack = 0;
				for (int index = createdIds.Count - 1; index >= 0; index--)
				{
					try
					{
						client.Tables.Delete(createdIds[index], client.Stopping);
						rolledBack++;
					}
					catch (Exception)
					{
						break;
					}
				}

				if (rolledBack != createdIds.Count)
				{
					throw CheatEngineToolException.PartialEffect(
						"The create batch failed and its rollback retained one or more records.",
						ToolHostEffect.Started,
						new RecordCreateRollback(createdIds.Count, rolledBack),
						RecordJsonContext.Default.RecordCreateRollback, false,
						"Use record_get or record_find to inspect retained records before deleting them.");
				}

				throw;
			}
		}, cancellationToken);
	}

	/// <summary>Updates a bounded batch of records.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordUpdate, Title = "Update address-list records", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Update 1 to 256 records in order. An update sets one or more of description, address, value, type, length, " +
		"unicode and pointer offsets. offsets are signed hexadecimal in dereference order, nearest the base first, " +
		"which is the reverse of Cheat Engine's internal order; they apply to any record except an Auto Assembler " +
		"record, and [] removes them. Cheat Engine clears offsets when address changes, so send them again with a " +
		"new address. The tool validates every request, reads every record and checks the nested records that a " +
		"moRecursiveSetValue record passes its value on to, at most 4096 per call, before changing the first one. " +
		"Within an update the description, address and type change first, then the string or byte array length and " +
		"encoding, then the offsets, then the value, which is written at the final address. A later host refusal can " +
		"leave the preceding updates applied; read the records before retrying the remainder.")]
	public RecordUpdateResult Update(
		[Description("The partial updates to apply, from 1 to 256 entries.")]
		RecordUpdateSpec[] updates,
		CancellationToken cancellationToken = default)
	{
		RecordArguments.Batch(updates, "updates");
		int[]?[] offsets = ValidateUpdates(updates);
		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run(CheatEngineToolNames.RecordUpdate, token =>
		{
			// Every record is copied and checked before the first change, so a refusal leaves the table untouched.
			MemoryRecordSnapshot[] current = new MemoryRecordSnapshot[updates.Length];
			bool[] layouts = new bool[updates.Length];
			for (int index = 0; index < updates.Length; index++)
			{
				RecordUpdateSpec update = updates[index];
				current[index] = client.Tables.GetRecord(new MemoryRecordId(update.Id), token);
				layouts[index] = CheckUpdate(update, current[index].Content.VariableType, index);
			}

			CheckRecursiveValues(updates, current, token);
			MemoryRecordSnapshot[] changed = new MemoryRecordSnapshot[updates.Length];
			for (int index = 0; index < updates.Length; index++)
			{
				RecordUpdateSpec update = updates[index];
				VariableType type = update.VariableType ?? current[index].Content.VariableType;
				changed[index] = Apply(client, update, offsets[index], layouts[index], ValueBytes(type, update.Value),
					token);
			}

			return new RecordUpdateResult(RecordEntries.Map(dispatch, CheatEngineToolNames.RecordUpdate, changed,
				token));
		}, cancellationToken);
	}

	/// <summary>Sets records active or inactive.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordSetActive, Title = "Set address-list records active",
		ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.MayPrompt)]
	[Description(
		"Set 1 to 256 records active or inactive, in order. Scripts can block Cheat Engine or show a dialog. Activating or deactivating an Auto Assembler record can " +
		"execute its enable or disable section, so it requires Mcp:EnableAutoAssembler; so does a record whose Cheat " +
		"Engine option moActivateChildrenAsWell (when activating) or moDeactivateChildrenAsWell (when deactivating) " +
		"passes the change on to a nested Auto Assembler record, checked through at most 4096 nested records. A " +
		"record Cheat Engine refuses keeps its state and the call goes on: its active then differs from " +
		"requestedActive and failure says why, with Cheat Engine's own reason and message for a failed activation, " +
		"such as aob_not_found or assert_failed. pending means Cheat Engine accepted an asynchronous activation " +
		"whose final state must be read later with record_get; the reason of a failure that comes after the call is " +
		"not reported. An error on a later record leaves the earlier records changed.")]
	public RecordActivationResult SetActive(
		[Description("The distinct current record ids to change, from 1 to 256 entries.")]
		int[] ids,
		[Description(
			"True to activate or freeze the records; false to deactivate them. To read why an activation failed, the " +
			"tool puts its own OnActivationFailure handler in front of a record's handler when the record has none " +
			"or a Lua function; the record's handler still runs, can ask Cheat Engine to retry at most 8 times, and " +
			"is assigned back afterwards unless it installed another handler, which is kept. A record with another " +
			"kind of handler keeps it untouched, and its failure reports not_reported.")]
		bool active,
		CancellationToken cancellationToken = default)
	{
		RecordArguments.Batch(ids, "ids");
		RecordReadTools.ValidateUniqueIds(ids, "ids");
		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run(CheatEngineToolNames.RecordSetActive, token =>
		{
			MemoryRecordSnapshot[] before =
				[.. ids.Select(id => client.Tables.GetRecord(new MemoryRecordId(id), token))];
			if (!dispatch.Features.IsEnabled(McpFeature.AutoAssembler) && RunsAutoAssembler(before, active, token))
			{
				dispatch.Features.Require(McpFeature.AutoAssembler, CheatEngineToolNames.RecordSetActive);
			}

			RecordActivation[] states = new RecordActivation[ids.Length];
			for (int index = 0; index < ids.Length; index++)
			{
				RecordLuaActivation after = dispatch.ExecuteLua(CheatEngineToolNames.RecordSetActive,
					RecordLuaScripts.SetActive, RecordLuaJsonContext.Default.RecordLuaActivation, token,
					before[index].Id.Value, active, MaximumFailureTextBytes, MaximumFailureHandlerCalls);
				states[index] = new RecordActivation(before[index].Id.Value, active, after.Active, after.Pending,
					Failure(active, after));
			}

			return new RecordActivationResult(states);
		}, cancellationToken);
	}

	/// <summary>Deletes records in one bounded batch.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordDelete, Title = "Delete address-list records", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.MayPrompt)]
	[Description(
		"Delete 1 to 256 records by current id. Deleting a group can also remove or reparent its children according to Cheat Engine's address-list behavior. Deleting an active Auto Assembler record can execute its disable section and requires Mcp:EnableAutoAssembler. Script execution can block Cheat Engine or show a dialog. A failure after an earlier delete leaves those earlier deletions in effect.")]
	public RecordDeleteResult Delete(
		[Description("The distinct current record ids to delete, from 1 to 256 entries.")]
		int[] ids,
		CancellationToken cancellationToken = default)
	{
		RecordArguments.Batch(ids, "ids");
		RecordReadTools.ValidateUniqueIds(ids, "ids");
		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run(CheatEngineToolNames.RecordDelete, token =>
		{
			MemoryRecordSnapshot[] records =
				[.. ids.Select(id => client.Tables.GetRecord(new MemoryRecordId(id), token))];
			if (records.Any(static record =>
					record.Content.VariableType is VariableType.AutoAssembler && record.State.IsActive))
			{
				dispatch.Features.Require(McpFeature.AutoAssembler, CheatEngineToolNames.RecordDelete);
			}

			foreach (MemoryRecordSnapshot record in records)
			{
				client.Tables.Delete(record.Id, token);
			}

			return new RecordDeleteResult(records.Length);
		}, cancellationToken);
	}

	/// <summary>Moves a record below a parent group or back to the root.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordMove, Title = "Move address-list record", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Move one record under parentId, or omit parentId to restore it to the address-list root. Cheat Engine refuses a record as its own parent and any parent that would create a hierarchy cycle. The result is a copied record after the move.")]
	public RecordMoveResult Move(
		[Description("The current record id to move.")]
		int id,
		[Description("The existing parent record id; omit to move to the root.")]
		int? parentId = null,
		CancellationToken cancellationToken = default)
	{
		RecordArguments.Id(id, "id");
		if (parentId is { } parent)
		{
			RecordArguments.Id(parent, "parentId");
			if (parent == id)
			{
				throw CheatEngineToolException.InvalidArgument("parentId", "must differ from id.");
			}
		}

		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run(CheatEngineToolNames.RecordMove, token =>
		{
			MemoryRecordId? parent = parentId is { } value ? new MemoryRecordId(value) : null;
			return new RecordMoveResult(RecordEntries.One(dispatch, CheatEngineToolNames.RecordMove,
				client.Tables.SetParent(new MemoryRecordId(id), parent, token), token));
		}, cancellationToken);
	}

	/// <summary>Creates a group and moves existing records below it.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordGroup, Title = "Group address-list records", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Create a group header and move 1 to 256 existing records under it. parentId can place the new group below an existing parent. The move is sequential: if Cheat Engine refuses a later member, the group and earlier moves remain, so inspect the table before cleanup.")]
	public RecordGroupResult Group(
		[Description("The new group description, up to 1024 characters.")]
		string description,
		[Description("The distinct current record ids to move under the new group, from 1 to 256 entries.")]
		int[] ids,
		[Description("An existing parent id for the new group; omit for the root.")]
		int? parentId = null,
		CancellationToken cancellationToken = default)
	{
		RecordArguments.Text(description, "description", RecordArguments.MaximumDescriptionLength);
		RecordArguments.Batch(ids, "ids");
		RecordReadTools.ValidateUniqueIds(ids, "ids");
		if (parentId is { } parent)
		{
			RecordArguments.Id(parent, "parentId");
			if (ids.Contains(parent))
			{
				throw CheatEngineToolException.InvalidArgument("parentId",
					"cannot name a record that this call moves.");
			}
		}

		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run(CheatEngineToolNames.RecordGroup, token =>
		{
			MemoryRecordId? parent = parentId is { } value ? new MemoryRecordId(value) : null;
			MemoryRecordSnapshot group = client.Tables.Create(new MemoryRecordDefinition(description, "0", string.Empty,
				VariableType.Grouped, parent), token);
			MemoryRecordSnapshot[] moved = new MemoryRecordSnapshot[ids.Length];
			for (int index = 0; index < ids.Length; index++)
			{
				moved[index] = client.Tables.SetParent(new MemoryRecordId(ids[index]), group.Id, token);
			}

			RecordEntry[] entries = RecordEntries.Map(dispatch, CheatEngineToolNames.RecordGroup,
				[client.Tables.GetRecord(group.Id, token), .. moved], token);
			return new RecordGroupResult(entries[0], entries[1..]);
		}, cancellationToken);
	}

	/// <summary>Stores Auto Assembler text on one existing Auto Assembler record.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordSetScript, Title = "Set address-list Auto Assembler script",
		ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[RequiresFeature(McpFeature.AutoAssembler)]
	[Description(
		"Replace the stored Auto Assembler text of one existing, inactive Auto Assembler record. Storing the text " +
		"does not activate the record, but a later record_set_active can execute it. An active record, or one still " +
		"activating, is refused as invalid_state: Cheat Engine would disable it by running the new text's disable " +
		"section with the allocations and symbols of the old one, so deactivate it with record_set_active first. " +
		"This tool requires Mcp:EnableAutoAssembler and preserves the supplied script as data through a fixed Lua " +
		"body.")]
	public RecordScriptResult SetScript(
		[Description("The current Auto Assembler record id.")]
		int id,
		[Description("The replacement Auto Assembler text, up to 1048576 characters and 1048576 UTF-8 bytes.")]
		string script,
		CancellationToken cancellationToken = default)
	{
		RecordArguments.Id(id, "id");
		ArgumentNullException.ThrowIfNull(script);
		RecordArguments.Script(script, "script");
		dispatch.Features.Require(McpFeature.AutoAssembler, CheatEngineToolNames.RecordSetScript);
		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run(CheatEngineToolNames.RecordSetScript, token =>
		{
			MemoryRecordSnapshot before = client.Tables.GetRecord(new MemoryRecordId(id), token);
			if (before.Content.VariableType is not VariableType.AutoAssembler)
			{
				throw CheatEngineToolException.InvalidState("The address-list record is not an Auto Assembler record.");
			}

			if (before.State.IsActive || before.State.IsAsyncProcessing)
			{
				throw CheatEngineToolException.InvalidState(
					"The Auto Assembler record is active or activating; Cheat Engine would disable it with the new " +
					"script and the old allocations.",
					"Deactivate the record with record_set_active, then set its script.");
			}

			dispatch.ExecuteLua(CheatEngineToolNames.RecordSetScript, RecordLuaScripts.SetScript,
				RecordJsonContext.Default.RecordLuaChanged, token, id, script);
			return new RecordScriptResult(RecordEntries.One(dispatch, CheatEngineToolNames.RecordSetScript,
				client.Tables.GetRecord(before.Id, token), token));
		}, cancellationToken);
	}

	/// <summary>Replaces the dropdown list and options of one address-list record.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordSetDropdown, Title = "Set address-list record dropdown",
		ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Replace the dropdown list of one address-list record, the value:description choices Cheat Engine offers when the user edits its value, and optionally its three dropdown options. items is the complete new list; [] removes the list and turns every option off. Read the current list first with record_get and includeDropdown true. Cheat Engine compares a value with the record's displayed value text, ignoring the case of ASCII letters, so a record shown as hexadecimal needs hexadecimal values; a '*' value is the text shown for any other value when all three options are on. Records whose list is linked to this record show the new list too. An Auto Assembler record, or a record whose list is linked to another record's list, is refused before any change. The result is the record copied with its dropdown.")]
	public RecordDropdownResult SetDropdown(
		[Description("The current record id.")]
		int id,
		[Description(
			"The complete replacement list in display order: 0 to 1024 items, together at most 262144 bytes of " +
			"value and description text when encoded as UTF-8; [] removes the list. An item with an empty value is " +
			"stored as a ':description' line, which Cheat Engine reads like a line without a colon.")]
		RecordDropdownItem[] items,
		[Description(
			"Cheat Engine's DropDownReadOnly option, shown as 'Disallow manual user input': true lets the user " +
			"only choose a listed value. Omit to keep the current setting; with items [] omit it or pass false.")]
		bool? disallowManualInput = null,
		[Description(
			"Cheat Engine's DropDownDescriptionOnly option: true shows only the descriptions in the list. Omit " +
			"to keep the current setting; with items [] omit it or pass false.")]
		bool? descriptionOnly = null,
		[Description(
			"Cheat Engine's DisplayAsDropDownListItem option: true makes the record display its value as the " +
			"matching list item. Omit to keep the current setting; with items [] omit it or pass false.")]
		bool? displayAsListItem = null,
		CancellationToken cancellationToken = default)
	{
		RecordArguments.Id(id, "id");
		string[] lines = RecordDropdowns.Lines(items, "items");
		if (lines.Length == 0)
		{
			RequireOff(disallowManualInput, "disallowManualInput");
			RequireOff(descriptionOnly, "descriptionOnly");
			RequireOff(displayAsListItem, "displayAsListItem");
			// Cheat Engine ignores the options of an empty list and does not save them, so removing it clears them.
			disallowManualInput = descriptionOnly = displayAsListItem = false;
		}

		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run(CheatEngineToolNames.RecordSetDropdown, token =>
		{
			MemoryRecordSnapshot before = client.Tables.GetRecord(new MemoryRecordId(id), token);
			if (before.Content.VariableType is VariableType.AutoAssembler)
			{
				throw CheatEngineToolException.InvalidState(
					"An Auto Assembler record has no value to choose from a dropdown list.");
			}

			dispatch.ExecuteLua(CheatEngineToolNames.RecordSetDropdown, RecordLuaScripts.SetDropdown,
				RecordJsonContext.Default.RecordLuaChanged, token, id, lines, disallowManualInput, descriptionOnly,
				displayAsListItem);
			RecordDropdown dropdown = RecordDropdowns.Read(dispatch, CheatEngineToolNames.RecordSetDropdown, [id],
				token)[0];
			return new RecordDropdownResult(RecordEntries.One(dispatch, CheatEngineToolNames.RecordSetDropdown,
				client.Tables.GetRecord(before.Id, token), token, dropdown));
		}, cancellationToken);
	}

	/// <summary>
	///     Maps the state Cheat Engine left a record in: a record in the other state that is not still activating was
	///     refused, with the reason Cheat Engine passed to <c>OnActivationFailure</c> when it passed one.
	/// </summary>
	internal static RecordActivationFailure? Failure(bool requested, RecordLuaActivation state)
	{
		if (state.Pending || state.Active == requested)
		{
			return null;
		}

		if (!requested || state.Reason is not { } reason)
		{
			return new RecordActivationFailure(RecordActivationFailureReason.NotReported);
		}

		const int lastReason = (int) RecordActivationFailureReason.DllInjectionFailed;
		RecordActivationFailureReason mapped = reason is >= 0 and <= lastReason
			? (RecordActivationFailureReason) reason
			: RecordActivationFailureReason.Unknown;
		return new RecordActivationFailure(mapped, string.IsNullOrEmpty(state.Text) ? null : state.Text);
	}

	/// <summary>Refuses an option set to true for an empty list, which Cheat Engine would ignore.</summary>
	private static void RequireOff(bool? option, string parameter)
	{
		if (option is true)
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"must be omitted or false when items is empty, because Cheat Engine ignores the options of an " +
				"empty list.");
		}
	}

	private static bool HasTypedUpdate(RecordUpdateSpec update)
	{
		return update.Description is not null || update.Address is not null || update.Value is not null ||
			   update.VariableType is not null;
	}

	/// <summary>
	///     Creates one record without writing memory: the fixed Lua body creates it from its description, address
	///     and type, the Client then places it below its parent, the layout, script and offsets follow, and the Client
	///     writes a non-empty value last, at the address the record resolves to once it is complete.
	/// </summary>
	private MemoryRecordSnapshot CreateOne(ICheatEngineClient client, RecordCreateSpec spec, int[]? offsets,
		List<MemoryRecordId> createdIds, CancellationToken cancellationToken)
	{
		const string operation = CheatEngineToolNames.RecordCreate;
		RecordLuaChanged blank = dispatch.ExecuteLua(operation, RecordLuaScripts.CreateRecord,
			RecordJsonContext.Default.RecordLuaChanged, cancellationToken, spec.Description, spec.Address,
			(int) spec.VariableType);
		MemoryRecordId id = new(blank.Id);
		createdIds.Add(id);
		if (spec.ParentId is { } parent)
		{
			client.Tables.SetParent(id, new MemoryRecordId(parent), cancellationToken);
		}

		if (RecordArguments.NeedsLayout(spec.VariableType, spec.Length, spec.Unicode, true))
		{
			SetLayout(operation, id, spec.Length, spec.Unicode, ValueBytes(spec.VariableType, spec.Value),
				cancellationToken);
		}

		if (spec.Script is not null)
		{
			dispatch.ExecuteLua(operation, RecordLuaScripts.SetScript, RecordJsonContext.Default.RecordLuaChanged,
				cancellationToken, id.Value, spec.Script);
		}

		if (offsets is { Length: > 0 })
		{
			SetOffsets(operation, id, offsets, cancellationToken);
		}

		return spec.Value.Length > 0
			? client.Tables.Update(id, new MemoryRecordUpdate(null, null, spec.Value, null), cancellationToken)
			: client.Tables.GetRecord(id, cancellationToken);
	}

	/// <summary>
	///     Checks one update against the record it changes, before the first change of the batch, and returns whether
	///     it needs the layout step.
	/// </summary>
	private static bool CheckUpdate(RecordUpdateSpec update, VariableType current, int index)
	{
		VariableType type = update.VariableType ?? current;
		string prefix = $"updates[{index}]";
		if (update.Offsets is not null && type is VariableType.AutoAssembler)
		{
			throw CheatEngineToolException.InvalidArgument($"{prefix}.offsets",
				"cannot be set on an Auto Assembler record.");
		}

		RecordArguments.Layout(type, update.Length, update.Unicode, prefix);
		if (update.Value is { } value)
		{
			if (value.Length == 0 && type is not VariableType.String)
			{
				throw CheatEngineToolException.InvalidArgument($"{prefix}.value",
					"can be empty only for a string record (6), where it writes an empty string.",
					"Omit value to keep the current value.");
			}

			RecordArguments.Value(type, value, $"{prefix}.value");
		}

		bool bytesChange = update.Value is not null ||
						   (update.VariableType is VariableType.ByteArray && current is not VariableType.ByteArray);
		return RecordArguments.NeedsLayout(type, update.Length, update.Unicode, bytesChange);
	}

	/// <summary>
	///     Refuses, before the first change of the batch, a value that is not a plain number when the record passes
	///     it on to a nested record of a number type through Cheat Engine's <c>moRecursiveSetValue</c> option. A
	///     record of a number type already takes only a plain number, so only the other records with children are
	///     checked, in one fixed Lua pass.
	/// </summary>
	private void CheckRecursiveValues(RecordUpdateSpec[] updates, MemoryRecordSnapshot[] records,
		CancellationToken cancellationToken)
	{
		int[] checkedIndexes =
		[
			.. Enumerable.Range(0, updates.Length).Where(index => updates[index].Value is { Length: > 0 } &&
				records[index].State.ChildCount > 0 &&
				!RecordArguments.IsNumberType(updates[index].VariableType ?? records[index].Content.VariableType))
		];
		if (checkedIndexes.Length == 0)
		{
			return;
		}

		bool[] reached = Reaches(CheatEngineToolNames.RecordUpdate,
			[.. checkedIndexes.Select(index => updates[index].Id)], RecordArguments.RecursiveSetValueOption,
			RecordArguments.NumberTypes, cancellationToken);
		for (int position = 0; position < checkedIndexes.Length; position++)
		{
			if (reached[position])
			{
				int index = checkedIndexes[position];
				RecordArguments.RecursiveValue(updates[index].Value!, $"updates[{index}].value");
			}
		}
	}

	/// <summary>
	///     Whether changing the state of the records can run Auto Assembler code: a listed Auto Assembler record, or
	///     one that Cheat Engine reaches through the option that passes this change on to child records.
	/// </summary>
	private bool RunsAutoAssembler(MemoryRecordSnapshot[] records, bool active, CancellationToken cancellationToken)
	{
		if (records.Any(static record => record.Content.VariableType is VariableType.AutoAssembler))
		{
			return true;
		}

		int[] parents = [.. records.Where(static record => record.State.ChildCount > 0).Select(static record =>
			record.Id.Value)];
		return parents.Length > 0 && Reaches(CheatEngineToolNames.RecordSetActive, parents,
			active ? ActivateChildrenOption : DeactivateChildrenOption, [(int) VariableType.AutoAssembler],
			cancellationToken).Contains(true);
	}

	/// <summary>
	///     Reports, for each record, whether Cheat Engine passes a change of it on, through <paramref name="option" />,
	///     to a nested record of one of <paramref name="types" />, examining at most
	///     <see cref="MaximumReachedRecords" /> nested records.
	/// </summary>
	private bool[] Reaches(string operation, int[] ids, string option, IReadOnlyList<int> types,
		CancellationToken cancellationToken)
	{
		RecordLuaReaches reach = dispatch.ExecuteLua(operation, RecordLuaScripts.Reaches,
			RecordLuaJsonContext.Default.RecordLuaReaches, cancellationToken, ids, option, types,
			MaximumReachedRecords);
		if (reach.Reaches is not { } reaches || reaches.Length != ids.Length)
		{
			throw CheatEngineToolException.Internal("The nested-record check did not return one flag per record.");
		}

		return reaches;
	}

	/// <summary>The byte count of a byte array value that the layout step raises the record to.</summary>
	private static int? ValueBytes(VariableType type, string? value)
	{
		return type is VariableType.ByteArray && value is { Length: > 0 } ? RecordArguments.ByteCount(value) : null;
	}

	/// <summary>
	///     Applies one validated update. Offsets follow the address, which clears them in Cheat Engine, and precede the
	///     value, which Cheat Engine writes at the address the offsets resolve to; a string or byte array layout comes
	///     before both, so that the value is written with the record's final length, encoding and display.
	/// </summary>
	private MemoryRecordSnapshot Apply(ICheatEngineClient client, RecordUpdateSpec update, int[]? offsets, bool layout,
		int? valueBytes, CancellationToken cancellationToken)
	{
		MemoryRecordId id = new(update.Id);
		if (offsets is null && !layout)
		{
			return client.Tables.Update(id,
				new MemoryRecordUpdate(update.Description, update.Address, update.Value, update.VariableType),
				cancellationToken);
		}

		if (update.Description is not null || update.Address is not null || update.VariableType is not null)
		{
			client.Tables.Update(id,
				new MemoryRecordUpdate(update.Description, update.Address, null, update.VariableType),
				cancellationToken);
		}

		if (layout)
		{
			SetLayout(CheatEngineToolNames.RecordUpdate, id, update.Length, update.Unicode, valueBytes,
				cancellationToken);
		}

		if (offsets is not null)
		{
			SetOffsets(CheatEngineToolNames.RecordUpdate, id, offsets, cancellationToken);
		}

		return update.Value is not null
			? client.Tables.Update(id, new MemoryRecordUpdate(null, null, update.Value, null), cancellationToken)
			: client.Tables.GetRecord(id, cancellationToken);
	}

	/// <summary>Replaces the pointer offsets of one record through the fixed Lua body.</summary>
	private void SetOffsets(string operation, MemoryRecordId id, int[] offsets, CancellationToken cancellationToken)
	{
		dispatch.ExecuteLua(operation, RecordLuaScripts.SetOffsets, RecordJsonContext.Default.RecordLuaChanged,
			cancellationToken, id.Value, offsets);
	}

	/// <summary>
	///     Sets the string or byte array layout of one record through the fixed Lua body; a byte array is raised to
	///     <paramref name="valueBytes" />, the byte count of the value about to be written.
	/// </summary>
	private void SetLayout(string operation, MemoryRecordId id, int? length, bool? unicode, int? valueBytes,
		CancellationToken cancellationToken)
	{
		dispatch.ExecuteLua(operation, RecordLuaScripts.SetLayout, RecordJsonContext.Default.RecordLuaChanged,
			cancellationToken, id.Value, length, unicode, valueBytes);
	}

	private static int[]?[] ValidateCreate(RecordCreateSpec[] records)
	{
		int[]?[] offsets = new int[]?[records.Length];
		for (int index = 0; index < records.Length; index++)
		{
			RecordCreateSpec? record = records[index];
			if (record is null)
			{
				throw CheatEngineToolException.InvalidArgument($"records[{index}]", "must not be null.");
			}

			string prefix = $"records[{index}]";
			RecordArguments.Text(record.Description, $"{prefix}.description", RecordArguments.MaximumDescriptionLength);
			RecordArguments.Text(record.Address, $"{prefix}.address", RecordArguments.MaximumAddressLength, true);
			RecordArguments.Text(record.Value, $"{prefix}.value", RecordArguments.MaximumValueLength);
			RecordArguments.Type(record.VariableType, $"{prefix}.variableType");
			RecordArguments.Value(record.VariableType, record.Value, $"{prefix}.value");
			RecordArguments.Layout(record.VariableType, record.Length, record.Unicode, prefix);
			if (record.Value.Length == 0 && record.Length is null &&
				record.VariableType is VariableType.String or VariableType.ByteArray)
			{
				throw CheatEngineToolException.InvalidArgument($"{prefix}.length",
					"is required for a string or byte array record created with an empty value, which would " +
					"otherwise read nothing.");
			}

			if (record.ParentId is { } parent)
			{
				RecordArguments.Id(parent, $"{prefix}.parentId");
			}

			if (record.Script is not null)
			{
				if (record.VariableType is not VariableType.AutoAssembler)
				{
					throw CheatEngineToolException.InvalidArgument($"{prefix}.script",
						"requires variableType AutoAssembler.");
				}

				RecordArguments.Script(record.Script, $"{prefix}.script");
			}

			if (record.Offsets is { } chain)
			{
				if (record.VariableType is VariableType.AutoAssembler)
				{
					throw CheatEngineToolException.InvalidArgument($"{prefix}.offsets",
						"cannot be set on an Auto Assembler record.");
				}

				offsets[index] = RecordArguments.Offsets(chain, $"{prefix}.offsets");
			}
		}

		return offsets;
	}

	private static int[]?[] ValidateUpdates(RecordUpdateSpec[] updates)
	{
		int[]?[] offsets = new int[]?[updates.Length];
		HashSet<int> seen = [];
		for (int index = 0; index < updates.Length; index++)
		{
			RecordUpdateSpec? update = updates[index];
			if (update is null)
			{
				throw CheatEngineToolException.InvalidArgument($"updates[{index}]", "must not be null.");
			}

			RecordArguments.Id(update.Id, $"updates[{index}].id");
			if (!seen.Add(update.Id))
			{
				throw CheatEngineToolException.InvalidArgument($"updates[{index}].id", "repeats an earlier record id.");
			}

			if (!HasTypedUpdate(update) && update.Offsets is null && update.Length is null && update.Unicode is null)
			{
				throw CheatEngineToolException.InvalidArgument($"updates[{index}]", "must set at least one field.");
			}

			if (update.Description is not null)
			{
				RecordArguments.Text(update.Description, $"updates[{index}].description",
					RecordArguments.MaximumDescriptionLength);
			}

			if (update.Address is not null)
			{
				RecordArguments.Text(update.Address, $"updates[{index}].address", RecordArguments.MaximumAddressLength,
					true);
			}

			if (update.Value is not null)
			{
				RecordArguments.Text(update.Value, $"updates[{index}].value", RecordArguments.MaximumValueLength);
			}

			if (update.VariableType is { } type)
			{
				RecordArguments.Type(type, $"updates[{index}].variableType");
				RecordArguments.Layout(type, update.Length, update.Unicode, $"updates[{index}]");
				if (update.Value is not null)
				{
					RecordArguments.Value(type, update.Value, $"updates[{index}].value");
				}
			}
			else if (update.Length is < 1 or > RecordArguments.MaximumLength)
			{
				throw CheatEngineToolException.InvalidArgument($"updates[{index}].length",
					$"must be from 1 to {RecordArguments.MaximumLength}.");
			}

			if (update.Offsets is { } chain)
			{
				if (update.VariableType is VariableType.AutoAssembler)
				{
					throw CheatEngineToolException.InvalidArgument($"updates[{index}].offsets",
						"cannot be set on an Auto Assembler record.");
				}

				offsets[index] = RecordArguments.Offsets(chain, $"updates[{index}].offsets");
			}
		}

		return offsets;
	}
}
