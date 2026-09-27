namespace CheatEngine.Mcp.Tools.Lua;

/// <summary>The composition entry point of the <c>lua</c> tool domain (2 tools in the v2 catalog).</summary>
public static class LuaToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>lua_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddLuaTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B11 adds AddJsonTypeInfoResolver(LuaJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
