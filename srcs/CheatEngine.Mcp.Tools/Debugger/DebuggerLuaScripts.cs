using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Debugger;

/// <summary>Fixed, bounded Lua bodies for debugger functions missing from CheatEngine.Client.</summary>
internal static class DebuggerLuaScripts
{
	internal const string Status = """
	                               local function read(name, ...)
	                                  local value = _G[name](...)
	                                  assert(type(value) == 'boolean', name .. ' did not return a boolean debugger state')
	                                  return value
	                               end
	                               local function interfaceName(value)
	                                  local names = {[1] = 'windows', [2] = 'veh', [3] = 'kernel'}
	                                  local name = names[value]
	                                  assert(math.type(value) == 'integer' and name ~= nil,
	                                     'Cheat Engine did not report a supported active debugger interface')
	                                  return name
	                               end
	                               local ok, result = pcall(function()
	                                  local attached = read('debug_isDebugging')
	                                  local active = nil
	                                  if attached then
	                                     active = interfaceName(debug_getCurrentDebuggerInterface())
	                                  end
	                                  return {stateValid = true, attached = attached, canBreak = read('debug_canBreak'),
	                                     broken = attached and read('debug_getContext', false) or false,
	                                     reportedBroken = attached and read('debug_isBroken') or false, stepping = read('debug_isStepping'),
	                                     activeInterface = active}
	                               end)
	                               if not ok then return {stateValid = false, attached = false, canBreak = false, broken = false,
	                                  reportedBroken = false, stepping = false, error = tostring(result):sub(1, 512)} end
	                               return result
	                               """;

	// Arguments: requested interface, selected PID, PID/start-time identity. An attached interface other than Windows,
	// VEH or kernel (Cheat Engine reports no integer for DBVM or ceserver, and 5 for a GDB server) is unsupported.
	// While Cheat Engine is connected to a ceserver, debugProcess creates its network debugger whatever interface is
	// requested, so the attach is refused before Cheat Engine attaches anything.
	internal const string Attach = """
	                               local function attached()
	                                  local value = debug_isDebugging()
	                                  assert(type(value) == 'boolean', 'debug_isDebugging did not return a boolean')
	                                  return value
	                               end
	                               local function activeInterface()
	                                  local value = debug_getCurrentDebuggerInterface()
	                                  local supported = math.type(value) == 'integer' and value >= 1 and value <= 3
	                                  return supported and value or nil
	                               end
	                               local function unsupportedInterface(state, effect)
	                                  local message = "Cheat Engine's debugger " .. state .. ' an interface that MCP '
	                                     .. 'cannot drive, such as DBVM, a GDB server or ceserver.'
	                                  return mcp.err('unsupported', message, effect,
	                                     'Detach it with debugger_detach, then select the Windows, VEH or '
	                                     .. "kernel debugger in Cheat Engine's settings or pass interface windows.")
	                               end
	                               local function responseInterface(value)
	                                  local names = {[0] = 'default', [1] = 'windows', [2] = 'veh', [3] = 'kernel'}
	                                  local name = names[value]
	                                  assert(math.type(value) == 'integer' and name ~= nil,
	                                     'Cheat Engine did not report a supported debugger interface')
	                                  return name
	                               end
	                               local pid = getOpenedProcessID()
	                               assert(pid == a[2] and pid > 0 and getProcesslist()[pid] ~= nil,
	                                  'The selected local process is no longer live')
	                               local used = attached()
	                               if used then
	                                  local current = activeInterface()
	                                  if current == nil then
	                                     return unsupportedInterface('is already attached through', 'not_started')
	                                  end
	                                  assert(a[1] == 0 or current == a[1],
	                                     'A different debugger interface is already attached; detach it explicitly first')
	                                  return {attached = true, alreadyAttached = true, processId = pid,
	                                     requestedInterface = responseInterface(a[1]), activeInterface = responseInterface(current),
	                                     usedFallback = a[1] ~= 0 and current ~= a[1]}
	                               end
	                               local remote = type(isConnectedToCEServer) == 'function' and isConnectedToCEServer()
	                               if remote == true then
	                                  return mcp.err('unsupported', 'Cheat Engine is connected to a ceserver, whose '
	                                     .. 'network debugger MCP cannot drive.', 'not_started',
	                                     'Disconnect Cheat Engine from the ceserver and open a local process, then '
	                                     .. 'attach again.')
	                               end
	                               local veh = _G.__cheatengine_mcp_debugger_veh_targets or {}
	                               _G.__cheatengine_mcp_debugger_veh_targets = veh
	                               assert(not veh[a[3]],
	                                  'This process incarnation already used the VEH debugger; restart the target before attaching again')
	                               -- Mark an explicit VEH request before CE is called:
	                               -- a failed attach may already have loaded the VEH helper.
	                               if a[1] == 2 then veh[a[3]] = true end
	                               debugProcess(a[1])
	                               assert(getOpenedProcessID() == pid and getProcesslist()[pid] ~= nil,
	                                  'The target exited during debugger attachment')
	                               assert(attached(), 'Cheat Engine did not attach the debugger')
	                               local current = activeInterface()
	                               if current == nil then
	                                  return unsupportedInterface('attached and remains attached through', 'completed')
	                               end
	                               if current == 2 then veh[a[3]] = true end
	                               return {attached = true, alreadyAttached = false, processId = pid,
	                                  requestedInterface = responseInterface(a[1]), activeInterface = responseInterface(current),
	                                  usedFallback = a[1] ~= 0 and current ~= a[1]}
	                               """;

