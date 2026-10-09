using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Speedhack;

/// <summary>Fixed, bounded Lua bodies used by the speedhack tools.</summary>
internal static class SpeedhackScripts
{
	private const string SymbolProbe = """
	                                   local function speedhackSymbol(effect)
	                                     if type(getAddressSafe) ~= 'function' then
	                                       return nil, nil, mcp.err('host_refused', 'Cheat Engine does not provide getAddressSafe for the speedhack symbol.', effect)
	                                     end
	                                     local ok, address = pcall(getAddressSafe, 'speedhack_wantedspeed')
	                                     if not ok then
	                                       return nil, nil, mcp.err('host_refused', 'Cheat Engine could not resolve the speedhack symbol.', effect)
	                                     end
	                                     if address == nil or address == 0 then return false end
	                                     if math.type(address) == 'integer' then return true, address end
	                                     return nil, nil, mcp.err('host_refused', 'Cheat Engine returned an invalid speedhack symbol address.', effect)
	                                   end
	                                   """ + "\n";

	/// <summary>Reads the configured speed and the durable target symbol without changing Cheat Engine.</summary>
	internal const string GetState = SymbolProbe + """
	                                 local speed = speedhack_getSpeed()
	                                 if type(speed) ~= 'number' or speed ~= speed or speed == math.huge or speed == -math.huge then
	                                     return mcp.err('host_refused', 'Cheat Engine did not return a finite speedhack speed.', 'not_started')
	                                 end
	                                 local hasSymbol, _, refused = speedhackSymbol('not_started')
	                                 if refused then return refused end
	                                 return { speed = speed, hooksInstalled = hasSymbol }
	                                 """;

	/// <summary>
	///     Restores speed 1 without recording a second restore resource and without ever activating the speedhack.
	///     Cheat Engine's <c>speedhack_setSpeed</c> ticks its Enable Speedhack checkbox for every speed, 1 included,
	///     and that activation hooks a process that has no speedhack_wantedspeed symbol yet, so the body calls it only
	///     in a process that has the symbol: there the activation never hooks again. Without the symbol it changes
	///     nothing at speed 1 and refuses another configured speed, which then belongs to a speedhack of another
	///     process. <c>speedhack_getSpeed</c> reports 1 also after a process switch freed Cheat Engine's speedhack
	///     while the target kept its speed, so with the symbol the body skips the call only when the target's own speed
	///     is 1 too. It refuses, like a speed change, while the debugger is stopped or the target is paused, because
	///     the Unity path restores through <c>mono_invoke_method</c>.
	/// </summary>
	internal const string SetNormal = SymbolProbe + """
		local pid = getOpenedProcessID()
		if math.type(pid) ~= 'integer' or pid <= 0 then
			return mcp.err('invalid_state', 'No process is attached.', 'not_started',
				'Attach a local process with process_attach first.')
		end
		local function finite(value)
			return type(value) == 'number' and value == value and value ~= math.huge and value ~= -math.huge
		end
		local current = speedhack_getSpeed()
		if not finite(current) then
			return mcp.err('host_refused', 'Cheat Engine did not return a finite speedhack speed.', 'not_started')
		end
		local hasSymbol, address, refused = speedhackSymbol('not_started')
		if refused then return refused end
		local normal = math.abs(current - 1) <= 0.000001
		if not hasSymbol then
			-- Never sped up: setting 1 would tick Enable Speedhack and hook this process.
			if normal then return { speed = current, hooksInstalled = false, firstActivation = false } end
			return mcp.err('invalid_state', string.format('Cheat Engine reports speed %g, but the opened process has '
				.. 'no speedhack_wantedspeed symbol, so setting 1 would activate the speedhack here.', current),
				'not_started', 'If another process was sped up, open it again and set speed 1 there; otherwise '
				.. 'untick Enable Speedhack in Cheat Engine.')
		end
		if normal and type(readFloat) == 'function' then
			-- Cheat Engine also reports 1 once a process switch freed its speedhack; the target keeps its own speed.
			local read, wanted = pcall(readFloat, address)
			if read and finite(wanted) and math.abs(wanted - 1) <= 0.000001 then
				return { speed = current, hooksInstalled = true, firstActivation = false }
			end
		end
		-- CE 7.7 can return an opaque value from debug_isBroken when no debugger is attached.
		-- A stopped context is the authoritative check before executing code in the target.
		local debugging = debug_isDebugging()
		if type(debugging) ~= 'boolean' then
			return mcp.err('host_refused', 'Cheat Engine did not return a boolean debugger state.', 'not_started')
		end
		local stopped = false
		if debugging then stopped = debug_getContext(false) end
		if type(stopped) ~= 'boolean' then
			return mcp.err('host_refused', 'Cheat Engine did not return a boolean stopped-context state.', 'not_started')
		end
		if stopped then
			return mcp.err('invalid_state', 'The debugger is stopped at a breakpoint.', 'not_started',
				'Continue the debugger before changing speed.')
		end
		if type(isPaused) == 'function' and isPaused() then
			return mcp.err('invalid_state', 'The target is paused.', 'not_started',
				'Resume it with process_set_paused before changing speed.')
		end
		speedhack_setSpeed(1)
		local speed = speedhack_getSpeed()
		if not finite(speed) then
			return mcp.err('host_refused', 'Cheat Engine did not return a finite speed after restoring it.', 'started')
		end
		local hooksInstalled, _, probeRefused = speedhackSymbol('started')
		if probeRefused then hooksInstalled = nil end
		return { speed = speed, hooksInstalled = hooksInstalled, firstActivation = false }
		""";

