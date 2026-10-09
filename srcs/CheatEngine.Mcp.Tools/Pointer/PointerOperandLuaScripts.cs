namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>Shared fixed Lua evaluator for captured and supplied x86/x64 memory operands.</summary>
internal static class PointerOperandLuaScripts
{
	internal const string Evaluator = """
		-- Effective-address grouping parses the one memory operand of the instruction at the execute breakpoint once,
		-- from Cheat Engine's own disassembly: a created disassembler prints hexadecimal instead of symbols and
		-- resolves RIP-relative operands to absolute addresses. Displacements and symbols fold into one constant;
		-- each hit adds the scaled registers that Cheat Engine sets as globals before the instruction runs.
		local operandSizes = {byte = 1, word = 2, dword = 4, fword = 6, qword = 8, tword = 10, dqword = 16,
		   xmmword = 16, ymmword = 32, zmmword = 64}
		local function mcpOperandRegisters(wide)
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
		local function mcpOperandOverride(bytes)
		   if type(bytes) ~= 'table' then return false end
		   for index = 1, #bytes do
		      if not legacyPrefixes[bytes[index]] then return false end
		      if bytes[index] == 0x67 then return true end
		   end
		   return false
		end
		-- Returns the operand plan, or nil with an error kind and message.
		local function mcpOperandPlan(address, opcode, parameters, length, wide, override, resolve)
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
		   local plan = {constant = 0, displacement = 0, terms = {}, narrow = not wide or override, operand = text:sub(open, close),
		      size = keyword and operandSizes[keyword] or nil}
		   local registers = mcpOperandRegisters(wide)
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
		         plan.terms[#plan.terms + 1] = {name = register[1], narrow = register[2], factor = sign * scale}
		         plan.narrow = plan.narrow or register[2]
		      elseif not quoted and upper:match('^[XYZ]MM%d+$') then
		         return nil, 'unsupported', instruction .. ' uses a vector index (VSIB) and accesses several addresses.'
		      elseif scale ~= 1 or term == '' or term:find('*', 1, true) ~= nil then
		         return nil, 'unsupported', malformed
		      elseif not quoted and (upper == 'RIP' or upper == 'EIP') then
		         plan.constant = plan.constant + sign * (address + length)
		         plan.narrow = plan.narrow or upper == 'EIP'
		      else
		         local value = nil
		         if not quoted and #term <= 16 and term:match('^%x+$') then
		            value = tonumber(term, 16)
		            plan.displacement = plan.displacement + sign * value
		         else
		            value = resolve(term)
		         end
		         if math.type(value) ~= 'integer' then
		            return nil, 'missing_facts', instruction .. ' names ' .. term:sub(1, 64) ..
		               ', which Cheat Engine could not resolve.'
		         end
		         plan.constant = plan.constant + sign * value
		      end
		   end
		   if count == 0 then return nil, 'unsupported', malformed end
		   return plan
		end
		local function mcpOperandAddress(plan, registers)
		   local value = plan.constant
		   for _, term in ipairs(plan.terms) do
		      local register = registers[term.name]
		      if math.type(register) ~= 'integer' and plan.narrow then
		         local alias = term.name:match('^R%d+$') and (term.name .. 'D') or ('E' .. term.name:sub(2))
		         register = registers[alias]
		      end
		      if math.type(register) ~= 'integer' then return nil, term.name end
		      value = value + register * term.factor
		   end
		   if plan.narrow then value = value & 0xFFFFFFFF end
		   return value
		end
		""";
}
