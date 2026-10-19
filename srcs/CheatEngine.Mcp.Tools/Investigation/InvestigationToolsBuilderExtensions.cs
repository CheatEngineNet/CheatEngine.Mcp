namespace CheatEngine.Mcp.Tools.Investigation;

/// <summary>The composition entry point for persistent investigation notes.</summary>
public static class InvestigationToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>Registers the note tools and their source-generated wire metadata.</summary>
		public ICheatEngineMcpBuilder AddInvestigationTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder.AddJsonTypeInfoResolver(InvestigationJsonContext.Default).AddToolType<InvestigationTools>();
		}
	}
}
