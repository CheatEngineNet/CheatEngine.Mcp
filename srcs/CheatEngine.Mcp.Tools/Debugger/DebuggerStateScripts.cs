namespace CheatEngine.Mcp.Tools.Debugger;

/// <summary>Reads a boolean stopped state without consulting CE's undefined unattached breakpoint value.</summary>
internal static class DebuggerStateScripts
{
	// Never consult CE 7.7's opaque unattached breakpoint value. Once attached, require a boolean from
	// debug_isBroken; unlike debug_getContext, this query does not overwrite prepared register globals.
	internal const string ReadStopped = """
		local function readDebuggerStopped()
			local queried, attached = pcall(function() return debug_isDebugging() end)
			if not queried or type(attached) ~= 'boolean' then
				return nil, 'Cheat Engine did not return a boolean debugger state.'
			end
			if not attached then return false end
			local read, stopped = pcall(function() return debug_isBroken() end)
			if not read or type(stopped) ~= 'boolean' then
				return nil, 'Cheat Engine did not return a boolean breakpoint state.'
			end
			return stopped
		end
		""";

	// debug_getContext(false) writes these fixed globals before returning. Preserve only that documented register
	// surface so callbacks and other Lua state remain visible to the caller.
	internal const string ReadContextStopped = """
		local function readDebuggerContextStopped()
			local targetRead, isX86 = pcall(function() return targetIsX86() end)
			if not targetRead or isX86 ~= true then
				return nil, 'Cheat Engine did not confirm an x86 or x64 target for debugger context.'
			end
			local interfaceRead, interface = pcall(function() return debug_getCurrentDebuggerInterface() end)
			if not interfaceRead or math.type(interface) ~= 'integer' or interface < 1 or interface > 3 then
				return nil, 'Cheat Engine did not report a supported active debugger interface.'
			end
			local names = {'THREADID', 'EFLAGS', 'EAX', 'EBX', 'ECX', 'EDX', 'ESI', 'EDI', 'EBP', 'ESP', 'EIP',
				'RAX', 'RBX', 'RCX', 'RDX', 'RSI', 'RDI', 'RBP', 'RSP', 'R8', 'R9', 'R10', 'R11', 'R12', 'R13',
				'R14', 'R15', 'RIP'}
			local saved = {}
			for _, name in ipairs(names) do
				saved[name] = rawget(_G, name)
			end
			local read, stopped = pcall(function() return debug_getContext(false) end)
			for _, name in ipairs(names) do
				rawset(_G, name, saved[name])
			end
			if not read or type(stopped) ~= 'boolean' then
				return nil, 'Cheat Engine did not return a boolean stopped-context state.'
			end
			return stopped
		end
		""";
}
