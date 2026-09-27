namespace CheatEngine.Mcp.Tools.Mono;

/// <summary>Fixed Lua bodies over Cheat Engine's Mono extension. Every caller value arrives through <c>a</c>.</summary>
internal static class MonoLuaScripts
{
	private const string Attached = """
	                                local function attached()
	                                  if type(getOpenedProcessID) == 'function' and getOpenedProcessID() == 0 then
	                                    return nil, mcp.err('not_attached', 'Cheat Engine has no selected target process.', 'not_started', 'Attach a process before using Mono tools.')
	                                  end
	                                  if monopipe == nil then return nil, mcp.err('not_attached', 'The Mono data collector is not attached to the selected target.', 'not_started', 'Call mono_attach after the target has loaded Mono.') end
	                                  if monoBase == nil or monoBase == 0 then
	                                    return nil, mcp.err('not_attached', 'The Mono collector pipe has no active runtime base.', 'not_started', 'Detach the stale collector and attach again after the target has loaded Mono.')
	                                  end
	                                  if type(debug_isBroken) == 'function' and debug_isBroken() then return nil, mcp.err('invalid_state', 'The debugger is stopped; Mono collector calls can hang while the target is frozen.', 'not_started', 'Continue the debugger, then retry the read-only Mono call.') end
	                                  return true
	                                end
	                                """ + "\n";

	internal const string Status = """
	                               local pipe = monopipe
	                               local attached = pipe ~= nil
	                               local il2cpp = false
	                               if attached then local ok, value = pcall(function() return pipe.IL2CPP end); il2cpp = ok and value == true end
	                               local version = nil
	                               if attached and type(mono_getMonoDatacollectorDLLVersion) == 'function' then version = mono_getMonoDatacollectorDLLVersion() end
	                               return {attached = attached, il2Cpp = il2cpp, collectorVersion = version}
	                               """;

	internal const string Attach = """
	                               if type(getOpenedProcessID) == 'function' and getOpenedProcessID() == 0 then
	                                 return mcp.err('not_attached', 'Cheat Engine has no selected target process.', 'not_started', 'Attach a process after it has loaded Mono.')
	                               end
	                               if type(debug_isBroken) == 'function' and debug_isBroken() then
	                                 return mcp.err('invalid_state', 'The debugger is stopped; attaching the Mono collector can hang while the target is frozen.', 'not_started', 'Continue the debugger, then attach Mono.')
	                               end
	                               if monopipe ~= nil then
	                                 return mcp.err('busy', 'The Mono collector is already attached outside this MCP activation.', 'not_started', 'Use the existing Cheat Engine session or detach it in Cheat Engine before MCP creates a new attachment.')
	                               end
	                               if type(mono_initialize) == 'function' then pcall(mono_initialize) end
	                               local result = LaunchMonoDataCollector()
	                               if result == nil or result == 0 or monopipe == nil then
	                                 return mcp.err('host_refused', 'Cheat Engine did not attach its Mono data collector to the selected target.', 'unknown', 'Check that Mono is loaded, the target is running, then use mono_get_status before retrying.')
	                               end
	                               local il2cpp = false
	                               local ok, value = pcall(function() return monopipe.IL2CPP end)
	                               if ok then il2cpp = value == true end
	                               local version = nil
	                               if type(mono_getMonoDatacollectorDLLVersion) == 'function' then version = mono_getMonoDatacollectorDLLVersion() end
	                               return {attached = true, il2Cpp = il2cpp, collectorVersion = version,
	                                 hostEffects = {'collector_injected', 'mono_error_ok_patched', 'cheat_engine_hooks_installed', 'uses_mono_table_option'}}
	                               """;

	internal const string Detach = """
	                               if monopipe ~= nil then
	                                 -- This follows monoscript.lua's own Ctrl+Activate branch: send the
	                                 -- collector terminate command while holding the pipe lock, then run
	                                 -- its timeout handler without a self argument.  The latter releases
	                                 -- the pipe and clears the script's attachment globals.
	                                 local pipe, locked = monopipe, false
	                                 local ok = pcall(function()
	                                   pipe.lock()
	                                   locked = true
	                                   pipe.writeByte(MONOCMD_TERMINATE)
	                                   pipe.unlock()
	                                   locked = false
	                                   if type(pipe.OnTimeout) == 'function' then
	                                     pipe.OnTimeout()
	                                   else
	                                     pipe.destroy()
	                                     monopipe = nil
	                                   end
	                                 end)
	                                 if locked then pcall(function() pipe.unlock() end) end
	                                 if not ok then return mcp.err('host_refused', 'Cheat Engine could not close the Mono collector pipe.', 'unknown', 'Restart the target before trying another collector attach.') end
	                               end
	                               if monoeventpipe ~= nil then pcall(function() monoeventpipe.destroy() end); monoeventpipe = nil end
	                               if mono_AddressLookupID ~= nil and type(unregisterAddressLookupCallback) == 'function' then pcall(unregisterAddressLookupCallback, mono_AddressLookupID); mono_AddressLookupID = nil end
	                               if mono_SymbolLookupID ~= nil and type(unregisterSymbolLookupCallback) == 'function' then pcall(unregisterSymbolLookupCallback, mono_SymbolLookupID); mono_SymbolLookupID = nil end
	                               if mono_StructureNameLookupID ~= nil and type(unregisterStructureNameLookup) == 'function' then pcall(unregisterStructureNameLookup, mono_StructureNameLookupID); mono_StructureNameLookupID = nil end
	                               if mono_StructureDissectOverrideID ~= nil and type(unregisterStructureDissectOverride) == 'function' then pcall(unregisterStructureDissectOverride, mono_StructureDissectOverrideID); mono_StructureDissectOverrideID = nil end
	                               if StructureElementCallbackID ~= nil and type(unregisterStructureAndElementListCallback) == 'function' then pcall(unregisterStructureAndElementListCallback, StructureElementCallbackID); StructureElementCallbackID = nil end
	                               mono_AttachedProcess = 0
	                               monoBase = 0
	                               return {detached = true, remainingEffects = {'collector_dll_remains_loaded', 'mono_error_ok_patch_remains_until_target_restart'}}
	                               """;

