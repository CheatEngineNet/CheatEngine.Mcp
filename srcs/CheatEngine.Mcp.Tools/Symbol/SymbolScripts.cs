namespace CheatEngine.Mcp.Tools.Symbol;

/// <summary>
///     The fixed Lua bodies of the <c>symbol_*</c> tools. Caller data reaches them only through <c>a[]</c>; every copy they
///     return is bounded, and none of them waits for Cheat Engine's symbol loader. Only <see cref="Find" /> has Cheat
///     Engine build unbounded copies, of the main and the registered symbol lists, because Cheat Engine offers no other
///     way to enumerate them.
/// </summary>
internal static class SymbolScripts
{
	/// <summary>The most registered symbols copied: <c>a[1]</c>.</summary>
	internal const int MaximumRegisteredSymbols = 8192;

	/// <summary>The most module names copied from the preference list.</summary>
	internal const int MaximumPreferenceModules = 1024;

	/// <summary>The most matches <see cref="Find" /> collects before it stops and reports truncation: <c>a[2]</c>.</summary>
	internal const int MaximumFoundSymbols = 10000;

	/// <summary>
	///     Searches the registered symbols, then the registered symbol lists (<c>enumRegisteredSymbolLists()</c>: the
	///     functions of <c>{$C}</c> Auto Assembler code, the TCC library, the IL2CPP method names of Cheat Engine's Mono
	///     script and lists registered from Lua), then Cheat Engine's main symbol list, for names that contain the text
	///     <c>a[1]</c> literally, ASCII letters in either case; when <c>a[5]</c> (base) and <c>a[6]</c> (size) are given,
	///     only symbols in <c>[base, base + size)</c> count. A name and address already collected is not collected again.
	///     It stops at the <c>a[2]</c>-th match, sorts the matches by lowercase name, name and unsigned address, and
	///     returns the <c>a[4]</c> matches after the first <c>a[3]</c>, each row of a symbol list completed with its module
	///     and size by one <c>getSymbolFromString</c> lookup in that list. <c>symbolsDoneLoading()</c> and the IL2CPP
	///     method list's <c>FullyLoaded</c> are read before the copies, so <c>symbolsLoaded</c> never claims more than
	///     they hold.
	/// </summary>
	/// <remarks>
	///     <para>
	///         <c>SymbolList.getSymbolList()</c> copies every symbol under Cheat Engine's read lock into one Lua table keyed
	///         by name (duplicate names collapse to one key); its cost grows with the loaded symbols and cannot be bounded
	///         here, so the loop keeps its per-symbol work to one integer range test and one pattern find that allocates
	///         nothing. Cheat Engine resolves a name through the registered symbol lists before the main list, so they are
	///         searched too; a registered list that returns no table is skipped, as missing registered symbols are.
	///     </para>
	///     <para>
	///         Cheat Engine 7.7's <c>autorun\monoscript.lua</c> registers the IL2CPP method list as the global
	///         <c>monoSymbolList</c> and fills it on a thread of its own after <c>symbolsDoneLoading()</c> turned
	///         true, setting its Lua field <c>FullyLoaded</c> false when it starts and true when it is done; while it
	///         is false, <c>symbolsLoaded</c> is false. The read is protected, so a missing list or a list without the
	///         field counts as loaded.
	///     </para>
	///     <para>
	///         A size of 1 from a registered symbol list is dropped: Cheat Engine adds IL2CPP methods, TCC library
	///         functions and <c>createSymbolList(t, name)</c> symbols with that placeholder size. Main-list sizes
	///         above 0 are kept.
	///     </para>
	///     <para>
	///         The pattern is built from the text by escaping every non-alphanumeric byte with <c>%</c>, which Lua reads
	///         as that byte literally, and turning every ASCII letter into a <c>[xX]</c> class: a literal, ASCII
	///         case-insensitive substring test. Measured against Cheat Engine's Lua 5.3 over one million names, it costs
	///         about half of lowercasing each name and running a plain find.
	///     </para>
	/// </remarks>
	internal const string Find = """
	                             local lower, upper, find, ult, kind = string.lower, string.upper, string.find, math.ult, math.type
	                             local pattern = string.gsub(string.gsub(a[1], '%W', '%%%0'), '%a', function(letter)
	                                 return '[' .. lower(letter) .. upper(letter) .. ']'
	                             end)
	                             local cap, first, limit, base, size = a[2], a[3], a[4], a[5], a[6]
	                             local loaded = symbolsDoneLoading() == true
	                             if loaded then
	                                 local read, complete = pcall(function() return monoSymbolList.FullyLoaded end)
	                                 if read and complete == false then loaded = false end
	                             end
	                             local main = getMainSymbolList()
	                             if main == nil then
	                                 return mcp.err('host_refused', 'Cheat Engine returned no main symbol list.', 'completed')
	                             end
	                             local found = {}
	                             local count = 0
	                             local truncated = false
	                             local seen = {}
	                             local function collect(name, address, registered, list, allocsize)
	                                 local addresses = seen[name]
	                                 if addresses ~= nil and addresses[address] then return end
	                                 if count == cap then
	                                     truncated = true
	                                     return
	                                 end
	                                 count = count + 1
	                                 found[count] = {lower(name), name, address, registered, list, allocsize}
	                                 if registered then
	                                     if addresses == nil then
	                                         addresses = {}
	                                         seen[name] = addresses
	                                     end
	                                     addresses[address] = true
	                                 end
	                             end
	                             local function search(list, registered)
	                                 local all = list.getSymbolList()
	                                 if type(all) ~= 'table' then return false end
	                                 for name, address in pairs(all) do
	                                     if kind(address) == 'integer' and (base == nil or ult(address - base, size))
	                                         and find(name, pattern) then
	                                         collect(name, address, registered, list)
	                                         if truncated then break end
	                                     end
	                                 end
	                                 return true
	                             end
	                             local defined = enumRegisteredSymbols()
	                             if type(defined) == 'table' then
	                                 for i = 1, #defined do
	                                     local entry = defined[i]
	                                     local name = type(entry) == 'table' and entry.symbolname or nil
	                                     local address = type(entry) == 'table' and entry.address or nil
	                                     if type(name) == 'string' and kind(address) == 'integer'
	                                         and (base == nil or ult(address - base, size)) and find(name, pattern) then
	                                         collect(name, address, true, nil, entry.allocsize)
	                                         if truncated then break end
	                                     end
	                                 end
	                             end
	                             local lists = enumRegisteredSymbolLists()
	                             if not truncated and type(lists) == 'table' then
	                                 for i = 1, #lists do
	                                     local list = lists[i]
	                                     if list ~= nil then search(list, true) end
	                                     if truncated then break end
	                                 end
	                             end
	                             if not truncated and not search(main, false) then
	                                 return mcp.err('host_refused', 'Cheat Engine returned no symbol list.', 'completed')
	                             end
	                             table.sort(found, function(x, y)
	                                 if x[1] ~= y[1] then return x[1] < y[1] end
	                                 if x[2] ~= y[2] then return x[2] < y[2] end
	                                 return ult(x[3], y[3])
	                             end)
	                             local symbols = {}
	                             for i = first + 1, math.min(count, first + limit) do
	                                 local entry = found[i]
	                                 local row = {name = entry[2], address = mcp.hex(entry[3])}
	                                 if entry[4] then row.registered = true end
	                                 if entry[5] == nil then
	                                     if kind(entry[6]) == 'integer' and entry[6] > 0 then row.size = entry[6] end
	                                 else
	                                     local info = entry[5].getSymbolFromString(entry[2])
	                                     local least = entry[4] and 1 or 0
	                                     if type(info) == 'table' and info.address == entry[3] then
	                                         if type(info.modulename) == 'string' and info.modulename ~= '' then
	                                             row.module = info.modulename
	                                         end
	                                         if kind(info.symbolsize) == 'integer' and info.symbolsize > least then
	                                             row.size = info.symbolsize
	                                         end
	                                     end
	                                 end
	                                 symbols[#symbols + 1] = row
	                             end
	                             return {total = count, truncated = truncated, symbolsLoaded = loaded, symbols = symbols}
	                             """;

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

	/// <summary>Enables requested system symbol sources. Windows may contact Microsoft's symbol service.</summary>
	internal const string EnableSources = """
	                                      local windows = false
	                                      local kernel = false
	                                      if a[1] then
	                                          local ok = pcall(enableWindowsSymbols)
	                                          if not ok then return mcp.err('host_refused', 'Cheat Engine could not enable Windows symbols; the download may have started.', 'unknown') end
	                                          windows = true
	                                      end
	                                      if a[2] then
	                                          local ok = pcall(enableKernelSymbols)
	                                          if not ok then return mcp.err('partial_effect', 'Cheat Engine could not enable kernel symbols; Windows symbols may already be enabled.', 'started') end
	                                          kernel = true
	                                      end
	                                      return {windowsEnabled = windows, kernelEnabled = kernel, externalAccess = windows}
	                                      """;
}