	// No arguments. Reports whether a debugger is attached and which interface Cheat Engine's settings select for
	// debugProcess(0), in the order its debugger thread tests them: a ceserver connection first, then the settings;
	// configured is nil when no setting is identified.
	internal const string DefaultInterface = """
		local attached = debug_isDebugging()
		assert(type(attached) == 'boolean', 'debug_isDebugging did not return a boolean')
		local function selected(form, name)
		   local ok, value = pcall(function() return form[name].Checked end)
		   return ok and value == true
		end
		local configured = nil
		local connected, remote = pcall(isConnectedToCEServer)
		local found, form = pcall(getSettingsForm)
		if connected and remote == true then
		   configured = 'ceserver'
		elseif found and form ~= nil then
		   local choices = {{'cbUseWindowsDebugger', 'windows'}, {'cbUseVEHDebugger', 'veh'}, {'cbKDebug', 'kernel'},
		      {'cbUseDBVMDebugger', 'dbvm'}}
		   for _, choice in ipairs(choices) do
		      if selected(form, choice[1]) then configured = choice[2] break end
		   end
		end
		return {attached = attached, configured = configured}
		""";

	internal const string Detach = """
	                               local function attached()
	                                  local value = debug_isDebugging()
	                                  assert(type(value) == 'boolean', 'debug_isDebugging did not return a boolean')
	                                  return value
	                               end
	                               local pid = getOpenedProcessID()
	                               assert(pid > 0, 'No process is selected')
	                               local wasAttached = attached()
	                               local continued = false
	                               if wasAttached and debug_getContext(false) then
	                                  debug_continueFromBreakpoint(co_run)
	                                  continued = true
	                               end
	                               unpause()
	                               detachIfPossible()
	                               assert(not attached(), 'Cheat Engine still reports a debugger attached')
	                               return {processId = pid, wasAttached = wasAttached, continued = continued, detached = true}
	                               """;

	internal const string BreakThread = """
	                                    assert(debug_isDebugging(), 'Debugger is not attached')
	                                    debug_breakThread(a[1])
	                                    return {threadId = a[1], requested = true}
	                                    """;

	// Arguments: namespace, resource id, address expression, size, trigger, method, thread id, one-shot.
	internal const string SetBreakpoint = LuaJobKernelScripts.Kernel + "\n" + """
	                                                                          assert(debug_isDebugging(), 'Debugger is not attached')
	                                                                          local address = getAddressSafe(a[3])
	                                                                          assert(address ~= nil, 'The breakpoint address could not be resolved')
	                                                                          local triggers = {execute = bptExecute, access = bptAccess, write = bptWrite}
	                                                                          local methods = {hardware = bpmDebugRegister, int3 = bpmInt3, pageException = bpmException}
	                                                                          local trigger = triggers[a[5]]
	                                                                          assert(trigger ~= nil, 'Unknown breakpoint trigger')
	                                                                          if a[5] ~= 'execute' then
	                                                                             assert(address % a[4] == 0, 'A data-breakpoint address must be aligned to its size')
	                                                                             assert(a[4] ~= 8 or targetIs64Bit(), 'An eight-byte data breakpoint requires a 64-bit target')
	                                                                          end
	                                                                          local method = a[6] == 'default' and nil or methods[a[6]]
	                                                                          assert(a[6] == 'default' or method ~= nil, 'Unknown breakpoint method')
	                                                                          local breakpointId = nil
	                                                                          local function release(entry)
	                                                                             if breakpointId ~= nil then
	                                                                                assert(debug_removeBreakpointByID(breakpointId) ~= false,
	                                                                                   'Cheat Engine rejected removal of the MCP-owned breakpoint')
	                                                                                breakpointId = nil
	                                                                             end
	                                                                          end
	                                                                          local function oneShot()
	                                                                             local removed = breakpointId == nil or debug_removeBreakpointByID(breakpointId) ~= false
	                                                                             assert(removed, 'Cheat Engine rejected removal of the one-shot breakpoint')
	                                                                             breakpointId = nil
	                                                                             resourceForget(a[1], a[2])
	                                                                             return 1
	                                                                          end
	                                                                          local callback = a[8] and oneShot or nil
	                                                                          local accepted, id
	                                                                          if a[7] ~= nil then
	                                                                             if method == nil and callback == nil then
	                                                                                accepted, id = debug_setBreakpointForThread(a[7], address, a[4], trigger)
	                                                                             else
	                                                                                accepted, id = debug_setBreakpointForThread(a[7], address, a[4], trigger, method, callback)
	                                                                             end
	                                                                          elseif method == nil and callback == nil then
	                                                                             accepted, id = debug_setBreakpoint(address, a[4], trigger)
	                                                                          elseif method == nil then
	                                                                             accepted, id = debug_setBreakpoint(address, a[4], trigger, callback)
	                                                                          else
	                                                                             accepted, id = debug_setBreakpoint(address, a[4], trigger, method, callback)
	                                                                          end
	                                                                          assert(accepted == true and id ~= nil, 'Cheat Engine rejected the breakpoint')
	                                                                          breakpointId = id
	                                                                          local recorded, record = pcall(resourceRecord, a[1], a[2], 'breakpoint',
	                                                                             {address = address, size = a[4], name = 'MCP debugger breakpoint', detail = a[5]}, release)
	                                                                          if not recorded then
	                                                                             local removed = debug_removeBreakpointByID(breakpointId)
	                                                                             breakpointId = nil
	                                                                             assert(removed ~= false, 'Breakpoint creation succeeded but state recording failed and cleanup was rejected')
	                                                                             error(record, 0)
	                                                                          end
	                                                                          return {resourceId = a[2], address = string.format('%X', address), trigger = a[5], size = a[4],
	                                                                             method = a[6] == 'pageException' and 'page_exception' or a[6], threadId = a[7], oneShot = a[8]}
	                                                                          """;

