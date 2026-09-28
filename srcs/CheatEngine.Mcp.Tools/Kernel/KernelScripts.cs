using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Kernel;

/// <summary>Implementation-owned fixed Lua bodies for DBK, DBVM and DBVM-watch lifecycle calls.</summary>
internal static class KernelScripts
{
	internal const string GetStatus = """
	                                  local function initialized(f)
	                                      if type(f) ~= 'function' then return false end
	                                      local ok, value = pcall(f)
	                                      return ok and value == true
	                                  end
	                                  local function register(f)
	                                      if type(f) ~= 'function' then return nil end
	                                      local ok, value = pcall(f)
	                                      return ok and type(value) == 'number' and mcp.hex(value) or nil
	                                  end
	                                  return {dbkInitialized = initialized(dbk_initialized), dbvmInitialized = initialized(dbvm_initialized),
	                                      cr0 = register(dbk_getCR0), cr3 = register(dbk_getCR3), cr4 = register(dbk_getCR4),
	                                      dbvmCr4 = register(dbvm_getCR4)}
	                                  """;

	/// <summary>
	///     Cheat Engine's own confirmation text for loading DBVM, <c>rsToUseThisFunctionYouWillNeedToRunDBVM</c>. A
	///     reason passed to <c>dbvm_initialize</c> replaces it in the dialog, so the body puts it before the MCP
	///     reason.
	/// </summary>
	internal const string DbvmCrashWarning =
		"To use this function you will need to run DBVM. There is a high chance running DBVM can crash your system " +
		"and make you lose your data(So don't forget to save first). Do you want to run DBVM?";

	// Arguments: offload the operating system, Cheat Engine's crash warning, the caller's reason or nil.
	internal const string InitializeDbvm = """
		if type(dbvm_initialize) ~= 'function' or type(dbvm_initialized) ~= 'function' then
			return mcp.err('unsupported', 'This Cheat Engine build has no DBVM initialization API.', 'not_started')
		end
		if not a[1] then
			-- Without offloading, Cheat Engine only sets its default DBVM keys and reports whether DBVM already runs.
			pcall(dbvm_initialize, false)
			local checked, initialized = pcall(dbvm_initialized)
			if checked and initialized == true then
				return {dbvmInitialized = true, offloadOperatingSystem = false, reason = a[3]}
			end
			return mcp.err('invalid_state', 'DBVM is not running; offloadOperatingSystem=false loads nothing.',
				'not_started', 'Check kernel_get_status. Only offloadOperatingSystem=true loads DBK and then DBVM '
					.. 'after a human confirms, which can crash the host.')
		end
		-- A reason replaces Cheat Engine's own crash warning in its dialog, so the warning comes first.
		local warning = a[2]
		if type(translate) == 'function' then
			local translated, text = pcall(translate, warning)
			if translated and type(text) == 'string' and text ~= '' then warning = text end
		end
		local prompt = warning
		if a[3] ~= nil then prompt = warning .. '\n\nReason given by the MCP client: ' .. a[3] end
		local called = pcall(dbvm_initialize, true, prompt)
		local checked, initialized = pcall(dbvm_initialized)
		if not called or not checked or initialized ~= true then
			return mcp.err('host_refused',
				'Cheat Engine did not confirm DBVM initialization; it may still be changing host state.', 'unknown',
				'Check kernel_get_status before deciding whether to retry.')
		end
		return {dbvmInitialized = true, offloadOperatingSystem = true, reason = a[3]}
		""";

	internal const string TranslateAddress = """
	                                         if type(dbk_getPhysicalAddress) ~= 'function' then
	                                             return mcp.err('unsupported', 'This Cheat Engine build has no DBK address-translation API.', 'not_started')
	                                         end
	                                         local called, physical = pcall(dbk_getPhysicalAddress, a[1])
	                                         if not called or type(physical) ~= 'number' then
	                                             return mcp.err('not_found', 'DBK could not translate the virtual address to a current physical page.', 'not_started',
	                                                 'Translate immediately before a physical access; the page may no longer be resident.')
	                                         end
	                                         return {virtualAddress = mcp.hex(a[1]), physicalAddress = mcp.hex(physical)}
	                                         """;

