namespace CheatEngine.Mcp.Tools.Processes;

/// <summary>The composition entry point of the <c>process</c> tool domain (9 tools in the v2 catalog).</summary>
public static class ProcessToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>process_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddProcessTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B1 adds AddJsonTypeInfoResolver(ProcessJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