	// Arguments: namespace, address expression.
	internal const string DeleteBreakpoint = LuaJobKernelScripts.Kernel + "\n" + """
		local address = getAddressSafe(a[2])
		assert(address ~= nil, 'The breakpoint address could not be resolved')
		local store = stateStore(a[1], false)
		if store == nil then
		   return {mcp_error = {kind = 'not_found', hostEffect = 'not_started',
		      message = 'No MCP-owned breakpoint exists at the requested address.',
		      hint = 'Use debugger_list_breakpoints to distinguish MCP-owned breakpoints from Cheat Engine breakpoints.'}}
		end
		local matched = nil
		for _, entry in pairs(store.resources) do
		   if entry.kind == 'breakpoint' and entry.address == address then matched = entry break end
		end
		if matched == nil then
		   return {mcp_error = {kind = 'not_found', hostEffect = 'not_started',
		      message = 'No MCP-owned breakpoint exists at the requested address.',
		      hint = 'MCP never deletes a breakpoint that it does not own.'}}
		end
		local released = resourceRelease(matched, 'deleted')
		return {resourceId = matched.id, address = string.format('%X', address), released = released,
		   cleanupError = released and nil or 'The recorded cleanup did not complete.'}
		""";

	// Arguments: namespace, limit.
	internal const string ListBreakpoints = LuaJobKernelScripts.Kernel + "\n" + """
		local all = debug_getBreakpointList() or {}
		local owned = {}
		local store = stateStore(a[1], false)
		if store ~= nil then
		   for _, entry in pairs(store.resources) do
		      if entry.kind == 'breakpoint' and math.type(entry.address) == 'integer' then
		         owned[entry.address] = entry.id
		      end
		   end
		end
		local items = {}
		local take = math.min(#all, a[2])
		for index = 1, take do
		   local address = all[index]
		   if math.type(address) == 'integer' then
		      items[#items + 1] = {address = string.format('%X', address), owned = owned[address] ~= nil,
		         resourceId = owned[address]}
		   end
		end
		return {breakpoints = items, total = #all, truncated = #all > take}
		""";

	internal const string Continue = """
	                                 assert(debug_isDebugging(), 'Debugger is not attached')
	                                 assert(debug_getContext(false), 'Debugger has no stopped context')
	                                 debug_continueFromBreakpoint(co_run)
	                                 return {mode = 'run', continued = true}
	                                 """;

	// Argument: into or over.
	internal const string Step = """
	                             assert(debug_isDebugging(), 'Debugger is not attached')
	                             assert(debug_getContext(false), 'Debugger has no stopped context')
	                             local modes = {into = co_stepinto, over = co_stepover}
	                             local mode = modes[a[1]]
	                             assert(mode ~= nil, 'Unknown step mode')
	                             debug_continueFromBreakpoint(mode)
	                             return {mode = a[1], continued = true}
	                             """;

	// Argument: include extras. Cheat Engine supplies FP0-FP7 and XMM0-XMM15 as byte tables in memory order.
	internal const string GetContext = """
		assert(debug_isDebugging(), 'Debugger is not attached')
		assert(debug_getContext(a[1]) == true, 'Debugger has no stopped context')
		local context = debug_getCurrentContextTable(a[1]) or {}
		local function byteText(value)
		   local length = #value
		   if length < 1 or length > 64 then return nil end
		   local parts = {}
		   for index = 1, length do
		      local byte = value[index]
		      if math.type(byte) ~= 'integer' or byte < 0 or byte > 255 then return nil end
		      parts[index] = string.format('%02X', byte)
		   end
		   return table.concat(parts, ' ')
		end
		local registers = {}
		local count = 0
		for name, value in pairs(context) do
		   if type(name) == 'string' and #name <= 16 and count < 128 then
		      local text = nil
		      if math.type(value) == 'integer' then text = string.format('%X', value)
		      elseif type(value) == 'number' then text = tostring(value)
		      elseif type(value) == 'string' then text = value:sub(1, 256)
		      elseif type(value) == 'table' then text = byteText(value) end
		      if text ~= nil then registers[name:upper()] = text count = count + 1 end
		   end
		end
		return {is64Bit = targetIs64Bit(), registers = registers, includesExtraRegisters = a[1]}
		""";

