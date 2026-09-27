namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>
///     The fixed Lua bodies of the <c>memory_*</c> tools, for the documented Cheat Engine functions that the Client does
///     not type. Caller data reaches them only through the <c>a</c> table; each runs inside the tool's dispatch.
/// </summary>
internal static class MemoryScripts
{
	/// <summary>
	///     For each address of <c>a[1]</c>: whether <c>inSystemModule</c> places it in a system module and, when
	///     <c>a[2]</c> is set, its <c>getRTTIClassName</c>. A missing or raising function reads as false or no class.
	/// </summary>
	internal const string AddressExtras = """
	                                      local addresses, rtti = a[1], a[2]
	                                      local count = #addresses
	                                      local system, classes = {n = count}, {n = count}
	                                      local inSystem, className = inSystemModule, getRTTIClassName
	                                      for i = 1, count do
	                                        local address = addresses[i]
	                                        system[i] = false
	                                        if type(inSystem) == 'function' then
	                                          local ok, value = pcall(inSystem, address)
	                                          system[i] = ok and value == true
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
	///     and reads the access again.
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
	                                   if read and write and execute then
	                                     fullAccess(address, size)
	                                   else
	                                     setMemoryProtection(address, size, {r = read, w = write, x = execute})
	                                   end
	                                   local current = flags(getMemoryProtection(address))
	                                   if current == nil then
	                                     return mcp.err('internal', 'Cheat Engine reported no protection after the change.',
	                                       'completed', 'Check the range with memory_get_address_info before changing it again.')
	                                   end
	                                   return {previous = previous, current = current}
	                                   """;
}
