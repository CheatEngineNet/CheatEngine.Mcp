namespace CheatEngine.Mcp.Tools.Exec;

/// <summary>The composition entry point of the <c>exec</c> tool domain (6 tools in the v2 catalog).</summary>
public static class ExecToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>exec_*</c> tool container and its JSON metadata.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddExecTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(ExecJsonContext.Default)
				.AddToolType<ExecTools>();
		}
	}
}