	// Arguments: register, signed value.
	internal const string SetRegister = """
	                                    assert(debug_isDebugging(), 'Debugger is not attached')
	                                    assert(debug_getContext(false), 'Debugger has no stopped context')
	                                    assert(targetIsX86(), 'Register editing requires an x86 or x64 target')
	                                    local name = a[1]
	                                    local aliases = {EAX = 'RAX', EBX = 'RBX', ECX = 'RCX', EDX = 'RDX', ESI = 'RSI', EDI = 'RDI',
	                                       EBP = 'RBP', ESP = 'RSP', EIP = 'RIP'}
	                                    local is32 = name == 'EFLAGS' or aliases[name] ~= nil
	                                    local wide = targetIs64Bit()
	                                    assert(wide or is32, 'This register is unavailable in a 32-bit target')
	                                    if is32 then assert(a[2] >= -2147483648 and a[2] <= 4294967295,
	                                       'The value does not fit a 32-bit register') end
	                                    local actual = wide and aliases[name] or name
	                                    local expected = is32 and (a[2] & 0xffffffff) or a[2]
	                                    assert(math.type(_G[actual]) == 'integer', 'The register is unavailable in the current context')
	                                    _G[actual] = expected
	                                    assert(debug_setContext(false) == true, 'Cheat Engine could not write the stopped context')
	                                    assert(debug_getContext(false) == true, 'Cheat Engine could not re-read the stopped context')
	                                    assert(_G[actual] == expected, 'The register read-back differs from the requested value')
	                                    debug_updateGUI()
	                                    return {register = name, contextRegister = actual, value = string.format('%X', expected), verified = true}
	                                    """;

	// Arguments: thread id, ignored.
	internal const string SetThreadIgnored = """
	                                         assert(debug_isDebugging(), 'Debugger is not attached')
	                                         if a[2] then debug_addThreadToNoBreakList(a[1]) else debug_removeThreadFromNoBreakList(a[1]) end
	                                         return {threadId = a[1], ignored = a[2]}
	                                         """;

	// Argument: maximum number of stack slots.
	internal const string GetStackTrace = """
	                                      assert(debug_isDebugging(), 'Debugger is not attached')
	                                      assert(debug_getContext(false), 'Debugger has no stopped context')
	                                      assert(targetIsX86(), 'Stack tracing requires an x86 or x64 target')
	                                      local wide = targetIs64Bit()
	                                      local pointerSize = wide and 8 or 4
	                                      local stack = wide and RSP or ESP
	                                      assert(math.type(stack) == 'integer', 'Cheat Engine did not provide the stack pointer')
	                                      local frames = {}
	                                      for index = 0, a[1] - 1 do
	                                         local slot = stack + index * pointerSize
	                                         local value = wide and readQword(slot) or readInteger(slot)
	                                         if math.type(value) == 'integer' and value > 0 then
	                                            local previous = nil
	                                            local text = nil
	                                            local ok, candidate = pcall(getPreviousOpcode, value)
	                                            if ok and math.type(candidate) == 'integer' then
	                                               previous = candidate
	                                               local disassembled, output = pcall(disassemble, previous)
	                                               if disassembled and type(output) == 'string' then text = output:sub(1, 512) end
	                                            end
	                                            if text ~= nil and text:lower():find('call', 1, true) ~= nil then
	                                               frames[#frames + 1] = {stackAddress = string.format('%X', slot),
	                                                  returnAddress = string.format('%X', value), callInstruction = text, isHeuristic = true}
	                                            end
	                                         end
	                                      end
	                                      return {stackPointer = string.format('%X', stack), pointerSize = pointerSize, frames = frames,
	                                         scannedSlots = a[1]}
	                                      """;

