using CheatEngine.Client;

namespace CheatEngine.Mcp.Tools;

/// <summary>CE-owned callbacks contain Lua only; no managed delegate survives a dispatched request.</summary>
internal static class LuaDebuggerScripts
{
	internal static object Invoke(ICheatEngineClient client, string operation, string body, params object?[] arguments) =>
		ToolExecution.Run(client, () =>
		{
			Dictionary<string, object?> result = (Dictionary<string, object?>) LuaToolRuntime.Execute(client, operation, body, arguments)!;
			return result.TryGetValue("error", out object? error)
				? new
				{
					success = false,
					error,
					result
				}
				: new
				{
					success = true,
					result
				};
		});

	internal const string Guard = """
		for _,entry in pairs(_G.__cheatengine_mcp_debugger_jobs or {}) do
			if entry.active or entry.cleanupError then return true end
		end
		return false
		""";

	// Arguments: id, address, kind, trigger, size, limit, lifetimeSeconds.
	internal const string Start = """
		assert(debug_isDebugging(), 'Debugger is not attached')
		assert(not debug_isBroken(), 'Continue the current breakpoint before starting a capture or trace')
		assert(targetIsX86(), 'These debugger workflows require an x86 or x64 target')
		assert(type(debug_removeBreakpointByID)=='function', 'This CE version cannot remove owned breakpoint IDs')
		assert(_G.debugger_onBreakpoint==nil, 'Another debugger_onBreakpoint hook is installed')
		local address=getAddressSafe(a[2])
		assert(address~=nil, 'Address could not be resolved')
		if a[4]~='execute' then assert(address%a[5]==0, 'A data breakpoint address must be aligned to its size') end
		assert(a[4]=='execute' or a[5]~=8 or targetIs64Bit(), 'Eight-byte data breakpoints require an x64 target')
		local store=_G.__cheatengine_mcp_debugger_jobs or {}
		_G.__cheatengine_mcp_debugger_jobs=store
		local count=0
		for _,job in pairs(store) do
			count=count+1
			assert(not job.cleanupError, 'A debugger job needs cleanup before another can start')
			assert(not job.active or (a[3]=='capture' and job.kind=='capture'), 'Stop active debugger jobs before starting a trace')
		end
		assert(count<32, 'At most 32 debugger jobs may be retained; stop completed jobs to release them')
		local existing=debug_getBreakpointList() or {}
		if a[3]=='trace' then assert(#existing==0, 'Remove existing breakpoints before starting a step trace') end
		for _,bp in ipairs(existing) do assert(bp~=address, 'A breakpoint already exists at this address') end
		local entry={id=a[1],kind=a[3],pid=getOpenedProcessID(),address=address,trigger=a[4],
			size=a[5],limit=a[6],hits={},total=0,dropped=0,active=true,started=getTickCount(),wide=targetIs64Bit()}
		assert(entry.pid and entry.pid>0, 'No process is attached')
		store[entry.id]=entry
		local function destroyTimer()
			if entry.timer then
				local timer=entry.timer
				timer.Enabled=false
				timer.destroy()
				entry.timer=nil
			end
		end
		entry.cleanup=function(reason)
			entry.active=false
			entry.reason=reason
			local ok,err=pcall(function()
				if entry.hook and _G.debugger_onBreakpoint==entry.hook then _G.debugger_onBreakpoint=nil end
				assert(not entry.pendingBreakpoint, 'Breakpoint creation did not return a usable ID; manual cleanup in Cheat Engine is required')
				if entry.breakpointId~=nil then
					assert(entry.pid==getOpenedProcessID(), 'Target changed; remove the retained breakpoint manually in Cheat Engine')
					assert(debug_removeBreakpointByID(entry.breakpointId)~=false, 'Cheat Engine rejected breakpoint removal')
					entry.breakpointId=nil
				end
			end)
			entry.cleanupError=not ok and tostring(err) or nil
			entry.finished=getTickCount()
			return ok
		end
		local function snapshot()
			-- CE fills globals before both per-breakpoint callbacks and debugger_onBreakpoint.
			-- debug_getContext is for UI-stopped contexts and must not replace these callback registers.
			local ip=entry.wide and RIP or EIP
			assert(math.type(ip)=='integer' and math.type(THREADID)=='integer', 'CE did not supply the breakpoint context')
			local registers={}
			local names=entry.wide and {'RAX','RBX','RCX','RDX','RSI','RDI','RBP','RSP','RIP','R8','R9','R10','R11','R12','R13','R14','R15','EFLAGS'}
				or {'EAX','EBX','ECX','EDX','ESI','EDI','EBP','ESP','EIP','EFLAGS'}
			for _,name in ipairs(names) do
				local value=_G[name]
				if math.type(value)=='integer' then registers[name]=string.format('0x%X',value) end
			end
			local hit={ip=string.format('0x%X',ip),threadId=THREADID,registers=registers}
			local instruction=ip
			if entry.trigger~='execute' then
				-- Hardware data traps generally report the instruction after the access. Reverse disassembly
				-- is a best-effort candidate, not proof of the executed instruction boundary.
				hit.instructionAddressIsHeuristic=true
				local ok,previous=pcall(getPreviousOpcode,ip)
				if ok and math.type(previous)=='integer' then instruction=previous else instruction=nil end
			end
			if instruction then
				hit.instructionAddress=string.format('0x%X',instruction)
				local ok,text=pcall(disassemble,instruction)
				if ok and type(text)=='string' then hit.disassembly=text:sub(1,512) end
			end
			return hit
		end
		local function append()
			entry.total=entry.total+1
			if #entry.hits<entry.limit then entry.hits[#entry.hits+1]=snapshot() else entry.dropped=entry.dropped+1 end
		end
		local function traceStep()
			if not entry.active or entry.pid~=getOpenedProcessID() then entry.cleanup('targetChanged'); return 0 end
			if THREADID~=entry.threadId or not debug_isStepping() then return 0 end
			local ok,err=pcall(function()
				append()
				if entry.total>=entry.limit then entry.cleanup('completed') else debug_continueFromBreakpoint(co_stepinto) end
			end)
			if not ok then entry.error=tostring(err); entry.cleanup('error'); return 0 end
			return entry.active and 1 or 0
		end
		entry.callback=function()
			if not entry.active or entry.pid~=getOpenedProcessID() then entry.cleanup('targetChanged'); return 0 end
			local ok,err=pcall(function()
				if entry.kind=='trace' then
					assert((entry.wide and RIP or EIP)==entry.address, 'Unexpected trace entry instruction')
					assert(_G.debugger_onBreakpoint==nil, 'Another debugger hook appeared before trace entry')
					entry.threadId=THREADID
					entry.hook=traceStep
					_G.debugger_onBreakpoint=entry.hook
					append()
					assert(debug_removeBreakpointByID(entry.breakpointId)~=false, 'Could not remove the trace entry breakpoint')
					entry.breakpointId=nil
					if entry.total>=entry.limit then entry.cleanup('completed') else debug_continueFromBreakpoint(co_stepinto) end
				else
					append()
					debug_continueFromBreakpoint(co_run)
				end
			end)
			if not ok then entry.error=tostring(err); entry.cleanup('error'); return 0 end
			return entry.kind=='capture' and 1 or (entry.active and 1 or 0)
		end
		local ok,err=pcall(function()
			entry.timer=createTimer(nil,false)
			assert(entry.timer~=nil, 'Could not create the debugger cleanup timer')
			entry.timer.Interval=100
			entry.timer.OnTimer=function()
				if entry.active and ((getTickCount()-entry.started)%4294967296>=a[7]*1000) then entry.cleanup('expired') end
				if entry.cleanupError then destroyTimer(); return end
				if not entry.active and (getTickCount()-entry.finished)%4294967296>=30000 then
					destroyTimer()
					store[entry.id]=nil
				end
			end
			local triggers={execute=bptExecute,access=bptAccess,write=bptWrite}
			entry.pendingBreakpoint=true
			local accepted,id=debug_setBreakpoint(address,entry.size,triggers[entry.trigger],entry.callback)
			entry.breakpointId=id
			entry.pendingBreakpoint=accepted==true and type(id)~='number'
			assert(accepted==true, 'Breakpoint was rejected')
			assert(type(id)=='number', 'CE did not return an owned breakpoint ID; manual removal is required')
			entry.timer.Enabled=true
		end)
		if not ok then
			local clean=entry.cleanup('startFailed')
			local timerClean,timerError=pcall(destroyTimer)
			if not timerClean then entry.cleanupError=tostring(timerError) end
			if clean and timerClean then store[entry.id]=nil end
			return {error=tostring(err),captureId=entry.id,traceId=entry.id,requiresManualRecovery=entry.cleanupError~=nil,cleanupError=entry.cleanupError}
		end
		return {captureId=entry.id,traceId=entry.id,processId=entry.pid,address=string.format('0x%X',address),
			breakpointId=entry.breakpointId,trigger=entry.trigger,size=entry.size,limit=entry.limit,lifetimeSeconds=a[7]}
		""";

