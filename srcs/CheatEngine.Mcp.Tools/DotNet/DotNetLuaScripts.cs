namespace CheatEngine.Mcp.Tools.DotNet;

/// <summary>Fixed, bounded Lua bodies for the .NET collector. Callers only ever supply values in <c>a</c>.</summary>
internal static class DotNetLuaScripts
{
	internal const string Status = """
	                               local collector = getDotNetDataCollector()
	                               if collector == nil then return {available = false, attached = false, domainCount = 0} end
	                               local ok, domains = pcall(function() return collector.enumDomains() end)
	                               if not ok or domains == nil then return {available = true, attached = false, domainCount = 0} end
	                               return {available = true, attached = collector.Attached == true, domainCount = #domains}
	                               """;

	internal const string Domains = """
	                                local collector = getDotNetDataCollector()
	                                if collector == nil then return mcp.err('not_attached', 'The .NET data collector is unavailable.', 'not_started', 'Attach a .NET target and check dotnet_get_status.') end
	                                local domains = collector.enumDomains()
	                                if domains == nil then return mcp.err('not_attached', 'The .NET data collector is not attached to the selected target.', 'not_started', 'Check dotnet_get_status after the target has loaded its CLR.') end
	                                local offset, limit, items = a[1], a[2], {}
	                                for i = offset + 1, math.min(#domains, offset + limit) do
	                                  local item = domains[i]
	                                  -- CElua documents named properties, while older builds expose the
	                                  -- same record through its positional fields.  Accept both shapes.
	                                  items[#items + 1] = {handle = tostring(item.DomainHandle or item[1]), name = tostring(item.Name or item[2] or '')}
	                                end
	                                local nextOffset = offset + #items < #domains and offset + #items or nil
	                                return {domains = items, total = #domains, nextOffset = nextOffset}
	                                """;

	internal const string Modules = """
	                                local collector = getDotNetDataCollector()
	                                if collector == nil then return mcp.err('not_attached', 'The .NET data collector is unavailable.', 'not_started') end
	                                local domain = tonumber(a[1])
	                                if domain == nil then return mcp.err('invalid_argument', 'domainHandle must be an opaque numeric handle from dotnet_list_domains.', 'not_started') end
	                                local modules = collector.enumModuleList(domain)
	                                if modules == nil then return mcp.err('not_found', 'The .NET collector did not find that application domain.', 'not_started', 'List domains again; collector handles are scoped to its current session.') end
	                                local offset, limit, items = a[2], a[3], {}
	                                for i = offset + 1, math.min(#modules, offset + limit) do
	                                  local item = modules[i]
	                                  items[#items + 1] = {handle = tostring(item.ModuleHandle or item[1]), name = tostring(item.Name or item[3] or item[2] or '')}
	                                end
	                                local nextOffset = offset + #items < #modules and offset + #items or nil
	                                return {domainHandle = a[1], modules = items, total = #modules, nextOffset = nextOffset}
	                                """;

	internal const string Types = """
	                              local collector = getDotNetDataCollector()
	                              if collector == nil then return mcp.err('not_attached', 'The .NET data collector is unavailable.', 'not_started') end
	                              local module = tonumber(a[1])
	                              if module == nil then return mcp.err('invalid_argument', 'moduleHandle must be an opaque numeric handle from dotnet_list_modules.', 'not_started') end
	                              local types = collector.enumTypeDefs(module)
	                              if types == nil then return mcp.err('not_found', 'The .NET collector did not find that module.', 'not_started', 'List modules again; collector handles are scoped to its current session.') end
	                              local offset, limit, items = a[2], a[3], {}
	                              for i = offset + 1, math.min(#types, offset + limit) do
	                                local item = types[i]
	                                items[#items + 1] = {token = tostring(item.TypeDefToken or item[1]), name = tostring(item.Name or item[2] or ''),
	                                  flags = item.Flags or item[3], extends = (item.Extends or item[4]) and tostring(item.Extends or item[4]) or nil}
	                              end
	                              local nextOffset = offset + #items < #types and offset + #items or nil
	                              return {moduleHandle = a[1], types = items, total = #types, nextOffset = nextOffset}
	                              """;

	internal const string Type = """
	                             local collector = getDotNetDataCollector()
	                             if collector == nil then return mcp.err('not_attached', 'The .NET data collector is unavailable.', 'not_started') end
	                             local module, token = tonumber(a[1]), tonumber(a[2])
	                             if module == nil or token == nil then return mcp.err('invalid_argument', 'moduleHandle and typeToken must be opaque numeric values from the .NET collector.', 'not_started') end
	                             local data = collector.getTypeDefData(module, token)
	                             if data == nil then return mcp.err('not_found', 'The .NET collector did not find that type.', 'not_started', 'List the module types again; tokens are scoped to the collector session.') end
	                             local fields, source = {}, data.Fields or data.fields or data[7] or {}
	                             for i = 1, math.min(#source, a[3]) do
	                               local field = source[i]
	                               fields[#fields + 1] = {name = tostring(field.Name or field.name or field[3] or ''), offset = field.Offset or field.offset or field[1] or 0,
	                                 fieldType = field.FieldTypeClassName or field.fieldTypeClassName or field.FieldType or field.fieldType or field[2]}
	                             end
	                             return {moduleHandle = a[1], typeToken = a[2], name = data.ClassName or data.className or data[6],
	                               objectType = data.ObjectType or data.objectType or data[1], elementType = data.ElementType or data.elementType or data[2],
	                               countOffset = data.CountOffset or data.countOffset or data[3], elementSize = data.ElementSize or data.elementSize or data[4],
	                               firstElementOffset = data.FirstElementOffset or data.firstElementOffset or data[5], fields = fields}
	                             """;

