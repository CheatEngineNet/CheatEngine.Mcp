namespace CheatEngine.Mcp.Tools.Debugger;

/// <summary>The composition entry point of the <c>debugger</c> tool domain (18 tools in the v2 catalog).</summary>
public static class DebuggerToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>debugger_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddDebuggerTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B10 adds AddJsonTypeInfoResolver(DebuggerJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
