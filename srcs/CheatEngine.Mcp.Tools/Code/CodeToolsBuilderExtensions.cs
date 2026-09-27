namespace CheatEngine.Mcp.Tools.Code;

/// <summary>The composition entry point of the <c>code</c> tool domain (13 tools in the v2 catalog).</summary>
public static class CodeToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>code_*</c> tool container and its source-generated JSON metadata.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddCodeTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(CodeJsonContext.Default)
				.AddToolType<CodeTools>();
		}
	}
}
