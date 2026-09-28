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

	// Only the native .NET branch of injectDotNetDLL calls the method; its Mono branch is refused before any effect.
	// Cheat Engine 7.7 ignores injectDotNetDLL's timeout argument, so none is passed.
	// It reads the method's int return value back with readInteger, which gives the unsigned 32-bit view, so the body
	// turns it back into the signed value.
	internal const string InjectDotNet = """
	                                     if monopipe then
	                                         return mcp.err('unsupported', 'The target has an active Cheat Engine Mono data collector, so Cheat Engine ' ..
	                                             'would load the assembly through Mono without calling the method or passing parameter.', 'not_started',
	                                             'On a Mono target call existing methods with mono_invoke_method.')
	                                     end
	                                     local called, result, code = pcall(injectDotNetDLL, a[1], a[2], a[3], a[4])
	                                     if called and result == nil and code == -4 then
	                                         return mcp.err('unsupported', 'Cheat Engine found no .NET Framework or .NET Core runtime in the target.',
	                                             'not_started', 'Inject into a process that has loaded mscoree.dll or coreclr.dll.')
	                                     end
	                                     if not called or result == nil or result == false then
	                                         local detail = ''
	                                         if called and math.type(code) == 'integer' then
	                                             detail = string.format(code < 0 and ' (Cheat Engine code %d)' or ' (Cheat Engine code 0x%X)', code)
	                                         end
	                                         return mcp.err('host_refused', 'Cheat Engine did not confirm managed assembly injection' .. detail ..
	                                             '; the target may have changed.', 'unknown', 'Inspect the target before retrying the injection.')
	                                     end
	                                     if math.type(result) == 'integer' and result >= 0x80000000 and result <= 0xFFFFFFFF then
	                                         result = result - 0x100000000
	                                     end
	                                     return {assemblyPath = a[1], className = a[2], methodName = a[3], result = tostring(result)}
	                                     """;

	// The helpers both call bodies share. Cheat Engine 7.7's executeMethod, which executeCodeEx also runs, writes the
	// low half of a double twice on a 32-bit target, so parameterList passes a double there as its two 32-bit halves,
	// low half first, which is the same stack layout.
	private const string CallPrelude = """
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
	                                   local function parameterList(types, values, wide)
	                                       local parameters = {}
	                                       for i = 1, #types do
	                                           if types[i] == 2 and not wide then
	                                               local low, high = string.unpack('<I4I4', string.pack('<d', values[i]))
	                                               parameters[#parameters + 1] = {type = 0, value = low}
	                                               parameters[#parameters + 1] = {type = 0, value = high}
	                                           else
	                                               parameters[#parameters + 1] = {type = types[i], value = values[i]}
	                                           end
	                                       end
	                                       return parameters
	                                   end
	                                   """ + "\n";

	internal const string CallRemote = CallPrelude + """
	                                   local isStopped, message = stopped()
	                                   if isStopped then return mcp.err('busy', message, 'not_started', 'Resume the target before calling code.') end
	                                   local parameters = parameterList(a[4], a[5], targetIs64Bit())
	                                   local called, result = pcall(function()
	                                       return executeCodeEx(a[2], a[3], a[1], table.unpack(parameters))
	                                   end)
	                                   if not called or result == nil then
	                                       return mcp.err('host_refused', 'Cheat Engine could not confirm the remote function call.', 'unknown',
	                                           'Inspect target state before repeating a code-execution request.')
	                                   end
	                                   return {functionAddress = mcp.hex(a[1]), returnValue = tostring(mcp.num(result))}
	                                   """;

	// On a 64-bit target executeMethod sets the instance register first and then loads arguments 1 to 4 into RCX,
	// RDX, R8 and R9 (XMM0 to XMM3 for float and double) and every later argument through RAX, reserving no slot for
	// this. So with RCX the body passes the instance to executeCodeEx as the first argument, which is the Microsoft
	// x64 member call (argument 1 in RDX or XMM1), and it refuses a register that an argument would overwrite.
	// executeMethod raises for an instance register of 8 to 15 on a 32-bit target, so that is refused as well.
	internal const string CallMethod = CallPrelude + """
	                                   local wide = targetIs64Bit()
	                                   if a[5] >= 8 and not wide then
	                                       return mcp.err('invalid_argument', 'classRegister: 8 to 15 (R8-R15) require a 64-bit target.', 'not_started',
	                                           'Use 0 to 3 or 5 to 7 on a 32-bit target; 1 is ECX.')
	                                   end
	                                   if wide and a[5] ~= 1 then
	                                       local slot = ({[2] = 2, [8] = 3, [9] = 4})[a[5]]
	                                       if slot ~= nil and a[6][slot] == 0 then
	                                           local name = ({[2] = 'RDX', [8] = 'R8', [9] = 'R9'})[a[5]]
	                                           return mcp.err('invalid_argument', string.format('classRegister: on a 64-bit target Cheat Engine loads ' ..
	                                               'integer argument %d into %s after it sets this, so the method would not receive the instance.', slot,
	                                               name), 'not_started', string.format('Use classRegister 1 for a standard x64 method, or call ' ..
	                                               'exec_call_remote with the instance placed as argument %d, which Cheat Engine loads into %s.', slot, name))
	                                       end
	                                       if a[5] == 0 and #a[6] > 4 then
	                                           return mcp.err('invalid_argument', 'classRegister: on a 64-bit target Cheat Engine loads the fifth and ' ..
	                                               'later arguments through RAX after it sets this, so the method would not receive the instance.',
	                                               'not_started', 'Use classRegister 1 for a standard x64 method, or pass at most 4 arguments.')
	                                       end
	                                   end
	                                   local isStopped, message = stopped()
	                                   if isStopped then return mcp.err('busy', message, 'not_started', 'Resume the target before calling code.') end
	                                   local parameters = parameterList(a[6], a[7], wide)
	                                   local called, result
	                                   if wide and a[5] == 1 then
	                                       table.insert(parameters, 1, {type = 0, value = a[4]})
	                                       called, result = pcall(function()
	                                           return executeCodeEx(a[2], a[3], a[1], table.unpack(parameters))
	                                       end)
	                                   else
	                                       local instance = {regnr = a[5], classinstance = a[4]}
	                                       called, result = pcall(function()
	                                           return executeMethod(a[2], a[3], a[1], instance, table.unpack(parameters))
	                                       end)
	                                   end
	                                   if not called or result == nil then
	                                       return mcp.err('host_refused', 'Cheat Engine could not confirm the instance method call.', 'unknown',
	                                           'Inspect target state before repeating a code-execution request.')
	                                   end
	                                   return {functionAddress = mcp.hex(a[1]), returnValue = tostring(mcp.num(result))}
	                                   """;

	// executeCodeLocal resolves a string or number parameter with getAddressFromName, which reads hexadecimal, so the
	// integer is passed as its unsigned 64-bit hexadecimal text.
	internal const string CallLocal = """
	                                  local address = getAddressSafe(a[1], true)
	                                  if address == nil then return mcp.err('not_found', 'localAddress does not resolve in Cheat Engine.', 'not_started') end
	                                  local called, result = pcall(executeCodeLocal, address, mcp.hex(a[2]))
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
