using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Processes;

/// <summary>
///     The fixed Lua bodies of <c>process_set_paused</c>. A pause that MCP makes is recorded in the Core-owned Lua
///     state root as a <c>pause</c> effect whose release resumes the process, so it blocks a target change and
///     <c>runtime_release_resources</c> undoes it.
/// </summary>
/// <remarks>
///     Cheat Engine's <c>pause</c> and <c>unpause</c> press its Pause button, which suspends or resumes the process
///     that is opened at that moment and does nothing when the button is already in the requested state. Opening
///     another process never resumes the paused one, so a release refuses once another process is opened instead of
///     resuming the wrong one. <c>isPaused</c> is also true while the debugger is stopped at a breakpoint, so both
///     directions refuse then: the pause state cannot be told apart from the break.
/// </remarks>
internal static class ProcessPauseScripts
{
	/// <summary>
	///     Pauses the opened process. MCP's tracked pause is reused only while it was recorded for the opened process;
	///     otherwise a pause of a running process is recorded under the new id. A target already paused by anything
	///     else, the user or an earlier activation, is left as it is. <c>a</c>: the activation namespace, the new
	///     resource id, then the id of MCP's tracked pause or <c>nil</c>.
	/// </summary>
	internal const string Pause = LuaJobKernelScripts.Kernel + "\n" + """
		local pid = getOpenedProcessID()
		if math.type(pid) ~= 'integer' or pid <= 0 then
			return mcp.err('invalid_state', 'No process is attached.', 'not_started',
				'Attach a local process with process_attach first.')
		end
		if type(debug_isBroken) == 'function' and debug_isBroken() then
			return mcp.err('invalid_state', 'The debugger is stopped at a breakpoint, so the target is already halted.',
				'not_started', 'Continue the target with debugger_continue, then repeat the call.')
		end
		-- MCP's tracked pause covers only the process it paused: after a switch in Cheat Engine's window, a pause
		-- of the now opened process is a new effect.
		local reused = nil
		if a[3] ~= nil then
			local tracked = resourceFind(a[1], a[3])
			if tracked ~= nil and tracked.processId == pid and tracked.cleanupError == nil then reused = a[3] end
		end
		if isPaused() then return {paused = true, resourceId = reused} end
		pause()
		if not isPaused() then
			return mcp.err('host_refused', 'Cheat Engine did not pause the target.', 'not_applied')
		end
		if reused ~= nil then return {paused = true, resourceId = reused} end
		local recorded, failure = pcall(resourceRecord, a[1], a[2], 'pause',
			{name = 'pause', detail = 'Resumes the paused process.'},
			function()
				assert(getOpenedProcessID() == pid, 'The paused process is no longer the opened process')
				unpause()
				assert(not isPaused() or (type(debug_isBroken) == 'function' and debug_isBroken()),
					'Cheat Engine did not resume the paused process')
			end)
		if not recorded then
			unpause()
			return mcp.err('host_refused',
				'MCP could not record the pause, so it resumed the target: ' .. tostring(failure),
				isPaused() and 'unknown' or 'not_applied',
				'Release retained resources with runtime_release_resources, then repeat the call.')
		end
		return {paused = true, resourceId = a[2]}
		""";

	/// <summary>
	///     Checks, without any effect, that the opened process can be resumed: a process is attached and the debugger
	///     is not stopped at a breakpoint. It runs before MCP's tracked pause is released, so a refusal leaves that
	///     pause in place. <c>a</c>: nothing.
	/// </summary>
	internal const string ResumeCheck = ResumePreconditions + "\n" + """
		return {paused = isPaused()}
		""";

	/// <summary>
	///     Resumes the opened process, then forgets every recorded <c>pause</c> effect of that process, in any
	///     activation, because this resume undid it. <c>a</c>: nothing.
	/// </summary>
	internal const string Resume = LuaJobKernelScripts.Kernel + "\n" + ResumePreconditions + "\n" + """
		unpause()
		if isPaused() then
			return mcp.err('host_refused', 'Cheat Engine did not resume the target.', 'unknown',
				'Resume the target with the Pause button in Cheat Engine.')
		end
		for _, entry in ipairs(stateEntriesOfKind('pause')) do
			if entry.family == 'resource' and entry.processId == pid and entry.cleanupError == nil then
				resourceForget(entry.namespace, entry.id)
			end
		end
		return {paused = false}
		""";

	/// <summary>The refusals that both resume bodies share; it defines <c>pid</c>, the opened process.</summary>
	private const string ResumePreconditions = """
		local pid = getOpenedProcessID()
		if math.type(pid) ~= 'integer' or pid <= 0 then
			return mcp.err('invalid_state', 'No process is attached.', 'not_started',
				'Attach a local process with process_attach first.')
		end
		if type(debug_isBroken) == 'function' and debug_isBroken() then
			return mcp.err('invalid_state', 'The debugger is stopped at a breakpoint, so resuming cannot be confirmed.',
				'not_started', 'Continue the target with debugger_continue, then repeat the call.')
		end
		""";
}
