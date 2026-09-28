using CheatEngine.Mcp.Tools.DotNet;

namespace CheatEngine.Mcp.Tools.Mono;

/// <summary>Fixed Lua bodies over Cheat Engine's Mono extension. Every caller value arrives through <c>a</c>.</summary>
/// <remarks>
///     <para>
///         Cheat Engine 7.7's <c>autorun\monoscript.lua</c> keeps its collector state in the global table
///         <c>libmono</c>: <c>ProcessID</c> (the process the collector is attached to, cleared by
///         <c>libmono.terminate</c>), <c>IL2CPP</c>, <c>abort</c> (a connection aborted in Cheat Engine),
///         <c>fail</c> (a pipe connection or call timed out or failed; only a new connection in <c>getMonoPipe</c>
///         clears it), <c>displayingTimeoutDialog</c>, <c>monopipes</c> (one pipe per thread) and <c>HeartBeat</c> (a
///         thread replaced on every collector launch). The bodies read those fields with <c>rawget</c>, because reading
///         <c>libmono.monopipe</c> connects a new pipe on demand; the guard does so on purpose, once, to recover from
///         <c>fail</c>.
///     </para>
///     <para>
///         <see cref="Attach" /> records the attachment it made in the Lua global <c>__cheatengine_mcp_mono</c>
///         (resource id, process id, heart beat), and <see cref="Detach" /> closes the collector only while that record
///         still describes the current attachment, so an attachment Cheat Engine or the user made is never torn down.
///     </para>
/// </remarks>
internal static class MonoLuaScripts
{
	/// <summary>The shared <c>keep</c> filter of <see cref="DotNetLuaScripts.NameFilter" />.</summary>
	private const string NameFilter = DotNetLuaScripts.NameFilter;

	private const string Attached = """
	                                -- MCP can close only an attachment this activation made with mono_attach; a
	                                -- detach that reports not_found or alreadyEnded leaves Cheat Engine's own.
	                                local recovery = "Call mono_detach. If it reports not_found or alreadyEnded,"
	                                  .. " Cheat Engine owns the current attachment: ask the user to deactivate, then"
	                                  .. " re-activate Mono features in Cheat Engine's Mono menu. Otherwise call"
	                                  .. " mono_attach while the target runs normally."
	                                local function attached()
	                                  local pid =type(getOpenedProcessID) == 'function' and getOpenedProcessID() or 0
	                                  if pid == nil or pid == 0 then
	                                    return nil, mcp.err('not_attached', 'Cheat Engine has no selected target process.', 'not_started', 'Attach a process before using Mono tools.')
	                                  end
	                                  local owner = type(libmono) == 'table' and rawget(libmono, 'ProcessID') or nil
	                                  if owner == nil or tonumber(owner) ~= pid then
	                                    return nil, mcp.err('not_attached', 'The Mono data collector is not attached to the selected target.', 'not_started', 'Call mono_attach after the target has loaded Mono; if mono_detach still reports an attachment, detach it first.')
	                                  end
	                                  if rawget(libmono, 'abort') == true then
	                                    local aborted = 'Cheat Engine aborted its Mono collector connection.'
	                                    return nil, mcp.err('not_attached', aborted, 'not_started', recovery)
	                                  end
	                                  -- Checked before any pipe is touched: a frozen target cannot answer a new
	                                  -- connection either.
	                                  if type(debug_isBroken) == 'function' and debug_isBroken() then return nil, mcp.err('invalid_state', 'The debugger is stopped; Mono collector calls can hang while the target is frozen.', 'not_started', 'Continue the debugger, then retry the Mono call.') end
	                                  if type(isPaused) == 'function' and isPaused() then return nil, mcp.err('invalid_state', 'The target is paused, so the collector cannot answer and Cheat Engine would show its Mono timeout dialog.', 'not_started', 'Resume the target with process_set_paused, then retry the Mono call.') end
	                                  -- While that dialog is up, getMonoPipe gives the main thread no pipe, and the
	                                  -- pipe the dialog waits on must stay open.
	                                  if rawget(libmono, 'displayingTimeoutDialog') == true then
	                                    return nil, mcp.err('invalid_state', 'Cheat Engine is showing its Mono timeout dialog for an earlier collector call.',
	                                      'not_started', 'Ask the user to answer the dialog in Cheat Engine (Wait, Cancel or Abort), then retry the Mono call.')
	                                  end
	                                  if rawget(libmono, 'fail') == true then
	                                    -- A pipe that timed out or failed sets fail, only a new connection in
	                                    -- getMonoPipe clears it, and getMonoPipe keeps reusing a connected pipe:
	                                    -- close this thread's pipe, then connect once. true skips the
	                                    -- libmono.terminate of the main thread's destroy override, which closes
	                                    -- whatever the global monopipe holds, so the global names the stale pipe.
	                                    -- A refusal after the pipe was closed reports that change as started.
	                                    local pipes, tid = rawget(libmono, 'monopipes'), nil
	                                    if type(getCurrentThreadID) == 'function' then tid = getCurrentThreadID() end
	                                    local stale = type(pipes) == 'table' and tid ~= nil and pipes[tid] or nil
	                                    local effect, closed = 'not_applied', ''
	                                    if stale ~= nil then
	                                      effect, closed = 'started', ' The failed pipe was closed.'
	                                      pipes[tid] = nil
	                                      local alias = monopipe
	                                      monopipe = stale
	                                      pcall(function() stale.destroy(true) end)
	                                      if rawequal(alias, stale) then monopipe = nil else monopipe = alias end
	                                    end
	                                    local connected, pipe = pcall(function() return libmono.monopipe end)
	                                    if not connected or pipe == nil or rawget(libmono, 'fail') == true then
	                                      return nil, mcp.err('invalid_state', 'The Mono collector pipe reported a timeout or an error, and Cheat Engine could not open a new connection to the collector.' .. closed,
	                                        effect, recovery)
	                                    end
	                                  end
	                                  return true
	                                end
	                                """ + "\n";

