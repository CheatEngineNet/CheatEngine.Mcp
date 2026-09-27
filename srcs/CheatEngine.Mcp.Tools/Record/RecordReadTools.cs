using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Client.Tables;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Record;

/// <summary>Reads bounded copied address-list records through the typed Client table API.</summary>
[McpServerToolType]
public sealed class RecordReadTools(ToolDispatch dispatch)
{
	/// <summary>Pages the top-level address-list records.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordList, Title = "List address-list records", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Page the top-level Cheat Engine address list in its current order. Each record includes its childCount; use record_get for a specific record after choosing it here. The total can change when the user or another plugin edits the table.")]
	public RecordPage List(
		[Description("The zero-based top-level record index where this page starts.")]
		int offset = 0,
		[Description("The maximum number of records to copy, from 1 to 1000.")]
		int limit = 100,
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
		return dispatch.Run(CheatEngineToolNames.RecordList, token =>
		{
			int total = client.Tables.GetRecordCount(token);
			if (offset >= total)
			{
				return new RecordPage(total, [], null);
			}

			int count = Math.Min(limit, total - offset);
			RecordEntry[] records = new RecordEntry[count];
			for (int index = 0; index < count; index++)
			{
				records[index] = RecordArguments.Entry(client.Tables.GetRecordAt(offset + index, token));
			}

			int next = offset + count;
			return new RecordPage(total, records, next < total ? next : null);
		}, cancellationToken);
	}

	/// <summary>Reads records by their Client-issued identifiers.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordGet, Title = "Get address-list records", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Copy 1 to 256 records by their current Cheat Engine identifiers. Identifiers expire when a table load reaches Cheat Engine; list records again after a table load. A missing id fails the whole read before any host change.")]
	public RecordGetResult Get(
		[Description("The current record ids to copy, from record_list, record_find or a mutation result.")]
		int[] ids,
		CancellationToken cancellationToken = default)
	{
		RecordArguments.Batch(ids, "ids");
		ValidateUniqueIds(ids, "ids");
		ICheatEngineClient client = dispatch.Client;
		return dispatch.Run<RecordGetResult>(CheatEngineToolNames.RecordGet, token =>
				new RecordGetResult([
					.. ids.Select(id => RecordArguments.Entry(client.Tables.GetRecord(new MemoryRecordId(id), token)))
				]),
			cancellationToken);
	}

	/// <summary>Finds copied top-level records using Client-side predicates.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordFind, Title = "Find address-list records", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Search top-level records by one or more predicates: case-insensitive description text, exact case-insensitive address expression, type or active state. Cheat Engine copies at most 4096 top-level records before this managed search; larger tables are refused rather than searched incompletely.")]
	public RecordFindResult Find(
		[Description("A case-insensitive description substring, 1 to 1024 characters.")]
		string? descriptionContains = null,
		[Description("An exact case-insensitive Cheat Engine address expression, 1 to 1024 characters.")]
		string? address = null,
		[Description("An exact Cheat Engine record type.")]
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
			RecordArguments.Type(type, "variableType");
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
			RecordEntry[] records = [.. found.Take(limit).Select(RecordArguments.Entry)];
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
				return new RecordSelection(true, RecordArguments.Entry(record));
			}

			if (failure.IsDefault)
			{
				return new RecordSelection(false, null);
			}

			throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
		}, cancellationToken);
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
