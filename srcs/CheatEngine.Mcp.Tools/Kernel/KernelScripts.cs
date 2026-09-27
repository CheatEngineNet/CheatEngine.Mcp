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

	internal const string InitializeDbvm = """
	                                       if type(dbvm_initialize) ~= 'function' or type(dbvm_initialized) ~= 'function' then
	                                           return mcp.err('unsupported', 'This Cheat Engine build has no DBVM initialization API.', 'not_started')
	                                       end
	                                       local called = pcall(dbvm_initialize, a[1], a[2])
	                                       local checked, initialized = pcall(dbvm_initialized)
	                                       if not called or not checked or initialized ~= true then
	                                           return mcp.err('host_refused', 'Cheat Engine did not confirm DBVM initialization; it may still be changing host state.',
	                                               'unknown', 'Check kernel_get_status before deciding whether to retry.')
	                                       end
	                                       return {dbvmInitialized = true, offloadOperatingSystem = a[1], reason = a[2]}
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
			active.watchSeen = 0
			active.onStop = function(current)
				local stopped, result = pcall(dbvm_watch_disable, current.watchId)
				if not stopped or result == false then error('DBVM watch ' .. tostring(current.watchId) .. ' could not be disabled', 0) end
			end
		end)
		return {watchId = job.watchId}
		""";

	internal static readonly string PollWatch = LuaJobKernelScripts.Kernel + "\n" + """
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
		if job == nil or job.kind ~= 'kernelwatch' then
			return mcp.err('not_found', 'Unknown or expired DBVM watch job: ' .. tostring(a[2]) .. '.', 'not_started',
				'Start a new watch or list current jobs with runtime_list_jobs.')
		end
		if job.state == 'running' then
			if type(dbvm_watch_retrievelog) ~= 'function' then
				return mcp.err('unsupported', 'This Cheat Engine build has no DBVM watch-log API.', 'started')
			end
			local called, entries = pcall(dbvm_watch_retrievelog, job.watchId)
			if not called or type(entries) ~= 'table' then
				return mcp.err('host_refused', 'DBVM could not retrieve watch events; the watch remains armed.', 'started',
					'Stop the job with runtime_stop_job if it must no longer watch memory.')
			end
			local seen = job.watchSeen or 0
			if seen > #entries then seen = 0 end
			for index = seen + 1, #entries do jobPush(job, copyEvent(entries[index], index)) end
			job.watchSeen = #entries
		end
		local page = jobPoll(job, a[3], a[4])
		return {job = page.job, events = page.items, firstSequence = page.firstSequence,
			nextAfterSequence = page.nextAfterSequence, more = page.more, dropped = page.dropped}
		""";
}
