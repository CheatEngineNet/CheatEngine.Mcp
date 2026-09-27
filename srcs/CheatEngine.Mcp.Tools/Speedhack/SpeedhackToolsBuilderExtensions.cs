namespace CheatEngine.Mcp.Tools.Speedhack;

/// <summary>The composition entry point of the <c>speedhack</c> tool domain (2 tools in the v2 catalog).</summary>
public static class SpeedhackToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>speedhack_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddSpeedhackTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B1 adds AddJsonTypeInfoResolver(SpeedhackJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
