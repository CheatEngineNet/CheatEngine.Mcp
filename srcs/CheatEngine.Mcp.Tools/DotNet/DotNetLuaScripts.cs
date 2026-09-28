namespace CheatEngine.Mcp.Tools.DotNet;

/// <summary>Fixed, bounded Lua bodies for the .NET collector. Callers only ever supply values in <c>a</c>.</summary>
internal static class DotNetLuaScripts
{
	/// <summary>
	///     <c>keep(list, filter, nameOf)</c>: the entries of an enumerated list whose name contains <c>filter</c>, or
	///     the list itself when <c>filter</c> is <c>nil</c>. Both sides are lowered with Lua's <c>string.lower</c>,
	///     which folds ASCII letters only, and the text is matched literally, never as a pattern. The Mono bodies share
	///     it.
	/// </summary>
	internal const string NameFilter = """
	                                   local function keep(list, filter, nameOf)
	                                     if filter == nil then return list end
	                                     local needle, kept = string.lower(filter), {}
	                                     for i = 1, #list do
	                                       if string.find(string.lower(nameOf(list[i])), needle, 1, true) ~= nil then kept[#kept + 1] = list[i] end
	                                     end
	                                     return kept
	                                   end
	                                   """ + "\n";

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

	/// <summary>
	///     Pages a module's type definitions; <c>a[4]</c>, when present, keeps only the types whose full name contains
	///     it, and <c>total</c> then counts the matches.
	/// </summary>
	internal const string Types = NameFilter + """
	                                           local collector = getDotNetDataCollector()
	                                           if collector == nil then return mcp.err('not_attached', 'The .NET data collector is unavailable.', 'not_started') end
	                                           local module = tonumber(a[1])
	                                           if module == nil then return mcp.err('invalid_argument', 'moduleHandle must be an opaque numeric handle from dotnet_list_modules.', 'not_started') end
	                                           local types = collector.enumTypeDefs(module)
	                                           if types == nil then return mcp.err('not_found', 'The .NET collector did not find that module.', 'not_started', 'List modules again; collector handles are scoped to its current session.') end
	                                           types = keep(types, a[4], function(item) return tostring(item.Name or item[2] or '') end)
	                                           local offset, limit, items = a[2], a[3], {}
	                                           for i = offset + 1, math.min(#types, offset + limit) do
	                                             local item = types[i]
	                                             items[#items + 1] = {token = tostring(item.TypeDefToken or item[1]), name = tostring(item.Name or item[2] or ''),
	                                               flags = item.Flags or item[3], extends = (item.Extends or item[4]) and tostring(item.Extends or item[4]) or nil}
	                                           end
	                                           local nextOffset = offset + #items < #types and offset + #items or nil
	                                           return {moduleHandle = a[1], types = items, total = #types, nextOffset = nextOffset}
	                                           """;

	/// <summary>
	///     One field of a type layout. Cheat Engine 7.7's collector reports <c>Token</c>, <c>Name</c>,
	///     <c>FieldType</c> (a CorElementType), <c>FieldTypeClassName</c>, <c>Offset</c>, <c>IsStatic</c>,
	///     <c>Attribs</c> and, for a static field, <c>Address</c> (<c>dotnetinfo.lua</c>, <c>getClassFields</c>);
	///     older builds only the positional <c>{Offset, FieldType, Name}</c>.
	/// </summary>
	private const string FieldShape = """
	                                  local function text(v) if type(v) == 'string' then return v end return nil end
	                                  local function integer(v) if math.type(v) == 'integer' then return v end return nil end
	                                  local function shape(field)
	                                    local kind = field.FieldType or field.fieldType or field[2]
	                                    local static = field.IsStatic or field.isStatic
	                                    local address = integer(field.Address or field.address)
	                                    return {name = tostring(field.Name or field.name or field[3] or ''), offset = integer(field.Offset or field.offset or field[1]),
	                                      fieldType = text(field.FieldTypeClassName or field.fieldTypeClassName) or text(kind), elementType = integer(kind),
	                                      isStatic = static == true, attributes = integer(field.Attribs or field.attribs),
	                                      staticAddress = static == true and address ~= nil and address ~= 0 and mcp.hex(address) or nil}
	                                  end
	                                  """ + "\n";

