namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>
///     The fixed Lua bodies of the structure tools. Caller data reaches them only through <c>a[]</c>; they touch only
///     Cheat Engine's structure API and never <c>createStructureFromName</c>, which Mono support may override.
/// </summary>
/// <remarks>
///     <para>
///         Scripts that accept a structure name check the result of <c>getStructure(name)</c>, then scan the global
///         list so a numeric-looking name never selects a structure by index. <see cref="DefinitionPage" /> instead
///         uses a bounded indexed global lookup for <c>structure_get</c>. Element fields are read through the documented
///         accessors (<c>Offset</c>, <c>Name</c>, <c>Vartype</c>, <c>getBytesize</c>, <c>ChildStruct</c>,
///         <c>getChildStructStart</c>) and the published <c>DisplayMethod</c>; optional ones are read under <c>pcall</c>.
///     </para>
///     <para>
///         Addresses that Cheat Engine parses itself (<c>autoGuess</c>, <c>fillFromDotNetAddress</c>) are passed as
///         <c>0x</c>-prefixed hexadecimal text, which both its number and its symbol paths read correctly.
///     </para>
/// </remarks>
internal static class StructureLuaScripts
{
	/// <summary>Shared local helpers; every body that needs them starts with this text.</summary>
	internal const string Helpers = """
	                                local function findStructure(name)
	                                  local ok, found = pcall(getStructure, name)
	                                  if ok and found ~= nil and found.Name == name then return found end
	                                  for i = 0, getStructureCount() - 1 do
	                                    local candidate = getStructure(i)
	                                    if candidate ~= nil and candidate.Name == name then return candidate end
	                                  end
	                                  return nil
	                                end
	                                local function listed(name)
	                                  for i = 0, getStructureCount() - 1 do
	                                    local candidate = getStructure(i)
	                                    if candidate ~= nil and candidate.Name == name then return true end
	                                  end
	                                  return false
	                                end
	                                local function missing(name, parameter)
	                                  return mcp.err('not_found', 'No structure is named ' .. name .. ' (' .. parameter .. ').',
	                                    'not_started', 'List the structures with structure_list; names are case-sensitive.')
	                                end
	                                local function exists(name)
	                                  return mcp.err('invalid_state', 'A structure named ' .. name .. ' already exists.', 'not_started',
	                                    'Pick another name, or delete the existing structure with structure_delete if you created it.')
	                                end
	                                local function summary(s)
	                                  return {name = s.Name, size = s.Size, elementCount = s.Count}
	                                end
	                                local function optional(read, kind)
	                                  local ok, value = pcall(read)
	                                  if not ok then return nil end
	                                  if kind == 'integer' then
	                                    if math.type(value) == 'integer' then return value end
	                                    return nil
	                                  end
	                                  if type(value) == kind then return value end
	                                  return nil
	                                end
	                                local function describe(e, i, detailed)
	                                  local item = {index = i, offset = e.Offset, name = e.Name, vartype = e.Vartype,
	                                    display = optional(function() return e.DisplayMethod end, 'string'), byteSize = e.getBytesize()}
	                                  local child = e.ChildStruct
	                                  if child ~= nil then
	                                    item.childStructure = child.Name
	                                    if detailed then item.childStructureStart = e.getChildStructStart() end
	                                  end
	                                  if detailed and item.vartype == 13 then
	                                    item.customType = optional(function() return e.CustomTypeName end, 'string')
	                                  end
	                                  if detailed and item.vartype == 9 then
	                                    item.bitStart = optional(function() return e.BitStart end, 'integer')
	                                    item.bitSize = optional(function() return e.BitSize end, 'integer')
	                                  end
	                                  return item
	                                end
	                                local function indexOf(s, e)
	                                  local index = optional(function() return e.index end, 'integer')
	                                  if index ~= nil then return index end
	                                  for i = 0, s.Count - 1 do
	                                    if s.getElement(i) == e then return i end
	                                  end
	                                  return -1
	                                end
	                                local function discard(s, failure)
	                                  local effect = 'not_applied'
	                                  if not pcall(function() s.destroy() end) then effect = 'unknown' end
	                                  return mcp.err('host_refused', 'Cheat Engine refused the structure: ' .. tostring(failure), effect,
	                                    'Nothing was kept when hostEffect is not_applied; check the arguments and repeat the call.')
	                                end
	                                local function resolveChildren(specs, parameter)
	                                  local children = {}
	                                  for i = 1, #specs do
	                                    local childName = specs[i][6]
	                                    if childName ~= nil then
	                                      local child = findStructure(childName)
	                                      if child == nil then
	                                        return nil, missing(childName, parameter .. '[' .. (i - 1) .. '].childStructure')
	                                      end
	                                      children[i] = child
	                                    end
	                                  end
	                                  return children
	                                end
	                                local function applySpec(e, f, child)
	                                  e.Offset = f[1]
	                                  e.Name = f[2]
	                                  e.Vartype = f[3]
	                                  if f[5] ~= nil then e.setBytesize(f[5]) end
	                                  if f[4] ~= nil then e.DisplayMethod = f[4] end
	                                  if child ~= nil then
	                                    e.ChildStruct = child
	                                    if f[7] ~= nil then e.setChildStructStart(f[7]) end
	                                  end
	                                end

	                                """;

