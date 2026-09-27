namespace CheatEngine.Mcp.Tools.Aob;

/// <summary>The composition entry point of the <c>aob</c> tool domain (2 tools in the v2 catalog).</summary>
public static class AobToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>Declares the <c>aob_*</c> tool container and its JSON metadata.</summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddAobTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(AobJsonContext.Default)
				.AddToolType<AobTools>();
		}
	}
}
