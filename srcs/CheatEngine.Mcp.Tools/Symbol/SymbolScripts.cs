namespace CheatEngine.Mcp.Tools.Symbol;

/// <summary>
///     The fixed Lua bodies of the <c>symbol_*</c> tools. Caller data reaches them only through <c>a[]</c>; every copy is
///     bounded, and none of them waits for Cheat Engine's symbol loader.
/// </summary>
internal static class SymbolScripts
{
	/// <summary>The most registered symbols copied: <c>a[1]</c>.</summary>
	internal const int MaximumRegisteredSymbols = 8192;

	/// <summary>The most module names copied from the preference list.</summary>
	internal const int MaximumPreferenceModules = 1024;

	/// <summary>Copies at most <c>a[1]</c> entries of <c>enumRegisteredSymbols()</c>.</summary>
	internal const string ListRegistered = """
		local list = enumRegisteredSymbols()
		if type(list) ~= 'table' then
			return mcp.err('host_refused', 'Cheat Engine returned no registered symbol list.', 'completed')
		end
		local count = #list
		local limit = math.min(count, a[1])
		local symbols = {}
		for i = 1, limit do
			local entry = list[i]
			symbols[i] = {
				name = tostring(entry.symbolname),
				address = math.type(entry.address) == 'integer' and mcp.hex(entry.address) or nil,
				allocSize = math.type(entry.allocsize) == 'integer' and entry.allocsize or nil,
				processId = math.type(entry.processid) == 'integer' and entry.processid or nil,
				doNotSave = entry.donotsave == true or nil
			}
		end
		return {symbols = symbols, count = count, truncated = count > limit}
		""";

	/// <summary>Copies at most 1024 names of <c>getModulePreference()</c>.</summary>
	internal const string GetModulePreference = """
		local current = getModulePreference()
		if type(current) ~= 'table' then current = {} end
		local modules = {}
		local limit = math.min(#current, 1024)
		for i = 1, limit do modules[i] = tostring(current[i]) end
		return {modules = modules, truncated = #current > limit}
		""";

	/// <summary>
	///     Sets the preference list to <c>a[1]</c> when <c>a[2]</c> (replace) is true, else puts <c>a[1]</c> first and keeps
	///     the other current modules after it, then reads the list back.
	/// </summary>
	internal const string SetModulePreference = """
		local wanted = a[1]
		local list = {}
		local seen = {}
		for i = 1, #wanted do
			list[#list + 1] = wanted[i]
			seen[string.lower(wanted[i])] = true
		end
		if not a[2] then
			local previous = getModulePreference()
			if type(previous) ~= 'table' then
				return mcp.err('host_refused', 'Cheat Engine returned no module preference list.', 'not_started')
			end
			if #previous > 1024 then
				return mcp.err('limit_exceeded', 'Cheat Engine has more than 1024 module preferences; it was not changed.', 'not_started')
			end
			for i = 1, #previous do
				local name = previous[i]
				if type(name) == 'string' and not seen[string.lower(name)] then
					list[#list + 1] = name
					seen[string.lower(name)] = true
				end
			end
		end
		setModulePreference(list)

		""" + GetModulePreference;

	/// <summary>
	///     Starts the reload named by <c>a[1]</c> (<c>new_modules</c>, <c>all</c> or <c>dotnet</c> with the optional module
	///     <c>a[2]</c>) without waiting, then reports <c>symbolsDoneLoading()</c>.
	/// </summary>
	internal const string Reload = """
		local scope = a[1]
		if scope == 'new_modules' then
			loadNewSymbols()
		elseif scope == 'all' then
			reinitializeSymbolhandler(false)
		elseif scope == 'dotnet' then
			if a[2] == nil then reinitializeDotNetSymbolhandler() else reinitializeDotNetSymbolhandler(a[2]) end
		else
			return mcp.err('invalid_argument', 'The reload scope is not new_modules, all or dotnet.', 'not_started')
		end
		return {done = symbolsDoneLoading() == true}
		""";

	/// <summary>Loads the symbols of file <c>a[1]</c> at base <c>a[2]</c>, enumerating structures when <c>a[3]</c>.</summary>
	internal const string AddModule = """
		local result = symbolHandlerAddModule(a[1], a[2], a[3])
		if result == false then
			return mcp.err('host_refused', 'Cheat Engine could not load symbols from the file.', 'completed')
		end
		return {loaded = true}
		""";
}