	internal const string Status = """
	                               local pid = type(getOpenedProcessID) == 'function' and getOpenedProcessID() or 0
	                               local owner = type(libmono) == 'table' and rawget(libmono, 'ProcessID') or nil
	                               local attached = pid ~= nil and pid ~= 0 and owner ~= nil and tonumber(owner) == pid and rawget(libmono, 'abort') ~= true
	                               local il2cpp, version, domains = false, nil, nil
	                               if attached then
	                                 il2cpp = rawget(libmono, 'IL2CPP') == true
	                                 -- A pipe that timed out answers out of step and a paused target cannot answer: skip the pipe calls.
	                                 if rawget(libmono, 'fail') ~= true and not (type(isPaused) == 'function' and isPaused()) then
	                                   if type(mono_getMonoDatacollectorDLLVersion) == 'function' then version = mono_getMonoDatacollectorDLLVersion() end
	                                   if type(mono_enumDomains) == 'function' then
	                                     local list = mono_enumDomains()
	                                     if type(list) == 'table' then
	                                       domains = {}
	                                       for i = 1, #list do domains[i] = tostring(list[i]) end
	                                     end
	                                   end
	                                 end
	                               end
	                               return {attached = attached, il2Cpp = il2cpp, collectorVersion = version, domains = domains}
	                               """;

	internal const string Attach = """
	                               local pid = type(getOpenedProcessID) == 'function' and getOpenedProcessID() or 0
	                               if pid == nil or pid == 0 then
	                                 return mcp.err('not_attached', 'Cheat Engine has no selected target process.', 'not_started', 'Attach a process after it has loaded Mono.')
	                               end
	                               if type(LaunchMonoDataCollector) ~= 'function' or type(libmono) ~= 'table' then
	                                 return mcp.err('unsupported', 'Cheat Engine has not loaded its Mono extension (autorun monoscript.lua).', 'not_started')
	                               end
	                               if type(debug_isBroken) == 'function' and debug_isBroken() then
	                                 return mcp.err('invalid_state', 'The debugger is stopped; attaching the Mono collector can hang while the target is frozen.', 'not_started', 'Continue the debugger, then attach Mono.')
	                               end
	                               if type(isPaused) == 'function' and isPaused() then
	                                 return mcp.err('invalid_state', 'The target is paused; Cheat Engine refuses the Mono attach with a dialog while the process is frozen.', 'not_started', 'Resume the target with process_set_paused, then attach Mono.')
	                               end
	                               local owner = rawget(libmono, 'ProcessID')
	                               if (owner ~= nil and tonumber(owner) == pid) or mono_AttachedProcess == pid then
	                                 return mcp.err('busy', 'The Mono collector is already attached outside this MCP activation.', 'not_started', 'Use the existing Cheat Engine session or detach it in Cheat Engine before MCP creates a new attachment.')
	                               end
	                               local result = LaunchMonoDataCollector()
	                               owner = rawget(libmono, 'ProcessID')
	                               if result == nil or result == false or result == 0 or owner == nil or tonumber(owner) ~= pid then
	                                 return mcp.err('host_refused', 'Cheat Engine did not attach its Mono data collector to the selected target.', 'unknown', 'Check that Mono is loaded, the target is running, then use mono_get_status before retrying.')
	                               end
	                               local version = nil
	                               if type(mono_getMonoDatacollectorDLLVersion) == 'function' then version = mono_getMonoDatacollectorDLLVersion() end
	                               local il2cpp = rawget(libmono, 'IL2CPP') == true
	                               -- Cheat Engine patches mono_error_ok only in mono-2.0-bdwgc, never on IL2CPP, and sets the table
	                               -- option only when its EnableUsesMonoOptionOnAttach setting is on.
	                               local effects = {'collector_injected'}
	                               if not il2cpp then effects[#effects + 1] = 'mono_error_ok_patched' end
	                               effects[#effects + 1] = 'cheat_engine_hooks_installed'
	                               local optionRead, optionSet = false, nil
	                               if type(getTableOption) == 'function' then optionRead, optionSet = pcall(getTableOption, 'UsesMono') end
	                               if not optionRead or (optionSet ~= nil and optionSet ~= false) then effects[#effects + 1] = 'uses_mono_table_option' end
	                               -- Cheat Engine replaces libmono.HeartBeat on every launch of its collector, so
	                               -- this record tells the attachment (a[1] is its resource id) from any later one.
	                               local record = {id = a[1], processId = pid}
	                               record.heartBeat = rawget(libmono, 'HeartBeat')
	                               rawset(_ENV, '__cheatengine_mcp_mono', record)
	                               return {attached = true, il2Cpp = il2cpp, collectorVersion = version, hostEffects = effects}
	                               """;

