namespace CheatEngine.Mcp.Tools.Code;

/// <summary>The composition entry point of the <c>code</c> tool domain (13 tools in the v2 catalog).</summary>
public static class CodeToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>code_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddCodeTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B6 adds AddJsonTypeInfoResolver(CodeJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