	/// <summary>
	///     Sets one valid speed and records a restore action in the Core-owned Lua state root. Arguments are speed, resource
	///     id and activation namespace. The body is deliberately fixed; callers only provide the number in <c>a[1]</c>.
	/// </summary>
	internal static readonly string SetSpeed = LuaJobKernelScripts.Kernel + "\n" + SymbolProbe + """
		local pid = getOpenedProcessID()
		if math.type(pid) ~= 'integer' or pid <= 0 then
			return mcp.err('invalid_state', 'No process is attached.', 'not_started', 'Attach a local process with process_attach first.')
		end
		-- CE 7.7 can return an opaque value from debug_isBroken when no debugger is attached.
		-- A stopped context is the authoritative check before executing code in the target.
		local debugging = debug_isDebugging()
		if type(debugging) ~= 'boolean' then
			return mcp.err('host_refused', 'Cheat Engine did not return a boolean debugger state.', 'not_started')
		end
		local stopped = false
		if debugging then stopped = debug_getContext(false) end
		if type(stopped) ~= 'boolean' then
			return mcp.err('host_refused', 'Cheat Engine did not return a boolean stopped-context state.', 'not_started')
		end
		if stopped then
			return mcp.err('invalid_state', 'The debugger is stopped at a breakpoint.', 'not_started', 'Continue the debugger before changing speed.')
		end
		if type(isPaused) == 'function' and isPaused() then
			return mcp.err('invalid_state', 'The target is paused.', 'not_started', 'Resume it with process_set_paused before changing speed.')
		end
		if type(targetIsX86) == 'function' and not targetIsX86() then
			return mcp.err('unsupported', 'Cheat Engine speedhack requires an x86 or x64 target.', 'not_started')
		end
		local before, _, refused = speedhackSymbol('not_started')
		if refused then return refused end
		speedhack_setSpeed(a[1])
		local speed = speedhack_getSpeed()
		if type(speed) ~= 'number' or speed ~= speed or speed == math.huge or speed == -math.huge then
			return mcp.err('host_refused', 'Cheat Engine did not return a finite speed after changing it.', 'started')
		end
		local after, _, probeRefused = speedhackSymbol('started')
		if probeRefused then after = nil end
		resourceRecord(a[3], a[2], 'speedhack', { name = 'speedhack', detail = 'Restores the configured speed to 1.' }, function()
			-- Opening another process unticks Enable Speedhack without restoring this one; ticking it again would
			-- hook the newly opened process instead, so the restore needs manual recovery then.
			assert(getOpenedProcessID() == pid, 'The sped-up process is no longer the opened process')
			-- Cheat Engine reports 1 also once a process switch freed its speedhack, while this process keeps its
			-- speed, so the call is skipped only when the target has no hooks or its own speed is 1 too.
			local configured = speedhack_getSpeed()
			if type(configured) == 'number' and math.abs(configured - 1) <= 0.000001 then
				local found, address, releaseRefused = speedhackSymbol('unknown')
				if releaseRefused then error('Cheat Engine could not resolve the speedhack symbol while restoring.', 0) end
				if not found then return end
				local read, wanted = pcall(readFloat, address)
				if read and type(wanted) == 'number' and math.abs(wanted - 1) <= 0.000001 then return end
			end
			speedhack_setSpeed(1)
			local restored = speedhack_getSpeed()
			assert(type(restored) == 'number' and restored == restored and restored ~= math.huge and restored ~= -math.huge,
				'Cheat Engine did not return a finite speed while restoring speedhack')
			assert(math.abs(restored - 1) <= 0.000001, 'Cheat Engine did not restore speedhack to 1')
		end)
		return { speed = speed, hooksInstalled = after, firstActivation = not before }
		""";
}
