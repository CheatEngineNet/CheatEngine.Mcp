namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>
///     The fixed Lua bodies of the <c>memory_*</c> tools, for the documented Cheat Engine functions that the Client does
///     not type. Caller data reaches them only through the <c>a</c> table; each runs inside the tool's dispatch.
/// </summary>
internal static class MemoryScripts
{
	/// <summary>
	///     For each address of <c>a[1]</c>: whether <c>inSystemModule</c> places it in a system module and, when
	///     <c>a[2]</c> is set, its <c>getRTTIClassName</c>. A missing or raising function leaves that system-module
	///     status unavailable and reports no class.
	/// </summary>
	internal const string AddressExtras = """
	                                      local addresses, rtti = a[1], a[2]
	                                      local count = #addresses
	                                      local system, classes = {n = count}, {n = count}
	                                      local inSystem, className = inSystemModule, getRTTIClassName
	                                      for i = 1, count do
	                                        local address = addresses[i]
	                                        if type(inSystem) == 'function' then
	                                          local ok, value = pcall(inSystem, address)
	                                          if ok and type(value) == 'boolean' then system[i] = value end
	                                        end
	                                        if rtti and type(className) == 'function' then
	                                          local ok, name = pcall(className, address)
	                                          if ok and type(name) == 'string' and name ~= '' then
	                                            classes[i] = name
	                                          end
	                                        end
	                                      end
	                                      return {system = system, rtti = classes}
	                                      """;

	/// <summary>
	///     Reads the access of the page at <c>a[1]</c>, then applies <c>a[3]</c> read, <c>a[4]</c> write and <c>a[5]</c>
	///     execute to <c>a[2]</c> bytes: <c>fullAccess</c> when all three are set, <c>setMemoryProtection</c> otherwise,
	///     and reads the access again. A refusal (<c>false</c>, with Cheat Engine's reason when it gives one) is a
	///     declared <c>host_refused</c>: <c>not_applied</c> when the access is unchanged, <c>started</c> when it
	///     changed and <c>unknown</c> when it cannot be read again.
	/// </summary>
	internal const string Protection = """
	                                   local address, size, read, write, execute = a[1], a[2], a[3], a[4], a[5]
	                                   local function flags(protection)
	                                     if type(protection) ~= 'table' then return nil end
	                                     return {read = protection.r == true, write = protection.w == true,
	                                       execute = protection.x == true}
	                                   end
	                                   local previous = flags(getMemoryProtection(address))
	                                   if previous == nil then
	                                     return mcp.err('invalid_state', 'Cheat Engine reported no protection for the address.',
	                                       'not_started', 'Check the address with memory_get_address_info.')
	                                   end
	                                   -- Cheat Engine 7.7 reads the keys R, W and X; a missing key is false and
	                                   -- grants no access. celua.txt documents r, w and x, so both are passed.
	                                   local ok, message
	                                   if read and write and execute then
	                                     ok = fullAccess(address, size)
	                                   else
	                                     ok, message = setMemoryProtection(address, size,
	                                       {R = read, W = write, X = execute, r = read, w = write, x = execute})
	                                   end
	                                   local current = flags(getMemoryProtection(address))
	                                   if not ok then
	                                     local effect = 'unknown'
	                                     if current ~= nil then
	                                       local same = current.read == previous.read
	                                         and current.write == previous.write and current.execute == previous.execute
	                                       effect = same and 'not_applied' or 'started'
	                                     end
	                                     local reason = type(message) == 'string' and message ~= ''
	                                       and ': ' .. message or '.'
	                                     local hint = 'Check the range with memory_get_address_info; where write and '
	                                       .. 'execute together are refused, make it writable, write, then executable.'
	                                     return mcp.err('host_refused',
	                                       'Cheat Engine refused the protection change' .. reason, effect, hint)
	                                   end
	                                   if current == nil then
	                                     return mcp.err('internal', 'Cheat Engine reported no protection after the change.',
	                                       'completed', 'Check the range with memory_get_address_info before changing it again.')
	                                   end
	                                   return {previous = previous, current = current}
	                                   """;
}
