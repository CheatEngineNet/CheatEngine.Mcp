namespace CheatEngine.Mcp.Tools.Exec;

/// <summary>Implementation-owned, bounded Lua bodies for execution capabilities absent from Client.</summary>
internal static class ExecScripts
{
	internal const string InjectLibrary = """
	                                      local called, injected = pcall(injectLibrary, a[1], a[2])
	                                      if not called or injected ~= true then
	                                          return mcp.err('host_refused', 'Cheat Engine did not confirm native library injection; the target may have changed.', 'unknown',
	                                              'Inspect the target before retrying the injection.')
	                                      end
	                                      return {libraryPath = a[1], skippedSymbolReloadWait = a[2]}
	                                      """;

	internal const string InjectDotNet = """
	                                     local called, result = pcall(injectDotNetDLL, a[1], a[2], a[3], a[4], a[5])
	                                     if not called or result == nil or result == false then
	                                         return mcp.err('host_refused', 'Cheat Engine did not confirm managed assembly injection; the target may have changed.', 'unknown',
	                                             'Inspect the target before retrying the injection.')
	                                     end
	                                     return {assemblyPath = a[1], className = a[2], methodName = a[3], result = tostring(result)}
	                                     """;

	internal const string CallRemote = """
	                                   local function stopped()
	                                       if type(debug_isBroken) == 'function' then
	                                           local ok, broken = pcall(debug_isBroken)
	                                           if ok and broken then return true, 'The target is stopped at a breakpoint.' end
	                                       end
	                                       if type(isPaused) == 'function' then
	                                           local ok, paused = pcall(isPaused)
	                                           if ok and paused then return true, 'The target is paused.' end
	                                       end
	                                       return false, nil
	                                   end
	                                   local isStopped, message = stopped()
	                                   if isStopped then return mcp.err('busy', message, 'not_started', 'Resume the target before calling code.') end
	                                   local parameters = {}
	                                   for i = 1, #a[4] do parameters[i] = {type = a[4][i], value = a[5][i]} end
	                                   local called, result = pcall(function()
	                                       return executeCodeEx(a[2], a[3], a[1], table.unpack(parameters))
	                                   end)
	                                   if not called or result == nil then
	                                       return mcp.err('host_refused', 'Cheat Engine could not confirm the remote function call.', 'unknown',
	                                           'Inspect target state before repeating a code-execution request.')
	                                   end
	                                   return {functionAddress = mcp.hex(a[1]), returnValue = tostring(mcp.num(result))}
	                                   """;

	internal const string CallMethod = """
	                                   local function stopped()
	                                       if type(debug_isBroken) == 'function' then
	                                           local ok, broken = pcall(debug_isBroken)
	                                           if ok and broken then return true, 'The target is stopped at a breakpoint.' end
	                                       end
	                                       if type(isPaused) == 'function' then
	                                           local ok, paused = pcall(isPaused)
	                                           if ok and paused then return true, 'The target is paused.' end
	                                       end
	                                       return false, nil
	                                   end
	                                   local isStopped, message = stopped()
	                                   if isStopped then return mcp.err('busy', message, 'not_started', 'Resume the target before calling code.') end
	                                   local parameters = {}
	                                   for i = 1, #a[6] do parameters[i] = {type = a[6][i], value = a[7][i]} end
	                                   local instance = {regnr = a[5], classinstance = a[4]}
	                                   local called, result = pcall(function()
	                                       return executeMethod(a[2], a[3], a[1], instance, table.unpack(parameters))
	                                   end)
	                                   if not called or result == nil then
	                                       return mcp.err('host_refused', 'Cheat Engine could not confirm the instance method call.', 'unknown',
	                                           'Inspect target state before repeating a code-execution request.')
	                                   end
	                                   return {functionAddress = mcp.hex(a[1]), returnValue = tostring(mcp.num(result))}
	                                   """;

	internal const string CallLocal = """
	                                  local address = getAddressSafe(a[1], true)
	                                  if address == nil then return mcp.err('not_found', 'localAddress does not resolve in Cheat Engine.', 'not_started') end
	                                  local called, result = pcall(executeCodeLocal, address, a[2])
	                                  if not called or result == nil then
	                                      return mcp.err('host_refused', 'Cheat Engine could not confirm the local function call.', 'unknown',
	                                          'Inspect Cheat Engine before repeating a code-execution request.')
	                                  end
	                                  return {functionAddress = mcp.hex(address), returnValue = tostring(mcp.num(result))}
	                                  """;

	internal const string CompileC = """
	                                 local called, symbols, message = pcall(compile, a[1], a[2], a[3], a[4], a[5])
	                                 if not called or type(symbols) ~= 'table' then
	                                     local text = tostring(message or 'Cheat Engine rejected the C source.')
	                                     return mcp.err('host_refused', 'Cheat Engine could not compile the C source: ' .. text:sub(1, 512), 'unknown',
	                                         'Inspect target state and the C source before retrying.')
	                                 end
	                                 local names = {}
	                                 for name, address in pairs(symbols) do
	                                     if type(name) == 'string' and type(address) == 'number' then names[#names + 1] = name end
	                                 end
	                                 table.sort(names)
	                                 local copied = {}
	                                 for index = 1, math.min(#names, 1024) do
	                                     local name = names[index]
	                                     copied[index] = {name = name, address = mcp.hex(symbols[name])}
	                                 end
	                                 return {symbols = copied, symbolsTruncated = #names > #copied, targetSelf = a[3], kernelMode = a[4]}
	                                 """;
}