	internal const string ReadPhysical = """
	                                     if type(dbvm_initialized) ~= 'function' then
	                                         return mcp.err('unsupported', 'This Cheat Engine build has no DBVM state API.', 'not_started')
	                                     end
	                                     local checked, initialized = pcall(dbvm_initialized)
	                                     if not checked or initialized ~= true then
	                                         return mcp.err('invalid_state', 'DBVM is not initialized, so physical memory cannot be read.', 'not_started',
	                                             'Call kernel_get_status and initialize DBVM in Cheat Engine before a physical read.')
	                                     end
	                                     if type(dbvm_readPhysicalMemory) ~= 'function' then
	                                         return mcp.err('unsupported', 'This Cheat Engine build has no DBVM physical-memory read API.', 'not_started')
	                                     end
	                                     local called, bytes = pcall(dbvm_readPhysicalMemory, a[1], a[2])
	                                     if not called or type(bytes) ~= 'table' or #bytes ~= a[2] then
	                                         return mcp.err('invalid_state', 'DBVM could not read the requested physical range.', 'not_started',
	                                             'Check kernel_get_status and translate the page again before retrying.')
	                                     end
	                                     for index = 1, #bytes do
	                                         if type(bytes[index]) ~= 'number' or bytes[index] < 0 or bytes[index] > 255 then
	                                             return mcp.err('host_refused', 'DBVM returned an invalid physical-memory byte table.', 'completed')
	                                         end
	                                     end
	                                     return {physicalAddress = mcp.hex(a[1]), bytes = mcp.bytes(bytes, a[2])}
	                                     """;

	internal const string WritePhysical = """
	                                      if type(dbvm_initialized) ~= 'function' then
	                                          return mcp.err('unsupported', 'This Cheat Engine build has no DBVM state API.', 'not_started')
	                                      end
	                                      local checked, initialized = pcall(dbvm_initialized)
	                                      if not checked or initialized ~= true then
	                                          return mcp.err('invalid_state', 'DBVM is not initialized, so physical memory cannot be written.', 'not_started',
	                                              'Call kernel_get_status and initialize DBVM in Cheat Engine before a physical write.')
	                                      end
	                                      if type(dbvm_writePhysicalMemory) ~= 'function' then
	                                          return mcp.err('unsupported', 'This Cheat Engine build has no DBVM physical-memory write API.', 'not_started')
	                                      end
	                                      local called, accepted = pcall(dbvm_writePhysicalMemory, a[1], a[2])
	                                      if not called or accepted == false then
	                                          return mcp.err('host_refused', 'DBVM did not confirm the physical-memory write; bytes may have changed.', 'unknown',
	                                              'Read the physical range before considering another write.')
	                                      end
	                                      return {physicalAddress = mcp.hex(a[1]), bytesWritten = #a[2]}
	                                      """;

