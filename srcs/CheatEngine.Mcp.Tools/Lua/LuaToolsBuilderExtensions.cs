namespace CheatEngine.Mcp.Tools.Lua;

/// <summary>The composition entry point of the <c>lua</c> tool domain (2 tools in the v2 catalog).</summary>
public static class LuaToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>Declares the <c>lua_*</c> tool container and its JSON metadata.</summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddLuaTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(LuaJsonContext.Default)
				.AddToolType<LuaTools>();
		}
	}
}
