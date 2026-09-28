namespace CheatEngine.Mcp.Tools.Record;

/// <summary>Fixed Lua bodies for record fields unavailable through the typed Client table API.</summary>
internal static class RecordLuaScripts
{
	/// <summary>
	///     Creates one address-list record from its description <c>a[1]</c>, address expression <c>a[2]</c> and value
	///     type <c>a[3]</c>, and never writes a value: the typed Client assigns a value while it creates a record, and
	///     Cheat Engine's SetValue refuses an empty value for a number type and writes an empty string for a string
	///     type. A record whose type Cheat Engine did not keep is removed again before the refusal is reported.
	/// </summary>
	internal const string CreateRecord = """
	                                     local list = getAddressList()
	                                     if list == nil then return mcp.err('host_refused', 'Cheat Engine has no address list.', 'not_started') end
	                                     local record = list.createMemoryRecord()
	                                     if record == nil then return mcp.err('host_refused', 'Cheat Engine did not create an address-list record.', 'not_started') end
	                                     local configured = pcall(function()
	                                         record.Description = a[1]
	                                         record.Address = a[2]
	                                         record.Type = a[3]
	                                     end)
	                                     if configured and record.Type == a[3] then return {id = record.ID} end
	                                     if pcall(function() record.destroy() end) then
	                                         return mcp.err('host_refused', 'Cheat Engine refused the description, address or type of the new record, which was removed.', 'not_started')
	                                     end
	                                     return mcp.err('partial_effect', 'Cheat Engine refused the new record and did not remove it; inspect the address list before retrying.', 'started')
	                                     """;

	/// <summary>
	///     Stores Auto Assembler text only after checking that the record remains an inactive Auto Assembler record.
	///     Cheat Engine disables an active script by running the current text's disable section with the allocations
	///     and symbols of the enable that ran, so new text on an active or activating record would disable the wrong
	///     code.
	/// </summary>
	internal const string SetScript = """
	                                  local list = getAddressList()
	                                  if list == nil then return mcp.err('host_refused', 'Cheat Engine has no address list.', 'not_started') end
	                                  local record = list.getMemoryRecordByID(a[1])
	                                  if record == nil then return mcp.err('not_found', 'The address-list record no longer exists.', 'not_started') end
	                                  if record.Type ~= vtAutoAssembler then return mcp.err('invalid_state', 'The address-list record is not an Auto Assembler record.', 'not_started') end
	                                  if record.Active or record.AsyncProcessing then
	                                      return mcp.err('invalid_state', 'The Auto Assembler record is active or activating; Cheat Engine would disable it with the new script and the old allocations.', 'not_started', 'Deactivate the record with record_set_active, then set its script.')
	                                  end
	                                  record.Script = a[2]
	                                  return {id = record.ID}
	                                  """;

