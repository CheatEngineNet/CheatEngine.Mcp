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
			RecordArguments.Entry(client.Tables.SelectRecord(new MemoryRecordId(id), token)), cancellationToken);
	}

	/// <summary>Creates a bounded batch of records.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordCreate, Title = "Create address-list records", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Create 1 to 256 value, group or Auto Assembler records in order. Each parentId must already identify a record, so one request cannot parent a record under another record it creates. Auto Assembler records and script text require Mcp:EnableAutoAssembler. If a later step fails, the tool deletes the records this call created; a rollback failure is reported as partial_effect with the retained count.")]
	public RecordCreateResult Create(
		[Description("The records to create, from 1 to 256 entries.")]
		RecordCreateSpec[] records,
		CancellationToken cancellationToken = default)
	{
		RecordArguments.Batch(records, "records");
		ValidateCreate(records);
		if (records.Any(static record =>
				record.VariableType is VariableType.AutoAssembler || record.Script is not null))
		{
			dispatch.Features.Require(McpFeature.AutoAssembler, CheatEngineToolNames.RecordCreate);
		}

		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run(CheatEngineToolNames.RecordCreate, token =>
		{
			RecordEntry[] created = new RecordEntry[records.Length];
			List<MemoryRecordId> createdIds = [];
			try
			{
				for (int index = 0; index < records.Length; index++)
				{
					RecordCreateSpec spec = records[index];
					MemoryRecordId? parent = spec.ParentId is { } parentId ? new MemoryRecordId(parentId) : null;
					MemoryRecordSnapshot snapshot = client.Tables.Create(new MemoryRecordDefinition(spec.Description,
						spec.Address, spec.Value, spec.VariableType, parent), token);
					createdIds.Add(snapshot.Id);
					if (spec.Script is not null)
					{
						dispatch.ExecuteLua(CheatEngineToolNames.RecordCreate, RecordLuaScripts.SetScript,
							RecordJsonContext.Default.RecordLuaChanged, token, snapshot.Id.Value, spec.Script);
						snapshot = client.Tables.GetRecord(snapshot.Id, token);
					}

					created[index] = RecordArguments.Entry(snapshot);
				}
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

			return new RecordCreateResult(created);
		}, cancellationToken);
	}

	/// <summary>Updates a bounded batch of records.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordUpdate, Title = "Update address-list records", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Update 1 to 256 records in order. An update sets one or more of description, address, value, type and pointer offsets. offsets use dereference order, nearest the base first, which is the reverse of Cheat Engine's internal order. The tool validates every request before changing the first record. A later host refusal can leave the preceding updates applied; read the records before retrying the remainder.")]
	public RecordUpdateResult Update(
		[Description("The partial updates to apply, from 1 to 256 entries.")]
		RecordUpdateSpec[] updates,
		CancellationToken cancellationToken = default)
	{
		RecordArguments.Batch(updates, "updates");
		ValidateUpdates(updates);
		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run(CheatEngineToolNames.RecordUpdate, token =>
		{
			RecordEntry[] changed = new RecordEntry[updates.Length];
			for (int index = 0; index < updates.Length; index++)
			{
				RecordUpdateSpec update = updates[index];
				MemoryRecordId id = new(update.Id);
				MemoryRecordSnapshot snapshot = client.Tables.GetRecord(id, token);
				VariableType effectiveType = update.VariableType ?? snapshot.Content.VariableType;
				if (update.Offsets is not null && effectiveType is not VariableType.Pointer)
				{
					throw CheatEngineToolException.InvalidArgument($"updates[{index}].offsets",
						"can be set only on a Pointer record.");
				}

				if (HasTypedUpdate(update))
				{
					snapshot = client.Tables.Update(id,
						new MemoryRecordUpdate(update.Description, update.Address, update.Value, update.VariableType),
						token);
				}

				if (update.Offsets is not null)
				{
					dispatch.ExecuteLua(CheatEngineToolNames.RecordUpdate, RecordLuaScripts.SetOffsets,
						RecordJsonContext.Default.RecordLuaChanged, token, id.Value, update.Offsets);
					snapshot = client.Tables.GetRecord(id, token);
				}

				changed[index] = RecordArguments.Entry(snapshot);
			}

			return new RecordUpdateResult(changed);
		}, cancellationToken);
	}

	/// <summary>Sets records active or inactive.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordSetActive, Title = "Set address-list records active",
		ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Set 1 to 256 records active or inactive. Activating or deactivating an Auto Assembler record can execute its enable or disable section, so it requires Mcp:EnableAutoAssembler. pending means Cheat Engine accepted an asynchronous activation whose final state must be read later.")]
	public RecordActivationResult SetActive(
		[Description("The distinct current record ids to change, from 1 to 256 entries.")]
		int[] ids,
		[Description("True to activate or freeze the records; false to deactivate them.")]
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
			if (before.Any(static record => record.Content.VariableType is VariableType.AutoAssembler))
			{
				dispatch.Features.Require(McpFeature.AutoAssembler, CheatEngineToolNames.RecordSetActive);
			}

			RecordActivation[] states = new RecordActivation[ids.Length];
			for (int index = 0; index < ids.Length; index++)
			{
				MemoryRecordSnapshot after = client.Tables.SetActive(before[index].Id, active, token);
				states[index] = new RecordActivation(after.Id.Value, active, after.State.IsActive,
					after.State.IsAsyncProcessing);
			}

			return new RecordActivationResult(states);
		}, cancellationToken);
	}

	/// <summary>Deletes records in one bounded batch.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordDelete, Title = "Delete address-list records", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Delete 1 to 256 records by current id. Deleting a group can also remove or reparent its children according to Cheat Engine's address-list behavior. Deleting an active Auto Assembler record can execute its disable section and requires Mcp:EnableAutoAssembler. A failure after an earlier delete leaves those earlier deletions in effect.")]
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
			return new RecordMoveResult(
				RecordArguments.Entry(client.Tables.SetParent(new MemoryRecordId(id), parent, token)));
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
			RecordEntry[] moved = new RecordEntry[ids.Length];
			for (int index = 0; index < ids.Length; index++)
			{
				moved[index] =
					RecordArguments.Entry(client.Tables.SetParent(new MemoryRecordId(ids[index]), group.Id, token));
			}

			return new RecordGroupResult(RecordArguments.Entry(client.Tables.GetRecord(group.Id, token)), moved);
		}, cancellationToken);
	}

	/// <summary>Stores Auto Assembler text on one existing Auto Assembler record.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordSetScript, Title = "Set address-list Auto Assembler script",
		ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Replace the stored Auto Assembler text of one existing Auto Assembler record. Storing the text does not activate the record, but a later record_set_active can execute it. This tool requires Mcp:EnableAutoAssembler and preserves the supplied script as data through a fixed Lua body.")]
	public RecordScriptResult SetScript(
		[Description("The current Auto Assembler record id.")]
		int id,
		[Description("The replacement Auto Assembler text, up to 1048576 characters.")]
		string script,
		CancellationToken cancellationToken = default)
	{
		RecordArguments.Id(id, "id");
		ArgumentNullException.ThrowIfNull(script);
		if (script.Length > RecordArguments.MaximumScriptLength)
		{
			throw CheatEngineToolException.LimitExceeded("script",
				$"must be at most {RecordArguments.MaximumScriptLength} characters.");
		}

		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run(CheatEngineToolNames.RecordSetScript, token =>
		{
			MemoryRecordSnapshot before = client.Tables.GetRecord(new MemoryRecordId(id), token);
			if (before.Content.VariableType is not VariableType.AutoAssembler)
			{
				throw CheatEngineToolException.InvalidState("The address-list record is not an Auto Assembler record.");
			}

			dispatch.Features.Require(McpFeature.AutoAssembler, CheatEngineToolNames.RecordSetScript);
			dispatch.ExecuteLua(CheatEngineToolNames.RecordSetScript, RecordLuaScripts.SetScript,
				RecordJsonContext.Default.RecordLuaChanged, token, id, script);
			return new RecordScriptResult(RecordArguments.Entry(client.Tables.GetRecord(before.Id, token)));
		}, cancellationToken);
	}

	private static bool HasTypedUpdate(RecordUpdateSpec update)
	{
		return update.Description is not null || update.Address is not null || update.Value is not null ||
			   update.VariableType is not null;
	}

	private static void ValidateCreate(RecordCreateSpec[] records)
	{
		for (int index = 0; index < records.Length; index++)
		{
			RecordCreateSpec? record = records[index];
			if (record is null)
			{
				throw CheatEngineToolException.InvalidArgument($"records[{index}]", "must not be null.");
			}

			RecordArguments.Text(record.Description, $"records[{index}].description",
				RecordArguments.MaximumDescriptionLength);
			RecordArguments.Text(record.Address, $"records[{index}].address", RecordArguments.MaximumAddressLength,
				true);
			RecordArguments.Text(record.Value, $"records[{index}].value", RecordArguments.MaximumValueLength);
			RecordArguments.Type(record.VariableType, $"records[{index}].variableType");
			if (record.ParentId is { } parent)
			{
				RecordArguments.Id(parent, $"records[{index}].parentId");
			}

			if (record.Script is not null)
			{
				if (record.VariableType is not VariableType.AutoAssembler)
				{
					throw CheatEngineToolException.InvalidArgument($"records[{index}].script",
						"requires variableType AutoAssembler.");
				}

				if (record.Script.Length > RecordArguments.MaximumScriptLength)
				{
					throw CheatEngineToolException.LimitExceeded($"records[{index}].script",
						$"must be at most {RecordArguments.MaximumScriptLength} characters.");
				}
			}
		}
	}

	private static void ValidateUpdates(RecordUpdateSpec[] updates)
	{
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

			if (!HasTypedUpdate(update) && update.Offsets is null)
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
			}

			if (update.Offsets is { } offsets && offsets.Length > RecordArguments.MaximumOffsets)
			{
				throw CheatEngineToolException.LimitExceeded($"updates[{index}].offsets",
					$"accepts at most {RecordArguments.MaximumOffsets} offsets.");
			}
		}
	}
}
