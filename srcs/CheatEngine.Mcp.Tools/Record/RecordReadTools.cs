using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Client.Tables;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Record;

/// <summary>Reads bounded copied address-list records through the typed Client table API.</summary>
[McpServerToolType]
public sealed class RecordReadTools(ToolDispatch dispatch)
{
	/// <summary>Pages the whole address list, nested records included, or one record's immediate children.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordList, Title = "List address-list records", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Page the whole Cheat Engine address list in its current order, where each record is followed by the records " +
		"nested below it, including those in collapsed groups; total counts every record. With parentId the page " +
		"holds only the immediate children of that record. Each record includes its childCount and, for a pointer " +
		"record, its offsets; use record_get for a specific record after choosing it here. With parentId a page " +
		"copies only the children it lists, however large the subtree below them. Only when a table load has reused " +
		"the id of a listed child that an earlier call returned does that page copy the parent's whole subtree " +
		"instead, at most 4096 records and 64 levels, to renew the ids; a larger subtree is then refused. The total " +
		"can change when the user or another plugin edits the table.")]
	public RecordPage List(
		[Description("The zero-based index, among the listed records, where this page starts.")]
		int offset = 0,
		[Description("The maximum number of records to copy, from 1 to 1000.")]
		int limit = 100,
		[Description(
			"The current id of the record whose immediate children to list; omit to list the whole address list.")]
		int? parentId = null,
		CancellationToken cancellationToken = default)
	{
		if (offset < 0)
		{
			throw CheatEngineToolException.InvalidArgument("offset", "must be zero or greater.");
		}

		if (limit < 1)
		{
			throw CheatEngineToolException.InvalidArgument("limit", "must be at least 1.");
		}

		if (limit > RecordArguments.MaximumListLimit)
		{
			throw CheatEngineToolException.LimitExceeded("limit",
				$"must be at most {RecordArguments.MaximumListLimit}.");
		}

		ICheatEngineClient client = dispatch.Client;
		if (parentId is { } parent)
		{
			RecordArguments.Id(parent, "parentId");
			return dispatch.Run(CheatEngineToolNames.RecordList,
				token => Children(new MemoryRecordId(parent), offset, limit, token), cancellationToken);
		}

		return dispatch.Run(CheatEngineToolNames.RecordList, token =>
		{
			int total = client.Tables.GetRecordCount(token);
			if (offset >= total)
			{
				return new RecordPage(total, [], null);
			}

			int count = Math.Min(limit, total - offset);
			MemoryRecordSnapshot[] records = new MemoryRecordSnapshot[count];
			for (int index = 0; index < count; index++)
			{
				records[index] = client.Tables.GetRecordAt(offset + index, token);
			}

			int next = offset + count;
			return new RecordPage(total, RecordEntries.Map(dispatch, CheatEngineToolNames.RecordList, records, token),
				next < total ? next : null);
		}, cancellationToken);
	}

	/// <summary>Reads records by their Client-issued identifiers.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordGet, Title = "Get address-list records", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Copy 1 to 256 records by their current Cheat Engine identifiers. Identifiers expire when a table load " +
		"reaches Cheat Engine; list records again after a table load. A missing id fails the whole read before any " +
		"host change. A pointer record carries its offsets in dereference order, in the signed hexadecimal form " +
		"record_update takes. With includeDropdown true each record also carries its dropdown list and options, in " +
		"the shape record_set_dropdown takes: at most 1024 items per record, and 4096 items and 1048576 bytes of " +
		"item text per call in id order, with truncated marking a list cut by these bounds.")]
	public RecordGetResult Get(
		[Description(
			"The distinct current record ids to copy, from 1 to 256 entries, from record_list, record_find or a mutation result.")]
		int[] ids,
		[Description("Whether to copy each record's dropdown list and options as dropdown; false by default.")]
		bool includeDropdown = false,
		CancellationToken cancellationToken = default)
	{
		RecordArguments.Batch(ids, "ids");
		ValidateUniqueIds(ids, "ids");
		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run<RecordGetResult>(CheatEngineToolNames.RecordGet, token =>
		{
			MemoryRecordSnapshot[] records =
				[.. ids.Select(id => client.Tables.GetRecord(new MemoryRecordId(id), token))];
			RecordDropdown[]? dropdowns = includeDropdown
				? RecordDropdowns.Read(dispatch, CheatEngineToolNames.RecordGet, ids, token)
				: null;
			return new RecordGetResult(RecordEntries.Map(dispatch, CheatEngineToolNames.RecordGet, records, token,
				dropdowns));
		}, cancellationToken);
	}

	/// <summary>Copies records without their dropdowns, as the live record resource projects them.</summary>
	/// <param name="ids">The distinct current record ids.</param>
	/// <param name="cancellationToken">The request cancellation.</param>
	/// <returns>The copied records in request order.</returns>
	public RecordGetResult Get(int[] ids, CancellationToken cancellationToken)
	{
		return Get(ids, false, cancellationToken);
	}

	/// <summary>Finds copied records of the whole address list using Client-side predicates.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordFind, Title = "Find address-list records", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Search every record of the address list, including records nested in groups at any depth, by one or more " +
		"predicates: case-insensitive description text, exact case-insensitive address expression, type or active " +
		"state. Cheat Engine copies the whole list, at most 4096 records with nested ones counted, before this " +
		"managed search; larger tables are refused rather than searched incompletely. Matches come in address-list " +
		"order, where a record is followed by the records nested below it; record_list with parentId shows a " +
		"group's immediate children.")]
	public RecordFindResult Find(
		[Description("A case-insensitive description substring, 1 to 1024 characters.")]
		string? descriptionContains = null,
		[Description("An exact case-insensitive Cheat Engine address expression, 1 to 1024 characters.")]
		string? address = null,
		[Description("An exact stored Cheat Engine value type as an integer: " + RecordArguments.StoredVariableTypes +
			".")]
		VariableType? variableType = null,
		[Description("Whether records must be active or inactive.")]
		bool? active = null,
		[Description("The most matching records to return, from 1 to 1000.")]
		int limit = 100,
		CancellationToken cancellationToken = default)
	{
		if (descriptionContains is null && address is null && variableType is null && active is null)
		{
			throw CheatEngineToolException.InvalidArgument("descriptionContains",
				"supply at least one of descriptionContains, address, variableType or active.");
		}

		if (descriptionContains is not null)
		{
			RecordArguments.Text(descriptionContains, "descriptionContains", RecordArguments.MaximumDescriptionLength,
				true);
		}

		if (address is not null)
		{
			RecordArguments.Text(address, "address", RecordArguments.MaximumAddressLength, true);
		}

		if (variableType is { } type)
		{
			RecordArguments.SearchType(type, "variableType");
		}

		if (limit < 1)
		{
			throw CheatEngineToolException.InvalidArgument("limit", "must be at least 1.");
		}

		if (limit > RecordArguments.MaximumListLimit)
		{
			throw CheatEngineToolException.LimitExceeded("limit",
				$"must be at most {RecordArguments.MaximumListLimit}.");
		}

		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run(CheatEngineToolNames.RecordFind, token =>
		{
			MemoryRecordSearch search = new(descriptionContains, address, variableType, active);
			MemoryRecordSnapshot[] found =
			[
				.. client.Tables.Find(search, new MemoryRecordCollectionRequest(RecordArguments.MaximumScannedRecords),
					token)
			];
			RecordEntry[] records = RecordEntries.Map(dispatch, CheatEngineToolNames.RecordFind, [.. found.Take(limit)],
				token);
			return new RecordFindResult(found.Length, found.Length > records.Length, records);
		}, cancellationToken);
	}

	/// <summary>Reads the current address-list selection.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordGetSelected, Title = "Get selected address-list record",
		ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Copy Cheat Engine's currently selected address-list record. selected is false when the list has no current selection; that is a normal state, not an error.")]
	public RecordSelection GetSelected(CancellationToken cancellationToken = default)
	{
		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run(CheatEngineToolNames.RecordGetSelected, token =>
		{
			if (client.Tables.TryGetSelectedRecord(out MemoryRecordSnapshot record, out CheatEngineFailure failure,
					token))
			{
				return new RecordSelection(true,
					RecordEntries.One(dispatch, CheatEngineToolNames.RecordGetSelected, record, token));
			}

			if (failure.IsDefault)
			{
				return new RecordSelection(false, null);
			}

			throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
		}, cancellationToken);
	}

	/// <summary>
	///     Copies one page of a record's immediate children at a cost that follows the page: the Client checks the
	///     parent id, a fixed Lua body returns the ids of the page, and the Client copies each child, which marks its
	///     id current. An id a later table load reused is stale to the Client and cannot be copied by id, so that
	///     page falls back to the bounded subtree copy, which renews every id below the parent.
	/// </summary>
	private RecordPage Children(MemoryRecordId parent, int offset, int limit, CancellationToken cancellationToken)
	{
		ICheatEngineClient client = dispatch.Client;
		client.Tables.GetRecord(parent, cancellationToken);
		RecordLuaChildren page = dispatch.ExecuteLua(CheatEngineToolNames.RecordList, RecordLuaScripts.ChildIds,
			RecordLuaJsonContext.Default.RecordLuaChildren, cancellationToken, parent.Value, offset, limit);
		int[] ids = page.Ids ?? [];
		if (ids.Length > limit || (ids.Length > 0 && offset + ids.Length > page.Total))
		{
			throw CheatEngineToolException.Internal("The child-id copy returned more ids than its page holds.");
		}

		MemoryRecordSnapshot[] records = new MemoryRecordSnapshot[ids.Length];
		for (int index = 0; index < ids.Length; index++)
		{
			if (!client.Tables.TryGetRecord(new MemoryRecordId(ids[index]), out MemoryRecordSnapshot child,
					out CheatEngineFailure failure, cancellationToken))
			{
				if (failure.Kind is CheatEngineFailureKind.InvalidState)
				{
					return SubtreeChildren(client, parent, offset, limit, cancellationToken);
				}

				throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
			}

			records[index] = child;
		}

		int next = offset + ids.Length;
		return new RecordPage(page.Total,
			RecordEntries.Map(dispatch, CheatEngineToolNames.RecordList, records, cancellationToken),
			ids.Length > 0 && next < page.Total ? next : null);
	}

	/// <summary>
	///     Copies one page of a record's immediate children from the bounded subtree copy, which makes every id below
	///     the parent current for later calls.
	/// </summary>
	private RecordPage SubtreeChildren(ICheatEngineClient client, MemoryRecordId parent, int offset, int limit,
		CancellationToken cancellationToken)
	{
		MemoryRecordHierarchySnapshot tree = client.Tables.GetHierarchy(parent,
			new MemoryRecordHierarchyRequest(RecordArguments.MaximumScannedRecords + 1,
				RecordArguments.MaximumHierarchyDepth), cancellationToken);
		PageSlice<MemoryRecordHierarchySnapshot> page =
			Paging.Slice(tree.Children, offset, limit, RecordArguments.MaximumListLimit);
		MemoryRecordSnapshot[] records = [.. page.Items.Select(static child => child.Record)];
		return new RecordPage(page.Total,
			RecordEntries.Map(dispatch, CheatEngineToolNames.RecordList, records, cancellationToken), page.NextOffset);
	}

	internal static void ValidateUniqueIds(int[] ids, string parameter)
	{
		HashSet<int> seen = [];
		for (int index = 0; index < ids.Length; index++)
		{
			RecordArguments.Id(ids[index], $"{parameter}[{index}]");
			if (!seen.Add(ids[index]))
			{
				throw CheatEngineToolException.InvalidArgument($"{parameter}[{index}]",
					"repeats an earlier record id.");
			}
		}
	}
}
