namespace CheatEngine.Mcp.Tools.Aob;

/// <summary>The fixed Lua bodies of the <c>aob_*</c> tools; caller data reaches them only through the <c>a</c> table.</summary>
internal static class AobScripts
{
	/// <summary>
	///     Runs Cheat Engine's <c>getUniqueAOB(address)</c> on <c>a[1]</c>, which returns a signature and the offset of the
	///     address from its start. The known no-unique error returns the quoted last attempt; other errors stay contract
	///     failures rather than being reported as a missing signature.
	/// </summary>
	internal const string UniqueAob = """
	                                  local finder = getUniqueAOB
	                                  if type(finder) ~= 'function' then
	                                    return mcp.err('unsupported', 'This Cheat Engine build has no getUniqueAOB.', 'not_started')
	                                  end
	                                  local ok, pattern, offset = pcall(finder, a[1])
	                                  if not ok then
	                                    local message = tostring(pattern)
	                                    local tried = string.match(message, 'Could not find a unique AOB.-"([^"]+)"')
	                                    if tried ~= nil then
	                                      return {found = false, tried = tried}
	                                    end
	                                    return mcp.err('host_refused', 'getUniqueAOB failed: ' .. message, 'completed')
	                                  end
	                                  if type(pattern) ~= 'string' or pattern == '' then
	                                    return {found = false}
	                                  end
	                                  local integerOffset = math.tointeger(offset)
	                                  if integerOffset == nil then
	                                    return mcp.err('host_refused',
	                                      'getUniqueAOB returned a signature without an integer offset.', 'completed')
	                                  end
	                                  return {found = true, pattern = pattern, offset = integerOffset}
	                                  """;
}