	/// <summary>
	///     Sets the layout of record <c>a[1]</c>: for a string record the length <c>a[2]</c> in characters and the
	///     UTF-16 flag <c>a[3]</c>, for a byte array record hexadecimal display and the length <c>a[2]</c> in bytes,
	///     raised to the byte count <c>a[4]</c> of the value about to be written; a <c>nil</c> argument keeps its
	///     setting. Cheat Engine shows and parses a byte array as decimal numbers unless its hexadecimal display is on,
	///     a string or byte array record reads nothing until it has a length, and Cheat Engine's SetValue lengthens a
	///     byte array only for a value more than 4 bytes longer than the record, so the tool sets the length itself.
	///     The layout is read back so that a refused assignment is reported.
	/// </summary>
	internal const string SetLayout = """
	                                  local list = getAddressList()
	                                  if list == nil then return mcp.err('host_refused', 'Cheat Engine has no address list.', 'not_started') end
	                                  local record = list.getMemoryRecordByID(a[1])
	                                  if record == nil then return mcp.err('not_found', 'The address-list record no longer exists.', 'not_started') end
	                                  if record.Type == vtString then
	                                      if a[3] ~= nil then record.String.Unicode = a[3] end
	                                      if a[2] ~= nil then record.String.Size = a[2] end
	                                      if (a[3] ~= nil and record.String.Unicode ~= a[3]) or (a[2] ~= nil and record.String.Size ~= a[2]) then
	                                          return mcp.err('host_refused', 'Cheat Engine did not store the string length or encoding; read the record before retrying.', 'started')
	                                      end
	                                  elseif record.Type == vtByteArray then
	                                      if a[3] ~= nil then return mcp.err('invalid_state', 'Only a string record has a UTF-16 option.', 'not_started') end
	                                      record.ShowAsHex = true
	                                      local size = a[2] or record.Aob.Size
	                                      if a[4] ~= nil and a[4] > size then size = a[4] end
	                                      if record.Aob.Size ~= size then record.Aob.Size = size end
	                                      if record.ShowAsHex ~= true or record.Aob.Size ~= size then
	                                          return mcp.err('host_refused', 'Cheat Engine did not store the byte count or hexadecimal display; read the record before retrying.', 'started')
	                                      end
	                                  elseif a[2] ~= nil or a[3] ~= nil then
	                                      return mcp.err('invalid_state', 'Only a string or byte array record has a length, and only a string record a UTF-16 option.', 'not_started')
	                                  end
	                                  return {id = record.ID}
	                                  """;

	/// <summary>
	///     Replaces the pointer offsets of any record except an Auto Assembler record. <c>a[2]</c> lists them in the v2
	///     dereference order, nearest the base first; Cheat Engine applies <c>Offset[count - 1]</c> first and
	///     <c>Offset[0]</c> last, so the first listed offset is stored at the highest index. An empty list removes the
	///     pointer chain. The offsets are read back so that a refused or truncated assignment is reported.
	/// </summary>
	internal const string SetOffsets = """
	                                   local list = getAddressList()
	                                   if list == nil then return mcp.err('host_refused', 'Cheat Engine has no address list.', 'not_started') end
	                                   local record = list.getMemoryRecordByID(a[1])
	                                   if record == nil then return mcp.err('not_found', 'The address-list record no longer exists.', 'not_started') end
	                                   if record.Type == vtAutoAssembler then return mcp.err('invalid_state', 'An Auto Assembler record has no pointer offsets.', 'not_started') end
	                                   local offsets = a[2]
	                                   local count = #offsets
	                                   record.setOffsetCount(count)
	                                   for i = 1, count do
	                                       record.setOffset(count - i, offsets[i])
	                                   end
	                                   if record.getOffsetCount() ~= count then
	                                       return mcp.err('host_refused', 'Cheat Engine did not store every pointer offset; read the record before retrying.', 'started')
	                                   end
	                                   for i = 1, count do
	                                       if record.getOffset(count - i) ~= offsets[i] then
	                                           return mcp.err('host_refused', 'Cheat Engine stored a different pointer offset; read the record before retrying.', 'started')
	                                       end
	                                   end
	                                   return {id = record.ID}
	                                   """;

	/// <summary>
	///     Copies the stored offset texts of every record in <c>a[1]</c>, in request order and in the v2 dereference
	///     order, nearest the base first, which is the reverse of Cheat Engine's <c>OffsetText</c> indexes. It reads
	///     the stored texts only, so an offset kept as a symbol or Lua expression is copied without being evaluated.
	///     The whole copy stops at <c>a[2]</c> offsets and <c>a[3]</c> bytes of text; a list it cut is shorter than the
	///     offset count.
	/// </summary>
	internal const string ReadOffsets = """
	                                    local list = getAddressList()
	                                    if list == nil then return mcp.err('host_refused', 'Cheat Engine has no address list.', 'not_started') end
	                                    local itemBudget = a[2]
	                                    local byteBudget = a[3]
	                                    local records = {}
	                                    for r = 1, #a[1] do
	                                        local record = list.getMemoryRecordByID(a[1][r])
	                                        if record == nil then return mcp.err('not_found', 'The address-list record no longer exists.', 'not_started') end
	                                        local offsets = {}
	                                        for i = record.OffsetCount - 1, 0, -1 do
	                                            local text = record.OffsetText[i] or ''
	                                            if itemBudget <= 0 or #text > byteBudget then break end
	                                            itemBudget = itemBudget - 1
	                                            byteBudget = byteBudget - #text
	                                            offsets[#offsets + 1] = text
	                                        end
	                                        records[r] = {id = record.ID, offsets = offsets}
	                                    end
	                                    return {records = records}
	                                    """;