	/// <summary>
	///     Pages the global structures. <c>a</c>: name filter or <c>nil</c>, offset, limit, the most structures to scan.
	/// </summary>
	internal const string List = """
	                             local needle = a[1]
	                             if needle ~= nil then needle = string.lower(needle) end
	                             local offset, limit = a[2], a[3]
	                             local count = getStructureCount()
	                             local scanned = math.min(count, a[4])
	                             local items, total = {}, 0
	                             for i = 0, scanned - 1 do
	                               local s = getStructure(i)
	                               if s ~= nil then
	                                 local name = s.Name
	                                 local readable, internal = pcall(function() return s.Internal end)
	                                 local visible = not readable or internal ~= true
	                                 if visible and (needle == nil or string.find(string.lower(name), needle, 1, true) ~= nil) then
	                                   total = total + 1
	                                   if total > offset and #items < limit then
	                                     items[#items + 1] = {name = name, size = s.Size, elementCount = s.Count}
	                                   end
	                                 end
	                               end
	                             end
	                             local nextOffset = nil
	                             if offset + #items < total then nextOffset = offset + #items end
	                             return {structures = items, total = total, nextOffset = nextOffset, truncated = count > scanned}
	                             """;

	/// <summary>
	///     Copies a structure and a page of its elements, optionally only those whose offset is in a range.
	///     <c>a</c>: name, lowest offset or <c>nil</c>, highest offset or <c>nil</c>, page offset, limit, detailed.
	/// </summary>
	internal const string Elements = Helpers + """
	                                           local s = findStructure(a[1])
	                                           if s == nil then return missing(a[1], 'name') end
	                                           local count = s.Count
	                                           local items, total = {}, 0
	                                           for i = 0, count - 1 do
	                                             local e = s.getElement(i)
	                                             local selected = true
	                                             if a[2] ~= nil or a[3] ~= nil then
	                                               local offset = e.Offset
	                                               selected = (a[2] == nil or offset >= a[2]) and (a[3] == nil or offset <= a[3])
	                                             end
	                                             if selected then
	                                               total = total + 1
	                                               if total > a[4] and #items < a[5] then items[#items + 1] = describe(e, i, a[6]) end
	                                             end
	                                           end
	                                           local nextOffset = nil
	                                           if a[4] + #items < total then nextOffset = a[4] + #items end
	                                           local internal = optional(function() return s.Internal end, 'boolean') == true
	                                           return {name = s.Name, size = s.Size, elementCount = count, internal = internal,
	                                             elements = items, total = total, nextOffset = nextOffset}
	                                           """;