	// Arguments: namespace, job id, buffer limit, ttl ms, address, trigger, size, aggregate by instruction, group by
	// effective address.
	internal const string StartCapture = LuaJobKernelScripts.Strategies + "\n" + """
		local function captureContext(trigger, includeStack)
		   local wide = targetIs64Bit()
		   local ip = wide and RIP or EIP
		   assert(math.type(ip) == 'integer' and math.type(THREADID) == 'integer',
		      'Cheat Engine did not supply the breakpoint context')
		   local names = wide and {'RAX','RBX','RCX','RDX','RSI','RDI','RBP','RSP','RIP','R8','R9','R10','R11',
		      'R12','R13','R14','R15','EFLAGS'} or {'EAX','EBX','ECX','EDX','ESI','EDI','EBP','ESP','EIP','EFLAGS'}
		   local registers = {}
		   for _, name in ipairs(names) do
		      local value = _G[name]
		      if math.type(value) == 'integer' then registers[name] = string.format('%X', value) end
		   end
		   local instruction = ip
		   local heuristic = false
		   if trigger ~= 'execute' then
		      heuristic = true
		      local ok, previous = pcall(getPreviousOpcode, ip)
		      if ok and math.type(previous) == 'integer' then instruction = previous else instruction = nil end
		   end
		   local text = nil
		   if instruction ~= nil then
		      local ok, output = pcall(disassemble, instruction)
		      if ok and type(output) == 'string' then text = output:sub(1, 512) end
		   end
		   return {ip = string.format('%X', ip), threadId = THREADID,
		      instructionAddress = instruction and string.format('%X', instruction) or nil,
		      isHeuristic = heuristic, disassembly = text, registers = registers,
		      stackPointer = includeStack and registers[wide and 'RSP' or 'ESP'] or nil}
		end
		-- Effective-address grouping parses the one memory operand of the instruction at the execute breakpoint once,
		-- from Cheat Engine's own disassembly: a created disassembler prints hexadecimal instead of symbols and
		-- resolves RIP-relative operands to absolute addresses. Displacements and symbols fold into one constant;
		-- each hit adds the scaled registers that Cheat Engine sets as globals before the instruction runs.
		local operandSizes = {byte = 1, word = 2, dword = 4, fword = 6, qword = 8, tword = 10, dqword = 16,
		   xmmword = 16, ymmword = 32, zmmword = 64}
		local function operandRegisters(wide)
		   local registers = {}
		   if not wide then
		      for _, name in ipairs({'EAX','EBX','ECX','EDX','ESI','EDI','EBP','ESP'}) do
		         registers[name] = {name, true}
		      end
		      return registers
		   end
		   for _, name in ipairs({'RAX','RBX','RCX','RDX','RSI','RDI','RBP','RSP'}) do
		      registers[name] = {name, false}
		      registers['E' .. name:sub(2)] = {name, true}
		   end
		   for index = 8, 15 do
		      registers['R' .. index] = {'R' .. index, false}
		      registers['R' .. index .. 'D'] = {'R' .. index, true}
		   end
		   return registers
		end
		-- Cheat Engine keeps the legacy prefixes first in the instruction bytes, before any REX or VEX prefix. Its
		-- 64-bit operand text names 64-bit registers even under an address-size override (67), which truncates the
		-- address to 32 bits, so only the bytes reveal the override.
		local legacyPrefixes = {[0xF0] = true, [0xF2] = true, [0xF3] = true, [0x2E] = true, [0x36] = true,
		   [0x3E] = true, [0x26] = true, [0x64] = true, [0x65] = true, [0x66] = true, [0x67] = true}
		local function addressSizeOverride(bytes)
		   if type(bytes) ~= 'table' then return false end
		   for index = 1, #bytes do
		      if not legacyPrefixes[bytes[index]] then return false end
		      if bytes[index] == 0x67 then return true end
		   end
		   return false
		end
		-- Returns the operand plan, or nil with an error kind and message.
		local function operandPlan(address, opcode, parameters, length, wide, override)
		   local name = type(opcode) == 'string' and opcode:lower() or ''
		   local text = type(parameters) == 'string' and parameters or ''
		   if name == '' or name == '??' then
		      return nil, 'invalid_argument', 'Cheat Engine could not decode an instruction at the capture address.'
		   end
		   local instruction = (name .. ' ' .. text):sub(1, 256)
		   if name == 'lea' or name == 'nop' then
		      return nil, 'invalid_argument', instruction .. ' only computes an address; it does not access memory.'
		   end
		   if override and not wide then
		      return nil, 'unsupported', instruction .. ' uses a 16-bit address-size override (67 prefix), whose ' ..
		         'memory operand Cheat Engine does not disassemble.'
		   end
		   local open = text:find('[', 1, true)
		   if open == nil then
		      return nil, 'invalid_argument', instruction .. ' has no explicit [...] memory operand.'
		   end
		   local close = text:find(']', open + 1, true)
		   if close == nil or text:find('[', close + 1, true) ~= nil then
		      return nil, 'unsupported', instruction .. ' does not have exactly one memory operand.'
		   end
		   local lead = text:sub(1, open - 1):match('([^,]*)$'):lower()
		   local segment = lead:match('([fg]s)%s*:%s*$')
		   if segment ~= nil then
		      return nil, 'unsupported', instruction .. ' is relative to the ' .. segment ..
		         ' segment base, which a breakpoint context does not expose.'
		   end
		   local keyword = lead:match('(%a+)%s+ptr')
		   local plan = {constant = 0, terms = {}, narrow = not wide or override,
		      size = keyword and operandSizes[keyword] or nil}
		   local registers = operandRegisters(wide)
		   local malformed = instruction .. ' has a memory operand that MCP cannot evaluate.'
		   local expression = text:sub(open + 1, close - 1)
		   local cursor, count = 1, 0
		   while cursor <= #expression do
		      local signs = expression:match('^[%s%+%-]*', cursor)
		      cursor = cursor + #signs
		      count = count + 1
		      if cursor > #expression or count > 8 then return nil, 'unsupported', malformed end
		      -- Cheat Engine prints a negative 8-bit displacement after a SIB byte as '-' and a signed number, such
		      -- as --80, so any minus sign makes the term negative.
		      local sign = signs:find('-', 1, true) ~= nil and -1 or 1
		      local term, quoted = nil, false
		      if expression:sub(cursor, cursor) == '"' then
		         local finish = expression:find('"', cursor + 1, true)
		         if finish == nil then return nil, 'unsupported', malformed end
		         term, quoted = expression:sub(cursor + 1, finish - 1), true
		         cursor = finish + 1
		      else
		         term = expression:match('^[^%+%-]*', cursor)
		         cursor = cursor + #term
		         term = term:match('^%s*(.-)%s*$')
		      end
		      local base, scale = term, 1
		      if not quoted then
		         local scaled, factor = term:match('^([%w_]+)%s*%*%s*(%d+)$')
		         if scaled ~= nil then base, scale = scaled, tonumber(factor) end
		      end
		      local upper = base:upper()
		      local register = not quoted and registers[upper] or nil
		      if register ~= nil then
		         if scale ~= 1 and scale ~= 2 and scale ~= 4 and scale ~= 8 then
		            return nil, 'unsupported', malformed
		         end
		         plan.terms[#plan.terms + 1] = {global = register[1], factor = sign * scale}
		         plan.narrow = plan.narrow or register[2]
		      elseif not quoted and upper:match('^[XYZ]MM%d+$') then
		         return nil, 'unsupported', instruction .. ' uses a vector index (VSIB) and accesses several addresses.'
		      elseif scale ~= 1 or term == '' or term:find('*', 1, true) ~= nil then
		         return nil, 'unsupported', malformed
		      elseif not quoted and (upper == 'RIP' or upper == 'EIP') then
		         plan.constant = plan.constant + sign * (address + length)
		      else
		         local value = nil
		         if not quoted and #term <= 16 and term:match('^%x+$') then
		            value = tonumber(term, 16)
		         else
		            value = getAddressSafe(term)
		         end
		         if math.type(value) ~= 'integer' then
		            return nil, 'unsupported', instruction .. ' names ' .. term:sub(1, 64) ..
		               ', which Cheat Engine could not resolve.'
		         end
		         plan.constant = plan.constant + sign * value
		      end
		   end
		   if count == 0 then return nil, 'unsupported', malformed end
		   return plan
		end
		local function operandAddress(plan)
		   local value = plan.constant
		   for _, term in ipairs(plan.terms) do
		      local register = _G[term.global]
		      if math.type(register) ~= 'integer' then return nil end
		      value = value + register * term.factor
		   end
		   if plan.narrow then value = value & 0xFFFFFFFF end
		   return value
		end
		-- One ring slot per group: a new group takes the slot of the oldest one and drops that group's index entry.
		local function captureGroup(current, key, context, effective, size)
		   local group = current.groups[key]
		   if group == nil then
		      local slot = current.last % current.limit + 1
		      local evictedKey = current.groupKeys[slot]
		      if evictedKey ~= nil then current.groups[evictedKey] = nil end
		      group = {context = context, hitCount = 1, firstContext = context, lastContext = context,
		         effectiveAddress = effective, operandSize = size}
		      current.groups[key] = group
		      current.groupKeys[slot] = key
		      jobPush(current, group)
		   else
		      group.hitCount = group.hitCount + 1
		      group.context = context
		      group.lastContext = context
		   end
		end
		assert(debug_isDebugging(), 'Debugger is not attached')
		assert(not debug_getContext(false), 'Continue the stopped context before starting a capture')
		assert(targetIsX86(), 'Captures require an x86 or x64 target')
		assert(type(debug_removeBreakpointByID) == 'function',
		   'This Cheat Engine version cannot safely remove owned breakpoint ids')
		local address = getAddressSafe(a[5])
		assert(address ~= nil, 'The capture address could not be resolved')
		local triggers = {execute = bptExecute, access = bptAccess, write = bptWrite}
		assert(triggers[a[6]] ~= nil, 'Unknown capture trigger')
		if a[6] ~= 'execute' then
		   assert(address % a[7] == 0, 'A data-breakpoint address must be aligned to its size')
		   assert(a[7] ~= 8 or targetIs64Bit(), 'An eight-byte data breakpoint requires a 64-bit target')
		end
		local operand = nil
		if a[9] then
		   assert(a[6] == 'execute' and not a[8],
		      'Effective-address grouping needs an execute capture without instruction grouping')
		   local disassembler = createDisassembler()
		   assert(disassembler ~= nil, 'Cheat Engine could not create a disassembler')
		   local decoded, data = pcall(function()
		      disassembler.disassemble(address)
		      return disassembler.getLastDisassembleData()
		   end)
		   pcall(disassembler.destroy)
		   if not decoded or type(data) ~= 'table' then
		      return mcp.err('host_refused', 'Cheat Engine could not disassemble the capture address.', 'not_started')
		   end
		   local length = type(data.bytes) == 'table' and #data.bytes or 0
		   local plan, kind, message = operandPlan(address, data.opcode, data.parameters, length, targetIs64Bit(),
		      addressSizeOverride(data.bytes))
		   if plan == nil then
		      return mcp.err(kind, message, 'not_started', 'Capture an instruction that reads or writes memory ' ..
		         'through one [...] operand, or omit groupByEffectiveAddress.')
		   end
		   operand = plan
		end
		local job = jobStart(a[1], a[2], 'debugcapture', a[3], a[4], function(job)
		   job.processId = getOpenedProcessID()
		   assert(math.type(job.processId) == 'integer' and job.processId > 0, 'No process is selected')
		   job.groups = {}
		   job.groupKeys = {}
		   job.total = 0
		   job.onStop = function(current)
		      if current.breakpointId ~= nil and getOpenedProcessID() == current.processId then
		         assert(debug_removeBreakpointByID(current.breakpointId) ~= false,
		            'Cheat Engine rejected removal of the capture breakpoint')
		      end
		      current.breakpointId = nil
		   end
		   job.callback = jobEvent(job, function(current)
		      if getOpenedProcessID() ~= current.processId then
		         jobFinish(current, 'target_changed', 'The selected target changed during capture')
		         return 0
		      end
		      local context = captureContext(a[6], true)
		      current.total = current.total + 1
		      if operand ~= nil then
		         local effective = operandAddress(operand)
		         assert(effective ~= nil, 'Cheat Engine did not supply the registers of the memory operand')
		         local key = string.format('%X', effective)
		         captureGroup(current, key, context, key, operand.size)
		      elseif a[8] then
		         captureGroup(current, context.instructionAddress or context.ip, context, nil, nil)
		      else
		         jobPush(current, {context = context, hitCount = 1})
		      end
		      jobProgress(current, current.total, nil)
		      return 0
		   end, 0)
		   local accepted, id = debug_setBreakpoint(address, a[7], triggers[a[6]], job.callback)
		   assert(accepted == true and id ~= nil, 'Cheat Engine rejected the capture breakpoint')
		   job.breakpointId = id
		end)
		return {jobId = job.id, processId = job.processId, address = string.format('%X', address),
		   lifetimeSeconds = math.floor(a[4] / 1000)}
		""";

