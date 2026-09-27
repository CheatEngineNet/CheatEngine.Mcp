namespace CheatEngine.Mcp.Tools.Asm;

/// <summary>The composition entry point of the <c>asm</c> tool domain (8 tools in the v2 catalog).</summary>
public static class AsmToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>asm_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddAsmTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B7 adds AddJsonTypeInfoResolver(AsmJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
