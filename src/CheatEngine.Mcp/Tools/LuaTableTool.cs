using System.ComponentModel;

using CheatEngine.Client;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Supplies address-list details unavailable through copied CheatEngine.Client table snapshots.</summary>
[McpServerToolType]
public sealed class LuaTableTool
{
	private const int MaximumOffsets = 128;
	private const int MaximumTextLength = 1_048_576;
	private readonly ICheatEngineClient _client;

	public LuaTableTool(ICheatEngineClient client)
	{
		_client = client;
	}

	[McpServerTool(Name = "get_memory_record_details"), Description("Read pointer offsets and script text for one current Cheat Engine memory-record ID.")]
	public object GetMemoryRecordDetails([Description("Current Cheat Engine memory-record ID. ")] int id)
	{
		if (id < 0)
		{
			return ToolExecution.Error("id must be non-negative.");
		}

		return Invoke("get_memory_record_details", """
			local r=getAddressList().getMemoryRecordByID(a[1]); if r==nil then error('Memory record not found') end; local count=r.getOffsetCount(); local offsets={}; local limit=math.min(count,128); for i=0,limit-1 do offsets[#offsets+1]=r.getOffset(i) end
			return {id=r.ID,index=r.Index,description=r.Description,address=r.getAddress(),offsetCount=count,truncated=count>limit,offsets=offsets,script=r.Script,active=r.Active,currentAddress=r.getCurrentAddress()}
			""", id);
	}

	[McpServerTool(Name = "set_memory_record_offsets"), Description("Replace a memory record's pointer offsets. Offsets are Cheat Engine internal order, index zero nearest the final value.")]
	public object SetMemoryRecordOffsets([Description("Current Cheat Engine memory-record ID. ")] int id,
		[Description("Pointer offsets in Cheat Engine internal order, up to 128 entries. ")] long[] offsets)
	{
		if (id < 0 || offsets is null || offsets.Length > MaximumOffsets)
		{
			return ToolExecution.Error("id must be non-negative and offsets must contain at most 128 entries.");
		}

		return Invoke("set_memory_record_offsets", """
			local r=getAddressList().getMemoryRecordByID(a[1]); if r==nil then error('Memory record not found') end; r.setOffsetCount(#a[2]); for i=1,#a[2] do r.setOffset(i-1,a[2][i]) end; return {id=r.ID,offsetCount=r.getOffsetCount()}
			""", id, offsets);
	}

	[McpServerTool(Name = "get_selected_memory_record"), Description("Read the currently selected Cheat Engine address-list record with pointer-offset details.")]
	public object GetSelectedMemoryRecord()
	{
		return Invoke("get_selected_memory_record", """
			local r=getAddressList().getSelectedRecord(); if r==nil then return {selected=false} end; local offsets={}; local count=r.getOffsetCount(); for i=0,math.min(count,128)-1 do offsets[#offsets+1]=r.getOffset(i) end; return {selected=true,id=r.ID,index=r.Index,description=r.Description,address=r.getAddress(),offsetCount=count,truncated=count>128,offsets=offsets}
			""");
	}

	[McpServerTool(Name = "select_memory_record"), Description("Select one current Cheat Engine address-list record by ID.")]
	public object SelectMemoryRecord([Description("Current Cheat Engine memory-record ID. ")] int id)
	{
		if (id < 0)
		{
			return ToolExecution.Error("id must be non-negative.");
		}

		return Invoke("select_memory_record", """
			local list=getAddressList(); local r=list.getMemoryRecordByID(a[1]); if r==nil then error('Memory record not found') end; list.setSelectedRecord(r); return {id=r.ID,selected=true}
			""", id);
	}

	[McpServerTool(Name = "set_memory_record_script"), Description("Set a bounded Auto Assembler script on a memory record. The record persists it in the cheat table.")]
	public object SetMemoryRecordScript([Description("Current Cheat Engine memory-record ID. ")] int id,
		[Description("Auto Assembler script text, up to 1 MiB. ")] string script)
	{
		if (id < 0 || script is null || script.Length > MaximumTextLength)
		{
			return ToolExecution.Error("id must be non-negative and script must contain at most 1048576 characters.");
		}

		return Invoke("set_memory_record_script", """
			local r=getAddressList().getMemoryRecordByID(a[1]); if r==nil then error('Memory record not found') end; r.Script=a[2]; return {id=r.ID,scriptLength=#a[2]}
			""", id, script);
	}

	[McpServerTool(Name = "find_memory_records_by_description"), Description("Find up to 1024 current address-list records matching an exact description.")]
	public object FindMemoryRecordsByDescription([Description("Exact record description, up to 256 characters. ")] string description,
		[Description("Maximum records to return (1-1024). ")] int maximumResults = 256)
	{
		if (string.IsNullOrWhiteSpace(description) || description.Length > 256 || maximumResults is < 1 or > 1024)
		{
			return ToolExecution.Error("description is required and maximumResults must be between 1 and 1024.");
		}

		return Invoke("find_memory_records_by_description", """
			local records=getAddressList().getMemoryRecordsWithDescription(a[1]) or {}; local items={}; local count=#records; for i=1,math.min(count,a[2]) do local r=records[i]; items[#items+1]={id=r.ID,index=r.Index,description=r.Description,address=r.getAddress(),active=r.Active} end; return {count=count,truncated=count>a[2],records=items}
			""", description, maximumResults);
	}

	private object Invoke(string operation, string body, params object?[] arguments) => LuaToolRuntime.Invoke(_client, operation, body, arguments);
}
