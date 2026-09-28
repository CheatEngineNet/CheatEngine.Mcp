namespace CheatEngine.Mcp.Tools.Asm;

/// <summary>Fixed Lua bodies for documented Auto Assembler source generation.</summary>
internal static class AsmScripts
{
	/// <summary>
	///     Runs Cheat Engine's <c>generateAPIHookScript(a[1], a[2], a[3], a[4], a[5])</c> and joins its two results
	///     into one script. Cheat Engine 7.7 returns the ENABLE text and the DISABLE text as two strings, each without
	///     its section header (<c>LuaHandler.pas</c> <c>generateAPIHookScript_lua</c>; the shipped
	///     <c>autorun/dotnetpatch.lua</c> reads <c>local ahe, ahd = generateAPIHookScript(...)</c>), so the body
	///     writes the <c>[ENABLE]</c> and <c>[DISABLE]</c> headers itself and turns Windows line breaks into line
	///     feeds. A raised error, such as an address Cheat Engine cannot resolve, is a declared host refusal.
	/// </summary>
	internal const string GenerateApiHook = """
	                                        if type(generateAPIHookScript) ~= 'function' then
	                                          return mcp.err('unsupported',
	                                            'This Cheat Engine build has no generateAPIHookScript.', 'not_started')
	                                        end
	                                        local called, enable, disable = pcall(function()
	                                          return generateAPIHookScript(a[1], a[2], a[3], a[4], a[5])
	                                        end)
	                                        if not called then
	                                          return mcp.err('host_refused',
	                                            'Cheat Engine could not generate an API-hook script: ' ..
	                                            tostring(enable), 'completed')
	                                        end
	                                        if type(enable) ~= 'string' or type(disable) ~= 'string' or
	                                          string.find(enable, '%S') == nil then
	                                          return mcp.err('host_refused',
	                                            'Cheat Engine returned no API-hook script.', 'completed')
	                                        end
	                                        enable = string.gsub(enable, '\r\n', '\n')
	                                        disable = string.gsub(disable, '\r\n', '\n')
	                                        return { script = '[ENABLE]\n' .. enable .. '\n[DISABLE]\n' .. disable }
	                                        """;

	/// <summary>
	///     Syntax-checks the <c>[DISABLE]</c> section of <c>a[1]</c> without applying it, through Cheat Engine's
	///     <c>autoAssembleCheck(script, false, false)</c>, and bounds the error text to <c>a[2]</c> bytes. Cheat Engine
	///     checks the section without the symbols that the <c>[ENABLE]</c> section would register.
	/// </summary>
	internal const string CheckDisable = """
	                                     local called, accepted, message = pcall(autoAssembleCheck, a[1], false, false)
	                                     if not called then return mcp.err('host_refused', 'Cheat Engine could not check the DISABLE section.', 'unknown') end
	                                     if accepted == true then return { accepted = true } end
	                                     local text = nil
	                                     local truncated = false
	                                     if type(message) == 'string' and message ~= '' then
	                                         text = message
	                                         if #text > a[2] then
	                                             text = text:sub(1, a[2])
	                                             truncated = true
	                                         end
	                                     end
	                                     return { accepted = false, hostMessages = text, hostMessagesTruncated = truncated }
	                                     """;
}