	internal const string Assemblies = Attached + """
	                                              local _, refusal = attached(); if refusal then return refusal end
	                                              local assemblies = mono_enumAssemblies()
	                                              if assemblies == nil then return mcp.err('host_refused', 'Cheat Engine did not enumerate Mono assemblies.', 'unknown') end
	                                              local offset, limit, items = a[1], a[2], {}
	                                              for i = offset + 1, math.min(#assemblies, offset + limit) do
	                                                local assembly = assemblies[i]
	                                                local image = mono_getImageFromAssembly(assembly)
	                                                items[#items + 1] = {assemblyHandle = tostring(assembly), imageHandle = tostring(image), name = tostring(mono_image_get_name(image) or '')}
	                                              end
	                                              local nextOffset = offset + #items < #assemblies and offset + #items or nil
	                                              return {assemblies = items, total = #assemblies, nextOffset = nextOffset}
	                                              """;

	internal const string Classes = Attached + """
	                                           local _, refusal = attached(); if refusal then return refusal end
	                                           local image = tonumber(a[1]); if image == nil then return mcp.err('invalid_argument', 'imageHandle must be a Mono image handle.', 'not_started') end
	                                           local classes = mono_image_enumClasses(image)
	                                           if classes == nil then return mcp.err('not_found', 'Cheat Engine did not find that Mono image.', 'not_started', 'List assemblies again; handles are scoped to the current collector attach.') end
	                                           local offset, limit, items = a[2], a[3], {}
	                                           for i = offset + 1, math.min(#classes, offset + limit) do
	                                             local class = classes[i]
	                                             items[#items + 1] = {handle = tostring(class.class), name = tostring(class.classname or ''), namespace = tostring(class.namespace or '')}
	                                           end
	                                           local nextOffset = offset + #items < #classes and offset + #items or nil
	                                           return {imageHandle = a[1], classes = items, total = #classes, nextOffset = nextOffset}
	                                           """;

	internal const string FindClass = Attached + """
	                                             local _, refusal = attached(); if refusal then return refusal end
	                                             local class = mono_findClass(a[1] or '', a[2])
	                                             if class == nil or class == 0 then return mcp.err('not_found', 'No Mono class matched the supplied namespace and name.', 'not_started', 'List the assembly classes to verify the namespace and class name.') end
	                                             return {handle = tostring(class), name = tostring(mono_class_getName(class) or a[2]), namespace = tostring(mono_class_getNamespace(class) or a[1] or '')}
	                                             """;

	internal const string Fields = Attached + """
	                                          local _, refusal = attached(); if refusal then return refusal end
	                                          local class = tonumber(a[1]); if class == nil then return mcp.err('invalid_argument', 'classHandle must be a Mono class handle.', 'not_started') end
	                                          local fields = mono_class_enumFields(class, a[2])
	                                          if fields == nil then return mcp.err('not_found', 'Cheat Engine did not find that Mono class.', 'not_started') end
	                                          local items = {}
	                                          for i = 1, math.min(#fields, a[3]) do
	                                            local field = fields[i]
	                                            items[#items + 1] = {handle = tostring(field.field), name = tostring(field.name or ''), typeName = field.typename,
	                                              offset = field.offset or 0, isStatic = field.isStatic == true, flags = field.flags}
	                                          end
	                                          return {classHandle = a[1], fields = items}
	                                          """;