	// Arguments: namespace, job id, buffer limit, ttl ms, address, mode, maximum steps, condition register, condition value, include stack.
	// Without a condition register the trace runs for the maximum steps; with one it also stops after the first
	// context whose register (or IP) equals the condition value, the entry context included.
	internal const string StartTrace = LuaJobKernelScripts.Strategies + "\n" + """
	                                                                           local function traceContext(includeStack)
	                                                                              local wide = targetIs64Bit()
	                                                                              local ip = wide and RIP or EIP
	                                                                              assert(math.type(ip) == 'integer' and math.type(THREADID) == 'integer',
	                                                                                 'Cheat Engine did not supply the stepping context')
	                                                                              local names = wide and {'RAX','RBX','RCX','RDX','RSI','RDI','RBP','RSP','RIP','R8','R9','R10','R11',
	                                                                                 'R12','R13','R14','R15','EFLAGS'} or {'EAX','EBX','ECX','EDX','ESI','EDI','EBP','ESP','EIP','EFLAGS'}
	                                                                              local registers = {}
	                                                                              for _, name in ipairs(names) do
	                                                                                 local value = _G[name]
	                                                                                 if math.type(value) == 'integer' then registers[name] = string.format('%X', value) end
	                                                                              end
	                                                                              local text = nil
	                                                                              local ok, output = pcall(disassemble, ip)
	                                                                              if ok and type(output) == 'string' then text = output:sub(1, 512) end
	                                                                              return {ip = string.format('%X', ip), threadId = THREADID, instructionAddress = string.format('%X', ip),
	                                                                                 isHeuristic = false, disassembly = text, registers = registers,
	                                                                                 stackPointer = includeStack and registers[wide and 'RSP' or 'ESP'] or nil}
	                                                                           end
	                                                                           local function conditionReached(context)
	                                                                              if a[8] == nil then return false end
	                                                                              if a[8] == 'IP' then return context.ip == a[9] end
	                                                                              return context.registers[a[8]] == a[9]
	                                                                           end
	                                                                           assert(debug_isDebugging(), 'Debugger is not attached')
	                                                                           assert(not debug_getContext(false), 'Continue the stopped context before starting a trace')
	                                                                           assert(targetIsX86(), 'Step traces require an x86 or x64 target')
	                                                                           if a[8] ~= nil and a[8] ~= 'IP' and a[8] ~= 'EFLAGS'
	                                                                              and (a[8]:sub(1, 1) == 'R') ~= targetIs64Bit() then
	                                                                              return mcp.err('invalid_argument',
	                                                                                 'stopCondition names ' .. a[8] .. ', which the traced target does not have.', 'not_started',
	                                                                                 'Use RAX-R15 or RIP on a 64-bit target, EAX-EIP on a 32-bit target, or IP or EFLAGS.')
	                                                                           end
	                                                                           assert(type(debug_removeBreakpointByID) == 'function',
	                                                                              'This Cheat Engine version cannot safely remove owned breakpoint ids')
	                                                                           assert(_G.debugger_onBreakpoint == nil, 'Another debugger_onBreakpoint hook is installed')
	                                                                           local address = getAddressSafe(a[5])
	                                                                           assert(address ~= nil, 'The trace address could not be resolved')
	                                                                           local job = jobStart(a[1], a[2], 'debugtrace', a[3], a[4], function(job)
	                                                                              job.processId = getOpenedProcessID()
	                                                                              assert(math.type(job.processId) == 'integer' and job.processId > 0, 'No process is selected')
	                                                                              job.total = 0
	                                                                              job.onStop = function(current)
	                                                                                 if current.hook ~= nil and _G.debugger_onBreakpoint == current.hook then _G.debugger_onBreakpoint = nil end
	                                                                                 if current.breakpointId ~= nil and getOpenedProcessID() == current.processId then
	                                                                                    assert(debug_removeBreakpointByID(current.breakpointId) ~= false,
	                                                                                       'Cheat Engine rejected removal of the trace entry breakpoint')
	                                                                                 end
	                                                                                 current.breakpointId = nil
	                                                                              end
	                                                                              local function append(current)
	                                                                                 local context = traceContext(a[10])
	                                                                                 current.total = current.total + 1
	                                                                                 jobPush(current, context)
	                                                                                 jobProgress(current, current.total, a[7])
	                                                                                 return context
	                                                                              end
	                                                                              local function step(current)
	                                                                                 if getOpenedProcessID() ~= current.processId then
	                                                                                    jobFinish(current, 'target_changed', 'The selected target changed during trace')
	                                                                                    return 0
	                                                                                 end
	                                                                                 if THREADID ~= current.threadId or not debug_isStepping() then return 0 end
	                                                                                 local context = append(current)
	                                                                                 if current.total >= a[7] or conditionReached(context) then
	                                                                                    jobFinish(current, 'completed')
	                                                                                    return 1
	                                                                                 end
	                                                                                 debug_continueFromBreakpoint(a[6] == 'over' and co_stepover or co_stepinto)
	                                                                                 return 1
	                                                                              end
	                                                                              job.callback = jobEvent(job, function(current)
	                                                                                 if getOpenedProcessID() ~= current.processId then
	                                                                                    jobFinish(current, 'target_changed', 'The selected target changed during trace')
	                                                                                    return 0
	                                                                                 end
	                                                                                 current.threadId = THREADID
	                                                                                 local context = append(current)
	                                                                                 assert(debug_removeBreakpointByID(current.breakpointId) ~= false,
	                                                                                    'Cheat Engine rejected removal of the trace entry breakpoint')
	                                                                                 current.breakpointId = nil
	                                                                                 if current.total >= a[7] or conditionReached(context) then
	                                                                                    jobFinish(current, 'completed')
	                                                                                    return 1
	                                                                                 end
	                                                                                 current.hook = jobEvent(current, step, 0)
	                                                                                 assert(_G.debugger_onBreakpoint == nil, 'Another debugger_onBreakpoint hook appeared during trace setup')
	                                                                                 _G.debugger_onBreakpoint = current.hook
	                                                                                 debug_continueFromBreakpoint(a[6] == 'over' and co_stepover or co_stepinto)
	                                                                                 return 0
	                                                                              end, 0)
	                                                                              local accepted, id = debug_setBreakpoint(address, 1, bptExecute, job.callback)
	                                                                              assert(accepted == true and id ~= nil, 'Cheat Engine rejected the trace entry breakpoint')
	                                                                              job.breakpointId = id
	                                                                           end)
	                                                                           return {jobId = job.id, processId = job.processId, address = string.format('%X', address),
	                                                                              lifetimeSeconds = math.floor(a[4] / 1000)}
	                                                                           """;

