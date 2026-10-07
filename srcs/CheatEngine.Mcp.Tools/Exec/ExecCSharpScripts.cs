namespace CheatEngine.Mcp.Tools.Exec;

/// <summary>Fixed Lua bridge for Cheat Engine's C# compiler.</summary>
internal static class ExecCSharpScripts
{
	// LuaHandler.compilecs returns a generated filename, or nil and the compiler's line-message text. An absent bridge is
	// an explicit unavailable outcome; a present bridge can still report its .NET Framework/compiler prerequisite through
	// its own diagnostic. Arguments remain in the typed a table so caller source never becomes Lua source.
	internal const string Compile = """
	                                local function diagnostic(value)
	                                    local text = tostring(value or 'Cheat Engine returned no compiler diagnostic.')
	                                    local maximum = 16384
	                                    if #text <= maximum then return text, false end
	                                    local ok, nextCharacter = pcall(utf8.offset, text, 0, maximum + 1)
	                                    if ok and nextCharacter ~= nil then
	                                        return text:sub(1, nextCharacter - 1), true
	                                    end
	                                    return text:sub(1, maximum), true
	                                end
	                                if type(compileCS) ~= 'function' then
	                                    return {compilerAvailable = false}
	                                end
	                                local called, filename, message = pcall(compileCS, a[1], a[2], a[3])
	                                if not called then
	                                    local text, truncated = diagnostic(filename)
	                                    return {diagnostic = text, diagnosticTruncated = truncated}
	                                end
	                                if type(filename) ~= 'string' or #filename == 0 then
	                                    local text, truncated = diagnostic(message)
	                                    return {diagnostic = text, diagnosticTruncated = truncated}
	                                end
	                                return {assemblyPath = filename}
	                                """;
}
