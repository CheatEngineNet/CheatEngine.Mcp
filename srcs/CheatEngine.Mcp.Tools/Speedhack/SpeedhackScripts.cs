using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Speedhack;

/// <summary>Fixed, bounded Lua bodies used by the speedhack tools.</summary>
internal static class SpeedhackScripts
{
	/// <summary>Reads the configured speed and the durable target symbol without changing Cheat Engine.</summary>
	internal const string GetState = """
	                                 local speed = speedhack_getSpeed()
	                                 if type(speed) ~= 'number' or speed ~= speed or speed == math.huge or speed == -math.huge then
	                                     return mcp.err('host_refused', 'Cheat Engine did not return a finite speedhack speed.', 'not_started')
	                                 end
	                                 local hasSymbol = false
	                                 if type(getAddressSafe) == 'function' then
	                                     local ok, address = pcall(getAddressSafe, 'speedhack_wantedspeed')
	                                     hasSymbol = ok and math.type(address) == 'integer'
	                                 end
	                                 return { speed = speed, hooksInstalled = hasSymbol }
	                                 """;

	/// <summary>Restores Cheat Engine's configured speed without recording a second restore resource.</summary>
	internal const string SetNormal = """
	                                  speedhack_setSpeed(1)
	                                  local speed = speedhack_getSpeed()
	                                  if type(speed) ~= 'number' or speed ~= speed or speed == math.huge or speed == -math.huge then
	                                      return mcp.err('host_refused', 'Cheat Engine did not return a finite speed after restoring it.', 'started')
	                                  end
	                                  local hasSymbol = false
	                                  if type(getAddressSafe) == 'function' then
	                                      local ok, address = pcall(getAddressSafe, 'speedhack_wantedspeed')
	                                      hasSymbol = ok and math.type(address) == 'integer'
	                                  end
	                                  return { speed = speed, hooksInstalled = hasSymbol }
	                                  """;

	/// <summary>
	///     Sets one valid speed and records a restore action in the Core-owned Lua state root. Arguments are speed, resource
	///     id and activation namespace. The body is deliberately fixed; callers only provide the number in <c>a[1]</c>.
	/// </summary>
	internal static readonly string SetSpeed = LuaJobKernelScripts.Kernel + "\n" + """
		local pid = getOpenedProcessID()
		if math.type(pid) ~= 'integer' or pid <= 0 then
			return mcp.err('invalid_state', 'No process is attached.', 'not_started', 'Attach a local process with process_attach first.')
		end
		if type(isPaused) == 'function' and isPaused() then
			return mcp.err('invalid_state', 'The target is paused.', 'not_started', 'Resume it with process_set_paused before changing speed.')
		end
		if type(debug_isBroken) == 'function' and debug_isBroken() then
			return mcp.err('invalid_state', 'The debugger is stopped at a breakpoint.', 'not_started', 'Continue the debugger before changing speed.')
		end
		if type(targetIsX86) == 'function' and not targetIsX86() then
			return mcp.err('unsupported', 'Cheat Engine speedhack requires an x86 or x64 target.', 'not_started')
		end
		local before = false
		if type(getAddressSafe) == 'function' then
			local ok, address = pcall(getAddressSafe, 'speedhack_wantedspeed')
			before = ok and math.type(address) == 'integer'
		end
		speedhack_setSpeed(a[1])
		local speed = speedhack_getSpeed()
		if type(speed) ~= 'number' or speed ~= speed or speed == math.huge or speed == -math.huge then
			return mcp.err('host_refused', 'Cheat Engine did not return a finite speed after changing it.', 'started')
		end
		local after = before
		if type(getAddressSafe) == 'function' then
			local ok, address = pcall(getAddressSafe, 'speedhack_wantedspeed')
			after = ok and math.type(address) == 'integer'
		end
		resourceRecord(a[3], a[2], 'speedhack', { name = 'speedhack', detail = 'Restores the configured speed to 1.' }, function()
			speedhack_setSpeed(1)
			local restored = speedhack_getSpeed()
			assert(type(restored) == 'number' and restored == restored and restored ~= math.huge and restored ~= -math.huge,
				'Cheat Engine did not return a finite speed while restoring speedhack')
			assert(math.abs(restored - 1) <= 0.000001, 'Cheat Engine did not restore speedhack to 1')
		end)
		return { speed = speed, hooksInstalled = after, firstActivation = not before }
		""";
}