	internal const string Type = FieldShape + """
	                                          local collector = getDotNetDataCollector()
	                                          if collector == nil then return mcp.err('not_attached', 'The .NET data collector is unavailable.', 'not_started') end
	                                          local module, token = tonumber(a[1]), tonumber(a[2])
	                                          if module == nil or token == nil then return mcp.err('invalid_argument', 'moduleHandle and typeToken must be opaque numeric values from the .NET collector.', 'not_started') end
	                                          local data = collector.getTypeDefData(module, token)
	                                          if data == nil then return mcp.err('not_found', 'The .NET collector did not find that type.', 'not_started', 'List the module types again; tokens are scoped to the collector session.') end
	                                          local fields, source = {}, data.Fields or data.fields or data[7] or {}
	                                          for i = 1, math.min(#source, a[3]) do
	                                            local field = shape(source[i])
	                                            field.offset = field.offset or 0
	                                            fields[#fields + 1] = field
	                                          end
	                                          -- The base type can live in another module: getTypeDefParent reports {ModuleHandle, TypedefToken}.
	                                          local baseType, baseModule, baseToken = nil, nil, nil
	                                          local found, parent = pcall(function() return collector.getTypeDefParent(module, token) end)
	                                          if found and type(parent) == 'table' then
	                                            local parentModule = integer(parent.ModuleHandle or parent.moduleHandle or parent[1])
	                                            local parentToken = integer(parent.TypedefToken or parent.TypeDefToken or parent.typedefToken or parent[2])
	                                            if parentModule ~= nil and parentModule ~= 0 and parentToken ~= nil and (parentToken & 0xFFFFFF) ~= 0 then
	                                              baseModule, baseToken = tostring(parentModule), tostring(parentToken)
	                                              local named, parentData = pcall(function() return collector.getTypeDefData(parentModule, parentToken) end)
	                                              if named and type(parentData) == 'table' then baseType = text(parentData.ClassName or parentData.className or parentData[6]) end
	                                            end
	                                          end
	                                          return {moduleHandle = a[1], typeToken = a[2], name = text(data.ClassName or data.className or data[6]), baseType = baseType,
	                                            baseTypeModuleHandle = baseModule, baseTypeToken = baseToken,
	                                            objectType = integer(data.ObjectType or data.objectType or data[1]), elementType = integer(data.ElementType or data.elementType or data[2]),
	                                            countOffset = integer(data.CountOffset or data.countOffset or data[3]), elementSize = integer(data.ElementSize or data.elementSize or data[4]),
	                                            firstElementOffset = integer(data.FirstElementOffset or data.firstElementOffset or data[5]), fields = fields}
	                                          """;