	internal const string Detach = """
	                               -- Only the attachment this activation made (a[1] is its resource id) is closed.
	                               -- Another heart beat or process means it already ended outside this call (a
	                               -- detach, a re-activation or a target switch in Cheat Engine), and the current
	                               -- attachment then stays as it is. An incomplete detach of this attachment
	                               -- already cleared ProcessID, so its retry runs the teardown again.
	                               local mono = type(libmono) == 'table' and libmono or nil
	                               local record = rawget(_ENV, '__cheatengine_mcp_mono')
	                               local mine = type(record) == 'table' and record.id ~= nil and record.id == a[1]
	                               if mine then rawset(_ENV, '__cheatengine_mcp_mono', nil) end
	                               local remaining = {'collector_dll_remains_loaded',
	                                 'mono_error_ok_patch_remains_until_target_restart',
	                                 'uses_mono_table_option_remains'}
	                               local owned = mine and mono ~= nil
	                                 and rawequal(rawget(mono, 'HeartBeat'), record.heartBeat)
	                               if owned then
	                                 local current = tonumber(rawget(mono, 'ProcessID'))
	                                 owned = current == record.processId
	                                   or (current == nil and record.incomplete == true)
	                               end
	                               if not owned then
	                                 return {detached = true, alreadyEnded = true, remainingEffects = remaining}
	                               end
	                               -- Cheat Engine's own detach (Ctrl+click on Activate mono features): send the
	                               -- collector its terminate command, call libmono.terminate on the main thread,
	                               -- then clear the process fields. The main-thread pipe serves the selected
	                               -- process, so the command is sent only while that is the recorded one.
	                               local pid = type(getOpenedProcessID) == 'function' and getOpenedProcessID() or 0
	                               local pipes, pipe = rawget(mono, 'monopipes'), nil
	                               if pid == record.processId then
	                                 if type(pipes) == 'table' and type(getCurrentThreadID) == 'function' then
	                                   pipe = pipes[getCurrentThreadID()]
	                                 end
	                                 if pipe == nil then pipe = monopipe end
	                               end
	                               if pipe ~= nil and MONOCMD_TERMINATE ~= nil then
	                                 pcall(function() pipe.writeByte(MONOCMD_TERMINATE) end)
	                               end
	                               local closed, reason = true, nil
	                               if type(rawget(mono, 'terminate')) == 'function' then
	                                 closed, reason = pcall(mono.terminate)
	                               end
	                               if monoeventpipe ~= nil then pcall(function() monoeventpipe.destroy() end); monoeventpipe = nil end
	                               if mono_AddressLookupID ~= nil and type(unregisterAddressLookupCallback) == 'function' then pcall(unregisterAddressLookupCallback, mono_AddressLookupID); mono_AddressLookupID = nil end
	                               if mono_SymbolLookupID ~= nil and type(unregisterSymbolLookupCallback) == 'function' then pcall(unregisterSymbolLookupCallback, mono_SymbolLookupID); mono_SymbolLookupID = nil end
	                               if mono_StructureNameLookupID ~= nil and type(unregisterStructureNameLookup) == 'function' then pcall(unregisterStructureNameLookup, mono_StructureNameLookupID); mono_StructureNameLookupID = nil end
	                               if mono_StructureDissectOverrideID ~= nil and type(unregisterStructureDissectOverride) == 'function' then pcall(unregisterStructureDissectOverride, mono_StructureDissectOverrideID); mono_StructureDissectOverrideID = nil end
	                               if StructureElementCallbackID ~= nil and type(unregisterStructureAndElementListCallback) == 'function' then pcall(unregisterStructureAndElementListCallback, StructureElementCallbackID); StructureElementCallbackID = nil end
	                               mono.ProcessID = nil
	                               mono_AttachedProcess = nil
	                               if not closed then
	                                 record.incomplete = true
	                                 rawset(_ENV, '__cheatengine_mcp_mono', record)
	                                 return mcp.err('host_refused', 'Cheat Engine could not close its Mono collector connection: ' .. tostring(reason), 'started', 'Retry with runtime_release_resources, or restart the target.')
	                               end
	                               return {detached = true, remainingEffects = remaining}
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

	/// <summary>
	///     Pages an image's classes; <c>a[4]</c>, when present, keeps only the classes whose namespace-qualified name
	///     (<c>Namespace.Name</c>) contains it, and <c>total</c> then counts the matches.
	/// </summary>
	internal const string Classes = Attached + NameFilter + """
	                                                        local _, refusal = attached(); if refusal then return refusal end
	                                                        local image = tonumber(a[1]); if image == nil then return mcp.err('invalid_argument', 'imageHandle must be a Mono image handle.', 'not_started') end
	                                                        local classes = mono_image_enumClasses(image)
	                                                        if classes == nil then return mcp.err('not_found', 'Cheat Engine did not find that Mono image.', 'not_started', 'List assemblies again; handles are scoped to the current collector attach.') end
	                                                        classes = keep(classes, a[4], function(class)
	                                                          local name, namespace = tostring(class.classname or ''), tostring(class.namespace or '')
	                                                          return namespace ~= '' and namespace .. '.' .. name or name
	                                                        end)
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

