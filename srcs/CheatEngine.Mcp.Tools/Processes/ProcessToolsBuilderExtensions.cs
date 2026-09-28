namespace CheatEngine.Mcp.Tools.Processes;

/// <summary>The composition entry point of the <c>process</c> tool domain (9 tools in the v2 catalog).</summary>
public static class ProcessToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>process_*</c> tool container and source-generated JSON metadata.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddProcessTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(ProcessJsonContext.Default)
				.AddToolType<ProcessTools>();
		}
	}
}