	/// <summary>
	///     Reports, for every record in <c>a[1]</c>, whether Cheat Engine would pass a change of that record on to a
	///     nested record whose type is listed in <c>a[3]</c>. Cheat Engine's SetValue and setActive pass a change to
	///     every child of a record whose <c>Options</c> contain the option <c>a[2]</c>, and each child passes it on the
	///     same way, so the walk follows exactly those children. It examines at most <c>a[4]</c> children in the whole
	///     call; a record whose walk runs out of that budget is reported as reaching one.
	/// </summary>
	internal const string Reaches = """
	                                local list = getAddressList()
	                                if list == nil then return mcp.err('host_refused', 'Cheat Engine has no address list.', 'not_started') end
	                                local option = ',' .. a[2] .. ','
	                                local wanted = {}
	                                for i = 1, #a[3] do wanted[a[3][i]] = true end
	                                local budget = a[4]
	                                local function follows(record)
	                                    local options = record.Options
	                                    if type(options) ~= 'string' then return false end
	                                    local names = string.gsub(options, '[%[%]%s]', '')
	                                    return string.find(',' .. names .. ',', option, 1, true) ~= nil
	                                end
	                                local reaches = {}
	                                for r = 1, #a[1] do
	                                    local record = list.getMemoryRecordByID(a[1][r])
	                                    if record == nil then return mcp.err('not_found', 'The address-list record no longer exists.', 'not_started') end
	                                    local found = false
	                                    local pending = {}
	                                    if follows(record) then pending[1] = record end
	                                    while not found and #pending > 0 do
	                                        local parent = table.remove(pending)
	                                        for i = 0, parent.Count - 1 do
	                                            budget = budget - 1
	                                            local child = parent.Child[i]
	                                            if budget < 0 or (child ~= nil and wanted[child.Type]) then
	                                                found = true
	                                                break
	                                            end
	                                            if child ~= nil and follows(child) then
	                                                pending[#pending + 1] = child
	                                            end
	                                        end
	                                    end
	                                    reaches[r] = found
	                                end
	                                return {reaches = reaches}
	                                """;