	/// <summary>
	///     Finds one global definition by bounded indexed lookup, then copies only the requested page of its elements.
	///     <c>a</c>: name, element offset, element limit, detailed format, the most global definitions to scan.
	/// </summary>
	internal const string DefinitionPage = """
	                                      local function missing(name)
	                                        return mcp.err('not_found', 'No structure is named ' .. name .. ' (name).', 'not_started',
	                                          'List the structures with structure_list; names are case-sensitive.')
	                                      end
	                                      local function optional(read, kind)
	                                        local ok, value = pcall(read)
	                                        if not ok then return nil end
	                                        if kind == 'integer' then
	                                          if math.type(value) == 'integer' then return value end
	                                          return nil
	                                        end
	                                        if type(value) == kind then return value end
	                                        return nil
	                                      end
	                                      local function describe(e, i, detailed)
	                                        local item = {index = i, offset = e.Offset, name = e.Name, vartype = e.Vartype,
	                                          display = optional(function() return e.DisplayMethod end, 'string'), byteSize = e.getBytesize()}
	                                        local child = e.ChildStruct
	                                        if child ~= nil then
	                                          item.childStructure = child.Name
	                                          if detailed then item.childStructureStart = e.getChildStructStart() end
	                                        end
	                                        if detailed and item.vartype == 13 then
	                                          item.customType = optional(function() return e.CustomTypeName end, 'string')
	                                        end
	                                        if detailed and item.vartype == 9 then
	                                          item.bitStart = optional(function() return e.BitStart end, 'integer')
	                                          item.bitSize = optional(function() return e.BitSize end, 'integer')
	                                        end
	                                        return item
	                                      end
	                                      local definitions = getStructureCount()
	                                      local scanned = math.min(definitions, a[5])
	                                      local s = nil
	                                      for i = 0, scanned - 1 do
	                                        local candidate = getStructure(i)
	                                        if candidate ~= nil and candidate.Name == a[1] then
	                                          s = candidate
	                                          break
	                                        end
	                                      end
	                                      if s == nil then
	                                        if definitions > scanned then
	                                          return mcp.err('limit_exceeded',
	                                            'structure_get scanned ' .. scanned .. ' of ' .. definitions .. ' global structures without finding ' .. a[1] .. '.',
	                                            'not_started', 'Use structure_list to locate the structure within the bounded global lookup.')
	                                        end
	                                        return missing(a[1])
	                                      end
	                                      local count = s.Count
	                                      local finish = math.min(count, a[2] + a[3])
	                                      local items = {}
	                                      for i = a[2], finish - 1 do items[#items + 1] = describe(s.getElement(i), i, a[4]) end
	                                      local nextOffset = nil
	                                      if finish < count then nextOffset = finish end
	                                      local internal = optional(function() return s.Internal end, 'boolean') == true
	                                      return {name = s.Name, size = s.Size, elementCount = count, internal = internal,
	                                        elements = items, total = count, nextOffset = nextOffset}
	                                      """;
	/// <summary>Copies one element by index. <c>a</c>: name, index.</summary>
	internal const string ElementAt = Helpers + """
	                                            local s = findStructure(a[1])
	                                            if s == nil then return missing(a[1], 'name') end
	                                            local count = s.Count
	                                            if a[2] >= count then
	                                              return mcp.err('not_found', 'Element ' .. a[2] .. ' is past the last element; ' .. s.Name ..
	                                                ' has ' .. count .. ' elements.', 'not_started', 'Read the element indices with structure_get.')
	                                            end
	                                            return {name = s.Name, element = describe(s.getElement(a[2]), a[2], false)}
	                                            """;

	/// <summary>
	///     Creates a global structure from element specifications, all or nothing. <c>a</c>: name, internal, specifications
	///     (<c>{offset, name, vartype, displayMethod, byteSize, childName, childStart}</c>).
	/// </summary>
	internal const string CreateFromElements = Helpers + """
	                                                     if findStructure(a[1]) ~= nil then return exists(a[1]) end
	                                                     local specs = a[3]
	                                                     local children, refusal = resolveChildren(specs, 'elements')
	                                                     if children == nil then return refusal end
	                                                     local s = createStructure(a[1])
	                                                     if s == nil then
	                                                       return mcp.err('host_refused', 'Cheat Engine did not create the structure.', 'not_applied')
	                                                     end
	                                                     local ok, failure = pcall(function()
	                                                       s.beginUpdate()
	                                                       local filled, fillFailure = pcall(function()
	                                                         for i = 1, #specs do applySpec(s.addElement(), specs[i], children[i]) end
	                                                       end)
	                                                       s.endUpdate()
	                                                       if not filled then error(fillFailure, 0) end
	                                                       s.addToGlobalStructureList()
	                                                       if a[2] then s.Internal = true end
	                                                     end)
	                                                     if not ok then return discard(s, failure) end
	                                                     return summary(s)
	                                                     """;

	/// <summary>Creates a global structure as a copy of another. <c>a</c>: name, internal, source name.</summary>
	internal const string CreateClone = Helpers + """
	                                              if findStructure(a[1]) ~= nil then return exists(a[1]) end
	                                              local source = findStructure(a[3])
	                                              if source == nil then return missing(a[3], 'cloneFrom') end
	                                              local s = nil
	                                              local ok, failure = pcall(function()
	                                                s = source.clone(a[1])
	                                                if s == nil then error('clone returned no structure', 0) end
	                                                if s.Name ~= a[1] then s.Name = a[1] end
	                                                if not listed(a[1]) then s.addToGlobalStructureList() end
	                                                if a[2] then s.Internal = true end
	                                              end)
	                                              if not ok then
	                                                if s == nil then
	                                                  return mcp.err('host_refused', 'Cheat Engine could not copy ' .. a[3] .. ': ' .. tostring(failure),
	                                                    'unknown', 'Check structure_list for a partial copy before repeating the call.')
	                                                end
	                                                return discard(s, failure)
	                                              end
	                                              return summary(s)
	                                              """;