	/// <summary>
	///     <c>fieldsOf(class, includeParents)</c>: Cheat Engine's <c>mono_class_enumFields</c> without its per-class
	///     cache.
	/// </summary>
	private const string FieldList = """
	                                 local function fieldsOf(class, includeParents)
	                                   -- Cheat Engine caches one field list per class and ignores includeParents when it reads the
	                                   -- cache, so the entry is set aside for this call and restored afterwards.
	                                   local function cache() return type(monocache) == 'table' and type(monocache.fields) == 'table' and monocache.fields or nil end
	                                   local saved = cache()
	                                   local previous = saved and saved[class] or nil
	                                   if saved then saved[class] = nil end
	                                   local fields = mono_class_enumFields(class, includeParents)
	                                   local restored = cache()
	                                   if restored then restored[class] = previous end
	                                   return fields
	                                 end
	                                 """ + "\n";

	/// <summary>
	///     Lists a class's fields; <c>a[4]</c>, when present, keeps only the fields whose name contains it before
	///     <c>a[3]</c> (maximumFields) applies.
	/// </summary>
	internal const string Fields = Attached + NameFilter + FieldList + """
	                                                                   local _, refusal = attached(); if refusal then return refusal end
	                                                                   local class = tonumber(a[1]); if class == nil then return mcp.err('invalid_argument', 'classHandle must be a Mono class handle.', 'not_started') end
	                                                                   local fields = fieldsOf(class, a[2] == true)
	                                                                   if fields == nil then return mcp.err('not_found', 'Cheat Engine did not find that Mono class.', 'not_started') end
	                                                                   fields = keep(fields, a[4], function(field) return tostring(field.name or '') end)
	                                                                   local items = {}
	                                                                   for i = 1, math.min(#fields, a[3]) do
	                                                                     local field = fields[i]
	                                                                     items[#items + 1] = {handle = tostring(field.field), name = tostring(field.name or ''), typeName = field.typename,
	                                                                       offset = field.offset or 0, isStatic = field.isStatic == true, isConst = field.isConst == true, flags = field.flags,
	                                                                       staticAddress = type(field.staticAddress) == 'number' and field.staticAddress ~= 0 and mcp.hex(field.staticAddress) or nil}
	                                                                   end
	                                                                   return {classHandle = a[1], fields = items}
	                                                                   """;

