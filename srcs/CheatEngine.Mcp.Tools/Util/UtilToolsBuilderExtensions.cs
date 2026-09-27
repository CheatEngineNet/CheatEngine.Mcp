namespace CheatEngine.Mcp.Tools.Util;

/// <summary>The composition entry point of the <c>util</c> tool domain (2 tools in the v2 catalog).</summary>
public static class UtilToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>Declares the <c>util_*</c> tool container and its JSON metadata.</summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddUtilTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(UtilJsonContext.Default)
				.AddToolType<UtilTools>();
		}
	}
}
