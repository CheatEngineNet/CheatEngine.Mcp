namespace CheatEngine.Mcp.Tools.Code;

/// <summary>Fixed, bounded Lua bodies for Cheat Engine code-view APIs absent from the typed Client.</summary>
internal static class CodeScripts
{
	internal const string DisassemblyColumns = """
	                                          local text = disassemble(a[1])
	                                          local extra, opcode, bytes, addressText = splitDisassembledString(text)
	                                          if type(addressText) ~= 'string' or type(opcode) ~= 'string' or type(extra) ~= 'string' then
	                                              return mcp.err('host_refused', 'Cheat Engine returned invalid disassembly columns.', 'completed')
	                                          end
	                                          return { addressText=addressText, opcode=opcode, extra=extra }
	                                          """;

	internal const string DisassembleBytes = """
	                                         local origin = 0
	                                         if a[2] ~= nil then
	                                             origin = getAddressSafe(a[2])
	                                             if origin == nil then return mcp.err('invalid_argument', 'origin could not be resolved.', 'not_started') end
	                                         end
	                                         local text = disassembleBytes(a[1], origin)
	                                         if type(text) ~= 'string' then return mcp.err('host_refused', 'Cheat Engine did not return a byte disassembly.', 'completed') end
	                                         return { origin=string.format('%X', origin), text=text }
	                                         """;

	internal const string GetFunction = """
	                                    local address = getAddressSafe(a[1])
	                                    if address == nil then return mcp.err('invalid_argument', 'address could not be resolved.', 'not_started') end
	                                    local startAddress, endAddress = getFunctionRange(address)
	                                    local found = startAddress ~= nil and endAddress ~= nil and endAddress >= startAddress
	                                    local jump = isJumpDestination(address, a[2]) == true
	                                    if not found then return { found=false, address=string.format('%X', address), addressIsJumpDestination=jump } end
	                                    return { found=true, address=string.format('%X', address), startAddress=string.format('%X', startAddress), endAddress=string.format('%X', endAddress), size=endAddress-startAddress, addressIsJumpDestination=jump }
	                                    """;

	internal const string Dissect = """
	                                local address = getAddressSafe(a[1])
	                                if address == nil then return mcp.err('invalid_argument', 'address could not be resolved.', 'not_started') end
	                                local dissect = getDissectCode()
	                                if dissect == nil then return mcp.err('host_refused', 'Cheat Engine has no code dissector.', 'not_started') end
	                                dissect.dissect(address, a[2])
	                                return { address=string.format('%X', address), size=a[2] }
	                                """;

	internal const string FindReferences = """
	                                       local address = getAddressSafe(a[1])
	                                       if address == nil then return mcp.err('invalid_argument', 'address could not be resolved.', 'not_started') end
	                                       local dissect = getDissectCode()
	                                       if dissect == nil then return mcp.err('host_refused', 'Cheat Engine has no code dissector.', 'not_started') end
	                                       local source = dissect.getReferences(address) or {}
	                                       local all, count, exact = {}, 0, true
	                                       for from, kind in pairs(source) do
	                                           count = count + 1
	                                           if count > a[4] then exact=false; break end
	                                           all[#all+1] = { from=from, kind=kind }
	                                           if count % 256 == 0 and mcp.expired() then
	                                               return mcp.err('timeout', 'Reading code-dissector references exceeded the dispatch budget.', 'completed',
	                                                   'Reduce the dissect range, then retry.')
	                                           end
	                                       end
	                                       table.sort(all, function(left, right) return math.ult(left.from, right.from) end)
	                                       local target = string.format('%X', address)
	                                       local page, first, last = {}, a[2] + 1, math.min(#all, a[2] + a[3])
	                                       for index=first,last do page[#page+1] = { fromAddress=string.format('%X', all[index].from), toAddress=target, kind=tostring(all[index].kind) } end
	                                       local nextOffset = (exact and last < #all) and last or ((not exact and last < #all) and last or nil)
	                                       return { address=target, total=count, exact=exact, references=page, nextOffset=nextOffset }
	                                       """;

	internal const string FindStrings = """
	                                    local dissect = getDissectCode()
	                                    if dissect == nil then return mcp.err('host_refused', 'Cheat Engine has no code dissector.', 'not_started') end
	                                    local source = dissect.getReferencedStrings() or {}
	                                    local all, count, scanned, exact = {}, 0, 0, true
	                                    local needle = a[1]
	                                    if needle ~= nil then needle = string.lower(needle) end
	                                    for address, text in pairs(source) do
	                                        scanned = scanned + 1
	                                        if scanned > a[4] then exact=false; break end
	                                        if needle == nil or string.find(string.lower(tostring(text)), needle, 1, true) ~= nil then
	                                            count = count + 1
	                                            all[#all+1] = { address=address, text=tostring(text) }
	                                        end
	                                        if scanned % 256 == 0 and mcp.expired() then
	                                            return mcp.err('timeout', 'Reading code-dissector strings exceeded the dispatch budget.', 'completed',
	                                                'Reduce the dissect range or narrow the filter, then retry.')
	                                        end
	                                    end
	                                    table.sort(all, function(left, right) return math.ult(left.address, right.address) end)
	                                    local page, first, last = {}, a[2] + 1, math.min(#all, a[2] + a[3])
	                                    for index=first,last do page[#page+1] = { address=string.format('%X', all[index].address), text=all[index].text } end
	                                    local nextOffset = (exact and last < #all) and last or ((not exact and last < #all) and last or nil)
	                                    return { total=count, exact=exact, strings=page, nextOffset=nextOffset }
	                                    """;

	internal const string ListFunctions = """
	                                      local dissect = getDissectCode()
	                                      if dissect == nil then return mcp.err('host_refused', 'Cheat Engine has no code dissector.', 'not_started') end
	                                      local source = dissect.getReferencedFunctions() or {}
	                                      local count = #source
	                                      local page, first, last = {}, a[1] + 1, math.min(count, a[1] + a[2])
	                                      for index=first,last do page[#page+1] = string.format('%X', source[index]) end
	                                      local nextOffset = last < count and last or nil
	                                      return { total=count, functions=page, nextOffset=nextOffset }
	                                      """;

	internal const string GetComments = """
	                                    local comments = {}
	                                    for index=1,#a[1] do
	                                        local address = getAddressSafe(a[1][index])
	                                        if address == nil then return mcp.err('invalid_argument', 'an address could not be resolved.', 'not_started') end
	                                        comments[index] = { address=string.format('%X', address), comment=getComment(address) }
	                                    end
	                                    return { comments=comments }
	                                    """;

	internal const string SetComment = """
	                                   local address = getAddressSafe(a[1])
	                                   if address == nil then return mcp.err('invalid_argument', 'address could not be resolved.', 'not_started') end
	                                   setComment(address, a[2])
	                                   return { comments={{ address=string.format('%X', address), comment=a[2] }} }
	                                   """;

	internal const string ClearDissect = """
	                                     local dissect = getDissectCode()
	                                     if dissect == nil then return mcp.err('host_refused', 'Cheat Engine has no code dissector.', 'not_started') end
	                                     dissect.clear()
	                                     return { cleared=true }
	                                     """;
}