	/// <summary>
	///     Sets record <c>a[1]</c> active or inactive as <c>a[2]</c> asks and reports why Cheat Engine refused an
	///     activation. Cheat Engine 7.7 calls a record's <c>OnActivationFailure(memrec, reason, reasonText)</c> only
	///     when an activation fails, with <c>reason</c> an integer of its <c>TFailReason</c> (0 <c>afUnknown</c> to 11
	///     <c>afDLLInjectionFailure</c>), and retries while the handler returns true. When the record has no handler or
	///     a Lua function as its handler, a handler that records the last reason is put in front of it for the
	///     activation; the record's own handler still runs for at most <c>a[4]</c> calls and is assigned back before
	///     the result is returned, unless it installed another handler while it ran, which is kept. Any other handler
	///     value is left untouched and its failure reason is not captured. The reason text is cut at <c>a[3]</c> bytes
	///     without splitting a UTF-8 sequence.
	/// </summary>
	internal const string SetActive = """
	                                  local list = getAddressList()
	                                  if list == nil then return mcp.err('host_refused', 'Cheat Engine has no address list.', 'not_started') end
	                                  local record = list.getMemoryRecordByID(a[1])
	                                  if record == nil then return mcp.err('not_found', 'The address-list record no longer exists.', 'not_started') end
	                                  local reason = nil
	                                  local text = nil
	                                  local own = nil
	                                  local capture = nil
	                                  if a[2] then
	                                      local calls = 0
	                                      local read, current = pcall(function() return record.OnActivationFailure end)
	                                      if read and (current == nil or type(current) == 'function') then
	                                          own = current
	                                          capture = function(memrec, why, message)
	                                              reason = why
	                                              text = message
	                                              calls = calls + 1
	                                              if own ~= nil and calls <= a[4] then return own(memrec, why, message) == true end
	                                              return false
	                                          end
	                                          if not pcall(function() record.OnActivationFailure = capture end) then capture = nil end
	                                      end
	                                  end
	                                  local changed = pcall(function() record.Active = a[2] end)
	                                  if capture ~= nil then
	                                      local read, current = pcall(function() return record.OnActivationFailure end)
	                                      if (not read or current == capture) and not pcall(function() record.OnActivationFailure = own end) then
	                                          return mcp.err('host_refused', 'Cheat Engine did not restore the record\'s own OnActivationFailure handler; read the record before retrying.', 'started')
	                                      end
	                                  end
	                                  if not changed then
	                                      return mcp.err('host_refused', 'Cheat Engine raised an error while it changed the record state; read the record before retrying.', 'unknown')
	                                  end
	                                  if type(reason) ~= 'number' then reason = nil end
	                                  if type(text) ~= 'string' or text == '' then
	                                      text = nil
	                                  elseif #text > a[3] then
	                                      text = string.sub(text, 1, a[3])
	                                      local lead = #text
	                                      while lead > 1 and string.byte(text, lead) >= 128 and string.byte(text, lead) < 192 do lead = lead - 1 end
	                                      local first = string.byte(text, lead)
	                                      local size = first >= 240 and 4 or first >= 224 and 3 or first >= 192 and 2 or 1
	                                      if lead + size - 1 > #text then text = string.sub(text, 1, lead - 1) end
	                                  end
	                                  return {active = record.Active == true, pending = record.AsyncProcessing == true, reason = reason, text = text}
	                                  """;

	/// <summary>
	///     Copies the ids of one page of the immediate children of record <c>a[1]</c>: <c>a[3]</c> ids at most,
	///     from the zero-based position <c>a[2]</c>, and the parent's child count. The cost follows the page, not
	///     the subtree.
	/// </summary>
	internal const string ChildIds = """
	                                 local list = getAddressList()
	                                 if list == nil then return mcp.err('host_refused', 'Cheat Engine has no address list.', 'not_started') end
	                                 local record = list.getMemoryRecordByID(a[1])
	                                 if record == nil then return mcp.err('not_found', 'The address-list record no longer exists.', 'not_started') end
	                                 local total = record.Count
	                                 local ids = {}
	                                 for index = a[2], math.min(total, a[2] + a[3]) - 1 do
	                                     local child = record.Child[index]
	                                     if child == nil then
	                                         return mcp.err('host_refused', 'Cheat Engine returned no child record at a position below its child count.', 'not_started')
	                                     end
	                                     ids[#ids + 1] = child.ID
	                                 end
	                                 return {total = total, ids = ids}
	                                 """;

