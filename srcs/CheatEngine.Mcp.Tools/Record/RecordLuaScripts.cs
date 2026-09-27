namespace CheatEngine.Mcp.Tools.Record;

/// <summary>Fixed Lua bodies for record fields unavailable through the typed Client table API.</summary>
internal static class RecordLuaScripts
{
	/// <summary>Stores Auto Assembler text only after checking that the record remains an Auto Assembler record.</summary>
	internal const string SetScript = """
	                                  local list = getAddressList()
	                                  if list == nil then return mcp.err('host_refused', 'Cheat Engine has no address list.', 'not_started') end
	                                  local record = list.getMemoryRecordByID(a[1])
	                                  if record == nil then return mcp.err('not_found', 'The address-list record no longer exists.', 'not_started') end
	                                  if record.Type ~= vtAutoAssembler then return mcp.err('invalid_state', 'The address-list record is not an Auto Assembler record.', 'not_started') end
	                                  record.Script = a[2]
	                                  return {id = record.ID}
	                                  """;

	/// <summary>Replaces pointer offsets in the v2 dereference order.</summary>
	internal const string SetOffsets = """
	                                   local list = getAddressList()
	                                   if list == nil then return mcp.err('host_refused', 'Cheat Engine has no address list.', 'not_started') end
	                                   local record = list.getMemoryRecordByID(a[1])
	                                   if record == nil then return mcp.err('not_found', 'The address-list record no longer exists.', 'not_started') end
	                                   if record.Type ~= vtPointer then return mcp.err('invalid_state', 'The address-list record is not a pointer record.', 'not_started') end
	                                   record.setOffsetCount(#a[2])
	                                   for i = 1, #a[2] do
	                                       record.setOffset(#a[2] - i, a[2][i])
	                                   end
	                                   return {id = record.ID}
	                                   """;
}