	// Arguments: id, kind, maximumResults, clear.
	internal const string Poll = """
		local entry=(_G.__cheatengine_mcp_debugger_jobs or {})[a[1]]
		assert(entry and entry.kind==a[2], 'Unknown or expired debugger job')
		local count=#entry.hits
		local take=math.min(count,a[3])
		local hits={}
		for i=1,take do hits[i]=entry.hits[i] end
		if a[4] then for i=1,take do table.remove(entry.hits,1) end end
		local result={captureId=entry.id,traceId=entry.id,processId=entry.pid,targetChanged=entry.pid~=getOpenedProcessID(),
			count=count,returned=take,pending=#entry.hits,total=entry.total,dropped=entry.dropped,
			active=entry.active,completed=entry.reason=='completed',expired=entry.reason=='expired',reason=entry.reason,
			error=entry.error or entry.cleanupError,requiresManualRecovery=entry.cleanupError~=nil}
		if entry.kind=='trace' then result.steps=hits else result.hits=hits end
		return result
		""";

	internal const string Stop = """
		local store=_G.__cheatengine_mcp_debugger_jobs or {}
		local entry=store[a[1]]
		if entry==nil then return {released=true,alreadyReleased=true} end
		assert(entry.kind==a[2], 'Debugger job kind does not match')
		if not entry.cleanup('stopped') then return {error=entry.cleanupError,captureId=entry.id,traceId=entry.id,requiresManualRecovery=true,released=false} end
		if entry.timer then
			local ok,err=pcall(function() entry.timer.Enabled=false; entry.timer.destroy(); entry.timer=nil end)
			if not ok then entry.cleanupError=tostring(err); return {error=entry.cleanupError,released=false,requiresManualRecovery=true} end
		end
		store[entry.id]=nil
		return {captureId=entry.id,traceId=entry.id,released=true}
		""";
}