	/// <summary>
	///     Creates a global structure from a PDB type of the loaded symbols. <c>a</c>: name, internal, type name, the most
	///     fields to copy.
	/// </summary>
	internal const string CreateFromPdb = Helpers + """
	                                                if findStructure(a[1]) ~= nil then return exists(a[1]) end
	                                                local fields = getStructureElementsFromName(a[3])
	                                                if fields == nil or #fields == 0 then
	                                                  return mcp.err('not_found', 'The loaded symbols describe no type named ' .. a[3] .. '.', 'not_started',
	                                                    'Load the module PDB with symbol_add_module(enumStructures=true), then check structure_get_pdb_layout.')
	                                                end
	                                                if #fields > a[4] then
	                                                  return mcp.err('limit_exceeded', 'The PDB type has ' .. #fields .. ' fields; at most ' .. a[4] ..
	                                                    ' can be copied.', 'not_started', 'Create the structure from selected elements instead.')
	                                                end
	                                                local s = createStructure(a[1])
	                                                if s == nil then
	                                                  return mcp.err('host_refused', 'Cheat Engine did not create the structure.', 'not_applied')
	                                                end
	                                                local ok, failure = pcall(function()
	                                                  s.beginUpdate()
	                                                  local filled, fillFailure = pcall(function()
	                                                    for i = 1, #fields do
	                                                      local field = fields[i]
	                                                      local e = s.addElement()
	                                                      e.Offset = field.offset
	                                                      e.Name = field.name or ''
	                                                      if field.vartype ~= nil then e.Vartype = field.vartype end
	                                                    end
	                                                  end)
	                                                  s.endUpdate()
	                                                  if not filled then error(fillFailure, 0) end
	                                                  s.addToGlobalStructureList()
	                                                  if a[2] then s.Internal = true end
	                                                end)
	                                                if not ok then return discard(s, failure) end
	                                                return summary(s)
	                                                """;

	/// <summary>Destroys a structure, which also removes it from the global list. <c>a</c>: name.</summary>
	internal const string Delete = Helpers + """
	                                         local s = findStructure(a[1])
	                                         if s == nil then return missing(a[1], 'name') end
	                                         local name = s.Name
	                                         s.destroy()
	                                         return {name = name}
	                                         """;

	/// <summary>
	///     Renames a structure in place, so pointer elements that point to it keep their child structure. The same name
	///     is a no-op; a name that any structure, internal ones included, already has is refused. <c>a</c>: name, new
	///     name.
	/// </summary>
	internal const string Rename = Helpers + """
	                                         local s = findStructure(a[1])
	                                         if s == nil then return missing(a[1], 'name') end
	                                         if a[2] == a[1] then return summary(s) end
	                                         if findStructure(a[2]) ~= nil then return exists(a[2]) end
	                                         local ok, failure = pcall(function() s.Name = a[2] end)
	                                         local now = s.Name
	                                         if not ok or now ~= a[2] then
	                                           local effect = 'unknown'
	                                           if now == a[1] then effect = 'not_applied' end
	                                           local reason = 'the name did not change'
	                                           if not ok then reason = tostring(failure) end
	                                           return mcp.err('host_refused', 'Cheat Engine did not rename ' .. a[1] .. ': ' .. reason, effect,
	                                             'Check the names with structure_list before repeating the call.')
	                                         end
	                                         return summary(s)
	                                         """;