	private const string Signature = """
	                                 local function describe(handle, name, flags)
	                                   local signature, names, returnType = nil, nil, nil
	                                   if type(mono_method_getSignature) == 'function' then signature, names, returnType = mono_method_getSignature(handle) end
	                                   local parameterNames = nil
	                                   if type(names) == 'table' then
	                                     parameterNames = {}
	                                     for j = 1, #names do parameterNames[j] = tostring(names[j]) end
	                                   end
	                                   local isStatic = nil
	                                   if math.type(flags) == 'integer' then isStatic = (flags & 0x10) ~= 0 else flags = nil end
	                                   return {handle = tostring(handle), name = tostring(name or ''), signature = type(signature) == 'string' and signature or nil,
	                                     returnType = type(returnType) == 'string' and returnType or nil, parameterNames = parameterNames,
	                                     flags = flags, isStatic = isStatic}
	                                 end
	                                 """ + "\n";

	/// <summary>
	///     Pages a class's methods; <c>a[4]</c>, when present, keeps only the methods whose name contains it, so only
	///     matching methods cost a signature call, and <c>total</c> then counts the matches.
	/// </summary>
	internal const string Methods = Attached + Signature + NameFilter + """
	                                                                    local _, refusal = attached(); if refusal then return refusal end
	                                                                    local class = tonumber(a[1]); if class == nil then return mcp.err('invalid_argument', 'classHandle must be a Mono class handle.', 'not_started') end
	                                                                    local methods = mono_class_enumMethods(class)
	                                                                    if methods == nil then return mcp.err('not_found', 'Cheat Engine did not find that Mono class.', 'not_started') end
	                                                                    methods = keep(methods, a[4], function(method) return tostring(method.name or '') end)
	                                                                    local offset, limit, items = a[2], a[3], {}
	                                                                    for i = offset + 1, math.min(#methods, offset + limit) do
	                                                                      local method = methods[i]
	                                                                      items[#items + 1] = describe(method.method, method.name, method.flags)
	                                                                    end
	                                                                    local nextOffset = offset + #items < #methods and offset + #items or nil
	                                                                    return {classHandle = a[1], methods = items, total = #methods, nextOffset = nextOffset}
	                                                                    """;

	internal const string FindMethod = Attached + Signature + """
	                                                          local _, refusal = attached(); if refusal then return refusal end
	                                                          local class = tonumber(a[1]); if class == nil then return mcp.err('invalid_argument', 'classHandle must be a Mono class handle.', 'not_started') end
	                                                          local method = mono_class_findMethod(class, a[2])
	                                                          if method == nil or method == 0 then return mcp.err('not_found', 'No Mono method with that name belongs to the class.', 'not_started', 'List methods to inspect overloads and spelling.') end
	                                                          local flags = nil
	                                                          if type(mono_method_getFlags) == 'function' then flags = mono_method_getFlags(method) end
	                                                          return describe(method, mono_method_getName(method) or a[2], flags)
	                                                          """;

	internal const string StaticFieldAddress = Attached + """
	                                                      local _, refusal = attached(); if refusal then return refusal end
	                                                      local class = tonumber(a[1]); if class == nil then return mcp.err('invalid_argument', 'classHandle must be a Mono class handle.', 'not_started') end
	                                                      local domain = nil
	                                                      if a[2] ~= nil then
	                                                        domain = tonumber(a[2])
	                                                        if domain == nil then return mcp.err('invalid_argument', 'domainHandle must be a Mono domain handle from mono_get_status.', 'not_started') end
	                                                      else
	                                                        local domains = mono_enumDomains(); domain = domains and domains[1] or nil
	                                                      end
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

	/// <summary>
	///     Invokes one method. The C# side sends type codes 0 to 5 as numbers and 6 as text; a type 12 value is resolved
	///     here as an address, because Cheat Engine's <c>mono_writeObject</c> turns a textual pointer into a string.
	/// </summary>
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
	                                                local parameters = mono_method_get_parameters(method)
	                                                if parameters == nil or parameters.parameters == nil then
	                                                  return mcp.err('host_refused', 'Cheat Engine could not read the Mono method parameters before invocation.', 'not_started', 'List methods again after confirming the collector is healthy.')
	                                                end
	                                                local supplied, args = a[3] or {}, {}
	                                                if #supplied > #parameters.parameters then
	                                                  return mcp.err('invalid_argument', string.format('arguments: the method takes %d parameters, but %d arguments were supplied.', #parameters.parameters, #supplied), 'not_started', 'Read the parameters with mono_find_method or mono_list_methods.')
	                                                end
	                                                for i = 1, #supplied do
	                                                  local kind, value = supplied[i][1], supplied[i][2]
	                                                  if kind == 12 then
	                                                    local address = getAddressSafe(value)
	                                                    if address == nil then
	                                                      return mcp.err('invalid_argument', string.format('arguments: entry %d is not an object or pointer address Cheat Engine can resolve.', i - 1), 'not_started')
	                                                    end
	                                                    value = address
	                                                  end
	                                                  args[i] = {type = kind, value = value}
	                                                end
	                                                local result, exception = mono_invoke_method(nil, method, instance, args)
	                                                if result == nil then
	                                                  return mcp.err('host_refused', 'Cheat Engine could not complete the Mono invocation: ' .. tostring(exception), 'unknown', 'Do not retry: the method may have run. Inspect the target state with read tools first.')
	                                                end
	                                                local text = nil
	                                                if type(result) == 'table' then
	                                                  local keys, parts = {}, {}
	                                                  for key in pairs(result) do keys[#keys + 1] = tostring(key) end
	                                                  table.sort(keys)
	                                                  for j = 1, #keys do parts[j] = keys[j] .. '=' .. tostring(result[keys[j]]) end
	                                                  text = table.concat(parts, ', ')
	                                                else
	                                                  text = tostring(result)
	                                                end
	                                                return {invoked = true, returnValue = text, exception = exception ~= nil and tostring(exception) or nil}
	                                                """;

