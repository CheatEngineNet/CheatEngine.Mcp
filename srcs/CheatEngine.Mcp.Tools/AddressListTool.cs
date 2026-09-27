using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Exposes bounded, Client-owned address-list operations.</summary>
[McpServerToolType]
public sealed class AddressListTool
{
	private const int MaximumRecords = 4096;
	private readonly ICheatEngineClient _client;

	public AddressListTool(ICheatEngineClient client)
	{
		_client = client;
	}

	[McpServerTool(Name = "get_address_list")]
	[Description("Copy the current top-level Cheat Engine address list.")]
	public object GetAddressList(
		[Description("Maximum records to materialize. The request fails if the table exceeds this bound.")]
		int maximumRecords = 256)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (maximumRecords is < 1 or > MaximumRecords)
			{
				return ToolExecution.Error($"maximumRecords must be between 1 and {MaximumRecords}.");
			}

			AddressTableSnapshot table = _client.Tables.GetSnapshot(new MemoryRecordCollectionRequest(maximumRecords));
			object[] records = [.. table.Records.Select(ToResponse)];
			return new { success = true, count = records.Length, records };
		});
	}

	[McpServerTool(Name = "add_memory_record")]
	[Description("Add a typed top-level memory record to the Cheat Engine address list.")]
	public object AddMemoryRecord(
		[Description("Display description.")] string description,
		[Description("Cheat Engine address expression, such as game.exe+10.")]
		string address,
		[Description("Initial Cheat Engine value text.")]
		string value,
		[Description("Cheat Engine variable type.")]
		VariableType variableType = VariableType.Dword)
	{
		return ToolExecution.Run(_client, () =>
		{
			MemoryRecordSnapshot record =
				_client.Tables.Create(new MemoryRecordDefinition(description, address, value, variableType));
			return new { success = true, record = ToResponse(record) };
		});
	}

	[McpServerTool(Name = "update_memory_record")]
	[Description("Update one memory record by Client-issued record ID.")]
	public object UpdateMemoryRecord(
		[Description("Client-issued memory-record ID from get_address_list or add_memory_record.")]
		int id,
		[Description("Replacement display description, if any.")]
		string? description = null,
		[Description("Replacement address expression, if any.")]
		string? address = null,
		[Description("Replacement value text, if any.")]
		string? value = null,
		[Description("Replacement Cheat Engine variable type, if any.")]
		VariableType? variableType = null)
	{
		return ToolExecution.Run(_client, () =>
		{
			MemoryRecordId recordId = new(id);
			MemoryRecordSnapshot record =
				description is null && address is null && value is null && variableType is null
					? _client.Tables.GetRecord(recordId)
					: _client.Tables.Update(recordId,
						new MemoryRecordUpdate(description, address, value, variableType));
			return new { success = true, record = ToResponse(record) };
		});
	}

	[McpServerTool(Name = "set_memory_record_active")]
	[Description("Activate or deactivate one memory record by Client-issued record ID.")]
	public object SetMemoryRecordActive(
		[Description("Client-issued memory-record ID.")]
		int id,
		[Description("Requested active state.")]
		bool active)
	{
		return ToolExecution.Run(_client, () =>
		{
			MemoryRecordSnapshot record = _client.Tables.SetActive(new MemoryRecordId(id), active);
			return new { success = true, record = ToResponse(record) };
		});
	}

	[McpServerTool(Name = "delete_memory_record")]
	[Description("Delete one memory record by Client-issued record ID.")]
	public object DeleteMemoryRecord([Description("Client-issued memory-record ID.")] int id)
	{
		return ToolExecution.Run(_client, () =>
		{
			_client.Tables.Delete(new MemoryRecordId(id));
			return new { success = true };
		});
	}

	private static object ToResponse(MemoryRecordSnapshot record)
	{
		return new
		{
			id = record.Id.Value,
			index = record.Index,
			description = record.Content.Description,
			address = record.Content.AddressExpression,
			value = record.Content.Value,
			variableType = record.Content.VariableType.ToString(),
			offsetCount = record.Content.OffsetCount,
			active = record.State.IsActive,
			currentAddress = record.State.CurrentAddress is { } address ? $"0x{address.Value:X}" : null,
			childCount = record.State.ChildCount
		};
	}
}
