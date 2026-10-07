namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>Fixed Lua dispatch for supplied pointer-access facts.</summary>
internal static class PointerAccessLuaScripts
{
	internal static string GetAccessInfo => $$"""
		{{PointerOperandLuaScripts.Evaluator}}
		local function signedHex(value)
		   if value < 0 then return '-' .. string.format('%X', -value) end
		   return string.format('%X', value)
		end
		local function suppliedMap(pairs)
		   local result = {}
		   for _, pair in ipairs(pairs) do
		      local digits = pair[2]:gsub('^0[xX]', '')
		      result[pair[1]] = tonumber(digits, 16)
		   end
		   return result
		end
		local instruction = (a[1] .. ' ' .. a[2]):sub(1, 512)
		local function refusal(status, message)
		   return {status = status, instruction = instruction, contextMayHaveChanged = a[10] == true,
		      uncertainty = message}
		end
		local opcode = a[1]:lower()
		local bitAddressOpcodes = {bt = true, btc = true, btr = true, bts = true}
		if bitAddressOpcodes[opcode] then
		   return refusal('unsupported', 'Bit-string memory operations can adjust the accessed address using a bit index ' ..
		      'outside the memory operand; supplied bracket arithmetic is insufficient.')
		end
		local prefixes = {lock = true, rep = true, repe = true, repz = true, repne = true, repnz = true,
		   xacquire = true, xrelease = true, bnd = true, data16 = true, addr16 = true, addr32 = true}
		if prefixes[opcode] then
		   return refusal('unsupported', 'Instruction prefixes in supplied text require opcode semantics that this analysis ' ..
		      'does not evaluate.')
		end
		local registers, symbols = suppliedMap(a[7]), suppliedMap(a[8])
		local plan, kind, message = mcpOperandPlan(a[3], a[1], a[2], a[4], a[5], mcpOperandOverride(a[6]),
		   function(name) return symbols[name:upper()] end)
		if plan == nil then
		   return refusal(kind == 'missing_facts' and 'missing_facts' or 'unsupported', message)
		end
		-- The shared capture evaluator accepts CE disassembly. Supplied facts additionally need a representable
		-- base/index shape before returning pointer guidance; never silently discard extra register terms.
		if #plan.terms > 2 then
		   return refusal('unsupported', 'The supplied operand has more than a base and an index register.')
		end
		local base, index, scale = nil, nil, nil
		for _, term in ipairs(plan.terms) do
		   if opcode == 'pop' and (term.name == 'RSP' or term.name == 'ESP') then
		      return refusal('unsupported', 'POP computes a stack-relative destination after changing the stack pointer; ' ..
		         'the supplied operand facts do not determine that intermediate context.')
		   end
		   if term.factor < 0 then
		      return refusal('unsupported', 'A subtracted register is not a supported x86/x64 address operand.')
		   end
		   if base == nil and term.factor == 1 then
		      base = term.name
		   elseif index == nil then
		      index, scale = term.name, term.factor
		   else
		      return refusal('unsupported', 'The supplied operand has more than one scaled index.')
		   end
		end
		local effective, missing = mcpOperandAddress(plan, registers)
		if effective == nil then
		   return refusal('missing_facts', 'Missing captured register ' .. missing .. '.')
		end
		local mismatch = false
		if a[9] ~= nil then
		   local digits = a[9]:gsub('^0[xX]', '')
		   mismatch = tonumber(digits, 16) ~= effective
		end
		local dynamic = index ~= nil
		local structure = (not dynamic and base ~= nil) and (effective - plan.displacement) or nil
		if structure ~= nil and plan.narrow then structure = structure & 0xFFFFFFFF end
		local uncertainty = nil
		if a[10] == true then
		   uncertainty = 'Post-execution registers may have changed before capture.'
		elseif dynamic then
		   uncertainty = 'Indexed addressing is a dynamic array offset, not a stable pointer-chain offset.'
		elseif mismatch then
		   uncertainty = 'The computed address differs from the supplied observed access address.'
		end
		return {status = 'supported', instruction = instruction, memoryOperand = plan.operand,
		   baseRegister = base, indexRegister = index, scale = scale, displacement = signedHex(plan.displacement),
		   effectiveAddress = string.format('%X', effective),
		   candidateStructureBase = structure and string.format('%X', structure) or nil,
		   nextPointerSearchValue = structure and string.format('%X', structure) or nil, dynamicOffset = dynamic,
		   observedAddressMismatch = mismatch, contextMayHaveChanged = a[10] == true, uncertainty = uncertainty}
		""";
}
