namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>Fixed Lua used only by the version-one structure interchange tools.</summary>
internal static class StructureInterchangeLua
{
	internal const string Export = StructureLuaScripts.Helpers + """
        local wanted, layouts, queued, seen, totalFields = a[1], {}, {}, {}, 0
        for i = 1, #wanted do
          local s = findStructure(wanted[i])
          if s == nil then return missing(wanted[i], 'names') end
          queued[#queued + 1] = s
        end
        local head = 1
        while head <= #queued do
          local s = queued[head]; head = head + 1
          local name, identity = s.Name, userDataToInteger(s)
          if seen[name] ~= nil and seen[name] ~= identity then
            return mcp.err('invalid_state', 'Distinct referenced layouts share the name ' .. name .. '.', 'not_started')
          end
          if not seen[name] then
            seen[name] = identity
            if #layouts >= a[2] then return mcp.err('limit_exceeded', 'The selected layouts exceed the export limit.', 'not_started') end
            if s.Count > 1024 then return mcp.err('limit_exceeded', 'Structure ' .. s.Name .. ' has more than 1024 fields.', 'not_started') end
            if totalFields > a[3] - s.Count then return mcp.err('limit_exceeded', 'The selected layouts exceed the 4096-field export limit.', 'not_started') end
            totalFields = totalFields + s.Count
            local fields = {}
            for index = 0, s.Count - 1 do
              local e = s.getElement(index)
              local nested = false
              local ok, value = pcall(function() return e.NestedStructure end)
              if ok and value == true then nested = true end
              local child = e.ChildStruct
              local field = {index = index, offset = e.Offset, name = e.Name, vartype = e.Vartype,
                display = optional(function() return e.DisplayMethod end, 'string'), byteSize = e.getBytesize(),
                nested = nested}
              if child ~= nil then
                field.childStructure = child.Name
                field.childStructureStart = e.getChildStructStart()
                if not nested then queued[#queued + 1] = child end
              end
              if field.vartype == 13 then field.customType = optional(function() return e.CustomTypeName end, 'string') end
              if field.vartype == 9 then field.bitStart = optional(function() return e.BitStart end, 'integer'); field.bitSize = optional(function() return e.BitSize end, 'integer') end
              fields[#fields + 1] = field
            end
            layouts[#layouts + 1] = {name = s.Name, internal = s.Internal == true, fields = fields}
          end
        end
        return {layouts = layouts}
        """;

	internal const string Import = StructureLuaScripts.Helpers + """
        local layouts, created = a[1], {}
        for i = 1, #layouts do if findStructure(layouts[i][1]) ~= nil then return exists(layouts[i][1]) end end
        local function rollback(reason)
          local clean = true
          for i = #created, 1, -1 do
            local ok = pcall(function() created[i].destroy() end)
            if not ok then clean = false end
          end
          if not clean then return mcp.err('partial_effect', 'Import failed and cleanup could not be confirmed: ' .. tostring(reason), 'unknown', 'List structures before retrying; do not assume unpublished child references were removed.') end
          return mcp.err('host_refused', 'Cheat Engine refused the import: ' .. tostring(reason), 'not_applied')
        end
        local ok, failure = pcall(function()
          for i = 1, #layouts do local s = createStructure(layouts[i][1]); if s == nil then error('createStructure returned nil', 0) end; created[i] = s end
          for i = 1, #layouts do
            local s, fields = created[i], layouts[i][3]
            s.beginUpdate()
            for j = 1, #fields do
              local f, e = fields[j], s.addElement()
              e.Offset = f[1]; e.Name = f[2]; e.Vartype = f[3]
              if f[4] ~= nil then e.DisplayMethod = f[4] end
              if f[5] ~= nil then e.setBytesize(f[5]) end
              if f[6] ~= nil then e.ChildStruct = created[f[6]]; if f[7] ~= nil then e.setChildStructStart(f[7]) end end
            end
            s.endUpdate()
          end
          for i = 1, #layouts do created[i].addToGlobalStructureList(); if layouts[i][2] then created[i].Internal = true end end
        end)
        if not ok then return rollback(failure) end
        local names = {}; for i = 1, #created do names[i] = created[i].Name end
        return {names = names, layoutCount = #created}
        """;
}