	/// <summary>
	///     Resolves the requested structures and the child structures their pointer elements reach (keyed by name, so a
	///     cycle ends), then returns Cheat Engine's <c>generate_c_header</c> text or, for the managed generator, a copy
	///     of every element. Cheat Engine's generator marks every byte of every element in a Lua table, so it is skipped
	///     (or refused when asked for) above a byte span. <c>a</c>: names, mode (<c>auto</c>, <c>cheat_engine</c> or
	///     <c>managed</c>), the most structures, the most elements, the most text bytes, the most element bytes for
	///     Cheat Engine's generator.
	/// </summary>
	internal const string CHeader = Helpers + """
	                                          local requested, mode = a[1], a[2]
	                                          local roots, queue, seen = {}, {}, {}
	                                          for i = 1, #requested do
	                                            local s = findStructure(requested[i])
	                                            if s == nil then return missing(requested[i], 'names[' .. (i - 1) .. ']') end
	                                            roots[i] = s
	                                            if seen[s.Name] == nil then
	                                              seen[s.Name] = true
	                                              queue[#queue + 1] = s
	                                            end
	                                          end
	                                          local head, elements, bytes = 1, 0, 0
	                                          while head <= #queue do
	                                            local s = queue[head]
	                                            head = head + 1
	                                            local count = s.Count
	                                            elements = elements + count
	                                            if elements > a[4] then
	                                              return mcp.err('limit_exceeded', 'The structures and the child structures they reach have more than ' ..
	                                                a[4] .. ' elements.', 'not_started', 'Request fewer structures in one call.')
	                                            end
	                                            for i = 0, count - 1 do
	                                              local e = s.getElement(i)
	                                              bytes = bytes + e.getBytesize()
	                                              if e.Vartype == 12 then
	                                                local child = e.ChildStruct
	                                                if child ~= nil and seen[child.Name] == nil then
	                                                  if #queue >= a[3] then
	                                                    return mcp.err('limit_exceeded', 'The structures reach more than ' .. a[3] ..
	                                                      ' structures through their pointer elements.', 'not_started', 'Request fewer structures in one call.')
	                                                  end
	                                                  seen[child.Name] = true
	                                                  queue[#queue + 1] = child
	                                                end
	                                              end
	                                            end
	                                          end
	                                          local names = {}
	                                          for i = 1, #queue do names[i] = queue[i].Name end
	                                          local native = mode ~= 'managed' and type(generate_c_header) == 'function'
	                                          if mode == 'cheat_engine' and not native then
	                                            return mcp.err('unsupported', 'This Cheat Engine defines no generate_c_header function ' ..
	                                              '(autorun/structureExportToCHeader.lua).', 'not_started',
	                                              'Omit generator, or pass managed, to use the managed generator.')
	                                          end
	                                          if native and bytes > a[6] then
	                                            if mode == 'cheat_engine' then
	                                              return mcp.err('limit_exceeded', 'The elements span ' .. bytes .. " bytes; Cheat Engine's generator " ..
	                                                'walks every byte, so it takes at most ' .. a[6] .. '.', 'not_started',
	                                                'Omit generator, or pass managed, whose cost does not grow with element sizes.')
	                                            end
	                                            native = false
	                                          end
	                                          if native then
	                                            local ok, text = pcall(generate_c_header, roots)
	                                            if ok and type(text) == 'string' then
	                                              if #text > a[5] then
	                                                return mcp.err('limit_exceeded', "Cheat Engine's header has " .. #text .. ' bytes; at most ' .. a[5] ..
	                                                  ' are returned.', 'not_started', 'Request fewer structures in one call.')
	                                              end
	                                              return {names = names, text = text}
	                                            end
	                                            if mode == 'cheat_engine' then
	                                              local reason = 'it returned no text'
	                                              if not ok then reason = tostring(text) end
	                                              return mcp.err('host_refused', 'Cheat Engine could not generate the header: ' .. reason, 'not_applied',
	                                                'Omit generator, or pass managed, to use the managed generator.')
	                                            end
	                                          end
	                                          local copied = {}
	                                          for i = 1, #queue do
	                                            local s = queue[i]
	                                            local items = {}
	                                            for j = 0, s.Count - 1 do
	                                              local e = s.getElement(j)
	                                              local vartype = e.Vartype
	                                              local item = {offset = e.Offset, vartype = vartype, byteSize = e.getBytesize()}
	                                              local name = e.Name
	                                              if name ~= nil and name ~= '' then item.name = name end
	                                              if vartype >= 0 and vartype <= 3 then
	                                                item.display = optional(function() return e.DisplayMethod end, 'string')
	                                              elseif vartype == 12 then
	                                                local child = e.ChildStruct
	                                                if child ~= nil then
	                                                  item.child = child.Name
	                                                  if optional(function() return e.NestedStructure end, 'boolean') == true then
	                                                    item.nested = true
	                                                  else
	                                                    local start = optional(function() return e.getChildStructStart() end, 'integer')
	                                                    if start ~= nil and start ~= 0 then item.childStart = start end
	                                                  end
	                                                end
	                                              elseif vartype == 9 then
	                                                item.bitStart = optional(function() return e.BitStart end, 'integer')
	                                                item.bitSize = optional(function() return e.BitSize end, 'integer')
	                                              elseif vartype == 13 then
	                                                item.customType = optional(function() return e.CustomTypeName end, 'string')
	                                              end
	                                              items[#items + 1] = item
	                                            end
	                                            copied[i] = {name = s.Name, size = s.Size, elements = items}
	                                          end
	                                          return {names = names, structures = copied}
	                                          """;