	/// <summary>
	///     Copies the dropdown of every record in <c>a[1]</c>, in order, through Cheat Engine's own accessors, which
	///     split each <c>value:description</c> line at its first colon and follow a link to another record's list. A
	///     record's copy stops at <c>a[2]</c> items, and the whole copy at <c>a[3]</c> items and <c>a[4]</c> bytes of
	///     value and description text; <c>truncated</c> marks every list cut by a bound.
	/// </summary>
	internal const string ReadDropdowns = """
	                                      local list = getAddressList()
	                                      if list == nil then return mcp.err('host_refused', 'Cheat Engine has no address list.', 'not_started') end
	                                      local itemBudget = a[3]
	                                      local byteBudget = a[4]
	                                      local records = {}
	                                      for r = 1, #a[1] do
	                                          local record = list.getMemoryRecordByID(a[1][r])
	                                          if record == nil then return mcp.err('not_found', 'The address-list record no longer exists.', 'not_started') end
	                                          local count = record.DropDownCount
	                                          local items = {}
	                                          local truncated = false
	                                          for i = 0, count - 1 do
	                                              if #items >= a[2] or itemBudget <= 0 then
	                                                  truncated = true
	                                                  break
	                                              end
	                                              local value = record.DropDownValue[i] or ''
	                                              local description = record.DropDownDescription[i] or ''
	                                              local size = #value + #description
	                                              if size > byteBudget then
	                                                  truncated = true
	                                                  break
	                                              end
	                                              itemBudget = itemBudget - 1
	                                              byteBudget = byteBudget - size
	                                              items[#items + 1] = {value = value, description = description}
	                                          end
	                                          local linkedTo = nil
	                                          if record.DropDownLinked then linkedTo = record.DropDownLinkedMemrec or '' end
	                                          records[r] = {id = record.ID, dropdown = {items = items, itemCount = count, truncated = truncated,
	                                              disallowManualInput = record.DropDownReadOnly == true, descriptionOnly = record.DropDownDescriptionOnly == true,
	                                              displayAsListItem = record.DisplayAsDropDownListItem == true, linkedTo = linkedTo}}
	                                      end
	                                      return {records = records}
	                                      """;

	/// <summary>
	///     Replaces the dropdown list of record <c>a[1]</c> with the <c>value:description</c> lines in <c>a[2]</c> and
	///     sets the DropDownReadOnly, DropDownDescriptionOnly and DisplayAsDropDownListItem options from <c>a[3]</c> to
	///     <c>a[5]</c>, keeping an option whose argument is <c>nil</c>. It refuses an Auto Assembler record and a
	///     record whose list is linked to another record's list, because Cheat Engine would ignore the record's own
	///     list and options, and reads the list back so a refused or changed line is reported.
	/// </summary>
	internal const string SetDropdown = """
	                                    local list = getAddressList()
	                                    if list == nil then return mcp.err('host_refused', 'Cheat Engine has no address list.', 'not_started') end
	                                    local record = list.getMemoryRecordByID(a[1])
	                                    if record == nil then return mcp.err('not_found', 'The address-list record no longer exists.', 'not_started') end
	                                    if record.Type == vtAutoAssembler then return mcp.err('invalid_state', 'An Auto Assembler record has no value to choose from a dropdown list.', 'not_started') end
	                                    if record.DropDownLinked then
	                                        return mcp.err('invalid_state', 'The record uses the dropdown list of another record.', 'not_started', 'Read the record with record_get and includeDropdown true, then change the record that linkedTo names.')
	                                    end
	                                    local lines = a[2]
	                                    local items = record.DropDownList
	                                    items.clear()
	                                    for i = 1, #lines do
	                                        items.add(lines[i])
	                                    end
	                                    if a[3] ~= nil then record.DropDownReadOnly = a[3] end
	                                    if a[4] ~= nil then record.DropDownDescriptionOnly = a[4] end
	                                    if a[5] ~= nil then record.DisplayAsDropDownListItem = a[5] end
	                                    if items.Count ~= #lines then
	                                        return mcp.err('host_refused', 'Cheat Engine did not store every dropdown item; read the record before retrying.', 'started')
	                                    end
	                                    for i = 1, #lines do
	                                        if items.getString(i - 1) ~= lines[i] then
	                                            return mcp.err('host_refused', 'Cheat Engine stored a different dropdown item; read the record before retrying.', 'started')
	                                        end
	                                    end
	                                    return {id = record.ID}
	                                    """;
}
