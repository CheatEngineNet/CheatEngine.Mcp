namespace CheatEngine.Mcp.Tools.Util;

/// <summary>The composition entry point of the <c>util</c> tool domain (2 tools in the v2 catalog).</summary>
public static class UtilToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>util_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddUtilTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B1 adds AddJsonTypeInfoResolver(UtilJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