	/// <summary>
	///     Adds elements, all or nothing: on a refusal the elements added by the call are destroyed again.
	///     <c>a</c>: name, specifications as for <see cref="CreateFromElements" />.
	/// </summary>
	internal const string AddElements = Helpers + """
	                                              local s = findStructure(a[1])
	                                              if s == nil then return missing(a[1], 'name') end
	                                              local specs = a[2]
	                                              local children, refusal = resolveChildren(specs, 'elements')
	                                              if children == nil then return refusal end
	                                              local added, position = {}, 0
	                                              s.beginUpdate()
	                                              local ok, failure = pcall(function()
	                                                for i = 1, #specs do
	                                                  position = i - 1
	                                                  local e = s.addElement()
	                                                  added[#added + 1] = e
	                                                  applySpec(e, specs[i], children[i])
	                                                end
	                                              end)
	                                              local closed, closeFailure = pcall(function() s.endUpdate() end)
	                                              if not ok then
	                                                local cleaned = pcall(function()
	                                                  for i = #added, 1, -1 do added[i].destroy() end
	                                                end)
	                                                if not closed then
	                                                  return mcp.err('partial_effect', 'Cheat Engine did not finish adding elements: ' ..
	                                                    tostring(closeFailure), 'unknown',
	                                                    'The elements may have changed; read the structure with structure_get before repeating the call.')
	                                                end
	                                                local effect = 'not_applied'
	                                                if not cleaned then effect = 'unknown' end
	                                                return mcp.err('host_refused', 'Cheat Engine refused elements[' .. position .. ']: ' .. tostring(failure),
	                                                  effect, 'The elements added by this call were removed when hostEffect is not_applied; fix that element and repeat the call.')
	                                              end
	                                              if not closed then
	                                                return mcp.err('partial_effect', 'Cheat Engine did not finish adding elements: ' ..
	                                                  tostring(closeFailure), 'unknown',
	                                                  'The elements may have changed; read the structure with structure_get before repeating the call.')
	                                              end
	                                              local indices = {}
	                                              for i = 1, #added do indices[i] = indexOf(s, added[i]) end
	                                              return {name = s.Name, size = s.Size, elementCount = s.Count, indices = indices, applied = #added}
	                                              """;

	/// <summary>
	///     Updates elements: every update is checked first, then applied in order until the first refusal. Elements are
	///     held by reference, so an offset change that reorders the structure does not redirect later updates.
	///     <c>a</c>: name, updates (<c>{index, offset, name, vartype, displayMethod, byteSize}</c>).
	/// </summary>
	internal const string UpdateElements = Helpers + """
	                                                 local s = findStructure(a[1])
	                                                 if s == nil then return missing(a[1], 'name') end
	                                                 local updates = a[2]
	                                                 local count = s.Count
	                                                 local targets = {}
	                                                 for i = 1, #updates do
	                                                   local u = updates[i]
	                                                   local where = 'updates[' .. (i - 1) .. ']'
	                                                   if u[1] >= count then
	                                                     return mcp.err('not_found', where .. '.index ' .. u[1] .. ' is past the last element; ' .. s.Name ..
	                                                       ' has ' .. count .. ' elements.', 'not_started', 'Read the element indices with structure_get.')
	                                                   end
	                                                   local e = s.getElement(u[1])
	                                                   if u[4] == nil then
	                                                     local vartype = e.Vartype
	                                                     if u[5] ~= nil and (vartype < 0 or vartype > 3) then
	                                                       return mcp.err('invalid_argument', where .. '.display applies only to integer elements.', 'not_started')
	                                                     end
	                                                     if u[6] ~= nil and vartype ~= 6 and vartype ~= 7 and vartype ~= 8 then
	                                                       return mcp.err('invalid_argument', where ..
	                                                         '.byteSize applies only to string, wstring and bytes elements.', 'not_started')
	                                                     end
	                                                   end
	                                                   targets[i] = e
	                                                 end
	                                                 local applied, failedIndex = 0, nil
	                                                 s.beginUpdate()
	                                                 local ok, failure = pcall(function()
	                                                   for i = 1, #updates do
	                                                     local u, e = updates[i], targets[i]
	                                                     failedIndex = i - 1
	                                                     if u[2] ~= nil then e.Offset = u[2] end
	                                                     if u[3] ~= nil then e.Name = u[3] end
	                                                     if u[4] ~= nil then e.Vartype = u[4] end
	                                                     if u[6] ~= nil then e.setBytesize(u[6]) end
	                                                     if u[5] ~= nil then e.DisplayMethod = u[5] end
	                                                     applied = applied + 1
	                                                   end
	                                                 end)
	                                                 local closed, closeFailure = pcall(function() s.endUpdate() end)
	                                                 if not closed then
	                                                   return mcp.err('partial_effect', 'Cheat Engine did not finish updating elements: ' ..
	                                                     tostring(closeFailure), 'unknown',
	                                                     'The elements may have changed; read the structure with structure_get before repeating the call.')
	                                                 end
	                                                 local indices = {}
	                                                 for i = 1, applied do indices[i] = indexOf(s, targets[i]) end
	                                                 local result = {name = s.Name, size = s.Size, elementCount = s.Count, indices = indices, applied = applied}
	                                                 if not ok then
	                                                   result.failedIndex = failedIndex
	                                                   result.failure = tostring(failure)
	                                                 end
	                                                 return result
	                                                 """;

