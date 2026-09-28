namespace CheatEngine.Mcp.Tools.Debugger;

/// <summary>The composition entry point of the <c>debugger</c> tool domain (18 tools in the v2 catalog).</summary>
public static class DebuggerToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>debugger_*</c> tool container and its generated JSON metadata.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddDebuggerTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(DebuggerJsonContext.Default)
				.AddToolType<DebuggerTools>();
		}
	}
}