	internal const string Methods = Attached + """
	                                           local _, refusal = attached(); if refusal then return refusal end
	                                           local class = tonumber(a[1]); if class == nil then return mcp.err('invalid_argument', 'classHandle must be a Mono class handle.', 'not_started') end
	                                           local methods = mono_class_enumMethods(class)
	                                           if methods == nil then return mcp.err('not_found', 'Cheat Engine did not find that Mono class.', 'not_started') end
	                                           local offset, limit, items = a[2], a[3], {}
	                                           for i = offset + 1, math.min(#methods, offset + limit) do
	                                             local method = methods[i]
	                                             local signature, parameters, returnType = nil, nil, nil
	                                             if type(mono_method_getSignature) == 'function' then signature, parameters, returnType = mono_method_getSignature(method.method) end
	                                             items[#items + 1] = {handle = tostring(method.method), name = tostring(method.name or ''), signature = signature, returnType = returnType}
	                                           end
	                                           local nextOffset = offset + #items < #methods and offset + #items or nil
	                                           return {classHandle = a[1], methods = items, total = #methods, nextOffset = nextOffset}
	                                           """;

	internal const string FindMethod = Attached + """
	                                              local _, refusal = attached(); if refusal then return refusal end
	                                              local class = tonumber(a[1]); if class == nil then return mcp.err('invalid_argument', 'classHandle must be a Mono class handle.', 'not_started') end
	                                              local method = mono_class_findMethod(class, a[2])
	                                              if method == nil or method == 0 then return mcp.err('not_found', 'No Mono method with that name belongs to the class.', 'not_started', 'List methods to inspect overloads and spelling.') end
	                                              local signature, parameters, returnType = nil, nil, nil
	                                              if type(mono_method_getSignature) == 'function' then signature, parameters, returnType = mono_method_getSignature(method) end
	                                              return {handle = tostring(method), name = tostring(mono_method_getName(method) or a[2]), signature = signature, returnType = returnType}
	                                              """;

	internal const string StaticFieldAddress = Attached + """
	                                                      local _, refusal = attached(); if refusal then return refusal end
	                                                      local class = tonumber(a[1]); if class == nil then return mcp.err('invalid_argument', 'classHandle must be a Mono class handle.', 'not_started') end
	                                                      local domain = a[2] and tonumber(a[2]) or nil
	                                                      if domain == nil then local domains = mono_enumDomains(); domain = domains and domains[1] or nil end
	                                                      if domain == nil then return mcp.err('not_found', 'No Mono application domain is available for the class static data.', 'not_started') end
	                                                      local address = mono_class_getStaticFieldAddress(domain, class)
	                                                      if address == nil or address == 0 then return mcp.err('not_found', 'Cheat Engine did not report static field data for the class.', 'not_started') end
	                                                      return {classHandle = a[1], domainHandle = tostring(domain), address = string.format('%X', address)}
	                                                      """;

	internal const string CompileMethod = Attached + """
	                                                 local _, refusal = attached(); if refusal then return refusal end
	                                                 local method = tonumber(a[1]); if method == nil then return mcp.err('invalid_argument', 'methodHandle must be a Mono method handle.', 'not_started') end
	                                                 local address = mono_compile_method(method)
	                                                 if address == nil or address == 0 then return mcp.err('host_refused', 'Mono did not JIT-compile the method.', 'unknown', 'Check the target is running and that the method is supported by the active Mono mode.') end
	                                                 return {methodHandle = a[1], nativeAddress = string.format('%X', address)}
	                                                 """;

	internal const string InvokeMethod = Attached + """
	                                                local _, refusal = attached(); if refusal then return refusal end
	                                                local method = tonumber(a[1]); if method == nil then return mcp.err('invalid_argument', 'methodHandle must be a Mono method handle.', 'not_started') end
	                                                local instance = 0
	                                                if a[2] ~= nil then
	                                                  instance = getAddressSafe(a[2])
	                                                  if instance == nil or instance == 0 then
	                                                    return mcp.err('invalid_argument', 'instanceAddress could not be resolved by Cheat Engine.', 'not_started')
	                                                  end
	                                                end
	                                                local supplied, args = a[3] or {}, {}
	                                                for i = 1, #supplied do args[i] = {type = supplied[i][1], value = supplied[i][2]} end
	                                                local parameters = mono_method_get_parameters(method)
	                                                if parameters == nil or parameters.parameters == nil then
	                                                  return mcp.err('host_refused', 'Cheat Engine could not read the Mono method parameters before invocation.', 'not_started', 'List methods again after confirming the collector is healthy.')
	                                                end
	                                                local result, exception = mono_invoke_method('', method, instance, args)
	                                                return {invoked = true, returnValue = result ~= nil and tostring(result) or nil,
	                                                  exception = exception ~= nil and tostring(exception) or nil}
	                                                """;

	internal const string Instances = Attached + """
	                                             local _, refusal = attached(); if refusal then return refusal end
	                                             local class = tonumber(a[1]); if class == nil then return mcp.err('invalid_argument', 'classHandle must be a Mono class handle.', 'not_started') end
	                                             local values = mono_class_findInstancesOfClassListOnly('', class)
	                                             if values == nil then return mcp.err('host_refused', 'Cheat Engine did not complete the Mono instance lookup.', 'unknown', 'Check the target is running; do not retry after a timeout until the collector is healthy.') end
	                                             local items = {}
	                                             for i = 1, math.min(#values, a[2]) do items[#items + 1] = {address = string.format('%X', values[i])} end
	                                             return {instances = items, total = #values}
	                                             """;
}