	/// <summary>The guard, the uncached field list and the shared .NET value reader of <see cref="Object" />.</summary>
	private const string ObjectPrelude = Attached + FieldList + DotNetLuaScripts.ValueReader;

	/// <summary>
	///     Identifies the object that contains <c>a[1]</c> and reads up to <c>a[3]</c> of its fields. Cheat Engine's
	///     <c>mono_object_getClass</c> (<c>MONOCMD_OBJECT_GETCLASS</c>) and <c>mono_object_findRealStartOfObject</c>
	///     make the collector dereference a guessed address inside the target, which Cheat Engine itself warns can
	///     crash it, so this body never sends the address. It walks pointer-aligned starts from the address down to
	///     <c>a[2]</c> bytes below it, reading target memory from Cheat Engine only, and accepts the nearest header
	///     that leads to a class pointer the collector lists in that class's own image: through the vtable on Mono
	///     (<c>MonoObject.vtable</c>, whose first pointer is the class), directly on IL2CPP
	///     (<c>Il2CppObject.klass</c>). A class structure names its image within its first pointers (first on IL2CPP, a
	///     few pointers in on Mono), so only those images are enumerated. A header whose class names a collector image
	///     but is in no image's list (a generic instance or an array class) stops the walk as <c>unsupported</c>, so
	///     the address is never attributed to an earlier object. The collector is then asked only about the listed
	///     class: its fields, parents and static data.
	/// </summary>
	/// <remarks>
	///     <para>
	///         Cheat Engine's pipe calls swallow a timeout or a pipe error in their own <c>pcall</c>: they set
	///         <c>libmono.fail</c>, or <c>libmono.abort</c> after Abort in the timeout dialog, and return <c>nil</c> or
	///         a partial list, and a pipe that timed out answers later calls out of step. The body checks both flags
	///         after each Cheat Engine Mono function it calls and stops at the first failure, so a missing image or
	///         class list is never mistaken for an unlisted class.
	///     </para>
	///     <para>
	///         When an IL2CPP collector reports no classes for an image, Cheat Engine's <c>mono_image_enumClasses</c>
	///         guesses the list with <c>mono_image_enumClasses_il2cppfallback</c>, a memory scan of the whole target
	///         whose entries the collector never confirmed. The body sets that global aside for its own enumeration and
	///         refuses such an image as <c>unsupported</c>.
	///     </para>
	/// </remarks>
	internal const string Object = ObjectPrelude + """
	                                               local _, refusal = attached(); if refusal then return refusal end
	                                               local address = getAddressSafe(a[1])
	                                               if address == nil then return mcp.err('invalid_argument', 'address could not be resolved by Cheat Engine.', 'not_started') end
	                                               local size, il2cpp = wide and 8 or 4, rawget(libmono, 'IL2CPP') == true
	                                               -- Cheat Engine's pipe calls swallow a failure: they set fail (or abort, after Abort in its timeout
	                                               -- dialog) and return nil or a partial list, and a timed-out pipe answers out of step afterwards.
	                                               local function broken(during)
	                                                 if rawget(libmono, 'abort') == true then
	                                                   return mcp.err('not_attached', 'Cheat Engine aborted its Mono collector connection while it ' .. during .. ', so the object was not read.', 'not_started', recovery)
	                                                 end
	                                                 if rawget(libmono, 'fail') == true then
	                                                   return mcp.err('invalid_state', 'The Mono collector pipe reported a timeout or an error while Cheat Engine ' .. during .. ', so the object was not read.', 'not_started',
	                                                     'Retry the call once: the next Mono call closes the failed pipe and reconnects to the collector, and says how to recover when it cannot.')
	                                                 end
	                                                 return nil
	                                               end
	                                               local assemblies = mono_enumAssemblies()
	                                               local failed = broken('enumerated Mono assemblies'); if failed then return failed end
	                                               if assemblies == nil then return mcp.err('host_refused', 'Cheat Engine did not enumerate Mono assemblies.', 'unknown') end
	                                               local images = {}
	                                               for i = 1, #assemblies do
	                                                 local image = mono_getImageFromAssembly(assemblies[i])
	                                                 failed = broken('read the images of Mono assemblies'); if failed then return failed end
	                                                 if math.type(image) == 'integer' and image ~= 0 then images[image] = true end
	                                               end
	                                               local function aligned(at)
	                                                 local value = readPointer(at)
	                                                 if math.type(value) == 'integer' and value ~= 0 and value % size == 0 then return value end
	                                                 return nil
	                                               end
	                                               -- The collector images named in a structure's first 32 pointers, read once per structure.
	                                               local owners = {}
	                                               local function imagesOf(class)
	                                                 local found = owners[class]
	                                                 if found == nil then
	                                                   local seen = {}
	                                                   found = {}
	                                                   for slot = 0, 31 do
	                                                     local value = readPointer(class + slot * size)
	                                                     if value ~= nil and images[value] == true and not seen[value] then
	                                                       seen[value] = true
	                                                       found[#found + 1] = value
	                                                     end
	                                                   end
	                                                   owners[class] = found
	                                                 end
	                                                 return found
	                                               end
	                                               -- The class list of an image, enumerated at most once; false for an image the IL2CPP collector
	                                               -- reports no classes for. Cheat Engine would then guess the list with a memory scan of the whole
	                                               -- target (mono_image_enumClasses_il2cppfallback) that the collector never confirmed, so that
	                                               -- global is set aside for the call and restored on every path.
	                                               local lists = {}
	                                               local function listed(image, class)
	                                                 local list = lists[image]
	                                                 if list == nil then
	                                                   local fallback = rawget(_ENV, 'mono_image_enumClasses_il2cppfallback')
	                                                   if il2cpp then rawset(_ENV, 'mono_image_enumClasses_il2cppfallback', function() return nil end) end
	                                                   local ok, classes = pcall(mono_image_enumClasses, image)
	                                                   if il2cpp then rawset(_ENV, 'mono_image_enumClasses_il2cppfallback', fallback) end
	                                                   if not ok then error(classes, 0) end
	                                                   local failed = broken('enumerated the classes of a Mono image'); if failed then return nil, failed end
	                                                   list = not il2cpp and {} or false
	                                                   if type(classes) == 'table' then
	                                                     list = {}
	                                                     for i = 1, #classes do
	                                                       local entry = classes[i]
	                                                       if type(entry) == 'table' and math.type(entry.class) == 'integer' then list[entry.class] = entry end
	                                                     end
	                                                   end
	                                                   lists[image] = list
	                                                 end
	                                                 return list and list[class] or nil
	                                               end
	                                               -- Nearest first. A vtable is allocated apart from its class, so a Mono header that points into
	                                               -- its own class (a raw class or System.Type handle field) is not an object header.
	                                               local start, lowest = address - address % size, math.max(0, address - a[2])
	                                               local object, class, entry, owner, unlisted, unknown = nil, nil, nil, nil, nil, nil
	                                               while object == nil and unlisted == nil and start >= lowest do
	                                                 local candidate = aligned(start)
	                                                 if candidate ~= nil and not il2cpp then
	                                                   local vtable = candidate
	                                                   candidate = aligned(vtable)
	                                                   if candidate ~= nil and vtable >= candidate and vtable - candidate < 0x200 then candidate = nil end
	                                                 end
	                                                 if candidate ~= nil then
	                                                   local found = imagesOf(candidate)
	                                                   for i = 1, #found do
	                                                     local match, failure = listed(found[i], candidate)
	                                                     if failure then return failure end
	                                                     if match ~= nil then object, class, entry, owner = start, candidate, match, found[i] break end
	                                                     if lists[found[i]] == false then unknown = found[i] end
	                                                   end
	                                                   -- A class that names a collector image yet is in no image's list is a generic instance or an
	                                                   -- array class: the address lies in that object, so the walk stops instead of reaching an earlier one.
	                                                   if object == nil and #found > 0 then unlisted = start end
	                                                 end
	                                                 start = start - size
	                                               end
	                                               if unlisted ~= nil and unknown ~= nil then
	                                                 return mcp.err('unsupported', string.format('The IL2CPP collector reported no classes for the image %X that the nearest object header, at %X, names, so its class could not be confirmed and the object was not read.', unknown, unlisted), 'not_started',
	                                                   'The collector was not asked about the address. This IL2CPP build cannot list an image\'s classes through the collector; read the object with memory_read, or use mono_find_class and mono_list_fields.')
	                                               end
	                                               if unlisted ~= nil then
	                                                 return mcp.err('unsupported', string.format('The nearest object header, at %X, leads to a class that no collector image lists, such as a generic instance or an array class, so the object was not read.', unlisted), 'not_started',
	                                                   'The collector was not asked about the address. Read that object with memory_read, or start from an object that references it.')
	                                               end
	                                               if object == nil then
	                                                 return mcp.err('not_found', 'No object header whose class the Mono collector lists was found from the address down to maxBacktrack bytes below it.', 'not_started',
	                                                   'The collector was not asked about the address. Check that it lies inside a live managed object, raise maxBacktrack for a large object, or use mono_find_class and mono_list_fields.')
	                                               end
	                                               local fields = fieldsOf(class, true)
	                                               failed = broken('listed the fields of the object\'s class'); if failed then return failed end
	                                               if fields == nil then
	                                                 return mcp.err('host_refused', 'Cheat Engine did not list the fields of the object\'s class.', 'not_started', 'Check the collector with mono_get_status, then retry.')
	                                               end
	                                               local items = {}
	                                               for i = 1, math.min(#fields, a[3]) do
	                                                 local field = fields[i]
	                                                 local offset = math.type(field.offset) == 'integer' and field.offset or 0
	                                                 local static, constant = field.isStatic == true, field.isConst == true
	                                                 local kind = math.type(field.monotype) == 'integer' and field.monotype or nil
	                                                 local at = nil
	                                                 if constant then
	                                                   at = nil
	                                                 elseif static then
	                                                   if math.type(field.staticAddress) == 'integer' and field.staticAddress ~= 0 then at = field.staticAddress end
	                                                 else
	                                                   at = object + offset
	                                                 end
	                                                 local value = nil
	                                                 if at ~= nil and kind ~= nil then
	                                                   local ok, read_value = pcall(read, kind, at)
	                                                   if ok then value = read_value end
	                                                 end
	                                                 items[#items + 1] = {name = tostring(field.name or ''), offset = offset, isStatic = static, isConst = constant,
	                                                   typeName = type(field.typename) == 'string' and field.typename or nil, elementType = kind,
	                                                   address = mcp.hex(at), value = value}
	                                               end
	                                               return {address = mcp.hex(object), offsetInObject = address - object, classHandle = tostring(class),
	                                                 className = tostring(entry.classname or ''), namespace = tostring(entry.namespace or ''),
	                                                 imageHandle = tostring(owner), totalFields = #fields, fields = items}
	                                               """;

