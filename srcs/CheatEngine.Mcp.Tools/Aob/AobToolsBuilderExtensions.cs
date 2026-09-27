namespace CheatEngine.Mcp.Tools.Aob;

/// <summary>The composition entry point of the <c>aob</c> tool domain (2 tools in the v2 catalog).</summary>
public static class AobToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>aob_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddAobTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B2 adds AddJsonTypeInfoResolver(AobJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
