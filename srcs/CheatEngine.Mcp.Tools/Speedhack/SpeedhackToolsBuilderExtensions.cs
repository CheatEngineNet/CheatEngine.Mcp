namespace CheatEngine.Mcp.Tools.Speedhack;

/// <summary>The composition entry point of the <c>speedhack</c> tool domain (2 tools in the v2 catalog).</summary>
public static class SpeedhackToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>Declares the <c>speedhack_*</c> tool container and its JSON metadata.</summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddSpeedhackTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(SpeedhackJsonContext.Default)
				.AddToolType<SpeedhackTools>();
		}
	}
}