	internal const string Methods = """
	                                local collector = getDotNetDataCollector()
	                                if collector == nil then return mcp.err('not_attached', 'The .NET data collector is unavailable.', 'not_started') end
	                                local module, token = tonumber(a[1]), tonumber(a[2])
	                                if module == nil or token == nil then return mcp.err('invalid_argument', 'moduleHandle and typeToken must be opaque numeric values from the .NET collector.', 'not_started') end
	                                local methods = collector.getTypeDefMethods(module, token)
	                                if methods == nil then return mcp.err('not_found', 'The .NET collector did not find that type.', 'not_started') end
	                                local offset, limit, items = a[3], a[4], {}
	                                for i = offset + 1, math.min(#methods, offset + limit) do
	                                  local item = methods[i]
	                                  local secondary = {}
	                                  local nativeBlocks = item.SecondaryNativeCode or item[7] or {}
	                                  for j = 1, #nativeBlocks do
	                                    local block = nativeBlocks[j]
	                                    local address = type(block) == 'table' and (block.Address or block.address or block[1]) or block
	                                    if type(address) == 'number' and address ~= 0 then secondary[#secondary + 1] = string.format('%X', address) end
	                                  end
	                                  local ilCode = item.ILCode or item.IlCode or item[5]
	                                  local nativeCode = item.NativeCode or item[6]
	                                  items[#items + 1] = {token = tostring(item.MethodToken or item[1]), name = tostring(item.Name or item[2] or ''),
	                                    attributes = item.Attributes or item[3], implementationFlags = item.ImplementationFlags or item[4],
	                                    ilCode = type(ilCode) == 'number' and ilCode ~= 0 and string.format('%X', ilCode) or nil,
	                                    nativeCode = type(nativeCode) == 'number' and nativeCode ~= 0 and string.format('%X', nativeCode) or nil, secondaryNativeCode = secondary}
	                                end
	                                local nextOffset = offset + #items < #methods and offset + #items or nil
	                                return {moduleHandle = a[1], typeToken = a[2], methods = items, total = #methods, nextOffset = nextOffset}
	                                """;

	internal const string Object = """
	                               local collector = getDotNetDataCollector()
	                               if collector == nil then return mcp.err('not_attached', 'The .NET data collector is unavailable.', 'not_started') end
	                               local address = getAddressSafe(a[1])
	                               if address == nil then return mcp.err('invalid_argument', 'address could not be resolved by Cheat Engine.', 'not_started') end
	                               local data = collector.getAddressData(address)
	                               local startAddress = data and (data.StartAddress or data.startAddress or data[1]) or nil
	                               if startAddress == nil or startAddress == 0 then return mcp.err('not_found', 'The .NET collector did not recognize an object at that address.', 'not_started') end
	                               local fields, source = {}, data.Fields or data.fields or data[8] or {}
	                               for i = 1, math.min(#source, a[2]) do
	                                 local field = source[i]
	                                 fields[#fields + 1] = {name = tostring(field.Name or field.name or field[3] or ''),
	                                   fieldType = field.FieldTypeClassName or field.fieldTypeClassName or field.FieldType or field.fieldType or field[2],
	                                   offset = field.Offset or field.offset or field[1]}
	                               end
	                               return {address = string.format('%X', startAddress), typeName = data.ClassName or data.className or data.TypeName or data.typeName or data[7], fields = fields}
	                               """;

	internal const string Instances = """
	                                  local collector = getDotNetDataCollector()
	                                  if collector == nil then return mcp.err('not_attached', 'The .NET data collector is unavailable.', 'not_started') end
	                                  local module, token = tonumber(a[1]), tonumber(a[2])
	                                  if module == nil or token == nil then return mcp.err('invalid_argument', 'moduleHandle and typeToken must be opaque numeric values from the .NET collector.', 'not_started') end
	                                  local values = collector.enumAllObjectsOfType(module, token)
	                                  if values == nil then return mcp.err('not_found', 'The .NET collector did not return instances for that type.', 'not_started') end
	                                  local items, cap = {}, a[3]
	                                  for i = 1, math.min(#values, cap) do items[#items + 1] = {address = string.format('%X', values[i])} end
	                                  return {instances = items, total = #values}
	                                  """;
}
