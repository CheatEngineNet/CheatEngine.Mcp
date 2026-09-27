using System.ComponentModel;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Record;

/// <summary>Clears the current address list through a fixed Client Lua call.</summary>
[McpServerToolType]
public sealed class RecordClearTools(ToolDispatch dispatch)
{
	internal const string ClearScript = """
	                                    local list = getAddressList()
	                                    if list == nil then return mcp.err('host_refused', 'Cheat Engine has no address list.', 'not_started') end
	                                    if list.Count > 100000 then
	                                        return mcp.err('limit_exceeded', 'The address list exceeds 100000 records; it was not cleared.', 'not_started')
	                                    end
	                                    local queue = {}
	                                    local last = 0
	                                    for i = 0, list.Count - 1 do
	                                        last = last + 1
	                                        queue[last] = list.getMemoryRecord(i)
	                                    end
	                                    local index = 1
	                                    while index <= last do
	                                        if last > 100000 then
	                                            return mcp.err('limit_exceeded', 'The address list exceeds 100000 records; it was not cleared.', 'not_started')
	                                        end
	                                    local record = queue[index]
	                                    if record.Type == vtAutoAssembler and record.Active and not a[1] then
	                                        return mcp.err('capability_disabled', 'An active Auto Assembler record requires Mcp:EnableAutoAssembler before the list can be cleared.', 'not_started')
	                                    end
	                                        for child = 0, record.Count - 1 do
	                                            last = last + 1
	                                            queue[last] = record.Child[child]
	                                        end
	                                        index = index + 1
	                                    end
	                                    local ok = pcall(function() list.clear() end)
	                                    if not ok then return mcp.err('host_refused', 'Cheat Engine could not clear the address list; inspect it before retrying.', 'unknown') end
	                                    if list.Count ~= 0 then return mcp.err('partial_effect', 'Some address-list records remain after clear.', 'started') end
	                                    return {deleted = last}
	                                    """;

	/// <summary>Deletes every address-list record, including child records.</summary>
	[McpServerTool(Name = CheatEngineToolNames.RecordClear, Title = "Clear address list", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Delete every Cheat Engine address-list record and return the number removed, including nested child records. Refuses lists above 100000 records before changing them. Active scripts may run their disable sections during clear.")]
	public RecordClearResult Clear(CancellationToken cancellationToken = default)
	{
		return dispatch.RunLua(CheatEngineToolNames.RecordClear, ClearScript,
			RecordJsonContext.Default.RecordClearResult, cancellationToken,
			dispatch.Features.IsEnabled(McpFeature.AutoAssembler));
	}
}

/// <summary>The number of records removed by a complete address-list clear.</summary>
public sealed record RecordClearResult(
	[property: Description("The removed records, including children.")]
	int Deleted);
