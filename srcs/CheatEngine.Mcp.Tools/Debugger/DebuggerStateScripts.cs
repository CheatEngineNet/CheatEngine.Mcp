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
}
