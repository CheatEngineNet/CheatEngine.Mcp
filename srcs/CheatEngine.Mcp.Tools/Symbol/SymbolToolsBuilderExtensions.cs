namespace CheatEngine.Mcp.Tools.Symbol;

/// <summary>The composition entry point of the <c>symbol</c> tool domain (8 tools in the v2 catalog).</summary>
public static class SymbolToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>symbol_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddSymbolTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B5 adds AddJsonTypeInfoResolver(SymbolJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
