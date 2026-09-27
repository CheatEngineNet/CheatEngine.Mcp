namespace CheatEngine.Mcp.Tools.Asm;

/// <summary>Fixed Lua bodies for documented Auto Assembler source generation.</summary>
internal static class AsmScripts
{
	internal const string GenerateApiHook = """
	                                        local script = generateAPIHookScript(a[1], a[2], a[3], a[4], a[5])
	                                        if type(script) ~= 'string' then return mcp.err('host_refused', 'Cheat Engine could not generate an API-hook script.', 'not_started') end
	                                        return { script=script }
	                                        """;
}
