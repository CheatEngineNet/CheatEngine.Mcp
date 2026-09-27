namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>The composition entry point of the <c>memory</c> tool domain (13 tools in the v2 catalog).</summary>
public static class MemoryToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>memory_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddMemoryTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B2 adds AddJsonTypeInfoResolver(MemoryJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