	internal static readonly string StartWatch = LuaJobKernelScripts.Kernel + "\n" + """
		if type(dbvm_initialized) ~= 'function' then
			return mcp.err('unsupported', 'This Cheat Engine build has no DBVM state API.', 'not_started')
		end
		local checked, initialized = pcall(dbvm_initialized)
		if not checked or initialized ~= true then
			return mcp.err('invalid_state', 'DBVM is not initialized, so a physical watch cannot be armed.', 'not_started',
				'Call kernel_get_status and initialize DBVM in Cheat Engine before starting a watch.')
		end
		local function createWatch(access, address, byteSize, options, entryCount)
			if access == 0 then return dbvm_watch_reads(address, byteSize, options, entryCount) end
			if access == 1 then return dbvm_watch_writes(address, byteSize, options, entryCount) end
			if access == 2 then return dbvm_watch_executes(address, byteSize, options, entryCount) end
			error('Unknown DBVM watch access.', 0)
		end
		if type(dbvm_watch_reads) ~= 'function' or type(dbvm_watch_writes) ~= 'function' or
			type(dbvm_watch_executes) ~= 'function' or type(dbvm_watch_disable) ~= 'function' then
			return mcp.err('unsupported', 'This Cheat Engine build has no complete DBVM watch API.', 'not_started')
		end
		local job = jobStart(a[1], a[2], 'kernelwatch', a[3], a[4], function(active)
			local id = createWatch(a[5], a[6], a[7], a[8], a[9])
			assert(type(id) == 'number' and id >= 0, 'DBVM watch creation failed')
			active.watchId = id
			active.watchRetrieved = 0
			active.onStop = function(current)
				local stopped, result = pcall(dbvm_watch_disable, current.watchId)
				if not stopped or result == false then error('DBVM watch ' .. tostring(current.watchId) .. ' could not be disabled', 0) end
			end
		end)
		return {watchId = job.watchId}
		""";

	/// <summary>
	///     Moves every event DBVM logged since the previous retrieval into the watch job's ring; the page itself is
	///     read by the normal job poll. Arguments: namespace, job id. DBVM empties its log after each complete
	///     retrieval, so every returned entry is new and <c>sourceIndex</c> counts all events retrieved for the watch.
	/// </summary>
	internal static readonly string DrainWatch = LuaJobKernelScripts.Kernel + "\n" + """
		local function addressOf(event, upper, lower)
			local value = event[upper]
			if value == nil then value = event[lower] end
			return type(value) == 'number' and mcp.hex(value) or nil
		end
		local function copyEvent(event, index)
			if type(event) ~= 'table' then return {sourceIndex = index} end
			return {sourceIndex = index, rip = addressOf(event, 'RIP', 'rip'), rsp = addressOf(event, 'RSP', 'rsp'),
				rax = addressOf(event, 'RAX', 'rax'), rbx = addressOf(event, 'RBX', 'rbx'),
				rcx = addressOf(event, 'RCX', 'rcx'), rdx = addressOf(event, 'RDX', 'rdx'),
				rsi = addressOf(event, 'RSI', 'rsi'), rdi = addressOf(event, 'RDI', 'rdi'),
				rbp = addressOf(event, 'RBP', 'rbp'), r8 = addressOf(event, 'R8', 'r8'),
				r9 = addressOf(event, 'R9', 'r9'), r10 = addressOf(event, 'R10', 'r10'),
				r11 = addressOf(event, 'R11', 'r11'), r12 = addressOf(event, 'R12', 'r12'),
				r13 = addressOf(event, 'R13', 'r13'), r14 = addressOf(event, 'R14', 'r14'),
				r15 = addressOf(event, 'R15', 'r15'), cr3 = addressOf(event, 'CR3', 'cr3')}
		end
		local job = jobFind(a[1], a[2])
		-- A missing job is reported by the job poll that follows, which also retires the managed handle.
		if job == nil or job.kind ~= 'kernelwatch' then return {found = false, retrieved = 0} end
		if job.state ~= 'running' then return {found = true, retrieved = 0} end
		if type(dbvm_watch_retrievelog) ~= 'function' then
			return mcp.err('unsupported', 'This Cheat Engine build has no DBVM watch-log API.', 'started')
		end
		local called, entries = pcall(dbvm_watch_retrievelog, job.watchId)
		if not called or type(entries) ~= 'table' then
			return mcp.err('host_refused', 'DBVM could not retrieve watch events; the watch remains armed.', 'started',
				'Stop the job with runtime_stop_job if it must no longer watch memory.')
		end
		-- Each retrieval returns only events logged since the previous one; a full ring evicts and counts the oldest.
		local retrieved = job.watchRetrieved or 0
		for index = 1, #entries do jobPush(job, copyEvent(entries[index], retrieved + index)) end
		job.watchRetrieved = retrieved + #entries
		return {found = true, retrieved = #entries}
		""";
}