	/// <summary>
	///     Scans for the class vtable the way the second half of Cheat Engine's
	///     <c>mono_class_findInstancesOfClassListOnly</c> does. The first half of that function calls
	///     <c>UnityEngine.Resources.FindObjectsOfTypeAll</c> in the target, so this body does not use it.
	/// </summary>
	internal const string Instances = Attached + """
	                                             local _, refusal = attached(); if refusal then return refusal end
	                                             local class = tonumber(a[1]); if class == nil then return mcp.err('invalid_argument', 'classHandle must be a Mono class handle.', 'not_started') end
	                                             local vtable = mono_class_getVTable(class)
	                                             if vtable == nil or vtable == 0 then
	                                               return mcp.err('not_found', 'Cheat Engine did not report a vtable for the Mono class.', 'not_started', 'Generic, abstract and not yet initialized classes can lack a vtable; check the class handle with mono_find_class.')
	                                             end
	                                             local wide = type(targetIs64Bit) ~= 'function' or targetIs64Bit()
	                                             local scan, list, total, items = createMemScan(), nil, 0, {}
	                                             local ok = pcall(function()
	                                               scan.firstScan(soExactValue, wide and vtQword or vtDword, rtRounded, string.format('%X', vtable), '', 0,
	                                                 wide and 0x7fffffffffffffff or 0xffffffff, '', fsmAligned, '8', true, true, false, false)
	                                               scan.waitTillDone()
	                                               list = createFoundList(scan)
	                                               list.initialize()
	                                               total = list.Count
	                                               for i = 0, math.min(total, a[2]) - 1 do
	                                                 local address = tonumber(list[i], 16)
	                                                 if address ~= nil then items[#items + 1] = {address = string.format('%X', address)} end
	                                               end
	                                             end)
	                                             if list ~= nil then pcall(function() list.destroy() end) end
	                                             pcall(function() scan.destroy() end)
	                                             if not ok then return mcp.err('host_refused', 'Cheat Engine could not complete the Mono vtable scan.', 'unknown', 'Check the target is running, then start a new search.') end
	                                             return {instances = items, total = total}
	                                             """;
}