	/// <summary>
	///     Pages a type's methods; <c>a[5]</c>, when present, keeps only the methods whose name contains it, and
	///     <c>total</c> then counts the matches.
	/// </summary>
	internal const string Methods = NameFilter + """
	                                             local collector = getDotNetDataCollector()
	                                             if collector == nil then return mcp.err('not_attached', 'The .NET data collector is unavailable.', 'not_started') end
	                                             local module, token = tonumber(a[1]), tonumber(a[2])
	                                             if module == nil or token == nil then return mcp.err('invalid_argument', 'moduleHandle and typeToken must be opaque numeric values from the .NET collector.', 'not_started') end
	                                             local methods = collector.getTypeDefMethods(module, token)
	                                             if methods == nil then return mcp.err('not_found', 'The .NET collector did not find that type.', 'not_started') end
	                                             methods = keep(methods, a[5], function(item) return tostring(item.Name or item[2] or '') end)
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

	/// <summary>
	///     Reads one field value locally the way Cheat Engine's <c>getDotNetValueReader</c> does, by CorElementType:
	///     primitives as decimal text, booleans as true or false, references and pointers as hexadecimal addresses.
	///     Value types and unknown codes have no value. Mono and IL2CPP number their field types the same way, so
	///     <c>mono_get_object</c> shares this reader; it also defines <c>wide</c>, whether the target is 64-bit.
	/// </summary>
	internal const string ValueReader = """
	                                   local wide = type(targetIs64Bit) ~= 'function' or targetIs64Bit()
	                                   local function unsigned(v)
	                                     if v == nil then return nil end
	                                     if v >= 0 then return tostring(v) end
	                                     local q = (v >> 1) // 5
	                                     return tostring(q) .. tostring(v - q * 10)
	                                   end
	                                   local function number(v) if v == nil then return nil end return tostring(mcp.num(v)) end
	                                   local function signed(v, bits) if v == nil then return nil end if v >= 1 << (bits - 1) then v = v - (1 << bits) end return tostring(v) end
	                                   local references = {[14] = true, [15] = true, [18] = true, [20] = true, [27] = true, [28] = true, [29] = true}
	                                   local function read(kind, at)
	                                     if kind == 2 then local v = readBytes(at, 1); if v == nil then return nil end return v ~= 0 and 'true' or 'false'
	                                     elseif kind == 3 or kind == 7 then local v = readSmallInteger(at); return v and tostring(v & 0xFFFF)
	                                     elseif kind == 4 then return signed(readBytes(at, 1), 8)
	                                     elseif kind == 5 then local v = readBytes(at, 1); return v and tostring(v)
	                                     elseif kind == 6 then local v = readSmallInteger(at); return v and signed(v & 0xFFFF, 16)
	                                     elseif kind == 8 then local v = readInteger(at); return v and signed(v & 0xFFFFFFFF, 32)
	                                     elseif kind == 9 then local v = readInteger(at); return v and tostring(v & 0xFFFFFFFF)
	                                     elseif kind == 10 then local v = readQword(at); return v and tostring(v)
	                                     elseif kind == 11 then return unsigned(readQword(at))
	                                     elseif kind == 12 then return number(readFloat(at))
	                                     elseif kind == 13 then return number(readDouble(at))
	                                     elseif kind == 24 then if wide then local v = readQword(at); return v and tostring(v) end local v = readInteger(at); return v and signed(v & 0xFFFFFFFF, 32)
	                                     elseif kind == 25 then if wide then return unsigned(readQword(at)) end local v = readInteger(at); return v and tostring(v & 0xFFFFFFFF)
	                                     elseif references[kind] then local v = readPointer(at); return v and mcp.hex(v)
	                                     end
	                                     return nil
	                                   end
	                                   """ + "\n";

	internal const string Object = FieldShape + ValueReader + """
	                                                            local collector = getDotNetDataCollector()
	                                                            if collector == nil then return mcp.err('not_attached', 'The .NET data collector is unavailable.', 'not_started') end
	                                                            local address = getAddressSafe(a[1])
	                                                            if address == nil then return mcp.err('invalid_argument', 'address could not be resolved by Cheat Engine.', 'not_started') end
	                                                            local data = collector.getAddressData(address)
	                                                            local startAddress = data and (data.StartAddress or data.startAddress or data[1]) or nil
	                                                            if startAddress == nil or startAddress == 0 then return mcp.err('not_found', 'The .NET collector did not recognize an object at that address.', 'not_started') end
	                                                            local fields, source = {}, data.Fields or data.fields or data[8] or {}
	                                                            for i = 1, math.min(#source, a[2]) do
	                                                              local field = shape(source[i])
	                                                              local at = nil
	                                                              if field.isStatic then
	                                                                if field.staticAddress ~= nil then at = tonumber(field.staticAddress, 16) end
	                                                              elseif field.offset ~= nil then
	                                                                at = startAddress + field.offset
	                                                              end
	                                                              local value = nil
	                                                              if at ~= nil and field.elementType ~= nil then
	                                                                local ok, read_value = pcall(read, field.elementType, at)
	                                                                if ok then value = read_value end
	                                                              end
	                                                              fields[#fields + 1] = {name = field.name, value = value, fieldType = field.fieldType, offset = field.offset, elementType = field.elementType}
	                                                            end
	                                                            return {address = string.format('%X', startAddress), typeName = text(data.ClassName or data.className or data.TypeName or data.typeName or data[7]), fields = fields}
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

	/// <summary>
	///     Reads a method's parameters. Cheat Engine 7.7's collector returns <c>{ {Name, CType}, ... }</c> and the
	///     signature text as a second value (<c>dotnetinfo.lua</c>, <c>getClassMethods</c>).
	/// </summary>
	internal const string MethodParameters = """
	                                         local collector = getDotNetDataCollector()
	                                         if collector == nil then return mcp.err('not_attached', 'The .NET data collector is unavailable.', 'not_started', 'Attach a .NET target and check dotnet_get_status.') end
	                                         local module, token = tonumber(a[1]), tonumber(a[2])
	                                         if module == nil or token == nil then return mcp.err('invalid_argument', 'moduleHandle and methodToken must be opaque numeric values from the .NET collector.', 'not_started') end
	                                         local list, signature = collector.getMethodParameters(module, token)
	                                         if type(list) ~= 'table' then return mcp.err('not_found', 'The .NET collector did not report parameters for that method.', 'not_started', 'List the type methods again; tokens are scoped to the collector session.') end
	                                         local items = {}
	                                         for i = 1, #list do
	                                           local item = list[i]
	                                           local name = item.Name or item.name or item[1]
	                                           local kind = item.CType or item.ctype or item.cType or item[2]
	                                           items[i] = {index = i - 1, name = type(name) == 'string' and name or '', elementType = math.type(kind) == 'integer' and kind or 0}
	                                         end
	                                         return {moduleHandle = a[1], methodToken = a[2], parameters = items, signature = type(signature) == 'string' and signature ~= '' and signature or nil}
	                                         """;
}
