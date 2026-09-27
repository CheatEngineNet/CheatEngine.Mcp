namespace CheatEngine.Mcp.Tools.Table;

/// <summary>The composition entry point of the <c>table</c> tool domain (3 tools in the v2 catalog).</summary>
public static class TableToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>table_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddTableTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B8 adds AddJsonTypeInfoResolver(TableJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
