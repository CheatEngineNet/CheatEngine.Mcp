namespace CheatEngine.Mcp.Tools.Aob;

/// <summary>The fixed Lua bodies of the <c>aob_*</c> tools; caller data reaches them only through the <c>a</c> table.</summary>
internal static class AobScripts
{
	/// <summary>
	///     Runs Cheat Engine's <c>getUniqueAOB(address)</c> on <c>a[1]</c>, which returns a signature and the offset of
	///     the address from its start. Cheat Engine 7.7 reports a failure as its result text,
	///     <c>ERROR: Could not find unique AOB, tried code "48 8B 05"</c>, with no meaningful offset
	///     (<c>frmautoinjectunit.pas</c> <c>GetUniqueAOB</c>): a result that holds more than hexadecimal digits,
	///     spaces and wildcards is that text, and its quoted part is the code that was tried. A raised error stays a
	///     contract failure rather than being reported as a missing signature.
	/// </summary>
	internal const string UniqueAob = """
	                                  local finder = getUniqueAOB
	                                  if type(finder) ~= 'function' then
	                                    return mcp.err('unsupported', 'This Cheat Engine build has no getUniqueAOB.',
	                                      'not_started')
	                                  end
	                                  local ok, pattern, offset = pcall(finder, a[1])
	                                  if not ok then
	                                    return mcp.err('host_refused', 'getUniqueAOB failed: ' .. tostring(pattern),
	                                      'completed')
	                                  end
	                                  if type(pattern) ~= 'string' or pattern == '' then
	                                    return {found = false}
	                                  end
	                                  if string.find(pattern, '[^%x%s%*%?]') ~= nil then
	                                    return {found = false, tried = string.match(pattern, '"([^"]+)"')}
	                                  end
	                                  local integerOffset = math.tointeger(offset)
	                                  if integerOffset == nil then
	                                    return mcp.err('host_refused',
	                                      'getUniqueAOB returned a signature without an integer offset.', 'completed')
	                                  end
	                                  return {found = true, pattern = pattern, offset = integerOffset}
	                                  """;
}
