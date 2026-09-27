namespace CheatEngine.Mcp.Tools.Runtime;

/// <summary>The composition entry point of the <c>runtime</c> tool domain (6 tools in the v2 catalog).</summary>
public static class RuntimeToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>runtime_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddRuntimeTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B1 adds AddJsonTypeInfoResolver(RuntimeJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
