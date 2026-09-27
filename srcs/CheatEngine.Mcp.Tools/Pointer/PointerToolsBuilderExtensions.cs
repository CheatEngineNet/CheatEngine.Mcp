namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>The composition entry point of the <c>pointer</c> tool domain (10 tools in the v2 catalog).</summary>
public static class PointerToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>pointer_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddPointerTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B4 adds AddJsonTypeInfoResolver(PointerJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