	// Arguments: namespace, job id, buffer limit, ttl ms, address.
	internal const string StartRunTo = LuaJobKernelScripts.Strategies + "\n" + """
	                                                                           assert(debug_isDebugging(), 'Debugger is not attached')
	                                                                           assert(debug_getContext(false), 'Debugger has no stopped context')
	                                                                           assert(type(debug_removeBreakpointByID) == 'function',
	                                                                              'This Cheat Engine version cannot safely remove owned breakpoint ids')
	                                                                           local address = getAddressSafe(a[5])
	                                                                           assert(address ~= nil, 'The destination address could not be resolved')
	                                                                           local job = jobStart(a[1], a[2], 'debugrunto', a[3], a[4], function(job)
	                                                                              job.processId = getOpenedProcessID()
	                                                                              assert(math.type(job.processId) == 'integer' and job.processId > 0, 'No process is selected')
	                                                                              job.onStop = function(current)
	                                                                                 if current.breakpointId ~= nil and getOpenedProcessID() == current.processId then
	                                                                                    assert(debug_removeBreakpointByID(current.breakpointId) ~= false,
	                                                                                       'Cheat Engine rejected removal of the run-to breakpoint')
	                                                                                 end
	                                                                                 current.breakpointId = nil
	                                                                              end
	                                                                              job.callback = jobEvent(job, function(current)
	                                                                                 if getOpenedProcessID() ~= current.processId then
	                                                                                    jobFinish(current, 'target_changed', 'The selected target changed before run-to completed')
	                                                                                    return 0
	                                                                                 end
	                                                                                 jobFinish(current, 'completed')
	                                                                                 return 1
	                                                                              end, 1)
	                                                                              local accepted, id = debug_setBreakpoint(address, 1, bptExecute, job.callback)
	                                                                              assert(accepted == true and id ~= nil, 'Cheat Engine rejected the run-to breakpoint')
	                                                                              job.breakpointId = id
	                                                                           end)
	                                                                           debug_continueFromBreakpoint(co_run)
	                                                                           return {jobId = job.id, processId = job.processId, address = string.format('%X', address),
	                                                                              lifetimeSeconds = math.floor(a[4] / 1000)}
	                                                                           """;
}
