namespace CheatEngine.Mcp.Tools.Lua;

/// <summary>The implementation-owned, bounded Lua bodies of the <c>lua_*</c> tools.</summary>
internal static class LuaScripts
{
	/// <summary>The most bytes read from the installed <c>celua.txt</c> reference.</summary>
	internal const int MaximumReferenceBytes = 4 * 1024 * 1024;

	/// <summary>The most lines examined from the installed <c>celua.txt</c> reference.</summary>
	internal const int MaximumReferenceLines = 65536;

	/// <summary>The longest reference line returned to a caller.</summary>
	internal const int MaximumReferenceLineBytes = 8192;

	/// <summary>
	///     Searches the current Cheat Engine installation's <c>celua.txt</c>. Arguments: case-insensitive query, page
	///     limit and zero-based offset among matching lines.
	/// </summary>
	internal const string FindApi = """
	                                local directory = getCheatEngineDir()
	                                if type(directory) ~= 'string' or #directory == 0 then
	                                    return mcp.err('host_refused', 'Cheat Engine did not report its installation directory.', 'not_started')
	                                end
	                                local tail = string.sub(directory, -1)
	                                local separator = (tail == '\\' or tail == '/') and '' or '\\'
	                                local file = io.open(directory .. separator .. 'celua.txt', 'rb')
	                                if file == nil then
	                                    return mcp.err('not_found', 'The Cheat Engine installation does not contain celua.txt.', 'not_started',
	                                        'Use the Lua reference that ships with the active Cheat Engine installation.')
	                                end

	                                local query = string.lower(a[1])
	                                local limit = a[2]
	                                local offset = a[3]
	                                local bytes = 0
	                                local sourceLines = 0
	                                local matches = 0
	                                local lines = {}
	                                local carry = ''
	                                local skipLeadingLineFeed = false
	                                local function close()
	                                    local invoked, closed = pcall(function() return file:close() end)
	                                    return invoked and closed == true
	                                end
	                                local function closeFailure()
	                                    return mcp.err('partial_effect', 'Cheat Engine did not confirm closing celua.txt after reading it.',
	                                        'cleanup_unconfirmed', 'Inspect Cheat Engine before repeating the Lua API search.')
	                                end
	                                local function truncated()
	                                    if not close() then return closeFailure() end
	                                    return {total = matches, nextOffset = nil, truncated = true, lines = lines}
	                                end
	                                local function timeout()
	                                    if not close() then return closeFailure() end
	                                    return mcp.err('timeout', 'Searching the installed celua.txt exceeded the dispatch budget.', 'completed',
	                                        'Narrow the query and retry.')
	                                end
	                                local function addLine(line)
	                                    if #line > 8192 then return false end
	                                    sourceLines = sourceLines + 1
	                                    if sourceLines > 65536 then return false end
	                                    if string.find(string.lower(line), query, 1, true) ~= nil then
	                                        if matches >= offset and #lines < limit then
	                                            lines[#lines + 1] = {number = sourceLines, text = line}
	                                        end
	                                        matches = matches + 1
	                                    end
	                                    if sourceLines % 256 == 0 and mcp.expired() then
	                                        return nil
	                                    end
	                                    return true
	                                end

	                                while true do
	                                    local chunk, readError = file:read(4096)
	                                    if chunk == nil then
	                                        if readError ~= nil then
	                                            if not close() then return closeFailure() end
	                                            return mcp.err('host_refused', 'Cheat Engine could not finish reading celua.txt.', 'completed',
	                                                'Inspect the celua.txt file in the active Cheat Engine installation.')
	                                        end
	                                        if #carry ~= 0 then
	                                            local continued = addLine(carry)
	                                            if continued == nil then
	                                                return timeout()
	                                            end
	                                            if not continued then return truncated() end
	                                        end
	                                        break
	                                    end
	                                    bytes = bytes + #chunk
	                                    if bytes > 4194304 then
	                                        return truncated()
	                                    end
	                                    local start = 1
	                                    if skipLeadingLineFeed and string.byte(chunk, 1) == 10 then start = 2 end
	                                    skipLeadingLineFeed = false
	                                    while start <= #chunk do
	                                        local finish = string.find(chunk, '[\r\n]', start)
	                                        if finish == nil then
	                                            carry = carry .. string.sub(chunk, start)
	                                            if #carry > 8192 then return truncated() end
	                                            break
	                                        end
	                                        local line = carry .. string.sub(chunk, start, finish - 1)
	                                        carry = ''
	                                        local terminator = string.byte(chunk, finish)
	                                        start = finish + 1
	                                        if terminator == 13 then
	                                            if string.byte(chunk, start) == 10 then
	                                                start = start + 1
	                                            elseif finish == #chunk then
	                                                skipLeadingLineFeed = true
	                                            end
	                                        end
	                                        local continued = addLine(line)
	                                        if continued == nil then
	                                            return timeout()
	                                        end
	                                                        if not continued then return truncated() end
	                                                        end
	                                                    end

	                                if not close() then return closeFailure() end
	                                local nextOffset = offset + #lines
	                                return {total = matches, nextOffset = nextOffset < matches and nextOffset or nil, truncated = false, lines = lines}
	                                """;
}
