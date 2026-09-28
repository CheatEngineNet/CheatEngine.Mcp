namespace CheatEngine.Mcp.Tools.Runtime;

/// <summary>The composition entry point of the <c>runtime</c> tool domain (6 tools in the v2 catalog).</summary>
public static class RuntimeToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>runtime_*</c> tool container and source-generated JSON metadata.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddRuntimeTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(RuntimeJsonContext.Default)
				.AddToolType<RuntimeTools>();
		}
	}
}