	/// <summary>
	///     Removes elements by index, highest first, until the first refusal. <c>a</c>: name, distinct indices in
	///     descending order.
	/// </summary>
	internal const string RemoveElements = Helpers + """
	                                                 local s = findStructure(a[1])
	                                                 if s == nil then return missing(a[1], 'name') end
	                                                 local indices = a[2]
	                                                 local count = s.Count
	                                                 local elements = {}
	                                                 for i = 1, #indices do
	                                                   if indices[i] >= count then
	                                                     return mcp.err('not_found', 'Element ' .. indices[i] .. ' is past the last element; ' .. s.Name ..
	                                                       ' has ' .. count .. ' elements.', 'not_started', 'Read the element indices with structure_get.')
	                                                   end
	                                                   elements[i] = s.getElement(indices[i])
	                                                 end
	                                                 local removed = 0
	                                                 s.beginUpdate()
	                                                 local ok, failure = pcall(function()
	                                                   for i = 1, #elements do
	                                                     elements[i].destroy()
	                                                     removed = removed + 1
	                                                   end
	                                                 end)
	                                                 local closed, closeFailure = pcall(function() s.endUpdate() end)
	                                                 if not closed then
	                                                   return mcp.err('partial_effect', 'Cheat Engine did not finish removing elements: ' ..
	                                                     tostring(closeFailure), 'unknown',
	                                                     'The elements may have changed; read the structure with structure_get before repeating the call.')
	                                                 end
	                                                 local done = {}
	                                                 for i = 1, removed do done[i] = indices[i] end
	                                                 local result = {name = s.Name, size = s.Size, elementCount = s.Count, indices = done, applied = removed}
	                                                 if not ok then
	                                                   result.failedIndex = removed
	                                                   result.failure = tostring(failure)
	                                                 end
	                                                 return result
	                                                 """;

	/// <summary>
	///     Lets Cheat Engine guess fields from memory, creating the structure first when asked. Cheat Engine reads
	///     from its base argument and labels the new elements from its offset argument, so the base is the object
	///     address plus the offset. <c>a</c>: name, read address as <c>0x</c> text, offset, size, create when missing.
	/// </summary>
	internal const string AutoGuess = Helpers + """
	                                            local s = findStructure(a[1])
	                                            local created = false
	                                            if s == nil then
	                                              if not a[5] then return missing(a[1], 'name') end
	                                              s = createStructure(a[1])
	                                              if s == nil then
	                                                return mcp.err('host_refused', 'Cheat Engine did not create the structure.', 'not_applied')
	                                              end
	                                              created = true
	                                            end
	                                            local ok, failure = pcall(function()
	                                              if created then s.addToGlobalStructureList() end
	                                              s.autoGuess(a[2], a[3], a[4])
	                                            end)
	                                            if not ok then
	                                              if created then return discard(s, failure) end
	                                              return mcp.err('host_refused', 'Cheat Engine could not guess the structure: ' .. tostring(failure),
	                                                'unknown', 'Read the structure with structure_get before repeating the call.')
	                                            end
	                                            return summary(s)
	                                            """;

	/// <summary>
	///     Fills a structure from the layout Cheat Engine's .NET data collector reports for an object, refusing while
	///     the target is halted. <c>a</c>: name, object address as <c>0x</c> text, rename, create when missing.
	/// </summary>
	internal const string FillFromDotNet = Helpers + """
	                                                 if type(debug_isBroken) == 'function' and debug_isBroken() then
	                                                   return mcp.err('busy', 'The target is stopped at a breakpoint, so its .NET runtime cannot answer.',
	                                                     'not_started', 'Continue the target with debugger_continue, then repeat the call.')
	                                                 end
	                                                 if type(isPaused) == 'function' and isPaused() then
	                                                   return mcp.err('busy', 'The target is paused, so its .NET runtime cannot answer.', 'not_started',
	                                                     'Resume the target with process_set_paused, then repeat the call.')
	                                                 end
	                                                 local s = findStructure(a[1])
	                                                 local created = false
	                                                 if s == nil then
	                                                   if not a[4] then return missing(a[1], 'name') end
	                                                   s = createStructure(a[1])
	                                                   if s == nil then
	                                                     return mcp.err('host_refused', 'Cheat Engine did not create the structure.', 'not_applied')
	                                                   end
	                                                   created = true
	                                                 end
	                                                 local before = s.Count
	                                                 local ok, failure = pcall(function() s.fillFromDotNetAddress(a[2], a[3]) end)
	                                                 if not ok then
	                                                   if created then return discard(s, failure) end
	                                                   return mcp.err('host_refused', 'Cheat Engine could not read the .NET layout: ' .. tostring(failure),
	                                                     'unknown', 'Read the structure with structure_get before repeating the call.')
	                                                 end
	                                                 if s.Count == before then
	                                                   local effect = 'not_applied'
	                                                   if created and not pcall(function() s.destroy() end) then effect = 'unknown' end
	                                                   return mcp.err('not_found', 'Cheat Engine found no .NET object layout at ' .. a[2] .. '.', effect,
	                                                     'Pass the address of a managed object, not of one of its fields, in a .NET target.')
	                                                 end
	                                                 if created then
	                                                   local added, failure = pcall(function() s.addToGlobalStructureList() end)
	                                                   if not added then return discard(s, failure) end
	                                                 end
	                                                 return summary(s)
	                                                 """;

	/// <summary>Copies the fields of a PDB type. <c>a</c>: type name, the most fields to copy.</summary>
	internal const string PdbLayout = """
	                                  local fields = getStructureElementsFromName(a[1])
	                                  if fields == nil then return {found = false, elements = {}, truncated = false} end
	                                  local limit = math.min(#fields, a[2])
	                                  local items = {}
	                                  for i = 1, limit do
	                                    local field = fields[i]
	                                    local vartype = field.vartype
	                                    if math.type(vartype) ~= 'integer' then vartype = nil end
	                                    items[i] = {offset = field.offset, name = field.name, vartype = vartype}
	                                  end
	                                  return {found = #fields > 0, elements = items, truncated = #fields > limit}
	                                  """;

	/// <summary>
	///     Formats the values Cheat Engine itself interprets (bit fields, custom types) at several base addresses.
	///     <c>a</c>: name, element indices, base addresses.
	/// </summary>
	internal const string FormattedValues = Helpers + """
	                                                  local s = findStructure(a[1])
	                                                  if s == nil then return missing(a[1], 'name') end
	                                                  local rows = {}
	                                                  for i = 1, #a[2] do
	                                                    local e = s.getElement(a[2][i])
	                                                    local row = {n = #a[3]}
	                                                    for j = 1, #a[3] do
	                                                      local ok, value = pcall(function() return e.getValueFromBase(a[3][j]) end)
	                                                      if ok and type(value) == 'string' and value ~= '??' then row[j] = value end
	                                                    end
	                                                    rows[i] = row
	                                                  end
	                                                  return {values = rows}
	                                                  """;

	/// <summary>
	///     Writes a value that Cheat Engine interprets (bit field, custom type) and reads it back.
	///     <c>a</c>: name, index, base address, value text.
	/// </summary>
	internal const string SetValue = Helpers + """
	                                           local s = findStructure(a[1])
	                                           if s == nil then return missing(a[1], 'name') end
	                                           if a[2] >= s.Count then
	                                             return mcp.err('not_found', 'Element ' .. a[2] .. ' is past the last element of ' .. s.Name .. '.',
	                                               'not_started', 'Read the element indices with structure_get.')
	                                           end
	                                           local e = s.getElement(a[2])
	                                           e.setValueFromBase(a[3], a[4])
	                                           local ok, value = pcall(function() return e.getValueFromBase(a[3]) end)
	                                           if not ok or type(value) ~= 'string' or value == '??' then value = nil end
	                                           return {value = value}
	                                           """;
}
